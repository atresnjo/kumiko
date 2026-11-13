using System.Text;
using System.Text.Json;
using Kumiko.UI;

namespace Kumiko.Runtime;

public sealed partial class Program<TModel, TMessage>
{
    readonly Dictionary<int, int> forwardSteps = [];
    readonly Dictionary<int, string> eventNames = [];
    bool debugEnabled, debugExpanded;
    int debugRevealed = -1, detailStep = -1;
    DebugTab debugTab, detailTab;
    string detailText = "";
    float debugViewportHeight;
    enum DebugTab
    {
        Message, Changes, Model
    }
    enum DebugOperation
    {
        None, Back, Forward, Latest, Pause, Resume, Select, Expand
    }
    readonly record struct DebugAction(DebugOperation Operation, int Step = 0);

    /// <summary>Shows the built-in event debugger in this program's view. Opt-in per app.</summary>
    public bool DebugEnabled
    {
        get => debugEnabled;
        set
        {
            CheckThread();
            debugEnabled = value;
            if (value)
                retainHistory = true;
            if (!value)
                foreach (var context in contextWindows.Keys.Append(lastContext).OfType<Context>().Distinct())
                    context.ClearDebugger();
            InvalidateWindows();
        }
    }
    public bool IsPaused => frozen;
    public bool CanGoBack => history[Position].Parent >= 0;
    public bool CanGoForward => forwardSteps.ContainsKey(Position);

    public bool Back()
    {
        CheckThread();
        if (!CanGoBack)
            return false;
        Step(history[Position].Parent);
        return true;
    }

    public bool Forward()
    {
        CheckThread();
        if (!forwardSteps.TryGetValue(Position, out var next))
            return false;
        Step(next);
        return true;
    }

    /// <summary>Resumes sources for the selected model. Already recorded commands are never restarted.</summary>
    public void Resume()
    {
        CheckThread();
        if (building is not null)
            throw new InvalidOperationException("History navigation cannot run inside a view.");
        if (!frozen)
            return;
        frozen = false;
        Reconcile();
        window?.Invalidate();
    }

    void RememberRoute(int step)
    {
        for (var child = step; history[child].Parent is var parent && parent >= 0; child = parent)
            forwardSteps[parent] = child;
    }

    void ResetDebugHistory()
    {
        forwardSteps.Clear();
        eventNames.Clear();
        detailStep = debugRevealed = -1;
        foreach (var entry in history.Skip(1))
            forwardSteps[entry.Parent] = entry.Id;
        RememberRoute(Position);
    }

    DebugAction DebugShortcut(Context c)
    {
        if (c.Shortcut(Key.Left, Modifiers.Primary | Modifiers.Alt))
            return new(DebugOperation.Back);
        if (c.Shortcut(Key.Right, Modifiers.Primary | Modifiers.Alt))
            return new(DebugOperation.Forward);
        return default;
    }

    void ApplyDebugAction(Context c, DebugAction action)
    {
        if (action.Operation == DebugOperation.None)
            return;
        var focus = c.DebuggerOwnsFocus ? c.Focus : 0;
        switch (action.Operation)
        {
            case DebugOperation.Back:
                Back();
                break;
            case DebugOperation.Forward:
                Forward();
                break;
            case DebugOperation.Latest:
                Step(history.Count - 1);
                break;
            case DebugOperation.Pause:
                Step(Position);
                break;
            case DebugOperation.Resume:
                Resume();
                break;
            case DebugOperation.Select:
                Step(action.Step);
                break;
            case DebugOperation.Expand:
                debugExpanded = !debugExpanded;
                debugRevealed = -1;
                break;
        }
        if (focus != 0)
            c.FocusNode(focus);
        c.Changed = true;
    }

    string EventName(int step)
    {
        if (eventNames.TryGetValue(step, out var name))
            return name;
        var message = history[step].Message;
        if (step == 0)
            name = "Initial model";
        else
        {
            var json = JsonSerializer.SerializeToElement(message!, messageType);
            name = json.ValueKind == JsonValueKind.Object && json.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
                ? type.GetString()! : message!.GetType().Name;
        }
        eventNames.Add(step, name);
        return name;
    }

    string DebugDetail()
    {
        if (detailStep == Position && detailTab == debugTab)
            return detailText;
        using var bytes = new MemoryStream();
        using (var json = new Utf8JsonWriter(bytes, new()
        {
            Indented = true
        }))
        {
            var step = history[Position];
            switch (debugTab)
            {
                case DebugTab.Message when Position == 0:
                    json.WriteStringValue("Initial model; no message.");
                    break;
                case DebugTab.Message:
                    JsonSerializer.Serialize(json, step.Message!, messageType);
                    break;
                case DebugTab.Model:
                    JsonSerializer.Serialize(json, step.Model, modelType);
                    break;
                case DebugTab.Changes:
                    json.WriteStartArray();
                    if (step.Parent >= 0)
                        Diff(json, "", JsonSerializer.SerializeToElement(history[step.Parent].Model, modelType),
                            JsonSerializer.SerializeToElement(step.Model, modelType));
                    json.WriteEndArray();
                    break;
            }
        }
        detailStep = Position;
        detailTab = debugTab;
        return detailText = Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
    }
}
