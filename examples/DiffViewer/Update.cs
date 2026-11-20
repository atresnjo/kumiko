using System.Collections.Immutable;
using DiffViewer.Core;
using Kumiko.Runtime;
using Kumiko.UI;

namespace DiffViewer;

internal sealed partial class DiffApp : IKumiko<Model, Msg>
{
    public static NavigationRoot<Msg> Root(Model model) => NavigationRoot<Msg>.Create(model.Navigation, "app",
        route => route == "diff" ? new DiffScreen(model).Map<Msg>(static message => message)
            : throw new ArgumentOutOfRangeException(nameof(route)), static request => new Msg.Navigate(request));

    public static Model Init => new();

    /// <summary>Applies a message, then loads the pull requests the palette newly shows in the background.</summary>
    public static (Model, Cmd<Msg>) Update(Model model, Msg message)
    {
        var (next, command) = Step(model, message);
        var (prefetching, prefetch) = Prefetch(next);
        return (prefetching, Cmd<Msg>.Batch(command, prefetch));
    }

    static (Model, Cmd<Msg>) Step(Model model, Msg message)
    {
        if (message is Msg.Loaded loaded && loaded.Generation == model.Generation)
            return Remember(Loaded(model, loaded.Document));
        if (message is Msg.QuickAccept accept && model.Quick is { } quick)
            return Accept(model, quick, accept.Index);
        if (message is Msg.QuickQuery query && model.Quick is not null)
            return SearchHost(Quick(model, query.Query, 0));
        if (message is Msg.OpenProject open)
            return (OpenProject(model, open.Path), Cmd<Msg>.None);
        if (message is Msg.OpenQuick palette)
            return Palette(model, palette.Query);
        if (message is Msg.RefreshPulls)
            return FetchPulls(model);
        if (message is Msg.PullsFound pulls)
            return PullsFound(model, pulls);
        if (message is Msg.PullsExpired expired)
            return Expired(model, expired);
        return (Apply(model, message), Cmd<Msg>.None);
    }

    static Model Apply(Model model, Msg message) => message switch
    {
        Msg.Loaded => model,
        Msg.Failed failed when failed.Generation == model.Generation => model with { Loading = false, Error = failed.Error },
        Msg.Failed => model,
        Msg.Reload or Msg.Changed => model with { Generation = model.Generation + 1, Loading = true },
        Msg.SetMode mode => Relayout(model with { Mode = mode.Mode }),
        Msg.ToggleWhitespace => model with { IgnoreWhitespace = !model.IgnoreWhitespace, Generation = model.Generation + 1, Loading = true },
        Msg.ToggleSidebar => model with { SidebarHidden = !model.SidebarHidden },
        Msg.ToggleZen => Zen(model, model.Zen is null),
        Msg.LeftFullScreen => Zen(model, false),
        Msg.Move move => Cursor(model, model.Cursor + move.Rows, VirtualRevealMode.Nearest),
        Msg.Top => Cursor(model, 0, VirtualRevealMode.Nearest),
        Msg.Bottom => Cursor(model, model.Layout.Count - 1, VirtualRevealMode.Nearest),
        // Zen mode reads its file whole as one hunk, so there the hunk keys step between blocks of changed lines.
        Msg.NextHunk next => Jump(model, model.Zen is null ? model.Layout.HunkRows : model.Layout.ChangeRows, next.Direction),
        Msg.NextFile next when model.Zen is not null => Show(model, Math.Clamp(model.CursorFileIndex + next.Direction, 0, model.Document.Files.Length - 1)),
        Msg.NextFile next => Jump(model, model.Layout.FileRows, next.Direction),
        Msg.Point point => model with { Cursor = Math.Clamp(point.Row, 0, Math.Max(0, model.Layout.Count - 1)) },
        Msg.TextPress press => Press(model, press),
        Msg.TextDrag drag when model.Selection is { Dragging: true } selection => Drag(model, selection, drag.Column),
        Msg.TextRelease when model.Selection is { Dragging: true } selection => Release(model, selection),
        Msg.SelectionStep step when model.Selection is { Found.Length: > 0 } selection => Step(model, selection, step.Direction),
        Msg.ClearSelection => model with { Selection = null },
        Msg.SelectFile select => Show(model with { Finder = null }, select.File),
        Msg.ToggleFile toggle => Toggle(model, toggle.File < 0 ? model.CursorFileIndex : toggle.File),
        Msg.SetAllCollapsed all => Collapse(model, all.Collapsed),
        Msg.SetHelp help => model with { Help = help.Open },
        Msg.OpenFinder => model with { Finder = new(), Search = null, Help = false },
        Msg.FinderQuery query => model with { Finder = new(query.Query) },
        Msg.FinderStep step when model.Finder is { } finder => model with
        {
            Finder = finder with
            {
                Selected = Math.Clamp(finder.Selected + step.Delta, 0, Math.Max(0, FileFinder.Match(model.Document.Files, finder.Query).Length - 1))
            }
        },
        Msg.FinderAccept when model.Finder is { } finder => Accept(model, finder),
        Msg.OpenSearch => model with { Search = model.Search ?? new(), Finder = null, Help = false },
        Msg.SearchQuery query => Find(model, query.Query),
        Msg.SearchStep step when model.Search is { Found.Length: > 0 } search => Step(model, search, step.Direction),
        Msg.CloseOverlay => model with { Finder = null, Search = null, Help = false, Quick = null },
        Msg.CommitsFound commits => CommitsFound(model, commits),
        Msg.HostsFound hosts => Requery(model with { SshHosts = hosts.Hosts }),
        Msg.ContextFound found when found.Project == model.ProjectId => Requery(model with
        {
            Context = new(found.Project, found.Worktrees, found.Remote, found.Pulls, found.Error, false)
        }),
        // Results move the highlight to the first row: a repository, not the bare path typed so far.
        Msg.HostReposFound found when model.Quick is { } quick => Quick(model with
        {
            HostRepos = model.HostRepos.SetItem(found.Host, new(found.Paths, false, found.Error))
        }, quick.Query, 0),
        Msg.OpenPull pull => OpenPull(model, pull.Pull),
        Msg.PullPrefetched prefetched => Prefetched(model, prefetched),
        Msg.OpenCommit commit => OpenCommit(model, commit.Commit),
        Msg.QuickStep step when model.Quick is { } quick => model with
        {
            Quick = quick with
            {
                Selected = Math.Clamp(quick.Selected + step.Delta, 0, Math.Max(0, quick.Items.Length - 1))
            }
        },
        Msg.ProjectsFound found => Found(model, found),
        Msg.Rescan => model with { Scanning = model.Store is not null, Scanned = false },
        Msg.SetSource source => model with
        {
            Request = new(source.Kind, model.ProjectPath, "HEAD") { Host = model.Request.Host },
            Generation = model.Generation + 1,
            Loading = true
        },
        Msg.FinderStep or Msg.FinderAccept or Msg.SearchStep or Msg.SelectionStep or Msg.TextDrag or Msg.TextRelease => model,
        Msg.QuickQuery or Msg.QuickStep or Msg.QuickAccept or Msg.OpenProject or Msg.Saved or Msg.PickHost or Msg.SearchHost or Msg.HostReposFound or Msg.ContextFound => model,
        Msg.OpenQuick or Msg.RefreshPulls or Msg.PullsFound or Msg.PullsExpired => model,
        Msg.Navigate navigate => model with { Navigation = model.Navigation.Move(navigate.Request, "app") },
        _ => model
    };

