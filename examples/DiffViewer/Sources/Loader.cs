using System.Collections.Immutable;
using DiffViewer.Core;

namespace DiffViewer.Sources;

/// <summary>Turns a request into a highlighted, diffed document. Runs on worker threads.</summary>
public static class Loader
{
    /// <summary>The empty tree, the base of a repository without commits.</summary>
    public const string EmptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

    /// <summary>
    /// Loads the request's changes with three lines of context; the file at <paramref name="whole"/>, zen mode's,
    /// keeps every line instead, so it reads from top to bottom with its changes in place.
    /// </summary>
    public static async Task<DiffDocument> Load(DiffRequest request, bool ignoreWhitespace, CancellationToken token, string? whole = null)
    {
        var (title, detail, root, pairs) = request.Kind switch
        {
            SourceKind.Paths => Folders.Pairs(request.Path, request.Target!, token),
            SourceKind.Demo => Demo.Pairs(),
            SourceKind.PullRequest => await PullPairs(request, token).ConfigureAwait(false),
            _ => await GitPairs(request, token).ConfigureAwait(false)
        };
        FilePair?[] pending = [.. pairs];
        pairs.Clear();
        var files = new FileDiff[pending.Length];
        // Each file's full text is released once it is built, so a large load never holds full and trimmed copies of
        // every file at once.
        Parallel.For(0, pending.Length, new ParallelOptions { CancellationToken = token }, i =>
        {
            var pair = pending[i]!;
            int context = pair.Path == whole ? Math.Max(pair.Old.Lines.Length, pair.New.Lines.Length) : 3;
            files[i] = Differ.Build(pair, ignoreWhitespace, context, token);
            pending[i] = null;
        });
        // Whitespace-only changes disappear when whitespace is ignored; their files go with them.
        var shown = files.Where(f => !ignoreWhitespace || f.Binary || f.Hunks.Length > 0 || f.Status != FileStatus.Modified)
            .OrderBy(f => f.Path, StringComparer.Ordinal);
        return new(title, detail, root, [.. shown]);
    }

