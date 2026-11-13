namespace Kumiko.Documents;

/// <summary>Content fingerprint. Missing is distinct from an empty file.</summary>
public sealed record FileVersion(bool Exists, string Digest)
{
    public static FileVersion Missing { get; } = new(false, "");
}

public enum DocumentOperation
{
    Save,
    Revert
}

/// <summary>Captured immutable input to an effect, also used to reject stale replies.</summary>
public sealed record DocumentRequest<T>(string Id, long Sequence, long Revision, DocumentOperation Operation,
    string Path, T Value, FileVersion Expected) where T : notnull;
public sealed record DocumentRead<T>(string Path, T Value, FileVersion Version) where T : notnull;
public sealed record DocumentSaved<T>(DocumentRequest<T> Request, FileVersion Version) where T : notnull;
public sealed record DocumentStart<T>(Document<T> Document, DocumentRequest<T> Request) where T : notnull;

/// <summary>Model-owned document. Methods are pure; storage belongs in Cmd effects.</summary>
public sealed record Document<T> where T : notnull
{
    public required string Id
    {
        get; init;
    }
    public required string Title
    {
        get; init;
    }
    public required T Value
    {
        get; init;
    }
    public required T SavedValue
    {
        get; init;
    }
    public string? Path
    {
        get; init;
    }
    public FileVersion Version { get; init; } = FileVersion.Missing;
    public bool HasSaved
    {
        get; init;
    }
    public long Revision
    {
        get; init;
    }
    public long Sequence
    {
        get; init;
    }
    public DocumentRequest<T>? Pending
    {
        get; init;
    }
    public bool Conflict
    {
        get; init;
    }
    public string? Error
    {
        get; init;
    }
    public bool Dirty => !HasSaved || !EqualityComparer<T>.Default.Equals(Value, SavedValue);
    public bool CanClose => !Dirty && Pending is null && !Conflict;

    public static Document<T> Create(string id, string title, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return new()
        {
            Id = id,
            Title = title,
            Value = value,
            SavedValue = value
        };
    }

    public static Document<T> Open(string id, DocumentRead<T> read) => Create(id, System.IO.Path.GetFileName(read.Path), read.Value) with
    {
        Path = read.Path,
        Version = read.Version,
        HasSaved = true
    };

    public Document<T> Edit(T value) => EqualityComparer<T>.Default.Equals(Value, value) ? this : this with
    {
        Value = value,
        Revision = checked(Revision + 1),
        Error = null
    };

    /// <summary>Save As never overwrites an existing destination unless its fingerprint is supplied explicitly.</summary>
    public DocumentStart<T> BeginSave(string? path = null, FileVersion? expected = null)
    {
        if (Pending is not null)
            throw new InvalidOperationException("Complete the pending document operation first.");
        string target = path ?? Path ?? throw new InvalidOperationException("Choose a Save As destination first.");
        if (!System.IO.Path.IsPathFullyQualified(target))
            throw new ArgumentException("Document destinations must be absolute paths.", nameof(path));
        var version = expected ?? (path is null || path == Path ? Version : FileVersion.Missing);
        var request = new DocumentRequest<T>(Id, checked(Sequence + 1), Revision, DocumentOperation.Save, target, Value, version);
        return new(this with
        {
            Sequence = request.Sequence,
            Pending = request,
            Error = null
        }, request);
    }

    public Document<T> Saved(DocumentSaved<T> result)
    {
        if (!Matches(result.Request) || result.Request.Operation != DocumentOperation.Save)
            return this;
        var captured = Pending!;
        return this with
        {
            Path = captured.Path,
            Title = System.IO.Path.GetFileName(captured.Path),
            Version = result.Version,
            SavedValue = captured.Value,
            HasSaved = true,
            Pending = null,
            Conflict = false,
            Error = null
        };
    }

    bool Matches(DocumentRequest<T> request) => Pending is { } pending && pending.Id == request.Id &&
        pending.Sequence == request.Sequence && pending.Revision == request.Revision && pending.Operation == request.Operation &&
        pending.Path == request.Path && pending.Expected == request.Expected;

    public Document<T> Failed(DocumentRequest<T> request, string error, bool conflict = false) => !Matches(request) ? this : this with
    {
        Pending = null,
        Conflict = Conflict || conflict,
        Error = error
    };

    public DocumentStart<T> BeginRevert()
    {
        if (Pending is not null || Path is null)
            throw new InvalidOperationException("Revert requires a saved document without pending work.");
        var request = new DocumentRequest<T>(Id, checked(Sequence + 1), Revision, DocumentOperation.Revert, Path, Value, Version);
        return new(this with
        {
            Sequence = request.Sequence,
            Pending = request
        }, request);
    }

    public Document<T> Reverted(DocumentRequest<T> request, DocumentRead<T> read)
    {
        if (!Matches(request) || request.Operation != DocumentOperation.Revert || read.Path != Pending!.Path)
            return this;
        if (Revision != request.Revision)
            return this with
            {
                Pending = null,
                Conflict = true,
                Error = "Document changed while reverting."
            };
        return this with
        {
            Value = read.Value,
            SavedValue = read.Value,
            HasSaved = true,
            Version = read.Version,
            Revision = checked(Revision + 1),
            Pending = null,
            Conflict = false,
            Error = null
        };
    }

    public Document<T> Observe(FileVersion version) => version == Version ? this : this with { Conflict = true };
    public Document<T> Reload(DocumentRead<T> read)
    {
        if (read.Path != Path)
            throw new ArgumentException("Reload belongs to a different path.", nameof(read));
        if (read.Version == Version)
            return this;
        if (Dirty || Pending is not null)
            return this with
            {
                Conflict = true
            };
        return this with
        {
            Value = read.Value,
            SavedValue = read.Value,
            Version = read.Version,
            Revision = checked(Revision + 1),
            Conflict = false
        };
    }

    public Document<T> Duplicate(string id, string title) => Create(id, title, Value);

    /// <summary>Apply a successful filesystem move. Pending saves must finish before moving.</summary>
    public Document<T> Moved(string oldPath, string newPath, FileVersion version)
    {
        if (Path != oldPath || Version != version || Pending is not null)
            throw new InvalidOperationException("Move no longer belongs to this document version.");
        return this with
        {
            Path = newPath,
            Title = System.IO.Path.GetFileName(newPath)
        };
    }
}
