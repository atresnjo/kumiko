using System.Collections.Immutable;
using System.Diagnostics;
using System.Formats.Tar;
using System.Text;

namespace DiffViewer.Sources;

/// <summary>A folder on this machine, or with a Host, on another machine reached with ssh.</summary>
public readonly record struct Place(string Path, string? Host = null)
{
    /// <summary>How the palette and the recent list name it: the path, or <c>host:path</c>.</summary>
    public string Id => Host is null ? Path : Remote.Format(Host, Path);
}

/// <summary>Names of remote checkouts and quoting for the remote shell.</summary>
public static class Remote
{
    /// <summary>
    /// Recognises <c>[user@]host:/path</c>, <c>[user@]host:~/path</c> and <c>ssh://[user@]host[:port]/path</c>
    /// (<c>/~/path</c> is the remote home). An ssh:// host keeps its scheme, user and port, which ssh accepts as a
    /// destination. A path must follow the colon, so revisions such as <c>origin:main</c> are never taken for a host.
    /// </summary>
    public static bool TryParse(string text, out string host, out string path)
    {
        host = path = "";
        if (text.StartsWith("ssh://", StringComparison.Ordinal))
        {
            var slash = text.IndexOf('/', "ssh://".Length);
            if (slash <= "ssh://".Length)
                return false;
            host = text[..slash];
            path = text[slash..];
            if (path.StartsWith("/~", StringComparison.Ordinal) && (path.Length == 2 || path[2] == '/'))
                path = path[1..];
            return true;
        }
        var colon = text.IndexOf(':');
        // One letter before the colon is a Windows drive, not a host.
        if (colon <= 1)
            return false;
        var name = text[..colon];
        var rest = text[(colon + 1)..];
        if (!(rest.StartsWith('/') || rest == "~" || rest.StartsWith("~/", StringComparison.Ordinal)) || name.StartsWith('-') ||
            !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or '@'))
            return false;
        host = name;
        path = rest;
        return true;
    }

    /// <summary>The inverse of <see cref="TryParse"/>.</summary>
    public static string Format(string host, string path) => host.StartsWith("ssh://", StringComparison.Ordinal)
        ? host + (path.StartsWith('/') ? path : "/" + path) : host + ":" + path;

    /// <summary>The host as people name it, without an ssh:// scheme.</summary>
    public static string Name(string host) => host.StartsWith("ssh://", StringComparison.Ordinal) ? host["ssh://".Length..] : host;

    /// <summary>One POSIX shell word: single quotes, with each quote closed, escaped and reopened.</summary>
    public static string Quote(string text) => "'" + text.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    /// <summary>A quoted path where a leading <c>~</c> is the remote home.</summary>
    public static string QuotePath(string path) => path == "~" ? "\"$HOME\""
        : path.StartsWith("~/", StringComparison.Ordinal) ? "\"$HOME\"/" + Quote(path[2..]) : Quote(path);
}

/// <summary>
/// Runs shell scripts on other machines with the ssh client: no prompts (keys or an agent must authenticate), no
/// terminal, and where the platform allows a shared connection kept for a minute, so a load's few Git calls open
/// one connection. DIFF_SSH names another client.
/// </summary>
public static class Ssh
{
    static readonly string Found = Find();
    static readonly string? Multiplex = SocketFolder();

    public static string Executable => Environment.GetEnvironmentVariable("DIFF_SSH") is { Length: > 0 } custom ? custom : Found;

    static string Find()
    {
        var name = OperatingSystem.IsWindows() ? "ssh.exe" : "ssh";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (directory.Length > 0 && File.Exists(Path.Combine(directory, name)))
                return Path.Combine(directory, name);
        string[] fallbacks = OperatingSystem.IsWindows() ? [@"C:\Windows\System32\OpenSSH\ssh.exe"] : ["/usr/bin/ssh", "/usr/local/bin/ssh"];
        return fallbacks.FirstOrDefault(File.Exists) ?? name;
    }

