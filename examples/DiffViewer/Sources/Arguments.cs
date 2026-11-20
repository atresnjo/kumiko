namespace DiffViewer.Sources;

/// <summary>Command-line parsing. It looks at the file system, so it runs before the program starts.</summary>
public static class Arguments
{
    /// <summary>
    /// Parses command-line arguments: <c>[--repo=PATH] [--staged] [BASE [TARGET]]</c>, <c>BASE..TARGET</c>, or
    /// <c>--compare LEFT RIGHT</c> for folders and files. A first argument naming an existing folder is the
    /// repository. A remote checkout is <c>host:/path</c>, <c>host:~/path</c> or <c>ssh://user@host:port/path</c>
    /// (first or in --repo), or <c>--ssh=HOST</c> with --repo or a first argument starting with / or ~.
    /// Kumiko's own --kumiko-* options are ignored.
    /// </summary>
    public static DiffRequest Parse(IReadOnlyList<string> args, string directory)
    {
        string? repo = null;
        string? host = null;
        var staged = false;
        var compare = false;
        var demo = false;
        var positional = System.Collections.Immutable.ImmutableArray.CreateBuilder<string>();
        foreach (var arg in args)
        {
            if (arg.StartsWith("--repo=", StringComparison.Ordinal))
                repo = arg["--repo=".Length..];
            else if (arg.StartsWith("--ssh=", StringComparison.Ordinal))
                host = arg["--ssh=".Length..];
            else if (arg == "--staged" || arg == "--cached")
                staged = true;
            else if (arg == "--compare" || arg == "--no-index")
                compare = true;
            else if (arg == "--demo")
                demo = true;
            else if (!arg.StartsWith('-'))
                positional.Add(arg);
        }
        if (repo is not null && Remote.TryParse(repo, out var repoHost, out var repoPath))
            (host, repo) = (repoHost, repoPath);
        if (!compare && positional.Count > 0)
        {
            // A leading existing folder names the repository: `diff ~/code/app main` compares that checkout with main.
            string? first = positional[0];
            if (repo is null && Remote.TryParse(first, out var firstHost, out var firstPath))
                (host, repo) = (firstHost, firstPath);
            else if (repo is null && host is not null && (first.StartsWith('/') || first.StartsWith('~')))
                repo = first;
            else if (host is null && Directory.Exists(Path.GetFullPath(first, directory)))
                repo = first;
            else
                first = null;
            if (first is not null)
                positional.RemoveAt(0);
        }
        if (host is not null && repo is null)
            throw new ArgumentException("--ssh needs the repository's path on the host: --repo=PATH or a path after it.");
        // A remote path stays as typed: relative paths and ~ belong to the remote home.
        var path = host is not null ? repo! : Path.GetFullPath(repo ?? directory, directory);
        if (demo)
            return new DiffRequest(SourceKind.Demo, path);
        if (compare)
        {
            if (positional.Count != 2)
                throw new ArgumentException("--compare needs a left and a right path.");
            if (host is not null)
                throw new ArgumentException("--compare works with local paths only.");
            return new DiffRequest(SourceKind.Paths, Path.GetFullPath(positional[0], directory), null, Path.GetFullPath(positional[1], directory));
        }
        DiffRequest request;
        if (staged)
            request = new DiffRequest(SourceKind.Staged, path, "HEAD");
        else if (positional.Count == 1 && positional[0].Split("..", 2) is [{ Length: > 0 } from, { Length: > 0 } to])
            request = new DiffRequest(SourceKind.Revisions, path, from, to);
        else if (positional.Count >= 2)
            request = new DiffRequest(SourceKind.Revisions, path, positional[0], positional[1]);
        else
            request = new DiffRequest(SourceKind.WorkingTree, path, positional.Count == 1 ? positional[0] : "HEAD");
        return request with
        {
            Host = host
        };
    }

    /// <summary>
    /// True when Path is inside a Git checkout, found by looking for .git up to the root; no process starts. A remote
    /// checkout is assumed to be one; loading it says otherwise.
    /// </summary>
    public static bool InRepository(DiffRequest request)
    {
        if (request.Kind is SourceKind.Paths or SourceKind.Demo || request.Host is not null)
            return true;
        for (var at = new DirectoryInfo(request.Path); at is not null; at = at.Parent)
            if (Directory.Exists(Path.Combine(at.FullName, ".git")) || File.Exists(Path.Combine(at.FullName, ".git")))
                return true;
        return false;
    }
}
