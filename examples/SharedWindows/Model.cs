using Kumiko.Runtime;

namespace SharedWindows;

public sealed record WorkspaceDocument(string Text = "One document, two windows.", bool Dark = false);
public sealed record WorkspaceModel(WorkspaceDocument Document, bool FirstOpen = true, bool SecondOpen = true,
    bool InspectorOpen = true, bool SettingsOpen = false, int FirstSelection = 0, int SecondSelection = 0, string Status = "Local demo");
public abstract partial record WorkspaceWindow
{
    public sealed record Editor(int Id) : WorkspaceWindow;
    public sealed record Inspector : WorkspaceWindow;
    public sealed record Settings : WorkspaceWindow;
}
public abstract partial record WorkspaceMessage
{
    public sealed record Edit(string Text) : WorkspaceMessage;
    public sealed record Select(int Window) : WorkspaceMessage;
    public sealed record OpenInspector : WorkspaceMessage;
    public sealed record OpenSettings : WorkspaceMessage;
    public sealed record OpenEditors : WorkspaceMessage;
    public sealed record SetDark(bool Dark) : WorkspaceMessage;
    public sealed record Close(WorkspaceWindow Window) : WorkspaceMessage;
    public sealed record Load : WorkspaceMessage;
    public sealed record Loaded(WorkspaceDocument? Document) : WorkspaceMessage;
    public sealed record Saved(string Status) : WorkspaceMessage;
}

public sealed partial class WorkspaceApp : IKumiko<WorkspaceModel, WorkspaceMessage>, IKumikoWindows<WorkspaceModel, WorkspaceMessage, WorkspaceWindow>
{
    public static NavigationRoot<WorkspaceMessage> WindowRoot(WorkspaceModel model, WorkspaceWindow window) => NavigationRoot<WorkspaceMessage>.Create(
        NavigationHistory<WorkspaceWindow>.Start(window), "window", route => new WorkspaceView(model, route).Map<WorkspaceMessage>(static message => message),
        static _ => throw new InvalidOperationException("This window has one root screen."));
    public static WorkspaceModel Init => new(new());
    public static (WorkspaceModel, Cmd<WorkspaceMessage>) Update(WorkspaceModel model, WorkspaceMessage message)
    {
        var next = message switch
        {
            WorkspaceMessage.Edit edit => model with { Document = model.Document with { Text = edit.Text }, Status = "Saving…" },
            WorkspaceMessage.SetDark theme => model with { Document = model.Document with { Dark = theme.Dark }, Status = "Saving…" },
            WorkspaceMessage.Select { Window: 1 } => model with { FirstSelection = model.FirstSelection + 1 },
            WorkspaceMessage.Select => model with { SecondSelection = model.SecondSelection + 1 },
            WorkspaceMessage.OpenInspector => model with { InspectorOpen = true },
            WorkspaceMessage.OpenSettings => model with { SettingsOpen = true },
            WorkspaceMessage.OpenEditors => model with { FirstOpen = true, SecondOpen = true },
            WorkspaceMessage.Close { Window: WorkspaceWindow.Editor { Id: 1 } } => model with { FirstOpen = false },
            WorkspaceMessage.Close { Window: WorkspaceWindow.Editor } => model with { SecondOpen = false },
            WorkspaceMessage.Close { Window: WorkspaceWindow.Inspector } => model with { InspectorOpen = false },
            WorkspaceMessage.Close { Window: WorkspaceWindow.Settings } => model with { SettingsOpen = false },
            WorkspaceMessage.Close => model,
            WorkspaceMessage.Load => model,
            WorkspaceMessage.Loaded { Document: { } document } => model with { Document = document, Status = "Loaded" },
            WorkspaceMessage.Loaded => model,
            WorkspaceMessage.Saved saved => model with { Status = saved.Status },
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        };
        var command = message switch
        {
            WorkspaceMessage.Edit or WorkspaceMessage.SetDark => Cmd<WorkspaceMessage>.Debounce<IWorkspaceStore>("save-document", TimeSpan.FromMilliseconds(250),
                async (store, token) =>
                {
                    await store.Save(next.Document, token);
                    return new WorkspaceMessage.Saved("Saved");
                },
                static error => new WorkspaceMessage.Saved("Save failed: " + error.Message)),
            WorkspaceMessage.Load => Cmd<WorkspaceMessage>.Run<IWorkspaceStore>("load-document",
                static async (store, token) => new WorkspaceMessage.Loaded(await store.Load(token)),
                static error => new WorkspaceMessage.Saved("Load failed: " + error.Message)),
            WorkspaceMessage.Select or WorkspaceMessage.OpenInspector or WorkspaceMessage.OpenSettings or
                WorkspaceMessage.OpenEditors or WorkspaceMessage.Close or WorkspaceMessage.Loaded or WorkspaceMessage.Saved => Cmd<WorkspaceMessage>.None,
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        };
        return (next, command);
    }

    public static IEnumerable<WindowSpec<WorkspaceWindow, WorkspaceMessage>> Windows(WorkspaceModel model)
    {
        if (model.FirstOpen)
            yield return Editor(1);
        if (model.SecondOpen)
            yield return Editor(2);
        if (model.InspectorOpen)
            yield return new(new WorkspaceWindow.Inspector(), "Inspector", new WorkspaceMessage.Close(new WorkspaceWindow.Inspector()))
            {
                Width = 400,
                Height = 300
            };
        if (model.SettingsOpen)
            yield return new(new WorkspaceWindow.Settings(), "Settings", new WorkspaceMessage.Close(new WorkspaceWindow.Settings()))
            {
                Width = 400,
                Height = 240
            };
    }
    static WindowSpec<WorkspaceWindow, WorkspaceMessage> Editor(int id) =>
        new(new WorkspaceWindow.Editor(id), $"Document {id}", new WorkspaceMessage.Close(new WorkspaceWindow.Editor(id)))
        {
            Width = 620,
            Height = 380
        };
}
