using System.Text;

namespace Kumiko.Desktop;

public static class TransferFormats
{
    public const string Text = "text/plain;charset=utf-8";
    public const string HTML = "text/html";
    public const string PNG = "image/png";
    public const string URIList = "text/uri-list";
    public const string FileList = "application/x-kumiko-file-list";
}

/// <summary>An immutable offer. Bytes are copied; lazy providers are cached by each snapshot, not by the offer.</summary>
public sealed record TransferRepresentation
{
    public string Format
    {
        get;
    }

    readonly byte[]? bytes;
    readonly Func<byte[]>? provider;
    public TransferRepresentation(string format, byte[] bytes)
    {
        Validate(format);
        Format = format;
        this.bytes = (byte[])bytes.Clone();
    }

    public TransferRepresentation(string format, Func<byte[]> provider)
    {
        Validate(format);
        Format = format;
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    static void Validate(string format)
    {
        if (string.IsNullOrWhiteSpace(format) || !format.Contains('/') || format.Any(char.IsControl))
            throw new ArgumentException("Transfer formats must be MIME names.", nameof(format));
    }

    internal byte[] Fetch() => bytes is { } value ? (byte[])value.Clone() : (byte[])(provider!() ?? throw new InvalidOperationException("A transfer provider returned null.")).Clone();
}

public sealed record TransferItem
{
    readonly TransferRepresentation[] representations;
    public IReadOnlyList<string> Formats => Array.AsReadOnly(representations.Select(r => r.Format).ToArray());
    internal IReadOnlyList<TransferRepresentation> Representations => representations;

    public TransferItem(params TransferRepresentation[] representations)
    {
        ArgumentNullException.ThrowIfNull(representations);
        if (representations.Any(r => r is null) || representations.Select(r => r.Format).Distinct(StringComparer.Ordinal).Count() != representations.Length)
            throw new ArgumentException("An item must offer each format once.", nameof(representations));
        this.representations = (TransferRepresentation[])representations.Clone();
    }
}

/// <summary>Ordered items with alternative serialized representations; contains no native resources.</summary>
public sealed record TransferData
{
    readonly TransferItem[] items;
    public IReadOnlyList<TransferItem> Items => Array.AsReadOnly(items);
    public IReadOnlyList<string> Formats => Array.AsReadOnly(items.SelectMany(i => i.Formats).Distinct(StringComparer.Ordinal).ToArray());

    public TransferData(params TransferItem[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Any(i => i is null))
            throw new ArgumentException("Transfer items cannot be null.", nameof(items));
        this.items = (TransferItem[])items.Clone();
    }

    public static TransferData Text(string text) => new(new TransferItem(new TransferRepresentation(TransferFormats.Text, Encoding.UTF8.GetBytes(text))));
    public static TransferData Files(params string[] paths)
    {
        return new(paths.Select(path =>
        {
            if (!Path.IsPathFullyQualified(path))
                throw new ArgumentException("Transfer file paths must be absolute.", nameof(paths));
            var bytes = Encoding.UTF8.GetBytes(new Uri(path).AbsoluteUri + "\r\n");
            return new TransferItem(new TransferRepresentation(TransferFormats.FileList, bytes), new TransferRepresentation(TransferFormats.URIList, bytes));
        }).ToArray());
    }
}

/// <summary>One independent provider cache and release lifetime, used by clipboard and native drag owners.</summary>
internal sealed class TransferSnapshot(App app, TransferData data, Action? release = null) : IDisposable
{
    internal const int MaximumForeignBytes = 64 * 1024 * 1024;
    readonly Dictionary<(int, string), Lazy<byte[]>> cache = new();
    bool disposed, fetching;
    internal TransferData Data => data;
    internal bool Fetching => fetching;

    internal byte[] ReadItem(int index, string format)
    {
        app.CheckThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (fetching)
            throw new InvalidOperationException("Clipboard providers cannot reenter transfer operations.");
        var key = (index, format);
        if (!cache.TryGetValue(key, out var value))
        {
            var representation = data.Items[index].Representations.FirstOrDefault(r => r.Format == format) ?? throw new KeyNotFoundException($"Format {format} is unavailable.");
            value = new(() => representation.Fetch());
            cache.Add(key, value);
        }

        fetching = true;
        try
        {
            return (byte[])value.Value.Clone();
        }
        finally
        {
            fetching = false;
        }
    }

    internal byte[] Read(string format)
    {
        var entries = data.Items.Select((item, index) => (item, index)).Where(e => e.item.Formats.Contains(format)).ToArray();
        if (entries.Length == 0)
            throw new KeyNotFoundException($"Format {format} is unavailable.");
        if (format is not (TransferFormats.Text or TransferFormats.URIList or TransferFormats.FileList))
            return ReadItem(entries[0].index, format);
        var separator = format == TransferFormats.Text ? "\n" : "\r\n";
        return Encoding.UTF8.GetBytes(string.Join(separator, entries.Select(e =>
        {
            var text = Encoding.UTF8.GetString(ReadItem(e.index, format));
            return format == TransferFormats.Text ? text : text.TrimEnd('\r', '\n');
        })));
    }

    internal TransferData Materialize(params string[] formats)
    {
        var selected = formats.Length == 0 ? data.Formats : formats;
        var items = data.Items.Select((item, index) => new TransferItem(item.Formats.Where(selected.Contains).Select(format => new TransferRepresentation(format, ReadItem(index, format))).ToArray())).Where(i => i.Formats.Count > 0).ToArray();
        if (formats.Length > 0 && items.Length == 0)
            throw new KeyNotFoundException("None of the requested formats are available.");
        return new(items);
    }

    public void Dispose()
    {
        app.CheckThread();
        if (disposed)
            return;
        if (fetching)
            throw new InvalidOperationException("A transfer provider cannot replace its snapshot.");
        disposed = true;
        cache.Clear();
        release?.Invoke();
    }
}