    /// <param name="knownRoot">The checkout's root when the caller knows it, which saves a round trip over SSH.</param>
    static async Task<(string, string, string, List<FilePair>)> GitPairs(DiffRequest request, CancellationToken token, string? knownRoot = null)
    {
        var host = request.Host;
        string root;
        try
        {
            root = knownRoot ?? (await Git.Text(new Place(request.Path, host), token, "rev-parse", "--show-toplevel").ConfigureAwait(false)).Trim();
        }
        catch (IOException error) when (host is not null && error.Message.Contains("not a git repository", StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"{Remote.Format(host, request.Path)} is not a Git repository. Press ⌘K and choose {Remote.Name(host)} " +
                "to list the repositories on it.", error);
        }
        var at = new Place(root, host);
        var from = request.Base ?? "HEAD";
        // Only a comparison with HEAD needs to know whether the repository has commits yet.
        if (from == "HEAD" && !await Exists(at, ["HEAD"], token).ConfigureAwait(false))
            from = EmptyTree;
        var arguments = new List<string> { "diff", "--name-status", "-z", "-M", "--no-ext-diff" };
        string? to = null;
        switch (request.Kind)
        {
            case SourceKind.Staged:
                arguments.Add("--cached");
                arguments.Add(from);
                break;
            case SourceKind.Revisions:
                to = request.Target!;
                arguments.Add(from);
                arguments.Add(to);
                break;
            default:
                arguments.Add(from);
                break;
        }
        arguments.Add("--");
        var fields = Git.Fields(await Git.Run(at, token, [.. arguments]).ConfigureAwait(false));
        var changes = new List<(FileStatus Status, string Path, string? OldPath)>();
        for (int i = 0; i < fields.Length; i++)
        {
            var code = fields[i];
            if (code.StartsWith('R') || code.StartsWith('C'))
            {
                changes.Add((code[0] == 'R' ? FileStatus.Renamed : FileStatus.Added, fields[i + 2], fields[i + 1]));
                i += 2;
                continue;
            }
            var status = code[0] switch
            {
                'A' => FileStatus.Added,
                'D' => FileStatus.Deleted,
                _ => FileStatus.Modified
            };
            changes.Add((status, fields[i + 1], null));
            i++;
        }
        if (request.Kind == SourceKind.WorkingTree)
        {
            var untracked = Git.Fields(await Git.Run(at, token, "ls-files", "--others", "--exclude-standard", "-z").ConfigureAwait(false));
            foreach (var path in untracked)
                changes.Add((FileStatus.Added, path, null));
        }

        // A remote working tree's files arrive in one stream, read while the old blobs are.
        var remoteFiles = host is not null && request.Kind == SourceKind.WorkingTree
            ? Ssh.ReadFiles(at, [.. changes.Where(c => c.Status != FileStatus.Deleted).Select(c => c.Path).Distinct(StringComparer.Ordinal)], token)
            : null;
        // Every blob is asked for at once (old sides, and new ones that come from Git), then read as it streams back.
        var names = new List<string>(changes.Count * 2);
        var slots = new (int Old, int New)[changes.Count];
        for (int i = 0; i < changes.Count; i++)
        {
            var (status, path, oldPath) = changes[i];
            int Ask(string name)
            {
                names.Add(name);
                return names.Count - 1;
            }
            slots[i] = (status == FileStatus.Added && oldPath is null ? -1 : Ask($"{from}:{oldPath ?? path}"),
                status == FileStatus.Deleted ? -1 : request.Kind == SourceKind.Staged ? Ask(":" + path) : to is not null ? Ask($"{to}:{path}") : -2);
        }
        // Over SSH, blobs above the limit (generated schemas, bundles) are named with their size instead of carried.
        var sizes = host is not null && names.Count > 0 ? await BlobReader.Sizes(at, names, token).ConfigureAwait(false) : null;
        var carried = sizes is null ? names : [.. names.Where((_, i) => sizes[i] <= RemoteBlobLimit)];
        byte[]?[] read;
        await using (var reader = new BlobReader(at))
            read = carried.Count == 0 ? [] : await reader.ReadAll(carried, token).ConfigureAwait(false);
        var blobs = new byte[]?[names.Count];
        for (int i = 0, next = 0; i < names.Count; i++)
            blobs[i] = sizes is null || sizes[i] <= RemoteBlobLimit ? read[next++] : null;
        var working = remoteFiles is null ? null : await remoteFiles.ConfigureAwait(false);
        var pairs = new List<FilePair>(changes.Count);
        for (int i = 0; i < changes.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var (status, path, oldPath) = changes[i];
            var (oldSlot, newSlot) = slots[i];
            TextFile Side(int slot) => sizes is not null && sizes[slot] > RemoteBlobLimit ? TextFile.TooLarge(sizes[slot]) : TextFile.Decode(blobs[slot] ?? []);
            var old = oldSlot < 0 ? TextFile.Empty : Side(oldSlot);
            // -1: no new side; -2: the working tree's file, from the remote stream or the disk.
            var next = newSlot >= 0 ? Side(newSlot) : newSlot == -1 ? TextFile.Empty
                : working is not null ? working.TryGetValue(path, out var bytes) ? TextFile.Decode(bytes) : TextFile.Empty
                : ReadWorking(Path.Combine(root, path));
            pairs.Add(new(path, oldPath, status, old, next));
            // The raw bytes go once decoded, so a large diff never holds both.
            if (oldSlot >= 0)
                blobs[oldSlot] = null;
            if (newSlot >= 0)
                blobs[newSlot] = null;
        }
        var name = Path.GetFileName(root);
        var detail = request.Kind switch
        {
            SourceKind.Staged => "Staged changes",
            SourceKind.Revisions when request.Title is { } subject && request.Target is { } target => $"{target[..Math.Min(7, target.Length)]} · {subject}",
            SourceKind.Revisions => $"{request.Base} → {request.Target}",
            _ => from == "HEAD" || from == EmptyTree ? "Working tree" : $"Working tree against {from}"
        };
        if (host is not null)
            detail += " on " + Remote.Name(host);
        return (name, detail, root, pairs);
    }

