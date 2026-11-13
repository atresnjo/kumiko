using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace Kumiko.Desktop;

/// <summary>Plans a Windows update from a staged package. Pure file work behind <see cref="WindowsUpdateHost"/>,
/// so every refusal runs on any host; only the host opens App Installer, starts the helper or quits.</summary>
internal static class WindowsUpdatePlanner
{
    const string ManifestNamespace = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    const string PublisherAlphabet = "0123456789abcdefghjkmnpqrstvwxyz";
    static readonly HashSet<string> Pending = new(StringComparer.OrdinalIgnoreCase);

    internal static Task<UpdateInstallation> PrepareAsync(WindowsUpdateHost host, StagedUpdate update, CancellationToken token) =>
        Task.Run(() => Prepare(host, update, token), token);

    static UpdateInstallation Prepare(WindowsUpdateHost host, StagedUpdate update, CancellationToken token)
    {
        if (update.Strategy != UpdateInstallStrategy.ExternalWindowsInstaller || !update.Release.Runtime.StartsWith("win-", StringComparison.Ordinal))
            throw new InvalidOperationException("Only a staged Windows update can be installed on Windows.");
        // One read-only handle, shared only with readers, covers verification and every later read of the package.
        using var package = new FileStream(update.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(package), Convert.FromHexString(update.Release.Sha256)))
            throw new InvalidDataException("The staged update no longer matches its verified checksum.");
        token.ThrowIfCancellationRequested();
        package.Position = 0;
        return host.PackageFullName is { } fullName ? WindowsPackageUpdate.Prepare(host, update, package, fullName, token) : PreparePortable(host, update, package, token);
    }

    /// <summary>The 13-character publisher id in package family names: Crockford-style base32 of the first
    /// 64 bits of SHA-256 over the UTF-16LE publisher, padded with one zero bit.</summary>
    internal static string PublisherId(string publisher)
    {
        var bits = BinaryPrimitives.ReadUInt64BigEndian(SHA256.HashData(Encoding.Unicode.GetBytes(publisher)));
        var id = new char[13];
        for (int i = 0; i < id.Length; i++)
        {
            int shift = 59 - 5 * i;
            id[i] = PublisherAlphabet[(int)((shift >= 0 ? bits >> shift : bits << -shift) & 31)];
        }
        return new string(id);
    }

    internal static (string Name, string Publisher, Version Version, string Architecture) ReadIdentity(Stream manifest)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 4 * 1024 * 1024
        };
        using var reader = XmlReader.Create(manifest, settings);
        reader.MoveToContent();
        if (reader.LocalName != "Package" || reader.NamespaceURI != ManifestNamespace || !reader.ReadToDescendant("Identity", ManifestNamespace))
            throw new InvalidDataException("The MSIX manifest has no package identity.");
        var name = reader.GetAttribute("Name") ?? "";
        var publisher = reader.GetAttribute("Publisher") ?? "";
        if (name.Length == 0 || publisher.Length == 0 || !Version.TryParse(reader.GetAttribute("Version"), out var version))
            throw new InvalidDataException("The MSIX manifest identity is incomplete.");
        return (name, publisher, version, reader.GetAttribute("ProcessorArchitecture") ?? "neutral");
    }

    static UpdateInstallation PreparePortable(WindowsUpdateHost host, StagedUpdate update, Stream package, CancellationToken token)
    {
        var exe = Path.GetFullPath(host.ProcessPath);
        var executable = Path.GetFileName(exe);
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A process hosted by dotnet cannot replace its installation; publish a self-contained or NativeAOT executable.");
        var target = Path.GetDirectoryName(exe)!;
        var parent = Path.GetDirectoryName(target) ?? throw new UnauthorizedAccessException("A portable installation at a drive root cannot be replaced.");
        foreach (var root in host.ProtectedRoots)
            if (Inside(target, root))
                throw new UnauthorizedAccessException($"{target} is inside {root}. A portable installation updates itself only from a folder the user owns; move it there or install the MSIX package.");
        RequireWritable(parent);
        RequireWritable(target);
        lock (Pending)
            if (!Pending.Add(target))
                throw new InvalidOperationException("An update is already pending for this installation.");
        // Siblings on the same volume, so the helper swaps with two renames and never copies.
        var fresh = Path.Combine(parent, "." + Path.GetFileName(target) + ".update-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(fresh);
            Extract(package, fresh, executable, token);
        }
        catch
        {
            if (Directory.Exists(fresh))
                Directory.Delete(fresh, true);
            lock (Pending)
                Pending.Remove(target);
            throw;
        }
        var installation = new UpdateInstallation(host, UpdateApplyMethod.PortableSwap, update.Release.Version, fresh);
        var plan = new WindowsSwapPlan(target, fresh, fresh + ".previous", fresh + ".log", exe);
        host.OnQuit(() => host.Start(WindowsUpdateScript.Helper(host, plan, installation.Relaunch)));
        return installation;
    }

    static bool Inside(string path, string root)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return (Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar).StartsWith(full, StringComparison.OrdinalIgnoreCase);
    }

    static void RequireWritable(string directory)
    {
        var probe = Path.Combine(directory, ".kumiko-write-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var file = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            throw new UnauthorizedAccessException($"{directory} is not writable by this user, so the update cannot replace the installation.", error);
        }
    }

    static void Extract(Stream package, string destination, string executable, CancellationToken token)
    {
        const int EntryLimit = 65536;
        const long ByteLimit = 4L * 1024 * 1024 * 1024;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entries = archive.Entries;
        if (entries.Count is 0 or > EntryLimit)
            throw new InvalidDataException("The update archive is empty or has too many entries.");
        if (entries.Any(e => e.FullName is "AppxManifest.xml" or "AppxSignature.p7x" or "AppxMetadata/AppxBundleManifest.xml"))
            throw new InvalidDataException("An MSIX package cannot replace a portable installation; publish the portable zip.");
        var paths = entries.Select(e => (Entry: e, Segments: Segments(e.FullName))).ToArray();
        // Portable zips hold <Name>/<files>; a single top-level folder is the installation itself.
        var first = paths[0].Segments[0];
        var strip = paths.All(p => p.Segments[0].Equals(first, StringComparison.OrdinalIgnoreCase) && (p.Segments.Length > 1 || IsDirectory(p.Entry))) ? 1 : 0;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination)) + Path.DirectorySeparatorChar;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var (entry, segments) in paths)
        {
            token.ThrowIfCancellationRequested();
            if (segments.Length == strip)
                continue;
            var path = Path.GetFullPath(Path.Combine(root, Path.Combine(segments[strip..])));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !seen.Add(path))
                throw new InvalidDataException("The update archive has an unsafe or duplicate entry: " + entry.FullName);
            if (IsDirectory(entry))
            {
                Directory.CreateDirectory(path);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = entry.Open();
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            long written = 0;
            int count;
            while ((count = input.Read(buffer)) > 0)
            {
                written += count;
                total += count;
                if (written > entry.Length || total > ByteLimit)
                    throw new InvalidDataException("The update archive expands beyond its declared size.");
                output.Write(buffer, 0, count);
            }
        }
        if (!File.Exists(Path.Combine(destination, executable)))
            throw new InvalidDataException($"The update does not contain {executable} at the top of the installation.");
    }

    static bool IsDirectory(ZipArchiveEntry entry) => entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');

    static string[] Segments(string name)
    {
        if (name.StartsWith('/') || name.StartsWith('\\'))
            throw new InvalidDataException("The update archive has an absolute entry: " + name);
        var segments = name.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(UnsafeSegment))
            throw new InvalidDataException("The update archive has an unsafe entry: " + name);
        return segments;
    }

    // Windows strips trailing dots and spaces and maps device names, so those segments could alias another path.
    static bool UnsafeSegment(string segment)
    {
        if (segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ') || segment.Any(c => c < 32 || "<>:\"|?*".Contains(c)))
            return true;
        var stem = segment.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && char.IsAsciiDigit(stem[3]));
    }
}

internal sealed record WindowsSwapPlan(string Target, string Fresh, string Backup, string Log, string Executable);
