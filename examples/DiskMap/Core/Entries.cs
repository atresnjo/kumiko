using System.Collections.Concurrent;
using System.IO.Enumeration;

namespace DiskMap.Core;

/// <summary>A worker's reusable buffer for one folder's listing: files become items, folders are queued.</summary>
sealed class Entries(HardLinks links, Names names)
{
    public Names Names { get; } = names;
    public List<Item> Files { get; } = [];
    public List<(string Name, long Modified)> Folders { get; } = [];
    public int Errors;
    public long Bytes;

    public void Clear()
    {
        Files.Clear();
        Folders.Clear();
        Errors = 0;
        Bytes = 0;
    }

    /// <summary>A file or link; a hard link already counted elsewhere adds no size (its ID is non-zero).</summary>
    public void Add(string name, ItemKind kind, long size, long apparent, long modified, ulong linkId)
    {
        if (linkId != 0 && !links.First(linkId))
        {
            size = 0;
            apparent = 0;
        }
        Files.Add(Item.File(name, size, apparent, modified, kind));
        Bytes += size;
    }
}

/// <summary>
/// One string per distinct name, shared across the tree: a home folder repeats index.js, package.json and .DS_Store
/// hundreds of thousands of times, so a scan keeps a fraction of the strings it reads. Lookups take the decoded name
/// without allocating; the table stops growing at a bound and is dropped with the scanner.
/// </summary>
sealed class Names
{
    const int Limit = 16_000_000;
    readonly ConcurrentDictionary<string, string> table = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> lookup;
    int count;

    public Names() => lookup = table.GetAlternateLookup<ReadOnlySpan<char>>();

    public string Get(ReadOnlySpan<char> name)
    {
        if (lookup.TryGetValue(name, out var shared))
            return shared;
        var created = name.ToString();
        if (Volatile.Read(ref count) >= Limit)
            return created;
        var added = table.GetOrAdd(created, created);
        if (ReferenceEquals(added, created))
            Interlocked.Increment(ref count);
        return added;
    }
}

/// <summary>File IDs of multiply linked files seen so far; only the first link carries the size.</summary>
sealed class HardLinks
{
    readonly HashSet<ulong> seen = [];

    public bool First(ulong id)
    {
        lock (seen)
            return seen.Add(id);
    }
}

/// <summary>The fallback listing through .NET's enumerator; disk size is the length rounded up to 4 KiB blocks.</summary>
static class PortableDirectory
{
    public static bool Read(string path, Entries entries)
    {
        try
        {
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = false,
                AttributesToSkip = 0,
                RecurseSubdirectories = false,
                ReturnSpecialDirectories = false
            };
            var listing = new FileSystemEnumerable<(string, bool, bool, long, long)>(path,
                (ref FileSystemEntry e) => (e.FileName.ToString(), e.IsDirectory, (e.Attributes & FileAttributes.ReparsePoint) != 0,
                    e.IsDirectory ? 0 : e.Length, e.LastWriteTimeUtc.ToUnixTimeSeconds()), options);
            foreach (var (name, folder, link, length, modified) in listing)
            {
                var shared = entries.Names.Get(name);
                if (link)
                    entries.Add(shared, ItemKind.Link, 0, 0, modified, 0);
                else if (folder)
                    entries.Folders.Add((shared, modified));
                else
                    entries.Add(shared, ItemKind.File, (length + 4095) / 4096 * 4096, length, modified, 0);
            }
            return true;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
