using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using Flint.Git;

namespace Flint.Services;

/// <summary>Lists gh logins for github.com without switching gh's active account. Tokens never leave the service.</summary>
public interface IGitHubAccounts
{
    Task<ImmutableArray<GhAccount>> List(CancellationToken token);
}
public enum LaunchKind
{
    Open, Reveal, Editor, Terminal, AddAccount
}
/// <summary>An operating-system action requested by the user. Paths are absolute.</summary>
public sealed record LaunchRequest(LaunchKind Kind, ImmutableArray<string> Paths);
/// <summary>Opens files, folders, the editor or a terminal through the platform shell, outside the Git service.</summary>
public interface ILauncher
{
    Task Launch(LaunchRequest request, CancellationToken token);
}
public sealed class GitHubAccounts(IProcessRunner runner) : IGitHubAccounts
{
    public async Task<ImmutableArray<GhAccount>> List(CancellationToken token)
    {
        try
        {
            var directory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var json = await runner.Run("gh", directory, ["auth", "status", "--hostname", "github.com", "--json", "hosts"], token).ConfigureAwait(false);
            return Accounts(json);
        }
        catch (Exception error) when (error is IOException or JsonException or System.ComponentModel.Win32Exception)
        {
            // gh missing, too old for --json, or not logged in.
            return [];
        }
    }
    public static ImmutableArray<GhAccount> Accounts(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("hosts", out var hosts) || !hosts.TryGetProperty("github.com", out var list))
            return [];
        var accounts = ImmutableArray.CreateBuilder<GhAccount>();
        foreach (var account in list.EnumerateArray())
            if (account.TryGetProperty("state", out var state) && state.GetString() == "success" && account.TryGetProperty("login", out var login))
                accounts.Add(new(login.GetString() ?? "", account.TryGetProperty("active", out var active) && active.GetBoolean(),
                    account.TryGetProperty("scopes", out var scopes) ? scopes.GetString() ?? "" : ""));
        return accounts.ToImmutable();
    }
}
public sealed class SystemLauncher : ILauncher
{
    public Task Launch(LaunchRequest request, CancellationToken token)
    {
        var paths = request.Paths.Take(20).ToArray();
        if (OperatingSystem.IsMacOS() && request.Kind == LaunchKind.AddAccount)
            Start("osascript", ["-e", "tell application \"Terminal\" to do script \"gh auth login --hostname github.com\"",
                "-e", "tell application \"Terminal\" to activate"]);
        else if (OperatingSystem.IsMacOS())
            Start("open", request.Kind switch
            {
                LaunchKind.Reveal => ["-R", .. paths],
                LaunchKind.Editor => ["-a", "Zed", .. paths],
                LaunchKind.Terminal => ["-a", "Terminal", .. paths],
                _ => paths
            });
        else if (OperatingSystem.IsWindows())
            foreach (var path in request.Kind == LaunchKind.AddAccount ? [""] : paths)
                Start(request.Kind == LaunchKind.Terminal ? "cmd.exe" : "explorer.exe", request.Kind switch
                {
                    LaunchKind.Reveal => ["/select," + path],
                    LaunchKind.Terminal => ["/c", "start", "cmd.exe", "/k", "cd", "/d", path],
                    LaunchKind.Editor => [path],
                    _ => [path]
                });
        else
            foreach (var path in paths)
                Start(request.Kind == LaunchKind.Terminal ? "x-terminal-emulator" : "xdg-open", request.Kind switch
                {
                    LaunchKind.Reveal => [Path.GetDirectoryName(path) ?? path],
                    LaunchKind.Terminal => ["--working-directory=" + path],
                    _ => [path]
                });
        return Task.CompletedTask;
    }
    static void Start(string executable, string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start);
    }
}
