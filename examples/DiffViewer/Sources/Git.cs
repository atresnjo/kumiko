using System.Diagnostics;
using System.Text;

namespace DiffViewer.Sources;

/// <summary>
/// Runs Git with an argument list, without prompts, pagers or optional locks. Locally no shell is involved; on a remote
/// host every argument is quoted for its shell.
/// </summary>
public static class Git
{
    static readonly string[] Fallbacks = ["/opt/homebrew/bin/git", "/usr/local/bin/git", "/usr/bin/git"];

    /// <summary>Git from PATH, or a standard install location when an app launched from Finder has a short PATH.</summary>
    public static string Executable { get; } = Find();

    static string Find()
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (directory.Length == 0)
                continue;
            var candidate = Path.Combine(directory, OperatingSystem.IsWindows() ? "git.exe" : "git");
            if (File.Exists(candidate))
                return candidate;
        }
        return Fallbacks.FirstOrDefault(File.Exists) ?? "git";
    }

    /// <summary>Git in a local folder, or on a remote host through ssh with the same environment.</summary>
    public static ProcessStartInfo Start(Place at, IEnumerable<string> arguments)
    {
        if (at.Host is { } host)
        {
            // A non-interactive login may lack Homebrew's folders in PATH.
            var script = "PATH=\"$PATH:/usr/local/bin:/opt/homebrew/bin\"; export PATH GIT_TERMINAL_PROMPT=0 GIT_PAGER=cat GIT_OPTIONAL_LOCKS=0 LC_ALL=C; " +
                "exec git -C " + Remote.QuotePath(at.Path) + string.Concat(arguments.Select(argument => " " + Remote.Quote(argument)));
            return Ssh.Start(host, script);
        }
        var start = new ProcessStartInfo(Executable)
        {
            WorkingDirectory = at.Path,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_PAGER"] = "cat";
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        start.Environment["LC_ALL"] = "C";
        return start;
    }

    /// <summary>Runs Git to completion and returns standard output as bytes; a failure throws its stderr.</summary>
    public static async Task<byte[]> Run(Place at, CancellationToken token, params string[] arguments)
    {
        using var process = Process.Start(Start(at, arguments)) ?? throw new IOException(at.Host is null ? "Could not start git." : "Could not start ssh.");
        process.StandardInput.Close();
        using var cancel = token.Register(() => Kill(process));
        using var output = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(output, token);
        var error = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token).ConfigureAwait(false);
        await copy.ConfigureAwait(false);
        // Over ssh, standard error is only read for a failure: a shared connection may hold it open.
        if (at.Host is not null && process.ExitCode == 0)
            return output.ToArray();
        var message = at.Host is null ? await error.ConfigureAwait(false) : await Ssh.Errors(error).ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new IOException(message.Trim().Length > 0 ? message.Trim() : $"git {arguments.FirstOrDefault()} exited with {process.ExitCode}.");
        return output.ToArray();
    }

    public static async Task<string> Text(Place at, CancellationToken token, params string[] arguments) =>
        Encoding.UTF8.GetString(await Run(at, token, arguments).ConfigureAwait(false));

    internal static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    /// <summary>Splits NUL-terminated output such as <c>--name-status -z</c>.</summary>
    public static string[] Fields(byte[] output)
    {
        var text = Encoding.UTF8.GetString(output);
        return text.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }
}

/// <summary>
/// Reads many blobs through one <c>git cat-file --batch</c> process: one request line per object, answered
/// in order, so a thousand-file diff starts one process instead of a thousand.
/// </summary>
public sealed class BlobReader : IAsyncDisposable
{
    readonly Process process;
    readonly Stream output;
    readonly StreamWriter input;
    readonly byte[] header = new byte[512];

    public BlobReader(Place at)
    {
        var start = Git.Start(at, ["cat-file", "--batch"]);
        process = Process.Start(start) ?? throw new IOException("Could not start git cat-file.");
        input = process.StandardInput;
        input.AutoFlush = false;
        // Headers are read a byte at a time; a buffer keeps that from becoming a system call each.
        output = new BufferedStream(process.StandardOutput.BaseStream, 1 << 16);
        _ = process.StandardError.ReadToEndAsync();
    }

