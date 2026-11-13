using System.Security.Cryptography;
using System.Text.Json;

namespace Kumiko.Desktop;

public sealed record UpdateRelease(string Version, string Runtime, Uri Download, string Sha256);
public enum UpdateInstallStrategy
{
    MacBundleInstaller,
    ExternalWindowsInstaller,
    LinuxPackageManager
}
public sealed record StagedUpdate(UpdateRelease Release, string Path, UpdateInstallStrategy Strategy);

/// <summary>Cross-platform signed HTTPS catalog and verified package staging. Reading and staging never install or launch a package.
/// On Windows, <see cref="InstallAsync"/> applies a staged update; Linux delegates installation to its package manager.
/// The existing Updater owns macOS bundle signer verification/replacement.</summary>
public sealed class UpdateFeed
{
    readonly HttpClient http;
    readonly Uri address;
    readonly string publicKey;
    /// <summary>The caller owns HttpClient. Envelope is {payload: base64 JSON, signature: RSA-PSS/SHA256 base64}.</summary>
    public UpdateFeed(HttpClient http, Uri address, string publicKeyPem)
    {
        RequireHttps(address);
        this.http = http;
        this.address = address;
        publicKey = publicKeyPem;
    }
    static void RequireHttps(Uri address)
    {
        if (!address.IsAbsoluteUri || address.Scheme != "https" || !string.IsNullOrEmpty(address.UserInfo))
            throw new ArgumentException("Updates require an absolute HTTPS URI without credentials.");
    }
    static void Validate(UpdateRelease release)
    {
        _ = SemanticVersion.Parse(release.Version);
        RequireHttps(release.Download);
        if (release.Runtime.Length is 0 or > 128 || release.Sha256.Length != 64 || !release.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid release target or checksum.");
    }
    public static UpdateRelease? Select(IEnumerable<UpdateRelease> releases, string currentVersion, string runtime, bool allowPrereleases)
    {
        var current = SemanticVersion.Parse(currentVersion);
        return releases.Select(release =>
        {
            Validate(release);
            return (Release: release, Version: SemanticVersion.Parse(release.Version));
        }).Where(r => r.Release.Runtime == runtime && (allowPrereleases || !r.Version.IsPrerelease) && r.Version.CompareTo(current) > 0)
            .OrderByDescending(r => r.Version).Select(r => r.Release).FirstOrDefault();
    }
    async Task<byte[]> Read(Uri uri, int limit, CancellationToken token)
    {
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        RequireHttps(response.RequestMessage?.RequestUri ?? uri);
        if (response.Content.Headers.ContentLength > limit)
            throw new InvalidDataException("Update response exceeds the configured limit.");
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[65536];
        int count;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (output.Length + count > limit)
                throw new InvalidDataException("Update response exceeds the configured limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
    public async Task<IReadOnlyList<UpdateRelease>> ReadAsync(CancellationToken token = default)
    {
        byte[] envelope = await Read(address, 1024 * 1024, token).ConfigureAwait(false);
        using var json = JsonDocument.Parse(envelope, new()
        {
            MaxDepth = 8
        });
        byte[] payload = json.RootElement.GetProperty("payload").GetBytesFromBase64(), signature = json.RootElement.GetProperty("signature").GetBytesFromBase64();
        using var verifier = RSA.Create();
        verifier.ImportFromPem(publicKey);
        if (verifier.KeySize < 2048 || !verifier.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            throw new InvalidDataException("Update catalog signature is invalid.");
        using var catalog = JsonDocument.Parse(payload, new()
        {
            MaxDepth = 8
        });
        if (catalog.RootElement.GetProperty("version").GetInt32() != 1)
            throw new InvalidDataException("Unsupported update catalog schema.");
        var releases = new List<UpdateRelease>();
        foreach (var item in catalog.RootElement.GetProperty("releases").EnumerateArray())
        {
            if (releases.Count >= 4096)
                throw new InvalidDataException("Too many releases in the catalog.");
            var release = new UpdateRelease(item.GetProperty("version").GetString()!, item.GetProperty("runtime").GetString()!,
                new Uri(item.GetProperty("url").GetString()!, UriKind.Absolute), item.GetProperty("sha256").GetString()!);
            Validate(release);
            releases.Add(release);
        }
        return releases.AsReadOnly();
    }
    public async Task<StagedUpdate> StageAsync(UpdateRelease release, string directory, CancellationToken token = default)
    {
        Validate(release);
        byte[] bytes = await Read(release.Download, 256 * 1024 * 1024, token).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(release.Sha256)))
            throw new InvalidDataException("Update package checksum mismatch.");
        token.ThrowIfCancellationRequested();
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".package");
        try
        {
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
                stream.Flush(true);
            }
            return new(release, path, release.Runtime.StartsWith("osx-", StringComparison.Ordinal) ? UpdateInstallStrategy.MacBundleInstaller :
                release.Runtime.StartsWith("win-", StringComparison.Ordinal) ? UpdateInstallStrategy.ExternalWindowsInstaller : UpdateInstallStrategy.LinuxPackageManager);
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    /// <summary>Windows only. Verifies the staged package against its catalog checksum again, then prepares it for this installation:
    /// an MSIX-packaged process gets a signed .msix (same package name and publisher, newer version) for App Installer;
    /// a portable installation gets the zip extracted beside it, swapped in by a helper after the application quits.
    /// Portable installations under Program Files, Windows or any folder the user cannot write are refused.
    /// Call <see cref="UpdateInstallation.Restart"/> on the UI thread to finish.</summary>
    public static Task<UpdateInstallation> InstallAsync(App app, StagedUpdate update, CancellationToken token = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("UpdateFeed.InstallAsync applies Windows updates. Use Updater on macOS and the package manager on Linux.");
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(update);
        return WindowsUpdatePlanner.PrepareAsync(WindowsUpdateHost.Current(app), update, token);
    }
}
