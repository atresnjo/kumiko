namespace DiffViewer.Sources;

/// <summary>Command-line parsing. It looks at the file system, so it runs before the program starts.</summary>
public static class Arguments
{
    /// <summary>
    /// Parses command-line arguments: <c>[--repo=PATH] [--staged] [BASE [TARGET]]</c>, <c>BASE..TARGET</c>, or
    /// <c>--compare LEFT RIGHT</c> for folders and files. A first argument naming an existing folder is the
    /// repository. Kumiko's own --kumiko-* options are ignored.
    /// </summary>
    public static DiffRequest Parse(IReadOnlyList<string> args, string directory)
    {
        var repo = directory;
        var staged = false;
        var compare = false;
        var demo = false;
        var positional = System.Collections.Immutable.ImmutableArray.CreateBuilder<string>();
        foreach (var arg in args)
        {
            if (arg.StartsWith("--repo=", StringComparison.Ordinal))
                repo = arg["--repo=".Length..];
            else if (arg == "--staged" || arg == "--cached")
                staged = true;
            else if (arg == "--compare" || arg == "--no-index")
                compare = true;
            else if (arg == "--demo")
                demo = true;
            else if (!arg.StartsWith('-'))
                positional.Add(arg);
        }
        repo = Path.GetFullPath(repo, directory);
        // A leading existing folder names the repository: `diff ~/code/app main` compares that checkout with main.
        if (!compare && positional.Count > 0 && Directory.Exists(Path.GetFullPath(positional[0], directory)))
        {
            repo = Path.GetFullPath(positional[0], directory);
            positional.RemoveAt(0);
        }
        if (demo)
            return new DiffRequest(SourceKind.Demo, repo);
        if (compare)
        {
            if (positional.Count != 2)
                throw new ArgumentException("--compare needs a left and a right path.");
            return new DiffRequest(SourceKind.Paths, Path.GetFullPath(positional[0], directory), null, Path.GetFullPath(positional[1], directory));
        }
        if (staged)
            return new DiffRequest(SourceKind.Staged, repo, "HEAD");
        if (positional.Count == 1 && positional[0].Split("..", 2) is [{ Length: > 0 } from, { Length: > 0 } to])
            return new DiffRequest(SourceKind.Revisions, repo, from, to);
        if (positional.Count >= 2)
            return new DiffRequest(SourceKind.Revisions, repo, positional[0], positional[1]);
        return new DiffRequest(SourceKind.WorkingTree, repo, positional.Count == 1 ? positional[0] : "HEAD");
    }

    /// <summary>True when Path is inside a Git checkout, found by looking for .git up to the root; no process starts.</summary>
    public static bool InRepository(DiffRequest request)
    {
        if (request.Kind is SourceKind.Paths or SourceKind.Demo)
            return true;
        for (var at = new DirectoryInfo(request.Path); at is not null; at = at.Parent)
            if (Directory.Exists(Path.Combine(at.FullName, ".git")) || File.Exists(Path.Combine(at.FullName, ".git")))
                return true;
        return false;
    }
}