    /// <summary>The largest blob carried over SSH; larger ones show their size, and a local clone shows them.</summary>
    const long RemoteBlobLimit = 1 << 20;

    /// <summary>A fetch that takes longer falls back to GitHub's API.</summary>
    static readonly TimeSpan FetchLimit = TimeSpan.FromMinutes(3);

    /// <summary>
    /// A pull request's files, through Git when a checkout of its repository is at hand: a known local clone first,
    /// then the checkout it was opened from (locally or on an SSH host). ls-remote names its head and GitHub's merge
    /// ref, whose first parent is the base tip (the remote's default branch without one); missing commits are fetched
    /// without writing any ref; merge base to head is diffed as revisions. Otherwise, or when Git fails, GitHub's API
    /// serves it.
    /// </summary>
    static async Task<(string, string, string, List<FilePair>)> PullPairs(DiffRequest request, CancellationToken token)
    {
        var pull = request.Pull ?? throw new IOException("The pull request has not been found yet.");
        var opened = new Place(request.Path, request.Host);
        foreach (var at in LocalClones(request.Clones, pull.Repository).Append(opened).Distinct())
        {
            try
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
                limit.CancelAfter(FetchLimit);
                if (await FromCheckout(at, pull, limit.Token).ConfigureAwait(false) is { } pairs)
                    return ($"{pull.Repository} #{pull.Number}", pull.Title, "", pairs);
            }
            catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception ||
                error is OperationCanceledException && !token.IsCancellationRequested)
            {
                // The next checkout, or the API below, serves pull requests Git cannot reach here.
            }
        }
        return await GitHub.Pairs(pull, token).ConfigureAwait(false);
    }

    /// <summary>
    /// The candidates whose .git/config names the repository as a remote, read lazily (no Git process); folders named
    /// like the repository are tried first.
    /// </summary>
    static IEnumerable<Place> LocalClones(IEnumerable<string> candidates, string repository)
    {
        var name = repository[(repository.LastIndexOf('/') + 1)..];
        foreach (var path in candidates.OrderBy(p => Path.GetFileName(p).Equals(name, StringComparison.OrdinalIgnoreCase) ? 0 : 1))
            if (RemoteUrls(path).Any(url => string.Equals(RepoInfo.Parse(url)?.Repository, repository, StringComparison.OrdinalIgnoreCase)))
                yield return new Place(path);
    }

    static string[] RemoteUrls(string checkout)
    {
        try
        {
            var config = Path.Combine(checkout, ".git", "config");
            return File.Exists(config) ? [.. File.ReadLines(config).Select(line => line.Trim())
                .Where(line => line.StartsWith("url", StringComparison.Ordinal) && line.Contains('=', StringComparison.Ordinal))
                .Select(line => line[(line.IndexOf('=', StringComparison.Ordinal) + 1)..].Trim())] : [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The pull request's files from a checkout whose remote is its repository; null for another repository.</summary>
    static async Task<List<FilePair>?> FromCheckout(Place at, PullRequest pull, CancellationToken token)
    {
        if (await RepoInfo.Remote(at, token).ConfigureAwait(false) is not { } remote ||
            !string.Equals(RepoInfo.Parse(remote.Url)?.Repository, pull.Repository, StringComparison.OrdinalIgnoreCase))
            return null;
        var number = pull.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string headRef = $"refs/pull/{number}/head", mergeRef = $"refs/pull/{number}/merge";
        var listed = (await Git.Text(at, token, "ls-remote", remote.Name, "HEAD", headRef, mergeRef).ConfigureAwait(false))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split('\t')).Where(f => f.Length == 2)
            .ToDictionary(f => f[1], f => f[0], StringComparer.Ordinal);
        if (!listed.TryGetValue(headRef, out var head))
            return null;
        var merge = listed.GetValueOrDefault(mergeRef);
        var tip = merge ?? listed.GetValueOrDefault("HEAD") ?? throw new IOException("The remote names no default branch.");
        // Only what is missing comes over the network; no ref is written, so the checkout's branches stay as they were.
        if (!await Exists(at, [head, tip], token).ConfigureAwait(false))
            await Git.Run(at, token, ["fetch", "--quiet", "--no-tags", "--no-write-fetch-head", remote.Name, headRef,
                .. merge is null ? new[] { "HEAD" } : [mergeRef]]).ConfigureAwait(false);
        var from = (await Git.Text(at, token, "merge-base", merge is null ? tip : merge + "^1", head).ConfigureAwait(false)).Trim();
        // Every Git call is a round trip over SSH, so the root is not asked for again.
        var (_, _, _, pairs) = await GitPairs(new(SourceKind.Revisions, at.Path, from, head) { Host = at.Host }, token, at.Path).ConfigureAwait(false);
        return pairs;
    }

    /// <summary>True when every revision names a commit present in the checkout, asked in one Git call.</summary>
    static async Task<bool> Exists(Place at, string[] revisions, CancellationToken token)
    {
        try
        {
            await Git.Run(at, token, ["rev-parse", "--quiet", .. revisions.Select(r => r + "^{commit}")]).ConfigureAwait(false);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    internal static TextFile ReadWorking(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Attributes.HasFlag(FileAttributes.Directory))
                return TextFile.Empty;
            return TextFile.Decode(File.ReadAllBytes(path));
        }
        catch (IOException)
        {
            return TextFile.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return TextFile.Empty;
        }
    }
}

/// <summary>Compares two folders, or two files, without Git.</summary>
public static class Folders
{
    static readonly HashSet<string> Skipped = new(StringComparer.Ordinal) { ".git", "node_modules", ".DS_Store" };

    public static (string, string, string, List<FilePair>) Pairs(string left, string right, CancellationToken token)
    {
        if (File.Exists(left) && File.Exists(right))
        {
            var pair = new FilePair(Path.GetFileName(right), Path.GetFileName(left) == Path.GetFileName(right) ? null : Path.GetFileName(left),
                FileStatus.Modified, Loader.ReadWorking(left), Loader.ReadWorking(right));
            return (Path.GetFileName(right), $"{left} → {right}", Path.GetDirectoryName(right) ?? right, [pair]);
        }
        if (!Directory.Exists(left) || !Directory.Exists(right))
            throw new DirectoryNotFoundException($"Compare needs two existing folders or two files: {left}, {right}");
        var before = Files(left, token);
        var after = Files(right, token);
        var pairs = new List<FilePair>();
        foreach (var path in before.Keys.Union(after.Keys).Order(StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            bool inOld = before.TryGetValue(path, out var oldInfo), inNew = after.TryGetValue(path, out var newInfo);
            if (inOld && inNew && Same(oldInfo!, newInfo!))
                continue;
            var status = !inOld ? FileStatus.Added : !inNew ? FileStatus.Deleted : FileStatus.Modified;
            pairs.Add(new(path, null, status, inOld ? Loader.ReadWorking(oldInfo!.FullName) : TextFile.Empty,
                inNew ? Loader.ReadWorking(newInfo!.FullName) : TextFile.Empty));
        }
        return (Path.GetFileName(right.TrimEnd('/')), $"{left} → {right}", right, pairs);
    }

    static Dictionary<string, FileInfo> Files(string root, CancellationToken token)
    {
        var result = new Dictionary<string, FileInfo>(StringComparer.Ordinal);
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if (Skipped.Contains(entry.Name) || entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    continue;
                if (entry is DirectoryInfo child)
                    pending.Push(child);
                else if (entry is FileInfo file)
                    result[Path.GetRelativePath(root, file.FullName).Replace('\\', '/')] = file;
            }
        }
        return result;
    }

    static bool Same(FileInfo a, FileInfo b)
    {
        if (a.Length != b.Length)
            return false;
        return File.ReadAllBytes(a.FullName).AsSpan().SequenceEqual(File.ReadAllBytes(b.FullName));
    }
}