    /// <summary>
    /// A private folder for control sockets: the per-user runtime folder, else a folder in /tmp named for the user
    /// (macOS's per-user temporary folder is too deep for a socket path). It is used only when its mode is 0700, so a
    /// folder someone else made is never trusted. None on Windows, whose ssh has no multiplexing.
    /// </summary>
    static string? SocketFolder()
    {
        if (OperatingSystem.IsWindows())
            return null;
        const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        // %C is 40 characters and ssh adds a 17-character suffix while it binds; socket paths stop at 103 bytes.
        string[] candidates = [
            Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtime ? Path.Combine(runtime, "kumiko-diff-ssh") : "",
            "/tmp/kumiko-diff-" + Environment.UserName];
        foreach (var folder in candidates.Where(c => c.Length is > 0 and <= 45))
        {
            try
            {
                Directory.CreateDirectory(folder, Private);
                if (File.GetUnixFileMode(folder) == Private)
                    return folder;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
        return null;
    }

    /// <summary>Starts <paramref name="script"/> with /bin/sh on the host; ssh hands it to the login shell as one word.</summary>
    public static ProcessStartInfo Start(string host, string script)
    {
        var start = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        string[] options = ["-T", "-o", "BatchMode=yes", "-o", "ConnectTimeout=10", "-o", "ServerAliveInterval=15"];
        foreach (var option in options)
            start.ArgumentList.Add(option);
        if (Multiplex is { } folder)
            foreach (var option in new[] { "-o", "ControlMaster=auto", "-o", "ControlPath=" + Path.Combine(folder, "%C"), "-o", "ControlPersist=60" })
                start.ArgumentList.Add(option);
        start.ArgumentList.Add("--");
        start.ArgumentList.Add(host);
        start.ArgumentList.Add("sh -c " + Remote.Quote(script));
        return start;
    }

    /// <summary>
    /// Standard error once the process has exited. A connection kept open in the background may hold the first
    /// client's standard error, so it is not awaited past a moment.
    /// </summary>
    internal static async Task<string> Errors(Task<string> error)
    {
        try
        {
            return await error.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return "";
        }
    }

    /// <summary>Repositories listed at most, so a host full of checkouts keeps the palette quick.</summary>
    public const int RepositoryLimit = 500;

    /// <summary>
    /// Git checkouts on a host, found in one session: below the home folder (four levels deep) and below /srv,
    /// /var/www and /opt (three), never inside hidden or node_modules folders. Paths below the home start with ~.
    /// </summary>
    public static async Task<ImmutableArray<string>> Repositories(string host, CancellationToken token)
    {
        const string script = "printf '%s\\n' \"$HOME\"; for r in \"$HOME\" /srv /var/www /opt; do [ -d \"$r\" ] || continue; " +
            "d=3; [ \"$r\" = \"$HOME\" ] && d=4; find \"$r\" -maxdepth $d \\( -name node_modules -o \\( -name '.?*' ! -name .git \\) \\) -prune " +
            "-o -name .git -print -prune 2>/dev/null; done; exit 0";
        using var process = Process.Start(Start(host, script)) ?? throw new IOException("Could not start ssh.");
        process.StandardInput.Close();
        using var cancel = token.Register(() => Git.Kill(process));
        var error = process.StandardError.ReadToEndAsync(token);
        var output = await process.StandardOutput.ReadToEndAsync(token).ConfigureAwait(false);
        await process.WaitForExitAsync(token).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            var message = (await Errors(error).ConfigureAwait(false)).Trim();
            throw new IOException(message.Length > 0 ? message : $"Listing repositories over ssh exited with {process.ExitCode}.");
        }
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
            return [];
        var home = lines[0].TrimEnd('/');
        return [.. lines.Skip(1).Where(l => l.EndsWith("/.git", StringComparison.Ordinal)).Select(l => l[..^"/.git".Length])
            .Select(path => path == home ? "~" : home.Length > 0 && path.StartsWith(home + "/", StringComparison.Ordinal) ? "~" + path[home.Length..] : path)
            .Distinct(StringComparer.Ordinal).OrderBy(path => path[0] == '~' ? 0 : 1).ThenBy(path => path, StringComparer.Ordinal).Take(RepositoryLimit)];
    }

    /// <summary>
    /// Working-tree files of a remote checkout, read in one tar stream; files that are missing, folders and other
    /// non-files are left out. A symbolic link reads as its target's path, as Git stores it.
    /// </summary>
    public static async Task<Dictionary<string, byte[]>> ReadFiles(Place root, IReadOnlyCollection<string> paths, CancellationToken token)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (paths.Count == 0)
            return files;
        var script = "cd " + Remote.QuotePath(root.Path) + " && exec tar -c -f - --no-recursion --null -T -";
        using var process = Process.Start(Start(root.Host!, script)) ?? throw new IOException("Could not start ssh.");
        using var cancel = token.Register(() => Git.Kill(process));
        var error = process.StandardError.ReadToEndAsync(token);
        var names = Task.Run(async () =>
        {
            try
            {
                var input = process.StandardInput.BaseStream;
                foreach (var path in paths)
                {
                    await input.WriteAsync(Encoding.UTF8.GetBytes(path), token).ConfigureAwait(false);
                    await input.WriteAsync(new byte[1], token).ConfigureAwait(false);
                }
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // tar ended early; its exit code and message tell why.
            }
        }, token);
        await using (var reader = new TarReader(process.StandardOutput.BaseStream, leaveOpen: true))
        {
            while (await reader.GetNextEntryAsync(copyData: false, token).ConfigureAwait(false) is { } entry)
            {
                if (entry.EntryType is TarEntryType.SymbolicLink)
                    files[entry.Name] = Encoding.UTF8.GetBytes(entry.LinkName);
                else if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile && entry.DataStream is { } data)
                {
                    var bytes = new byte[entry.Length];
                    await data.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
                    files[entry.Name] = bytes;
                }
            }
        }
        await names.ConfigureAwait(false);
        await process.WaitForExitAsync(token).ConfigureAwait(false);
        // tar reports files that vanished since Git listed them and goes on; a failed connection (255) or nothing
        // read at all is an error.
        if (process.ExitCode == 255 || process.ExitCode != 0 && files.Count == 0)
        {
            var message = (await Errors(error).ConfigureAwait(false)).Trim();
            throw new IOException(message.Length > 0 ? message : $"Reading files over ssh exited with {process.ExitCode}.");
        }
        return files;
    }
}
