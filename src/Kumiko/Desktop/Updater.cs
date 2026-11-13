using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Kumiko.Platform;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Desktop;

public sealed record UpdateOptions(string Repository, string Asset, string Name, TimeSpan? FirstCheck = null, TimeSpan? Interval = null);
public enum UpdatePhase
{
    Off,
    Idle,
    Busy,
    Ready,
    Installed
}

public sealed class Updater : IDisposable
{
    readonly App app;
    readonly UpdateOptions options;
    readonly HttpClient http = new()
    {
        Timeout = TimeSpan.FromMinutes(15)
    };
    readonly CancellationTokenSource cancellation = new();
    readonly SemaphoreSlim gate = new(1, 1);
    readonly string bundle = "", current = "", cache = "";
    IDisposable? timer;
    string? staged;
    int phase;
    public UpdatePhase Phase => (UpdatePhase)Volatile.Read(ref phase);
    public string? ReadyVersion
    {
        get; private set;
    }

    public event Action? Changed;
    public event Action<Exception>? Failed;
    public Updater(App app, UpdateOptions options)
    {
        this.app = app;
        this.options = options;
        if (options.Repository.Split('/') is not [var owner, var repo] || owner.Length == 0 || repo.Length == 0 || options.Asset != Path.GetFileName(options.Asset))
            throw new ArgumentException("Expected owner/repository and a plain asset filename.");
        if (!OperatingSystem.IsMacOS())
            return;
        using var pool = new AutoreleasePool();
        var main = O.O(O.Class("NSBundle"), "mainBundle");
        bundle = O.Text(O.O(main, "bundlePath"));
        current = O.Text(O.O(O.O(main, "infoDictionary"), "objectForKey:", O.TempString("CFBundleShortVersionString")));
        if (!bundle.EndsWith(".app", StringComparison.Ordinal) || bundle.StartsWith("/Volumes/", StringComparison.Ordinal) || bundle.Contains("/AppTranslocation/", StringComparison.Ordinal) || !Version.TryParse(current, out _))
            return;
        cache = Path.Combine(Paths.Get(DirectoryKind.Cache), options.Name, "updates");
        phase = (int)UpdatePhase.Idle;
        timer = app.Schedule(options.FirstCheck ?? TimeSpan.FromSeconds(20), () => _ = CheckAsync(), options.Interval ?? TimeSpan.FromHours(6));
    }

    public async Task CheckAsync()
    {
        if (Phase is not UpdatePhase.Idle || !await gate.WaitAsync(0))
            return;
        Volatile.Write(ref phase, (int)UpdatePhase.Busy);
        string? working = null;
        try
        {
            var token = cancellation.Token;
            var feed = $"https://github.com/{options.Repository}/releases/latest/download";
            using var manifest = JsonDocument.Parse(await http.GetStringAsync(feed + "/manifest.json", token));
            var root = manifest.RootElement;
            var version = root.GetProperty("version").GetString() ?? "";
            if (!Version.TryParse(version, out var next) || version.Any(c => !char.IsAsciiDigit(c) && c != '.') || next <= Version.Parse(current))
                return;
            var digest = root.GetProperty("files").GetProperty(options.Asset).GetProperty("sha256").GetString() ?? "";
            if (digest.Length != 64 || !digest.All(Uri.IsHexDigit))
                throw new InvalidDataException("Invalid release checksum.");
            var team = await Team(bundle, token);
            working = Path.Combine(cache, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(working);
            var zip = Path.Combine(working, options.Asset);
            var tagged = $"https://github.com/{options.Repository}/releases/download/v{version}/{Uri.EscapeDataString(options.Asset)}";
            using (var response = await http.GetAsync(tagged, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                await using var output = File.Create(zip);
                await response.Content.CopyToAsync(output, token);
            }

            await using (var input = File.OpenRead(zip))
                if (!CryptographicOperations.FixedTimeEquals(await SHA256.HashDataAsync(input, token), Convert.FromHexString(digest)))
                    throw new InvalidDataException("Update checksum mismatch.");
            var unpacked = Path.Combine(working, "unpacked");
            await Run("/usr/bin/ditto", token, "-x", "-k", zip, unpacked);
            var candidate = Path.Combine(unpacked, Path.GetFileName(bundle));
            await Run("/usr/bin/codesign", token, "--verify", "--deep", "--strict", candidate);
            if (await Team(candidate, token) != team)
                throw new InvalidDataException("Update signer does not match the installed app.");
            var actual = (await Run(
                "/usr/bin/plutil",
                token,
                "-extract",
                "CFBundleShortVersionString",
                "raw",
                "-o",
                "-",
                Path.Combine(candidate, "Contents", "Info.plist")
            )).Trim();
            if (actual != version)
                throw new InvalidDataException("Update bundle version differs from the manifest.");
            File.Delete(zip);
            staged = candidate;
            ReadyVersion = version;
            working = null;
            Volatile.Write(ref phase, (int)UpdatePhase.Ready);
            app.Dispatch(() => Changed?.Invoke());
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            app.Dispatch(() => Failed?.Invoke(ex));
        }
        finally
        {
            if (working is not null && Directory.Exists(working))
                Directory.Delete(working, true);
            if (Phase == UpdatePhase.Busy)
                Volatile.Write(ref phase, (int)UpdatePhase.Idle);
            gate.Release();
        }
    }

    public async Task InstallAsync()
    {
        await gate.WaitAsync(cancellation.Token);
        try
        {
            if (Phase != UpdatePhase.Ready || staged is null)
                throw new InvalidOperationException("No verified update is ready.");
            string parent = Path.GetDirectoryName(bundle)!, name = Path.GetFileName(bundle), suffix = Guid.NewGuid().ToString("N");
            var fresh = Path.Combine(parent, "." + name + ".new-" + suffix);
            var old = Path.Combine(parent, "." + name + ".old-" + suffix);
            await Run("/usr/bin/ditto", cancellation.Token, staged, fresh);
            Directory.Move(bundle, old);
            try
            {
                Directory.Move(fresh, bundle);
            }
            catch
            {
                Directory.Move(old, bundle);
                throw;
            }

            Directory.Delete(old, true);
            Volatile.Write(ref phase, (int)UpdatePhase.Installed);
            app.Dispatch(() => Changed?.Invoke());
        }
        finally
        {
            gate.Release();
        }
    }

    public void Restart()
    {
        if (Phase != UpdatePhase.Installed)
            throw new InvalidOperationException("Install the verified update before restarting.");
        Process.Start(new ProcessStartInfo("/usr/bin/open") { ArgumentList = { "-n", bundle } });
        app.Quit();
    }

    static async Task<string> Team(string path, CancellationToken token)
    {
        var report = await Run("/usr/bin/codesign", token, "-dv", "--verbose=2", path);
        var team = report.Split('\n').FirstOrDefault(s => s.StartsWith("TeamIdentifier=", StringComparison.Ordinal))?[15..].Trim();
        return team is { Length: > 0 } && team != "not set" ? team : throw new InvalidDataException("The application must have a team signature.");
    }

    static async Task<string> Run(string command, CancellationToken token, params string[] arguments)
    {
        var start = new ProcessStartInfo(command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not start " + command);
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        var output = await stdout + await stderr;
        if (process.ExitCode != 0)
            throw new IOException(command + ": " + output);
        return output;
    }

    public void Dispose()
    {
        timer?.Dispose();
        cancellation.Cancel();
        http.Dispose();
    }
}
