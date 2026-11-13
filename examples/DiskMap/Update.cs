using System.Collections.Immutable;
using DiskMap.Core;
using Kumiko.Runtime;

namespace DiskMap;

internal sealed partial class DiskMapApp : IKumiko<Model, Msg>
{
    public static NavigationRoot<Msg> Root(Model model) => NavigationRoot<Msg>.Create(model.Navigation, "app",
        route => route == "map" ? new MapScreen(model).Map<Msg>(static message => message)
            : throw new ArgumentOutOfRangeException(nameof(route)), static request => new Msg.Navigate(request));

    public static Model Init => new();

    /// <summary>A model that starts scanning a folder at once.</summary>
    public static Model Start(string root) => new()
    {
        Root = root,
        Generation = 1,
        Scanning = []
    };

    /// <summary>A model showing an already scanned tree, as the demo and offscreen checks use.</summary>
    public static Model Show(string root, Item tree, float width = 0, float height = 0) => Relayout(new Model
    {
        Root = root,
        Trail = [tree],
        Width = width,
        Height = height
    }, false);

    /// <summary>Messages with effects first; everything else changes only the model.</summary>
    public static (Model, Cmd<Msg>) Update(Model model, Msg message)
    {
        if (message is Msg.Scanned scanned && scanned.Generation == model.Generation)
            return (Scanned(model, scanned), Effects.ReadVolume(model.Root));
        if (message is Msg.Reveal reveal)
            return (model with
            {
                Menu = null
            }, Effects.Reveal(reveal.Path));
        if (message is Msg.OpenItem open)
            return (model with
            {
                Menu = null
            }, Effects.Open(open.Path));
        if (message is Msg.ConfirmTrash && model.Confirm is { } confirm)
            return (model with
            {
                Confirm = null
            }, Effects.Trash(confirm.Paths));
        if (message is Msg.ChooseFolder)
            return (model with
            {
                Menu = null
            }, Effects.Choose(model.Root));
        if (message is Msg.Chosen { Path: { } path })
            return (Start(path) with
            {
                Generation = model.Generation + 1,
                Home = model.Home,
                Metric = model.Metric,
                Width = model.Width,
                Height = model.Height
            }, Cmd<Msg>.None);
        return (Apply(model, message), Cmd<Msg>.None);
    }

    static Model Apply(Model model, Msg message) => message switch
    {
        Msg.Navigate navigate => model with { Navigation = model.Navigation.Move(navigate.Request, "app") },
        Msg.Progress progress when progress.Generation == model.Generation => model with { Progress = progress.Value },
        Msg.Progress => model,
        Msg.Scanned => model,
        Msg.ScanFailed failed when failed.Generation == model.Generation => model with { Scanning = null, Progress = null, Error = failed.Error },
        Msg.ScanFailed => model,
        Msg.VolumeRead volume => model with { Volume = volume.Volume },
        Msg.Resized size when Math.Abs(size.Width - model.Width) >= .5f || Math.Abs(size.Height - model.Height) >= .5f =>
            Relayout(model with
            {
                Width = size.Width,
                Height = size.Height
            }, false, relist: false),
        Msg.Resized => model,
        Msg.Select select => Select(model, select.Names),
        Msg.Open open => Open(model, open.Names),
        Msg.OpenSelected when model.Selected is { } selected => Open(model, selected.Names),
        Msg.OpenSelected => model,
        Msg.Up => Up(model),
        Msg.Crumb crumb when crumb.Depth >= 0 && crumb.Depth < model.Trail.Length - 1 => Back(model, crumb.Depth),
        Msg.Crumb => model,
        Msg.Step step => Step(model, step.Delta),
        Msg.SetMetric metric when metric.Metric != model.Metric => Relayout(model with { Metric = metric.Metric }, true),
        Msg.SetMetric => model,
        Msg.SetFilter filter => Relist(model with { Filter = filter.Filter }),
        Msg.FocusFilter => model with { FilterFocus = model.FilterFocus + 1 },
        Msg.ToggleMark when model.Selected is { Item.Kind: not ItemKind.Rest } selected => Mark(model, model.PathOf(selected)),
        Msg.ToggleMark => model,
        Msg.ClearMarks => model with { Marked = model.Marked.Clear() },
        Msg.OpenMenu menu when Resolve(model, menu.Names) is { } item => model with { Menu = new(item, menu.At), Selected = item },
        Msg.OpenMenu => model,
        Msg.CloseMenu => model with { Menu = null },
        Msg.AskTrash => AskTrash(model),
        Msg.ConfirmTrash => model,
        Msg.CancelTrash => model with { Confirm = null },
        Msg.Trashed trashed => Trashed(model, trashed),
        Msg.Rescan when model.Loaded && model.Scanning is null => model with { Generation = model.Generation + 1, Scanning = model.Names, Progress = null, Notice = null, Menu = null },
        Msg.Rescan => model,
        Msg.Chosen => model,
        Msg.SetHelp help => model with { Help = help.Shown, Menu = null },
        Msg.Noted noted => model with { Notice = noted.Notice },
        Msg.Reveal or Msg.OpenItem or Msg.ChooseFolder => model,
        _ => model
    };

