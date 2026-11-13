using System.IO.Compression;
using System.Security.Cryptography;

namespace Kumiko.Desktop;

/// <summary>MSIX updates go to App Installer instead of PackageManager.AddPackageAsync: no hand-written WinRT
/// async completion runs in this process, Windows still verifies the signature and that Name and Publisher match
/// the installed family, and App Installer owns closing the running app and relaunching it.</summary>
internal static class WindowsPackageUpdate
{
    internal static UpdateInstallation Prepare(WindowsUpdateHost host, StagedUpdate update, Stream package, string fullName, CancellationToken token)
    {
        // Package full name: Name_Version_Architecture_ResourceId_PublisherId (appmodel.h, PACKAGE_ID).
        if (fullName.Split('_') is not [var name, var installed, var architecture, _, var publisherId] || !Version.TryParse(installed, out var current))
            throw new InvalidDataException("The process package identity is not a package full name.");
        using (var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
        {
            var manifest = archive.GetEntry("AppxManifest.xml");
            if (manifest is null || manifest.Length > 4 * 1024 * 1024)
                throw new InvalidDataException("The update is not an MSIX package; bundles and portable zips cannot update a packaged installation.");
            if (archive.GetEntry("AppxSignature.p7x") is null)
                throw new InvalidDataException("The MSIX update is not signed.");
            using var stream = manifest.Open();
            var next = WindowsUpdatePlanner.ReadIdentity(stream);
            if (next.Name != name)
                throw new InvalidDataException($"The MSIX package name {next.Name} differs from the installed {name}.");
            if (WindowsUpdatePlanner.PublisherId(next.Publisher) != publisherId)
                throw new InvalidDataException("The MSIX publisher differs from the installed package publisher.");
            if (!next.Architecture.Equals(architecture, StringComparison.OrdinalIgnoreCase) && !next.Architecture.Equals("neutral", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The MSIX architecture {next.Architecture} differs from the installed {architecture}.");
            if (next.Version <= current)
                throw new InvalidDataException($"The MSIX version {next.Version} is not newer than the installed {current}.");
        }
        token.ThrowIfCancellationRequested();
        // Packaged desktop apps get new files under AppData redirected into a private location that App Installer
        // cannot see; the package's own Packages\<family>\TempState folder is the real, user-owned path.
        var folder = Path.Combine(host.LocalAppData, "Packages", name + "_" + publisherId, "TempState");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, $"{name}-{update.Release.Version}-{Guid.NewGuid():N}.msix");
        package.Position = 0;
        try
        {
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                package.CopyTo(output);
            using (var copy = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
                if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(copy), Convert.FromHexString(update.Release.Sha256)))
                    throw new InvalidDataException("The MSIX copy does not match the verified checksum.");
            File.SetAttributes(target, FileAttributes.ReadOnly);
        }
        catch
        {
            if (File.Exists(target))
            {
                File.SetAttributes(target, FileAttributes.Normal);
                File.Delete(target);
            }
            throw;
        }
        return new UpdateInstallation(host, UpdateApplyMethod.AppInstaller, update.Release.Version, target);
    }
}
