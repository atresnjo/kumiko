using System.Collections.Immutable;

namespace DiffViewer.Sources;

/// <summary>A commit of the open repository; Parent is its first parent, empty for a first commit.</summary>
public sealed record Commit(string Sha, string Parent, string Subject, string Author, string When)
{
    public string Short => Sha[..Math.Min(7, Sha.Length)];
}

public static class Commits
{
    public const int Limit = 200;

    /// <summary>The newest commits reachable from HEAD, newest first; an empty list outside a repository.</summary>
    public static async Task<ImmutableArray<Commit>> Log(string path, CancellationToken token)
    {
        try
        {
            var text = await Git.Text(path, token, "log", $"--max-count={Limit}", "--format=%H%x1f%P%x1f%s%x1f%an%x1f%ar%x1e").ConfigureAwait(false);
            return [.. text.Split('\x1e', StringSplitOptions.RemoveEmptyEntries).Select(record => record.Trim('\n').Split('\x1f'))
                .Where(fields => fields.Length == 5)
                .Select(fields => new Commit(fields[0], fields[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "",
                    fields[2], fields[3], fields[4]))];
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception)
        {
            return [];
        }
    }
}
