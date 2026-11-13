using Kumiko.Desktop;
using Kumiko.Runtime;
using Kumiko.UI;

static class ScopeFlows
{
    public static void Run(string output, Action<bool, string> check)
    {
        using var program = ScopeApp.CreateProgram();
        using var tester = new Tester(program, new(Width: 420, Height: 300));
        tester.Get(Role.Button, "Owner").Click();
        tester.Key(Key.F, Modifiers.Primary);
        check(program.Model.FocusCount == 1, "focus-scoped shortcut targets its current owner");
        tester.Get(Role.Button, "Other").Click();
        tester.Key(Key.F, Modifiers.Primary);
        check(program.Model.FocusCount == 1, "focus-scoped shortcut cannot execute after focus moves");
        tester.Key(Key.G, Modifiers.Primary);
        check(program.Model.AppCount == 1, "app action carries its app message without an editor identity");
        tester.Get(Role.TextField, "Editor").Click();
        tester.Key(Key.A, Modifiers.Primary);
        tester.TypeText("replaced");
        check(program.Model.AppCount == 1 && program.Model.Text == "replaced", "editor selection wins over a colliding desktop action");
        tester.Get(Role.Button, "Open modal").Click();
        var nativeAction = tester.DesktopMenu.Single(a => a.Id == "app");
        check(nativeAction.Disabled, "modal background desktop-menu projection is disabled");
        nativeAction.Click!(nativeAction);
        tester.Key(Key.G, Modifiers.Primary);
        check(program.Model.AppCount == 1, "modal blocks both background menu callbacks and shortcuts");
        tester.Get(Role.Button, "Dismiss").Click();
        tester.Key(Key.G, Modifiers.Primary);
        check(program.Model.AppCount == 2, "closing modal releases background shortcuts");
        var stale = tester.DesktopMenu.Single(a => a.Id == "app");
        tester.Frame();
        stale.Click!(stale);
        tester.Settle();
        check(program.Model.AppCount == 2, "a retained action expires on rebuild even without a model change");
        tester.Screenshot(Path.Combine(output, "scope.png"));
        File.WriteAllText(Path.Combine(output, "scope.txt"), tester.Dump());
        foreach (var mode in new[] { 1, 2 })
        {
            using var invalid = ScopeApp.CreateProgram(new ScopeModel(Mode: mode, Collision: OperatingSystem.IsMacOS() ? Modifiers.Meta : Modifiers.Control));
            try
            {
                using var rejected = new Tester(invalid);
                throw new InvalidOperationException("Invalid action set was accepted");
            }
            catch (ArgumentException error)
            {
                check(error.Message.Contains(mode == 1 ? "identity" : "collision", StringComparison.Ordinal),
                    mode == 1 ? "duplicate action identities fail before interaction" : "normalized shortcut collisions fail before interaction");
            }
        }
        var beforeTransition = tester.DesktopMenu.Single(a => a.Id == "app");
        program.Send(new ScopeMessage.App());
        program.ProcessMessages();
        beforeTransition.Click!(beforeTransition);
        program.ProcessMessages();
        check(program.Model.AppCount == 3, "model transition invalidates callbacks before the next frame");
        tester.Settle();
        tester.Dispose();
        stale = tester.DesktopMenu.Single(a => a.Id == "app");
        stale.Click!(stale);
        program.ProcessMessages();
        check(program.Model.AppCount == 3, "disposed context rejects retained menu callbacks");
    }
}

internal sealed record ScopeModel(int FocusCount = 0, int AppCount = 0, bool Modal = false, int Mode = 0, Modifiers Collision = Modifiers.Primary, string Text = "select me");
internal abstract partial record ScopeMessage
{
    public sealed record Focus : ScopeMessage;
    public sealed record App : ScopeMessage;
    public sealed record Open : ScopeMessage;
    public sealed record Close : ScopeMessage;
    public sealed record Nothing : ScopeMessage;
    public sealed record Edit(string Text) : ScopeMessage;
}
internal sealed partial class ScopeApp : IKumiko<ScopeModel, ScopeMessage>
{
    public static ScopeModel Init => new();
    public static NavigationRoot<ScopeMessage> Root(ScopeModel model) => NavigationRoot<ScopeMessage>.Create(
        NavigationHistory<int>.Start(0), "scope", _ => new ScopeScreen(model).Map<ScopeMessage>(static m => m),
        static _ => new ScopeMessage.Nothing());
    public static (ScopeModel, Cmd<ScopeMessage>) Update(ScopeModel model, ScopeMessage message) => (message switch
    {
        ScopeMessage.Focus => model with { FocusCount = model.FocusCount + 1 },
        ScopeMessage.App => model with { AppCount = model.AppCount + 1 },
        ScopeMessage.Open => model with { Modal = true },
        ScopeMessage.Close => model with { Modal = false },
        ScopeMessage.Nothing => model,
        ScopeMessage.Edit edit => model with { Text = edit.Text },
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    }, Cmd<ScopeMessage>.None);
}
internal sealed class ScopeScreen(ScopeModel model) : Screen<ScopeModel, ScopeMessage>(model)
{
    protected override void Render()
    {
        using var page = C.Div(key: "page").SizeFull().Padding(16).Gap(12);
        ulong ownerId;
        using (var owner = C.Div(key: "owner").Width(160).Height(32))
        {
            C.Text("Owner", key: "label");
            ownerId = Ui.Button(owner, "Owner", new ScopeMessage.Nothing(), true).Id;
        }
        var actions = Ui.DesktopActions(Actions(Model.Mode, Model.Collision), ownerId);
        Ui.TextField("editor", "Editor", Model.Text, static text => new ScopeMessage.Edit(text));
        Ui.Button("Other", new ScopeMessage.Nothing(), key: "other");
        Ui.Button("Open modal", new ScopeMessage.Open(), key: "open");
        C.Text($"Focus: {Model.FocusCount} App: {Model.AppCount}", key: "counts");
        if (Model.Modal)
        {
            using var modal = C.Overlay("modal").Padding(24).Background(C.Theme.Background);
            Ui.Button("Dismiss", new ScopeMessage.Close(), key: "dismiss");
        }
        actions.Shortcuts();
    }
    static IEnumerable<DesktopAction<ScopeMessage>> Actions(int mode, Modifiers collision)
    {
        yield return new("focus", "Focus action", new ScopeMessage.Focus(), Key.F, Scope: DesktopActionScope.Focus);
        yield return new("app", "App action", new ScopeMessage.App(), Key.G, Scope: DesktopActionScope.App);
        yield return new("reserved", "Reserved editor chord", new ScopeMessage.App(), Key.A, Scope: DesktopActionScope.App);
        if (mode == 1)
            yield return new("app", "Duplicate", new ScopeMessage.App());
        if (mode == 2)
            yield return new("collision", "Collision", new ScopeMessage.App(), Key.G,
                collision);
    }
}
