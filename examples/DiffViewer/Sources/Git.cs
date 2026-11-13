using System.Diagnostics;
using System.Text;

namespace DiffViewer.Sources;

/// <summary>Runs Git with an argument list (never a shell), without prompts, pagers or optional locks.</summary>
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

    public static ProcessStartInfo Start(string directory, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(Executable)
        {
            WorkingDirectory = directory,
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
    public static async Task<byte[]> Run(string directory, CancellationToken token, params string[] arguments)
    {
        using var process = Process.Start(Start(directory, arguments)) ?? throw new IOException("Could not start git.");
        process.StandardInput.Close();
        using var cancel = token.Register(() => Kill(process));
        using var output = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(output, token);
        var error = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token).ConfigureAwait(false);
        await copy.ConfigureAwait(false);
        var message = await error.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new IOException(message.Trim().Length > 0 ? message.Trim() : $"git {arguments.FirstOrDefault()} exited with {process.ExitCode}.");
        return output.ToArray();
    }

    public static async Task<string> Text(string directory, CancellationToken token, params string[] arguments) =>
        Encoding.UTF8.GetString(await Run(directory, token, arguments).ConfigureAwait(false));

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

    public BlobReader(string directory)
    {
        var start = Git.Start(directory, ["cat-file", "--batch"]);
        process = Process.Start(start) ?? throw new IOException("Could not start git cat-file.");
        input = process.StandardInput;
        input.AutoFlush = false;
        output = process.StandardOutput.BaseStream;
        _ = process.StandardError.ReadToEndAsync();
    }

    /// <summary>The object named "REV:path" (":path" for the index), or null when it does not exist.</summary>
    public async Task<byte[]?> Read(string name, CancellationToken token)
    {
        await input.WriteAsync(name.AsMemory(), token).ConfigureAwait(false);
        await input.WriteAsync("\n".AsMemory(), token).ConfigureAwait(false);
        await input.FlushAsync(token).ConfigureAwait(false);
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