    static Model Cursor(Model model, int row, VirtualRevealMode mode) => model with
    {
        Cursor = Math.Clamp(row, 0, Math.Max(0, model.Layout.Count - 1)),
        Reveal = model.Reveal + 1,
        RevealMode = mode
    };

    /// <summary>Moves to the next/previous start row (hunk or file header) relative to the cursor.</summary>
    static Model Jump(Model model, ImmutableArray<int> starts, int direction)
    {
        if (starts.IsEmpty)
            return model;
        int index = starts.BinarySearch(model.Cursor);
        int target;
        if (direction > 0)
            target = index >= 0 ? index + 1 : ~index;
        else
            target = index >= 0 ? index - 1 : ~index - 1;
        target = Math.Clamp(target, 0, starts.Length - 1);
        return Cursor(model, starts[target], VirtualRevealMode.Top);
    }

    static Model Loaded(Model model, DiffDocument document)
    {
        // The cursor stays on the same file and line when they still exist after a reload.
        var file = model.CursorFile;
        int line = model.Cursor < model.Layout.Count ? RowLayout.LineOf(model.Layout.Rows[model.Cursor]) : -1;
        var anchor = file is not null && line >= 0 ? file.Lines[line] : default;
        var next = model with
        {
            Document = document,
            Loading = false,
            Error = null
        };
        int index = file is null ? -1 : IndexOf(document, file.Path);
        // Zen mode moves on to the cursor's file, or the first one, when its file is no longer changed.
        if (next.Zen is { } zen && IndexOf(document, zen.Path) < 0 && document.Files.Length > 0)
            next = next with
            {
                Zen = zen with
                {
                    Path = document.Files[Math.Max(0, index)].Path
                }
            };
        next = next with
        {
            Layout = Rows(next)
        };
        int row = 0;
        if (index >= 0)
        {
            var lines = document.Files[index].Lines;
            int match = -1;
            for (int i = 0; line >= 0 && i < lines.Length && match < 0; i++)
                if (lines[i].Kind == anchor.Kind && (anchor.Kind == LineKind.Removed ? lines[i].Old == anchor.Old : lines[i].New == anchor.New))
                    match = i;
            row = next.Layout.Find(index, match);
        }
        next = next with
        {
            Cursor = Math.Min(row, Math.Max(0, next.Layout.Count - 1)),
            Reveal = model.RevealLoaded ? model.Reveal + 1 : model.Reveal,
            RevealMode = model.RevealLoaded ? VirtualRevealMode.Nearest : model.RevealMode,
            RevealLoaded = false
        };
        return Reselect(next.Search is { } search ? Find(next, search.Query) with
        {
            Cursor = next.Cursor
        } : next);
    }