    /// <summary>A finished scan: the first one shows the tree, a rescan replaces its folder and keeps the place.</summary>
    static Model Scanned(Model model, Msg.Scanned scanned)
    {
        var done = model with
        {
            Scanning = null,
            Progress = null,
            ScanTime = scanned.Time,
            Error = null
        };
        if (!model.Loaded || scanned.Names.IsEmpty)
        {
            var names = model.Names;
            var trail = Tree.Follow(scanned.Item, names);
            return Reselect(Relayout(done with
            {
                Trail = trail
            }, false), model);
        }
        var full = Tree.Follow(model.Tree!.Value, scanned.Names);
        if (full.Length != scanned.Names.Length + 1)
            return done;
        var replaced = Tree.Replace(full, scanned.Item);
        return Reselect(Relayout(done with
        {
            Trail = Tree.Follow(replaced[0], model.Names)
        }, false), model);
    }

    /// <summary>Keeps the selection and marks that still name an item of the new tree.</summary>
    static Model Reselect(Model next, Model old)
    {
        var selected = old.Selected is { } pick ? Resolve(next, pick.Names) : null;
        var marked = old.Marked.Where(path => Exists(next, path)).ToImmutableSortedSet(StringComparer.Ordinal);
        return next with
        {
            Selected = selected,
            Marked = marked
        };
    }

    static bool Exists(Model model, string path)
    {
        if (model.Tree is not { } tree || Tree.Names(model.Root, path) is not { } names || names.IsEmpty)
            return false;
        var parent = Tree.Follow(tree, names.Take(names.Length - 1));
        return parent.Length == names.Length && parent[^1].Child(names[^1]) is not null;
    }

    /// <summary>The item at names below the open folder, or null.</summary>
    static Pick? Resolve(Model model, ImmutableArray<string> names)
    {
        if (model.Folder is not { } at || names.IsEmpty)
            return null;
        foreach (var name in names)
        {
            if (at.Child(name) is not { } child)
                return Merged(model, names);
            at = child;
        }
        return new(names, at);
    }

    /// <summary>A tile of merged small items, which exists only in the layout.</summary>
    static Pick? Merged(Model model, ImmutableArray<string> names)
    {
        var tiles = model.Layout.Tiles;
        for (int i = 0; i < tiles.Length; i++)
            if (tiles[i].Item.Kind == ItemKind.Rest && tiles[i].Depth == names.Length - 1 && model.Layout.NamesOf(i).SequenceEqual(names))
                return new(names, tiles[i].Item);
        return null;
    }

    static Model Select(Model model, ImmutableArray<string> names) => model with { Selected = Resolve(model, names), Menu = null, Reveal = model.Reveal + 1 };

    static Model Open(Model model, ImmutableArray<string> names)
    {
        if (Resolve(model, names) is not { } item)
            return model;
        if (!item.Item.Opens)
            return model with
            {
                Selected = item,
                Menu = null
            };
        var trail = model.Trail.AddRange(Tree.Follow(model.Folder!.Value, names).Skip(1));
        return Relayout(model with
        {
            Trail = trail,
            Selected = null,
            Menu = null
        }, true);
    }

    static Model Up(Model model) => model.Trail.Length > 1 ? Back(model, model.Trail.Length - 2) : model with { Selected = null, Menu = null };

