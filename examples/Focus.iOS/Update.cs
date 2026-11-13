using Kumiko.Runtime;

internal sealed partial class FocusApp : IKumiko<FocusModel, FocusMessage>
{
    public static FocusModel Init => new();

    public static (FocusModel, Cmd<FocusMessage>) Update(FocusModel model, FocusMessage message) =>
        (message switch
        {
            FocusMessage.SelectTab tab => Select(model, tab.Id),
            FocusMessage.Navigate navigate => Navigate(model, navigate.Request),
            FocusMessage.OpenDrawer drawer when drawer.Kind is "duration" or "blocking" or "connection" =>
                model with
                {
                    Drawer = drawer.Kind
                },
            FocusMessage.OpenDrawer => model,
            FocusMessage.CloseDrawer => model with { Drawer = null },
            FocusMessage.SetDuration duration when duration.Minutes is 15 or 25 or 45 or 60 =>
                model with
                {
                    Minutes = duration.Minutes,
                    Remaining = duration.Minutes * 60,
                    Running = false,
                    Started = false,
                    Drawer = null
                },
            FocusMessage.SetDuration => model,
            FocusMessage.ToggleBlock block when Targets.Any(target => target.Id == block.Id) => model with
            {
                Blocked = model.Blocked.Contains(block.Id) ? model.Blocked.Remove(block.Id) : model.Blocked.Add(block.Id)
            },
            FocusMessage.ToggleBlock => model,
            FocusMessage.Start => model with { Running = true, Started = true, Remaining = model.Remaining > 0 ? model.Remaining : model.Minutes * 60 },
            FocusMessage.Pause => model with { Running = false },
            FocusMessage.Tick when model.Running => model with { Remaining = Math.Max(0, model.Remaining - 1), Running = model.Remaining > 1 },
            FocusMessage.Tick => model,
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        }, Cmd<FocusMessage>.None);

    /// <summary>Replaces the visit with another tab. Unknown tabs, the current tab and an open drawer change nothing.</summary>
    static FocusModel Select(FocusModel model, string id)
    {
        if (TabFor(id) is not { } tab || model.Drawer is not null || tab == model.Navigation.Current.Route)
            return model;
        return model with
        {
            Navigation = model.Navigation.Replace(tab)
        };
    }

    static FocusTab? TabFor(string id)
    {
        for (var index = 0; index < Tabs.Length; index++)
            if (Tabs[index].Id == id)
                return (FocusTab)index;
        return null;
    }

    /// <summary>Stale or foreign requests leave the history unchanged; the bottom drawer blocks navigation.</summary>
    static FocusModel Navigate(FocusModel model, HistoryRequest request)
    {
        if (model.Drawer is not null)
            return model;
        var history = model.Navigation.Move(request, Host);
        if (ReferenceEquals(history, model.Navigation))
            return model;
        return model with
        {
            Navigation = history
        };
    }
}
