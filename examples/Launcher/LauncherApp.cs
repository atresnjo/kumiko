using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Serialization;
using Kumiko.Desktop;
using Kumiko.Rendering;
using Kumiko.Runtime;

namespace Launcher;

public enum LauncherRoute
{
    Search
}
public enum HostAction
{
    None, Show, Hide, Refresh, Launch, Startup, Shortcut, Quit
}
public sealed record HostRequest(int Sequence = 0, HostAction Action = HostAction.None, string Value = "");
public sealed record LauncherModel(NavigationHistory<LauncherRoute> Navigation)
{
    public ImmutableArray<InstalledApplication> Entries { get; init; } = [];
    public string Query { get; init; } = "";
    public long SelectionRequest
    {
        get; init;
    }
    public int Selected
    {
        get; init;
    }
    public bool Loading { get; init; } = true;
    public bool Visible
    {
        get; init;
    }
    public bool FocusSearch { get; init; } = true;
    public bool Settings
    {
        get; init;
    }
    public string? Error
    {
        get; init;
    }
    public StartupStatus Startup { get; init; } = new(StartupState.Disabled);
    public string Shortcut { get; init; } = "Alt+Shift+Space";
    public string ShortcutDraft { get; init; } = "Alt+Shift+Space";
    public HostRequest Request { get; init; } = new();
    [JsonIgnore]
    public ImmutableArray<InstalledApplication> Results => Entries.Where(a => CultureInfo.CurrentCulture.CompareInfo.IndexOf(a.Name, Query.Trim(), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0).ToImmutableArray();
}

public abstract partial record LauncherMsg
{
    public sealed record Query(string Value) : LauncherMsg;
    public sealed record Move(int Delta) : LauncherMsg;
    public sealed record Open(string? Id = null) : LauncherMsg;
    public sealed record Summon : LauncherMsg;
    public sealed record Dismiss : LauncherMsg;
    public sealed record DidHide : LauncherMsg;
    public sealed record FocusedSearch : LauncherMsg;
    public sealed record Refresh : LauncherMsg;
    public sealed record Catalog(ImmutableArray<InstalledApplication> Entries, string? Error = null) : LauncherMsg;
    public sealed record Failure(string Error) : LauncherMsg;
    public sealed record ShowSettings : LauncherMsg;
    public sealed record Startup(bool Enabled) : LauncherMsg;
    public sealed record StartupRead(StartupStatus Status) : LauncherMsg;
    public sealed record ShortcutDraft(string Value) : LauncherMsg;
    public sealed record SaveShortcut : LauncherMsg;
    public sealed record ShortcutSaved(string Value) : LauncherMsg;
    public sealed record Quit : LauncherMsg;
    public sealed record Navigate(HistoryRequest Request) : LauncherMsg;
}

public sealed partial class LauncherApp : IKumiko<LauncherModel, LauncherMsg>
{
    public static LauncherModel Init => new(NavigationHistory<LauncherRoute>.Start(LauncherRoute.Search));
    public static NavigationRoot<LauncherMsg> Root(LauncherModel model) => NavigationRoot<LauncherMsg>.Create(model.Navigation, "launcher",
        _ => new LauncherScreen(model).Map<LauncherMsg>(static message => message), static request => new LauncherMsg.Navigate(request));

    public static (LauncherModel, Cmd<LauncherMsg>) Update(LauncherModel model, LauncherMsg message) => (message switch
    {
        LauncherMsg.Query query => model with { Query = query.Value, Selected = 0, SelectionRequest = model.SelectionRequest + 1, Error = null },
        LauncherMsg.Move move => model with { Selected = Math.Clamp(model.Selected + move.Delta, 0, Math.Max(0, model.Results.Length - 1)), SelectionRequest = model.SelectionRequest + 1 },
        LauncherMsg.Open open => Open(model, open.Id),
        LauncherMsg.Summon => Request(model with { Visible = true, Settings = false, FocusSearch = true, Query = "", Selected = 0, SelectionRequest = model.SelectionRequest + 1 }, HostAction.Show),
        LauncherMsg.Dismiss => model.Settings ? model with { Settings = false, FocusSearch = true } : Request(model with { Visible = false }, HostAction.Hide),
        LauncherMsg.DidHide => model with { Visible = false },
        LauncherMsg.FocusedSearch => model with { FocusSearch = false },
        LauncherMsg.Refresh => Request(model with { Loading = true, Error = null }, HostAction.Refresh),
        LauncherMsg.Catalog catalog => model with { Entries = catalog.Entries, Selected = 0, SelectionRequest = model.SelectionRequest + 1, Loading = false, Error = catalog.Error ?? model.Error },
        LauncherMsg.Failure failure => model with { Error = failure.Error },
        LauncherMsg.ShowSettings => model with { Settings = !model.Settings, FocusSearch = model.Settings },
        LauncherMsg.Startup startup => Request(model, HostAction.Startup, startup.Enabled.ToString()),
        LauncherMsg.StartupRead startup => model with { Startup = startup.Status, Error = null },
        LauncherMsg.ShortcutDraft shortcut => model with { ShortcutDraft = shortcut.Value },
        LauncherMsg.SaveShortcut => Request(model, HostAction.Shortcut, model.ShortcutDraft.Trim()),
        LauncherMsg.ShortcutSaved shortcut => model with { Shortcut = shortcut.Value, ShortcutDraft = shortcut.Value, Error = null },
        LauncherMsg.Quit => Request(model, HostAction.Quit),
        LauncherMsg.Navigate => model,
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    }, Cmd<LauncherMsg>.None);

    static LauncherModel Request(LauncherModel model, HostAction action, string value = "") => model with { Request = new(model.Request.Sequence + 1, action, value) };

    static LauncherModel Open(LauncherModel model, string? id)
    {
        if (model.Loading)
            return model;
        var results = model.Results;
        var target = id is null ? results.ElementAtOrDefault(model.Selected) : results.FirstOrDefault(e => e.Id == id);
        return target is null ? model : Request(model with
        {
            Visible = false,
            Error = null
        }, HostAction.Launch, target.Id);
    }
}