    /// <summary>Opens an ancestor and selects the child the trail came through.</summary>
    static Model Back(Model model, int depth)
    {
        var child = model.Trail[depth + 1];
        var trail = model.Trail.RemoveRange(depth + 1, model.Trail.Length - depth - 1);
        return Relayout(model with
        {
            Trail = trail,
            Selected = new([child.Name], child),
            Menu = null
        }, true);
    }

    /// <summary>Steps through the open folder's items in the sidebar's order, skipping filtered ones.</summary>
    static Model Step(Model model, int delta)
    {
        var items = model.Items;
        if (items.IsEmpty)
            return model;
        int at = model.Selected is { Names.Length: 1 } pick ? items.IndexOf(pick.Item) : -1;
        int next = at < 0 ? (delta > 0 ? 0 : items.Length - 1) : Math.Clamp(at + delta, 0, items.Length - 1);
        return model with
        {
            Selected = new([items[next].Name], items[next]),
            Menu = null,
            Reveal = model.Reveal + 1
        };
    }

    /// <summary>Lists the open folder's items as the sidebar shows them: by the metric, matching the filter.</summary>
    static Model Relist(Model model)
    {
        if (model.Folder is not { } folder)
            return model with
            {
                Items = []
            };
        var ranked = folder.Ranked(model.Metric);
        var filter = model.Filter;
        return model with
        {
            Items = filter.Length == 0 ? ranked : [.. ranked.Where(n => Matches(n, filter))]
        };
    }

    public static bool Matches(Item node, string filter) => filter.Length == 0 || node.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);

    static Model Mark(Model model, string path) => model with
    {
        Marked = model.Marked.Contains(path) ? model.Marked.Remove(path) : model.Marked.Add(path)
    };

    static Model AskTrash(Model model)
    {
        if (!model.Marked.IsEmpty)
            return model with
            {
                Confirm = new([.. model.Marked], model.Marked.Sum(p => SizeAt(model, p))),
                Menu = null
            };
        if (model.Selected is { Item.Kind: not ItemKind.Rest } selected)
            return model with
            {
                Confirm = new([model.PathOf(selected)], selected.Item.Size),
                Menu = null
            };
        return model with
        {
            Menu = null
        };
    }

    static long SizeAt(Model model, string path)
    {
        if (model.Tree is not { } tree || Tree.Names(model.Root, path) is not { IsEmpty: false } names)
            return 0;
        var parent = Tree.Follow(tree, names.Take(names.Length - 1));
        return parent.Length == names.Length ? parent[^1].Child(names[^1])?.Size ?? 0 : 0;
    }

    /// <summary>Removes the items that reached the Trash from the tree; ancestors lose their sizes.</summary>
    static Model Trashed(Model model, Msg.Trashed trashed)
    {
        if (model.Tree is not { } root)
            return model;
        foreach (var path in trashed.Paths)
        {
            if (Tree.Names(model.Root, path) is not { IsEmpty: false } names)
                continue;
            var parent = Tree.Follow(root, names.Take(names.Length - 1));
            if (parent.Length == names.Length)
                root = Tree.Remove(parent, ImmutableHashSet.Create(StringComparer.Ordinal, names[^1]))[0];
        }
        var next = Relayout(model with
        {
            Trail = Tree.Follow(root, model.Names)
        }, true);
        var notice = trashed.Error ?? (trashed.Paths.Length == 1 ? $"Moved {Path.GetFileName(trashed.Paths[0])} to the Trash" : $"Moved {trashed.Paths.Length} items to the Trash");
        return Reselect(next, model) with
        {
            Notice = notice
        };
    }

    /// <summary>Lays out the open folder for the reported size; glides when the change came from navigation.</summary>
    /// <summary>Lists its items again unless only the size changed.</summary>
    internal static Model Relayout(Model model, bool glide, bool relist = true)
    {
        if (relist)
            model = Relist(model);
        if (model.Folder is not { } folder || model.Width < 1 || model.Height < 1)
            return model with
            {
                Layout = TreemapLayout.Empty,
                Glide = false
            };
        return model with
        {
            Layout = Treemap.Build(folder, model.FolderKey, model.Metric, model.Width, model.Height),
            Glide = glide
        };
    }
}