    /// <summary>The object named "REV:path" (":path" for the index), or null when it does not exist.</summary>
    public async Task<byte[]?> Read(string name, CancellationToken token)
    {
        await input.WriteAsync(name.AsMemory(), token).ConfigureAwait(false);
        await input.WriteAsync("\n".AsMemory(), token).ConfigureAwait(false);
        await input.FlushAsync(token).ConfigureAwait(false);
        return await Answer(token).ConfigureAwait(false);
    }

    /// <summary>
    /// Many objects in one exchange: every name is sent while the answers stream back in order, so a diff costs one
    /// round trip rather than one per file, which over SSH is the whole difference.
    /// </summary>
    public async Task<byte[]?[]> ReadAll(IReadOnlyList<string> names, CancellationToken token)
    {
        var requests = Task.Run(async () =>
        {
            foreach (var name in names)
            {
                await input.WriteAsync(name.AsMemory(), token).ConfigureAwait(false);
                await input.WriteAsync("\n".AsMemory(), token).ConfigureAwait(false);
            }
            await input.FlushAsync(token).ConfigureAwait(false);
        }, token);
        var answers = new byte[]?[names.Count];
        for (int i = 0; i < answers.Length; i++)
            answers[i] = await Answer(token).ConfigureAwait(false);
        await requests.ConfigureAwait(false);
        return answers;
    }

    /// <summary>
    /// The sizes of many objects in one exchange through <c>git cat-file --batch-check</c>; -1 for a missing one.
    /// Over SSH this tells which blobs are too large to carry before any is sent.
    /// </summary>
    public static async Task<long[]> Sizes(Place at, IReadOnlyList<string> names, CancellationToken token)
    {
        using var process = Process.Start(Git.Start(at, ["cat-file", "--batch-check"])) ?? throw new IOException("Could not start git cat-file.");
        using var cancel = token.Register(() => Git.Kill(process));
        _ = process.StandardError.ReadToEndAsync(token);
        var requests = Task.Run(async () =>
        {
            foreach (var name in names)
                await process.StandardInput.WriteAsync((name + "\n").AsMemory(), token).ConfigureAwait(false);
            process.StandardInput.Close();
        }, token);
        var sizes = new long[names.Count];
        for (int i = 0; i < sizes.Length; i++)
        {
            var line = await process.StandardOutput.ReadLineAsync(token).ConfigureAwait(false) ?? throw new IOException("git cat-file ended early.");
            var parts = line.Split(' ');
            sizes[i] = line.EndsWith(" missing", StringComparison.Ordinal) || line.EndsWith(" ambiguous", StringComparison.Ordinal) || parts.Length < 3 ||
                !long.TryParse(parts[^1], out var size) ? -1 : size;
        }
        await requests.ConfigureAwait(false);
        return sizes;
    }

    async Task<byte[]?> Answer(CancellationToken token)
    {
        int length = 0;
        while (true)
        {
            if (length == header.Length)
                throw new IOException("git cat-file returned an oversized header.");
            int read = await output.ReadAsync(header.AsMemory(length, 1), token).ConfigureAwait(false);
            if (read == 0)
                throw new IOException("git cat-file ended early.");
            if (header[length] == (byte)'\n')
                break;
            length++;
        }
        var line = Encoding.UTF8.GetString(header, 0, length);
        if (line.EndsWith(" missing", StringComparison.Ordinal) || line.EndsWith(" ambiguous", StringComparison.Ordinal))
            return null;
        var parts = line.Split(' ');
        if (parts.Length < 3 || !int.TryParse(parts[2], out var size))
            throw new IOException("Unexpected git cat-file header: " + line);
        var data = new byte[size];
        await output.ReadExactlyAsync(data, token).ConfigureAwait(false);
        var newline = new byte[1];
        await output.ReadExactlyAsync(newline, token).ConfigureAwait(false);
        return parts[1] == "blob" ? data : null;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            input.Close();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Git.Kill(process);
        }
        process.Dispose();
    }
}