    /// <summary>Rebuilds rows (after a mode or collapse change), keeping the cursor on its file and line.</summary>
    static Model Relayout(Model model, int focusFile = -1)
    {
        int file = focusFile >= 0 ? focusFile : model.CursorFileIndex;
        int line = focusFile < 0 && model.Cursor < model.Layout.Count ? RowLayout.LineOf(model.Layout.Rows[model.Cursor]) : -1;
        var layout = Rows(model);
        var next = model with
        {
            Layout = layout,
            Cursor = file < 0 ? 0 : layout.Find(file, line),
            Reveal = model.Reveal + 1,
            RevealMode = VirtualRevealMode.Nearest
        };
        return Reselect(next.Search is { } search ? Find(next, search.Query) with
        {
            Cursor = next.Cursor
        } : next);
    }

    /// <summary>The document's rows; zen mode lays out its file alone.</summary>
    static RowLayout Rows(Model model) => RowLayout.Build(model.Document.Files, model.Mode, model.Collapsed,
        model.Zen is { } zen ? IndexOf(model.Document, zen.Path) : -1);

    static int IndexOf(DiffDocument document, string path)
    {
        for (int i = 0; i < document.Files.Length; i++)
            if (document.Files[i].Path == path)
                return i;
        return -1;
    }

    /// <summary>
    /// Zen mode shows the cursor's file alone and whole in the split view; leaving it returns to the mode it started
    /// from and to every file's hunks, with the cursor on the same line. Either way the diff reloads, since only the
    /// zen file is read whole.
    /// </summary>
    static Model Zen(Model model, bool on)
    {
        if (on == (model.Zen is not null))
            return model;
        if (on && model.Document.Files.IsEmpty)
            return model;
        return Relayout(model with
        {
            Zen = on ? new(model.Document.Files[Math.Max(0, model.CursorFileIndex)].Path, model.Mode) : null,
            Mode = on ? ViewMode.Split : model.Zen!.Return,
            Generation = model.Generation + 1,
            Loading = true,
            RevealLoaded = true
        });
    }

    /// <summary>Shows a file: in zen mode it replaces the zen file, otherwise the cursor moves to its header.</summary>
    static Model Show(Model model, int file)
    {
        if (file < 0 || file >= model.Document.Files.Length)
            return model;
        if (model.Zen is { } zen && zen.Path != model.Document.Files[file].Path)
            model = Relayout(model with
            {
                Zen = zen with
                {
                    Path = model.Document.Files[file].Path
                },
                Generation = model.Generation + 1,
                Loading = true,
                RevealLoaded = true
            }, file);
        return Cursor(model, model.Layout.FileRows[file], VirtualRevealMode.Top);
    }

    static Model Toggle(Model model, int file)
    {
        if (file < 0 || file >= model.Document.Files.Length)
            return model;
        var path = model.Document.Files[file].Path;
        var collapsed = model.Collapsed.Contains(path) ? model.Collapsed.Remove(path) : model.Collapsed.Add(path);
        return Relayout(model with
        {
            Collapsed = collapsed
        }, file);
    }

    static Model Collapse(Model model, bool all)
    {
        var collapsed = all ? model.Collapsed.Union(model.Document.Files.Select(f => f.Path)) : model.Collapsed.Clear();
        return Relayout(model with
        {
            Collapsed = collapsed
        }, Math.Max(0, model.CursorFileIndex));
    }

    static Model Accept(Model model, Finder finder)
    {
        var matches = FileFinder.Match(model.Document.Files, finder.Query);
        if (matches.Length == 0)
            return model with
            {
                Finder = null
            };
        int file = matches[Math.Clamp(finder.Selected, 0, matches.Length - 1)];
        if (model.Zen is null && model.Collapsed.Contains(model.Document.Files[file].Path))
            model = Toggle(model, file);
        return Show(model with
        {
            Finder = null
        }, file);
    }

    static Model Find(Model model, string query)
    {
        var matches = query.Length == 0 ? [] : TextSearch.Rows(model.Document.Files, model.Layout, query);
        int current = 0;
        while (current < matches.Length && matches[current] < model.Cursor)
            current++;
        var search = new Search { Query = query, Found = matches, Current = matches.Length == 0 ? 0 : current % matches.Length };
        var next = model with
        {
            Search = search
        };
        return matches.Length == 0 ? next : Cursor(next, matches[search.Current], VirtualRevealMode.Top);
    }

    static Model Step(Model model, Search search, int direction)
    {
        var found = search.Found;
        int current = ((search.Current + direction) % found.Length + found.Length) % found.Length;
        return Cursor(model with
        {
            Search = search with
            {
                Current = current
            }
        }, found[current], VirtualRevealMode.Top);
    }
}
