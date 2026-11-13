using System.Reflection;
using System.Text.Json.Serialization;
using Kumiko;
using Kumiko.Runtime;
using Kumiko.UI;

static class KumikoEnforcementChecks
{
    public static void Run(string output, Action<bool, string> check)
    {
        var publicMethods = typeof(Context).GetMethods(BindingFlags.Public | BindingFlags.Instance);
        check(!publicMethods.Any(m => m.Name is "Checkbox" or "Toggle" or "Slider" && m.GetParameters().Any(p => p.ParameterType.IsByRef)),
            "public widgets cannot write application state by reference");
        check(typeof(Context).GetProperty("Changed")!.SetMethod?.IsPublic == false,
            "applications cannot request manual state rebuilds");
        check(!typeof(Dev).GetMethods().Any(m => m.Name == "Run" && m.GetParameters().Any(p => p.ParameterType == typeof(Action<Context>))) &&
            !typeof(App).GetMethods().Any(m => m.Name == "NewWindow" && m.GetParameters().Any(p => p.ParameterType == typeof(Action<Context>))) &&
            !typeof(Tester).GetConstructors().Any(m => m.GetParameters().Any(p => p.ParameterType == typeof(Action<Context>))),
            "all public native hosts and testers require a Kumiko program");
        var calls = 0;
        using var nondeterministic = new Program<EnforcementModel, EnforcementMessage>(new(), (m, _) =>
            (m with
            {
                Count = ++calls
            }, Cmd<EnforcementMessage>.None), View,
            EnforcementJson.Default.EnforcementModel, EnforcementJson.Default.EnforcementMessage);
        using var tester = new Tester(nondeterministic, new(DevMode: true));
        nondeterministic.Send(new EnforcementMessage.Add());
        Reject(() => tester.Frame(), "determin", check);
        check(nondeterministic.History.Count == 1, "invalid updates do not enter the recorded history");
        using var impure = new Program<EnforcementModel, EnforcementMessage>(new(), (m, _) => (m, Cmd<EnforcementMessage>.None),
            (c, m, _) =>
            {
                m.Hidden.Add(1);
                c.Text("Mutated");
            },
            EnforcementJson.Default.EnforcementModel, EnforcementJson.Default.EnforcementMessage);
        Reject(() => { using var invalid = new Tester(impure, new(DevMode: true)); }, "mutat", check);
        using var program = new Program<EnforcementModel, EnforcementMessage>(new(), (m, msg) =>
            (msg switch
            {
                EnforcementMessage.Add => m with { Enabled = !m.Enabled },
                EnforcementMessage.Rename text => m with { Name = text.Text },
                _ => m
            }, Cmd<EnforcementMessage>.None), View,
            EnforcementJson.Default.EnforcementModel, EnforcementJson.Default.EnforcementMessage);
        using var controlled = new Tester(program, new(DevMode: true));
        controlled.Get(Role.Checkbox, "Enabled").Click();
        controlled.Get(Role.TextField, "Name").Click();
        controlled.TypeText("Kumiko");
        check(program.Model.Enabled && program.Model.Name == "Kumiko", "controlled widgets change the model through recorded messages");
        var field = controlled.Context.Accessibility.Single(n => n.Role == Role.TextField);
        check(controlled.Context.PerformAccessibilityAction(field.Id, new(AccessibilityAction.SetValue, Text: "Accessible Kumiko")),
            "controlled text accepts native accessibility editing");
        controlled.Frame();
        check(program.Model.Name == "Accessible Kumiko", "accessibility editing dispatches a recorded model transition");
        RejectEffectUI(controlled.Context.Input, check);
        using var badSource = new Program<EnforcementModel, EnforcementMessage>(new(), (m, _) => (m, Cmd<EnforcementMessage>.None), View,
            EnforcementJson.Default.EnforcementModel, EnforcementJson.Default.EnforcementMessage,
            model => [new Sub<EnforcementMessage>("bad-ui", send =>
            {
                _ = controlled.Context.Time;
                return new Stop();
            })]);
        Reject(() => { using var invalid = new Tester(badSource); }, "UI state", check);
        program.Save(Path.Combine(output, "enforcement-session.jsonl"));
        controlled.Screenshot(Path.Combine(output, "enforcement.png"));
    }

    static void RejectEffectUI(Input input, Action<bool, string> check)
    {
        bool mapped = false;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var program = new Program<EnforcementModel, EnforcementMessage>(new(), (m, _) =>
            (m, Cmd<EnforcementMessage>.Run("read-ui", async _ =>
            {
                await release.Task;
                return new EnforcementMessage.Rename(input.Key.ToString());
            },
                _ =>
            {
                mapped = true;
                return new EnforcementMessage.Add();
            })), View,
            EnforcementJson.Default.EnforcementModel, EnforcementJson.Default.EnforcementMessage);
        using var tester = new Tester(program);
        program.Send(new EnforcementMessage.Add());
        tester.Frame();
        release.SetResult();
        if (!SpinWait.SpinUntil(() => program.PendingCommands == 0, TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Command did not finish");
        Reject(() => tester.Frame(), "UI state", check);
        check(!mapped, "commands cannot hide UI-state violations in their error mapping");
    }

    sealed class Stop : IDisposable
    {
        public void Dispose()
        {
        }
    }

    static void View(Context c, EnforcementModel m, Dispatch<EnforcementMessage> send)
    {
        using var root = c.Div(key: "form").SizeFull().Gap(Space.S2);
        c.Checkbox("Enabled", m.Enabled, _ => new EnforcementMessage.Add(), send, key: "enabled");
        c.TextField(m.Name, text => new EnforcementMessage.Rename(text), send, new(Name: "Name"), key: "name");
    }

    static void Reject(Action action, string text, Action<bool, string> check)
    {
        try
        {
            action();
            throw new Exception("Invalid Kumiko operation accepted: " + text);
        }
        catch (InvalidOperationException error) when (error.Message.Contains(text, StringComparison.OrdinalIgnoreCase))
        {
            check(true, "DevMode rejects " + text + " violations: " + error.Message);
        }
    }
}

internal sealed record EnforcementModel(int Count = 0, bool Enabled = false, string Name = "")
{
    // Deliberately unsafe: framework-only tests exercise the runtime fallback for foreign code.
    public List<int> Hidden { get; init; } = [];
}
[JsonPolymorphic]
[JsonDerivedType(typeof(EnforcementMessage.Add), "add")]
[JsonDerivedType(typeof(EnforcementMessage.Rename), "rename")]
internal abstract record EnforcementMessage
{
    public sealed record Add : EnforcementMessage;
    public sealed record Rename(string Text) : EnforcementMessage;
}
[JsonSerializable(typeof(EnforcementModel))]
[JsonSerializable(typeof(EnforcementMessage))]
internal partial class EnforcementJson : JsonSerializerContext;
