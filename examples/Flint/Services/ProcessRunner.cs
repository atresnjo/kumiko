using System.Diagnostics;

namespace Flint.Services;

/// <summary>ArgumentList bypasses shell parsing; both pipes drain concurrently and cancellation kills the process tree.</summary>
public interface IProcessRunner
{
    Task<string> Run(string executable, string directory, IEnumerable<string> arguments, CancellationToken token,
        IReadOnlyDictionary<string, string>? environment = null, bool allowDifference = false);
}
/// <summary>A failed command. Message is the last lines of stderr; Output holds everything the command and its hooks printed.</summary>
public sealed class ProcessException(string message, string output, int code) : IOException(message)
{
    public string Output { get; } = output;
    public int Code { get; } = code;
}
/// <summary>searchPath supplies the PATH children receive and tools are found on; defaults and per-call variables override it.</summary>
public sealed class ProcessRunner(IReadOnlyDictionary<string, string>? defaults = null, Func<Task<string>>? searchPath = null) : IProcessRunner
{
    public async Task<string> Run(string executable, string directory, IEnumerable<string> arguments, CancellationToken token,
        IReadOnlyDictionary<string, string>? environment = null, bool allowDifference = false)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        // Git must never prompt on an invisible stdin or invoke a pager/editor.
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_PAGER"] = "cat";
        start.Environment["GIT_EDITOR"] = "true";
        start.Environment["GIT_MERGE_AUTOEDIT"] = "no";
        start.Environment["GH_PROMPT_DISABLED"] = "1";
        start.Environment["LC_ALL"] = "C";
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        start.Environment["GIT_LITERAL_PATHSPECS"] = "1";
        if (searchPath is not null)
            start.Environment["PATH"] = await searchPath().ConfigureAwait(false);
        if (defaults is not null)
            foreach (var (key, value) in defaults)
                start.Environment[key] = value;
        if (environment is not null)
            foreach (var (key, value) in environment)
                start.Environment[key] = value;
        var path = start.Environment.TryGetValue("PATH", out var search) ? search ?? "" : "";
        start.FileName = SearchPath.Resolve(executable, path)
            ?? throw new IOException($"Could not find {executable} on PATH. Install it or add its folder to your login shell's PATH.");
        using var process = Process.Start(start) ?? throw new IOException($"Could not start {executable}.");
        process.StandardInput.Close();
        using var cancel = token.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
        });
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token).ConfigureAwait(false);
        var output = await stdout.ConfigureAwait(false);
        var error = await stderr.ConfigureAwait(false);
        if (process.ExitCode != 0 && !(allowDifference && process.ExitCode == 1))
        {
            var text = error.Trim().Length > 0 ? error.Trim() : output.Trim().Length > 0 ? output.Trim() : $"{executable} exited with code {process.ExitCode}.";
            var all = string.Join('\n', new[] { output.Trim(), error.Trim() }.Where(s => s.Length > 0));
            throw new ProcessException(string.Join('\n', text.Split('\n')[^Math.Min(3, text.Split('\n').Length)..]), all, process.ExitCode);
        }
        return output;
    }
}
