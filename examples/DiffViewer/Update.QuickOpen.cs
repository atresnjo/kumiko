using System.Collections.Immutable;
using DiffViewer.Core;
using DiffViewer.Sources;
using Kumiko.Runtime;

namespace DiffViewer;

internal sealed partial class DiffApp
{
    /// <summary>Opens or re-queries the palette, keeping the highlight on the same item when it is still listed.</summary>
    static Model Quick(Model model, string query, int selected, string? keep = null)
    {
        var items = QuickOpen.Build(model, query);
        for (int i = 0; keep is not null && i < items.Length; i++)
            if (items[i].Id == keep)
                selected = i;
        return model with
        {
            Quick = new QuickOpen(query, Math.Clamp(selected, 0, Math.Max(0, items.Length - 1))) { Items = items }
        };
    }

    static Model Found(Model model, Msg.ProjectsFound found)
    {
        var next = model with
        {
            Projects = found.Projects,
            Cached = true,
            Scanned = model.Scanned || found.Scanned,
            Scanning = model.Scanning && !found.Scanned
        };
        return Requery(next);
    }

    /// <summary>Closes the palette, then runs the chosen item's message as if it had been sent directly.</summary>
    static (Model, Cmd<Msg>) Accept(Model model, QuickOpen quick, int index)
    {
        var at = index >= 0 ? index : quick.Selected;
        // Refreshing pull requests keeps the palette open to show them.
        if (at < quick.Items.Length && quick.Items[at].Message is Msg.RefreshPulls)
            return FetchPulls(model);
        var closed = model with
        {
            Quick = null
        };
        return at < quick.Items.Length ? Update(closed, quick.Items[at].Message) : (closed, Cmd<Msg>.None);
    }

    /// <summary>
    /// Replaces the diff with a repository's working tree. The new generation cancels the previous load and its
    /// result is ignored; folds, search and the cursor belong to the old project and are dropped.
    /// </summary>
    static Model OpenProject(Model model, string path) => Remote.TryParse(path, out var host, out var remote)
        ? Open(model, new(SourceKind.WorkingTree, remote.Length > 1 ? remote.TrimEnd('/') : remote, "HEAD") { Host = host })
        : Open(model, new(SourceKind.WorkingTree, Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), "HEAD"));

    /// <summary>Replaces the diff with another request, as opening a project, pull request or commit does.</summary>
    static Model Open(Model model, DiffRequest request)
    {
        return model with
        {
            Request = request,
            Generation = model.Generation + 1,
            Loading = true,
            Error = null,
            Document = DiffDocument.Empty,
            Layout = RowLayout.Empty,
            Collapsed = model.Collapsed.Clear(),
            Cursor = 0,
            Reveal = model.Reveal + 1,
            Finder = null,
            Search = null,
            Help = false,
            Quick = null
        };
    }

    /// <summary>Puts a loaded repository first in the recent list and saves the list off the UI thread.</summary>
    static (Model, Cmd<Msg>) Remember(Model model)
    {
        if (model.Request.Kind is SourceKind.Paths or SourceKind.Demo || model.Document.Root.Length == 0 || model.Store is not { } store)
            return (model, Cmd<Msg>.None);
        var root = new Place(model.Document.Root, model.Request.Host).Id;
        if (model.Recent.FirstOrDefault() == root)
            return (model, Cmd<Msg>.None);
        ImmutableArray<string> recent = [root, .. model.Recent.Where(p => p != root).Take(ProjectStore.RecentLimit - 1)];
        return (model with
        {
            Recent = recent
        }, Cmd<Msg>.Run("recent", _ =>
        {
            ProjectFiles.SaveRecent(store, recent);
            return Task.FromResult<Msg>(new Msg.Saved());
        }, _ => new Msg.Saved()));
    }
}
