using System.Collections.Immutable;
using Flint.Git;

namespace Flint;

/// <summary>A visible file-tree row. Folder keys end with '/'; file keys are the path itself.</summary>
public readonly record struct TreeRow(string Key, int Depth, FileChange? File)
{
    public bool Folder => File is null;
    public string Path => Folder ? Key[..^1] : Key;
    public string Name => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// Flattens files that the Git service sorted folders-first into visible rows in one linear pass. Views build only
/// the rows a virtual list shows; updates use the same order for keyboard movement and Shift ranges.
/// </summary>
public static class FileTree
{
    public static ImmutableArray<TreeRow> Rows(ImmutableArray<FileChange> files, ImmutableHashSet<string> collapsed, string prefix)
    {
        var rows = ImmutableArray.CreateBuilder<TreeRow>(files.Length + 8);
        var previous = "";
        string? skip = null;
        foreach (var file in files)
        {
            if (skip is not null && file.Path.StartsWith(skip, StringComparison.Ordinal))
                continue;
            skip = null;
            var slash = file.Path.LastIndexOf('/');
            var directory = slash < 0 ? "" : file.Path[..slash];
            var at = Common(previous, directory);
            previous = directory;
            while (at < directory.Length)
            {
                var end = directory.IndexOf('/', at);
                var folder = end < 0 ? directory : directory[..end];
                rows.Add(new(folder + "/", Depth(folder), null));
                if (collapsed.Contains(prefix + folder))
                {
                    skip = folder + "/";
                    previous = folder;
                    break;
                }
                at = folder.Length + 1;
            }
            if (skip is null)
                rows.Add(new(file.Path, Depth(file.Path), file));
        }
        return rows.Count == rows.Capacity ? rows.MoveToImmutable() : rows.ToImmutable();
    }
    /// <summary>Characters of directory already open from the previous file's directory, ending at a segment boundary.</summary>
    static int Common(string previous, string directory)
    {
        if (previous.Length == 0 || directory.Length == 0)
            return 0;
        var length = 0;
        var limit = Math.Min(previous.Length, directory.Length);
        for (var i = 0; i <= limit; i++)
        {
            var a = i == previous.Length ? '/' : previous[i];
            var b = i == directory.Length ? '/' : directory[i];
            if (a != b)
                break;
            if (a == '/')
                length = i + 1;
        }
        return length;
    }
    static int Depth(string path)
    {
        var depth = 0;
        foreach (var ch in path)
            if (ch == '/')
                depth++;
        return depth;
    }
    /// <summary>Files named by selected keys; a folder key selects every file below it.</summary>
    public static ImmutableArray<FileChange> Files(ImmutableArray<FileChange> files, ImmutableArray<string> keys)
    {
        var folders = keys.Where(k => k.EndsWith('/')).ToArray();
        return [.. files.Where(f => keys.Contains(f.Path) || folders.Any(d => f.Path.StartsWith(d, StringComparison.Ordinal)))];
    }
}

public enum SideKind
{
    Section, Folder, Branch, Remote, Tag, Stash, Worktree
}

/// <summary>A sidebar row. Text is the source name or folder path; Source indexes the snapshot list it came from.</summary>
public readonly record struct SideItem(SideKind Kind, string Text, int Depth, int Source = -1, int Count = 0)
{
    public string Id => Kind switch
    {
        SideKind.Section => "section:" + Text,
        SideKind.Folder => "folder:" + Text,
        SideKind.Branch => "branch:" + Text,
        SideKind.Remote => "remote:" + Text,
        SideKind.Tag => "tag:" + Text,
        SideKind.Stash => "stash:" + Text,
        _ => "worktree:" + Text
    };
    public bool Leaf => Kind is not (SideKind.Section or SideKind.Folder);
    public string Label => Kind is SideKind.Worktree ? System.IO.Path.GetFileName(Text)
        : Kind is SideKind.Section or SideKind.Tag or SideKind.Stash ? Text : Text[(Text.LastIndexOf('/') + 1)..];
}

/// <summary>Sidebar rows for worktrees, branch folders, remotes, tags and stashes, honouring collapse and filter.</summary>
public static class SideTree
{
    public static readonly ImmutableHashSet<string> DefaultCollapsed = ["section:Tags"];
    public static ImmutableArray<SideItem> Items(Snapshot snapshot, ImmutableHashSet<string> collapsed, string filter)
    {
        var items = ImmutableArray.CreateBuilder<SideItem>();
        var query = filter.Trim();
        bool Open(string id) => query.Length > 0 || !collapsed.Contains(id);
        bool Match(string text) => query.Length == 0 || text.Contains(query, StringComparison.OrdinalIgnoreCase);
        if (snapshot.Worktrees.Length > 1)
        {
            items.Add(new(SideKind.Section, "Worktrees", 0, Count: snapshot.Worktrees.Length));
            if (Open("section:Worktrees"))
                for (var i = 0; i < snapshot.Worktrees.Length; i++)
                    if (Match(System.IO.Path.GetFileName(snapshot.Worktrees[i].Path)))
                        items.Add(new(SideKind.Worktree, snapshot.Worktrees[i].Path, 1, i));
        }
        var branches = 0;
        var remotes = 0;
        var tags = 0;
        foreach (var reference in snapshot.Refs)
        {
            if (reference.Kind == "branch")
                branches++;
            else if (reference.Kind == "remote")
                remotes++;
            else
                tags++;
        }
        items.Add(new(SideKind.Section, "Branches", 0, Count: branches));
        if (Open("section:Branches"))
            Walk(items, snapshot.Refs, "branch", SideKind.Branch, "heads/", 1, collapsed, query);
        items.Add(new(SideKind.Section, "Remotes", 0, Count: Math.Max(snapshot.Remotes.Length, remotes > 0 ? 1 : 0)));
        if (Open("section:Remotes"))
        {
            foreach (var remote in snapshot.Remotes)
            {
                var root = "remotes/" + remote;
                if (!Match(remote) && !snapshot.Refs.Any(r => r.Kind == "remote" && r.Name.StartsWith(remote + "/", StringComparison.Ordinal) && Match(r.Name)))
                    continue;
                items.Add(new(SideKind.Folder, root, 1));
                if (Open("folder:" + root))
                    Walk(items, snapshot.Refs, "remote", SideKind.Remote, root + "/", 2, collapsed, Match(remote) ? "" : query,
                        remote + "/", snapshot.Remotes, query.Length > 0);
            }
        }
        items.Add(new(SideKind.Section, "Tags", 0, Count: tags));
        if (Open("section:Tags"))
            for (var i = 0; i < snapshot.Refs.Length; i++)
                if (snapshot.Refs[i].Kind == "tag" && Match(snapshot.Refs[i].Name))
                    items.Add(new(SideKind.Tag, snapshot.Refs[i].Name, 1, i));
        items.Add(new(SideKind.Section, "Stashes", 0, Count: snapshot.Stashes.Length));
        if (Open("section:Stashes"))
            for (var i = 0; i < snapshot.Stashes.Length; i++)
                if (Match(snapshot.Stashes[i].Message))
                    items.Add(new(SideKind.Stash, snapshot.Stashes[i].Message.Length > 0 ? snapshot.Stashes[i].Message : snapshot.Stashes[i].Ref, 1, i));
        return items.ToImmutable();
    }
    /// <summary>Refs arrive leaves-first in tree order; folders open lazily for the first visible leaf inside them.</summary>
    static void Walk(ImmutableArray<SideItem>.Builder items, ImmutableArray<GitRef> refs, string kind, SideKind leaf, string root, int depth,
        ImmutableHashSet<string> collapsed, string query, string prefix = "", ImmutableArray<string> remoteNames = default, bool expand = false)
    {
        var opened = ImmutableArray.CreateBuilder<string>();
        string? skip = null;
        for (var i = 0; i < refs.Length; i++)
        {
            if (!refs[i].Name.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            if (!remoteNames.IsDefault && remoteNames.Any(remote => remote.Length >= prefix.Length
                && refs[i].Name.StartsWith(remote + "/", StringComparison.Ordinal)))
                continue;
            var name = refs[i].Name[prefix.Length..];
            if (refs[i].Kind != kind || skip is not null && name.StartsWith(skip, StringComparison.Ordinal))
                continue;
            if (query.Length > 0 && !refs[i].Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;
            skip = null;
            var folders = name.Split('/');
            var keep = 0;
            while (keep < opened.Count && keep < folders.Length - 1 && opened[keep] == folders[keep])
                keep++;
            while (opened.Count > keep)
                opened.RemoveAt(opened.Count - 1);
            for (var level = keep; level < folders.Length - 1; level++)
            {
                var path = root + string.Join('/', folders, 0, level + 1);
                items.Add(new(SideKind.Folder, path, depth + level));
                opened.Add(folders[level]);
                if (!expand && query.Length == 0 && collapsed.Contains("folder:" + path))
                {
                    skip = path[root.Length..] + "/";
                    break;
                }
            }
            if (skip is null)
                items.Add(new(leaf, refs[i].Name, depth + folders.Length - 1, i));
        }
    }
}
