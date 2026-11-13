using System.Collections.Immutable;
using Kumiko.Desktop;
using Kumiko.Runtime;
using Kumiko.UI;

namespace Kumiko.Documents;

public enum CloseDecision
{
    Cancel,
    Discard,
    Saved
}
public sealed record DocumentClose(ImmutableList<string> Ids, ImmutableDictionary<string, long> Accepted, string? PendingId);

/// <summary>Pure close/quit transaction. No window disappears until all decisions succeed.</summary>
public sealed record DocumentWorkspace<T>(ImmutableList<Document<T>> Documents) where T : notnull
{
    public DocumentClose? Close
    {
        get; init;
    }
    public ImmutableList<string> RecentFiles { get; init; } = [];
    public DocumentWorkspace<T> Put(Document<T> document)
    {
        int index = Documents.FindIndex(d => d.Id == document.Id);
        return this with
        {
            Documents = index < 0 ? Documents.Add(document) : Documents.SetItem(index, document)
        };
    }
    public DocumentWorkspace<T> Remember(string path, int limit = 10)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        return this with
        {
            RecentFiles = RecentFiles.Remove(path).Insert(0, path).Take(limit).ToImmutableList()
        };
    }
    public DocumentWorkspace<T> RequestClose(string id) => Begin(ImmutableList.Create(id));
    public DocumentWorkspace<T> RequestQuit() => Begin(Documents.Select(d => d.Id).ToImmutableList());
    DocumentWorkspace<T> Begin(ImmutableList<string> ids)
    {
        if (Close is not null)
            return this;
        return (this with
        {
            Close = new(ids, ImmutableDictionary<string, long>.Empty, null)
        }).Advance();
    }
    DocumentWorkspace<T> Advance()
    {
        if (Close is not { } close)
            return this;
        var pending = Documents.FirstOrDefault(d => close.Ids.Contains(d.Id) && (d.Pending is not null || !d.CanClose && (!close.Accepted.TryGetValue(d.Id, out var revision) || revision != d.Revision)));
        if (pending is not null)
            return this with
            {
                Close = close with
                {
                    PendingId = pending.Id
                }
            };
        return this with
        {
            Documents = Documents.RemoveAll(d => close.Ids.Contains(d.Id)),
            Close = null
        };
    }
    public DocumentWorkspace<T> ResolveClose(string id, CloseDecision decision)
    {
        if (Close is not { } close || close.PendingId != id)
            return this;
        if (decision == CloseDecision.Cancel)
            return this with
            {
                Close = null
            };
        var document = Documents.FirstOrDefault(d => d.Id == id);
        // Discard never races an in-flight write; wait for its completion/cancellation first.
        if (document?.Pending is not null || decision == CloseDecision.Saved && document is { CanClose: false })
            return this;
        return (this with
        {
            Close = close with
            {
                Accepted = close.Accepted.SetItem(id, document?.Revision ?? 0),
                PendingId = null
            }
        }).Advance();
    }
}

/// <summary>Shared action projection for toolbars, menus, palettes and document windows.</summary>
public static class DocumentActions
{
    public static IEnumerable<DesktopAction<TMessage>> For<T, TMessage>(Document<T> document,
        TMessage save, TMessage saveAs, TMessage revert, TMessage close) where T : notnull
    {
        yield return new("document.save", "Save", save, Key.S, Enabled: document.Pending is null && document.Dirty);
        yield return new("document.save-as", "Save As…", saveAs, Key.S, Modifiers.Primary | Modifiers.Shift, Enabled: document.Pending is null);
        yield return new("document.revert", "Revert", revert, Enabled: document.Path is not null && document.Pending is null);
        yield return new("document.close", "Close", close, Key.W);
    }
}
