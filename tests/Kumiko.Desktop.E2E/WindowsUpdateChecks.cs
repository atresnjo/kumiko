using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kumiko.Desktop;

// Windows update planning runs on every host through an injected host seam. Nothing here
// installs a package, opens App Installer, starts PowerShell or quits the test process.
static class WindowsUpdateChecks
{
    const string Publisher = "CN=Contoso Ltd, O=Contoso Ltd, C=US";

    sealed class Recorder
    {
        public readonly List<string> Events = new();
        public readonly List<ProcessStartInfo> Started = new();
        public Action? Quit;
    }

    public static void Run(string output, Action<bool, string> check)
    {
        var report = Path.Combine(output, "windows-update");
        Directory.CreateDirectory(report);
        var root = Directory.CreateTempSubdirectory("kumiko-windows-update-").FullName;
        try
        {
            Identity(check);
            Portable(root, report, check);
            PortableRefusals(root, check);
            Packaged(root, check);
            if (!OperatingSystem.IsWindows())
                check(Throws<PlatformNotSupportedException>(() => UpdateFeed.InstallAsync(null!, null!).GetAwaiter().GetResult()), "non-Windows hosts refuse to apply a Windows update");
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }

    static void Identity(Action<bool, string> check)
    {
        check(WindowsUpdatePlanner.PublisherId("CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US") == "8wekyb3d8bbwe", "MSIX publisher ids match the Windows hash for a known publisher");
        check(WindowsUpdateScript.CommandLine(["a b", "c\"d", "e\\", "", "f\\ g\\"]) == "\"a b\" \"c\\\"d\" e\\ \"\" \"f\\ g\\\\\"", "relaunch arguments use Windows command-line quoting");
        check(WindowsUpdateScript.Literal("it's \u2019x\u2019") == "'it''s \u2019\u2019x\u2019\u2019'", "PowerShell literals double ASCII and typographic single quotes");
    }

    static WindowsUpdateHost Host(string exe, string localAppData, Recorder recorder, string? fullName = null, IReadOnlyList<string>? protectedRoots = null) => new(
        exe,
        4242,
        fullName,
        localAppData,
        protectedRoots ?? [],
        @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
        Path.GetDirectoryName(exe)!,
        ["--open", "report one.txt"],
        path => recorder.Events.Add("open " + path),
        start =>
        {
            recorder.Events.Add("start");
            recorder.Started.Add(start);
        },
        () => recorder.Events.Add("quit"),
        action => recorder.Quit += action);

    static StagedUpdate Stage(string directory, byte[] package, string version = "1.1.0", UpdateInstallStrategy strategy = UpdateInstallStrategy.ExternalWindowsInstaller)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".package");
        File.WriteAllBytes(path, package);
        var release = new UpdateRelease(version, "win-x64", new Uri("https://updates.example.test/app.zip"), Convert.ToHexStringLower(SHA256.HashData(package)));
        return new(release, path, strategy);
    }

    static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
            foreach (var (name, text) in entries)
            {
                using var stream = archive.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes(text));
            }
        return memory.ToArray();
    }

    static string Install(string root, string folder)
    {
        var directory = Path.Combine(root, folder);
        Directory.CreateDirectory(directory);
        var exe = Path.Combine(directory, "Crm.exe");
        File.WriteAllText(exe, "old");
        return exe;
    }

    static bool Throws<T>(Action action, string? message = null) where T : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (Exception error) when (error is T || error.InnerException is T)
        {
            var actual = error as T ?? (T)error.InnerException!;
            return message is null || actual.Message.Contains(message, StringComparison.Ordinal);
        }
    }

    static UpdateInstallation Prepare(WindowsUpdateHost host, StagedUpdate update) => WindowsUpdatePlanner.PrepareAsync(host, update, default).GetAwaiter().GetResult();

    static void Portable(string root, string report, Action<bool, string> check)
    {
        var apps = Path.Combine(root, "portable", "apps");
        var exe = Install(apps, "Crm's Tools");
        var target = Path.GetDirectoryName(exe)!;
        var recorder = new Recorder();
        var host = Host(exe, Path.Combine(root, "portable", "local"), recorder);
        var update = Stage(Path.Combine(root, "portable", "staged"), Zip(("Crm/Crm.exe", "new"), ("Crm/data/readme.txt", "notes")));
        var installation = Prepare(host, update);
        check(installation.Method == UpdateApplyMethod.PortableSwap && installation.Version == "1.1.0", "an unpackaged process plans a portable directory swap");
        check(Path.GetDirectoryName(installation.Path) == apps, "the verified zip is extracted beside the installation, on the same volume");
        check(File.ReadAllText(Path.Combine(installation.Path, "Crm.exe")) == "new" && File.ReadAllText(Path.Combine(installation.Path, "data", "readme.txt")) == "notes", "the single top-level package folder becomes the new installation");
        check(File.ReadAllText(exe) == "old" && recorder.Events.Count == 0, "preparing never touches the running installation or starts anything");
        check(Throws<InvalidOperationException>(() => Prepare(host, update), "already pending"), "a second update for the same installation is refused");
        installation.Restart();
        check(recorder.Events.SequenceEqual(["quit"]), "restart asks the application to quit before any helper starts");
        recorder.Quit!();
        var helper = recorder.Started.Single();
        check(helper.FileName == host.PowerShell && !helper.UseShellExecute && helper.CreateNoWindow, "the helper is the system PowerShell, hidden and without the shell");
        check(helper.ArgumentList.Contains("-NoProfile") && helper.ArgumentList[^2] == "-EncodedCommand" && helper.WorkingDirectory == apps, "the helper runs a private encoded command outside the installation directory");
        var script = Encoding.Unicode.GetString(Convert.FromBase64String(helper.ArgumentList[^1]));
        File.WriteAllText(Path.Combine(report, "portable-helper.ps1"), script);
        check(script.Contains("$target = " + WindowsUpdateScript.Literal(target), StringComparison.Ordinal) && target.Contains('\'') && script.Contains("Crm''s Tools", StringComparison.Ordinal), "paths reach the helper as escaped literals");
        check(script.Contains("Get-Process -Id 4242", StringComparison.Ordinal) && script.Contains("$old.Path -eq $exe", StringComparison.Ordinal), "the helper waits only for the original process identity");
        check(script.Contains("Rename-Item -LiteralPath $backup -NewName " + WindowsUpdateScript.Literal("Crm's Tools"), StringComparison.Ordinal), "the helper restores the previous installation when the swap fails");
        check(!script.Contains("Move-Item", StringComparison.Ordinal) && !script.Contains("-Destination", StringComparison.Ordinal), "the swap uses only literal same-folder renames");
        check(script.Contains("$relaunch = $true", StringComparison.Ordinal) && script.Contains(WindowsUpdateScript.Literal("--open \"report one.txt\""), StringComparison.Ordinal), "restart relaunches with the original arguments");

        var quietExe = Install(apps, "Quiet");
        var quiet = new Recorder();
        var quietHost = Host(quietExe, Path.Combine(root, "portable", "local"), quiet);
        Prepare(quietHost, Stage(Path.Combine(root, "portable", "staged"), Zip(("Crm.exe", "flat"))));
        quiet.Quit!();
        var quietScript = Encoding.Unicode.GetString(Convert.FromBase64String(quiet.Started.Single().ArgumentList[^1]));
        check(quietScript.Contains("$relaunch = $false", StringComparison.Ordinal), "a normal quit applies the update without relaunching");
        using var plan = File.Create(Path.Combine(report, "plan.json"));
        using var json = new Utf8JsonWriter(plan, new()
        {
            Indented = true
        });
        json.WriteStartObject();
        json.WriteString("method", installation.Method.ToString());
        json.WriteString("version", installation.Version);
        json.WriteString("extracted", Path.GetRelativePath(root, installation.Path));
        json.WriteString("helper", Path.GetFileName(helper.FileName));
        json.WriteEndObject();
    }

    static void PortableRefusals(string root, Action<bool, string> check)
    {
        var apps = Path.Combine(root, "refusals");
        var local = Path.Combine(apps, "local");
        var staged = Path.Combine(apps, "staged");
        var valid = Zip(("Crm/Crm.exe", "new"));
        int Leftovers(string parent) => Directory.EnumerateDirectories(parent, ".*.update-*").Count();

        var tampered = Stage(staged, valid);
        File.AppendAllText(tampered.Path, "x");
        var exe = Install(apps, "Tampered");
        var recorder = new Recorder();
        check(Throws<InvalidDataException>(() => Prepare(Host(exe, local, recorder), tampered), "checksum"), "a staged package changed after verification is refused");
        check(Leftovers(apps) == 0 && recorder.Quit is null, "a refused package leaves no extraction and arms nothing");

        exe = Install(Path.Combine(apps, "Program Files"), "Crm");
        check(Throws<UnauthorizedAccessException>(() => Prepare(Host(exe, local, new(), protectedRoots: [Path.Combine(apps, "Program Files")]), Stage(staged, valid)), "Program Files"), "installations under protected roots are refused with a clear error");

        if (!OperatingSystem.IsWindows())
        {
            var locked = Path.Combine(apps, "locked");
            exe = Install(locked, "Crm");
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            try
            {
                check(Throws<UnauthorizedAccessException>(() => Prepare(Host(exe, local, new()), Stage(staged, valid)), "not writable"), "a folder the user cannot write is refused before extraction");
                check(!Directory.EnumerateFileSystemEntries(locked).Skip(1).Any(), "the unwritable folder is left untouched");
            }
            finally
            {
                File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        exe = Install(apps, "Traversal");
        check(Throws<InvalidDataException>(() => Prepare(Host(exe, local, new()), Stage(staged, Zip(("Crm/Crm.exe", "new"), ("Crm/../../escaped.txt", "x"))))), "archive entries escaping the extraction folder are refused");
        check(!File.Exists(Path.Combine(apps, "escaped.txt")) && Leftovers(apps) == 0, "a traversal attempt writes nothing outside and cleans its extraction");

        exe = Install(apps, "Missing");
        check(Throws<InvalidDataException>(() => Prepare(Host(exe, local, new()), Stage(staged, Zip(("Crm/Other.exe", "new")))), "Crm.exe"), "a package without the running executable is refused");

        exe = Install(apps, "Msix");
        check(Throws<InvalidDataException>(() => Prepare(Host(exe, local, new()), Stage(staged, Msix("Contoso.Crm", Publisher, "1.1.0.0"))), "MSIX"), "an MSIX package cannot replace a portable installation");

        exe = Install(apps, "Strategy");
        check(Throws<InvalidOperationException>(() => Prepare(Host(exe, local, new()), Stage(staged, valid, strategy: UpdateInstallStrategy.MacBundleInstaller))), "only staged Windows updates are applied");

        var dotnet = Install(apps, "dotnet");
        var host = Path.Combine(Path.GetDirectoryName(dotnet)!, "dotnet.exe");
        File.Move(dotnet, host);
        check(Throws<InvalidOperationException>(() => Prepare(Host(host, local, new()), Stage(staged, valid)), "dotnet"), "a process hosted by dotnet never swaps the runtime folder");
    }

    static byte[] Msix(string name, string publisher, string version, bool signed = true, string architecture = "x64")
    {
        var manifest = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
              <Identity Name="{name}" Publisher="{publisher}" Version="{version}" ProcessorArchitecture="{architecture}" />
            </Package>
            """;
        return signed ? Zip(("AppxManifest.xml", manifest), ("Crm.exe", "new"), ("AppxSignature.p7x", "signature")) : Zip(("AppxManifest.xml", manifest), ("Crm.exe", "new"));
    }

    static void Packaged(string root, Action<bool, string> check)
    {
        var folder = Path.Combine(root, "packaged");
        var exe = Install(folder, "WindowsApps");
        var local = Path.Combine(folder, "local");
        var staged = Path.Combine(folder, "staged");
        var fullName = "Contoso.Crm_1.0.0.0_x64__" + WindowsUpdatePlanner.PublisherId(Publisher);
        var recorder = new Recorder();
        var host = Host(exe, local, recorder, fullName);
        var package = Msix("Contoso.Crm", Publisher, "1.1.0.0");
        var installation = Prepare(host, Stage(staged, package));
        var family = "Contoso.Crm_" + WindowsUpdatePlanner.PublisherId(Publisher);
        check(installation.Method == UpdateApplyMethod.AppInstaller, "a packaged process hands its update to App Installer");
        check(Path.GetDirectoryName(installation.Path) == Path.Combine(local, "Packages", family, "TempState") && installation.Path.EndsWith(".msix", StringComparison.Ordinal), "the MSIX is copied to the package's own non-virtualized temp folder");
        check(SHA256.HashData(File.ReadAllBytes(installation.Path)).SequenceEqual(SHA256.HashData(package)), "the handed-off MSIX is byte-identical to the verified package");
        check(recorder.Events.Count == 0 && recorder.Quit is null, "preparing an MSIX opens nothing and arms no helper");
        installation.Restart();
        check(recorder.Events.SequenceEqual(["open " + installation.Path, "quit"]), "restart opens App Installer and then quits so the package is not in use");

        check(Throws<InvalidDataException>(() => Prepare(Host(exe, local, new(), fullName), Stage(staged, Msix("Contoso.Crm", "CN=Mallory", "1.1.0.0"))), "publisher"), "an MSIX from another publisher is refused");
        check(Throws<InvalidDataException>(() => Prepare(Host(exe, local, new(), fullName), Stage(staged, Msix("Contoso.Other", Publisher, "1.1.0.0"))), "name"), "an MSIX for another package name is refused");
        check(Throws<InvalidDataException>(() => Prepare(Host(exe, local, new(), fullName), Stage(staged, Msix("Contoso.Crm", Publisher, "1.0.0.0"))), "newer"), "an MSIX that is not newer than the installed package is refused");
        check(Throws<InvalidDataException>(() => Prepare(Host(exe, local, new(), fullName), Stage(staged, Msix("Contoso.Crm", Publisher, "1.1.0.0", architecture: "arm64"))), "architecture"), "an MSIX for another architecture is refused");
        check(Throws<InvalidDataException>(() => Prepare(Host(exe, local, new(), fullName), Stage(staged, Msix("Contoso.Crm", Publisher, "1.1.0.0", signed: false))), "signed"), "an unsigned MSIX is refused");
        check(Throws<InvalidDataException>(() => Prepare(Host(exe, local, new(), fullName), Stage(staged, Zip(("Crm/Crm.exe", "new"))))), "a portable zip cannot update a packaged installation");
    }
}
