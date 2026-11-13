using System.Text.Json;
using Kumiko.Runtime;
using Kumiko.UI;

var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/navigation/retention");
Directory.CreateDirectory(output);
var checks = new List<string>();
void Check(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException(description);
    checks.Add(description);
}
ComplexFlows.Run(output, Check);
if (args.Contains("--complex-only"))
{
    File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(checks, ReportJson.Default.ListString));
    Console.WriteLine($"PASS {checks.Count} complex navigation checks");
    return;
}
GuardFlows.Run(output, Check);
StackHostChecks.Run(output, Check);
AsyncGuardFlows.Run(output, Check);
foreach (var frames in new[] { 1, 119, 500 })
{
    using var program = RetentionApp.CreateProgram();
    program.Record(Path.Combine(output, $"retention-{frames}.jsonl"));
    using var tester = new Tester(program, new(500, 400, DevMode: true));
    tester.Get(Role.ScrollArea, "Page scroll").Scroll(220);
    tester.Settle();
    var scrolled = tester.Context.Nodes.ToArray().Single(n => n.Key == "row-0").Rect.Y;
    tester.Get(Role.TextField, "Draft").Click();
    tester.Key(Key.Home);
    tester.Key(Key.Right);
    tester.Settle();
    program.Send(new RetentionMsg.Cover(true));
    for (var i = 0; i < frames; i++)
        tester.Frame();
    Check(!tester.Context.Accessibility.Any(n => n.Name == "Draft"), $"covered editor absent after {frames} frames");
    tester.Get(Role.Button, "Return").Click();
    tester.Settle();
    Check(tester.Context.Nodes.ToArray().Single(n => n.Key == "row-0").Rect.Y == scrolled,
        $"scroll retained across {frames} covered frames");
    tester.TypeText("X");
    tester.Settle();
    Check(program.Model.Text == "aXbc", $"caret retained across {frames} covered frames");
    program.Send(new RetentionMsg.HideList(true));
    tester.Settle();
    program.Send(new RetentionMsg.HideList(false));
    tester.Settle();
    Check(tester.Context.Nodes.ToArray().Single(n => n.Key == "row-0").Rect.Y == scrolled,
        $"a list briefly omitted by a live owner keeps its scroll ({frames})");
    tester.Context.Blur();
    tester.Settle();
    program.Send(new RetentionMsg.Cover(true));
    tester.Settle();
    tester.Get(Role.Button, "Return").Click();
    tester.Settle();
    tester.TypeText("Q");
    tester.Settle();
    Check(program.Model.Text == "aXbc", $"focus restoration is not reused after focus moved away ({frames})");
    tester.Get(Role.TextField, "Draft").Click();
    tester.Key(Key.Home);
    tester.Key(Key.Right);
    tester.Key(Key.Right);
    tester.Settle();
    tester.Screenshot(Path.Combine(output, $"retention-{frames}.png"));
    File.WriteAllText(Path.Combine(output, $"retention-{frames}.txt"), tester.Dump());
    program.Send(new RetentionMsg.Remove());
    tester.Settle();
    program.Send(new RetentionMsg.Restart());
    tester.Settle();
    tester.TypeText("Y");
    tester.Settle();
    Check(program.Model.Text == "abc", "removed owner releases focus immediately");
    tester.Get(Role.TextField, "Draft").Click();
    tester.Key(Key.End);
    tester.TypeText("Z");
    tester.Settle();
    Check(program.Model.Text == "abcZ", "recreated owner starts fresh edit state");
}
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(checks, ReportJson.Default.ListString));
Console.WriteLine($"PASS {checks.Count} navigation retention checks");

sealed record RetentionModel(bool Covered = false, bool Exists = true, string Text = "abc", bool ListHidden = false);
abstract partial record RetentionMsg
{
    public sealed record Cover(bool Value) : RetentionMsg;
    public sealed record Edit(string Text) : RetentionMsg;
    public sealed record Remove : RetentionMsg;
    public sealed record Restart : RetentionMsg;
    public sealed record HideList(bool Value) : RetentionMsg;
}
sealed partial class RetentionApp : IKumiko<RetentionModel, RetentionMsg>
{
    public static Kumiko.Runtime.NavigationRoot<RetentionMsg> Root(RetentionModel model) => Kumiko.Runtime.NavigationRoot<RetentionMsg>.Create(
        Kumiko.Runtime.NavigationHistory<string>.Start("root"), "app", _ => new RetentionView(model).Map<RetentionMsg>(static message => message),
        static _ => throw new InvalidOperationException("This application has one root screen."));
    public static RetentionModel Init => new();
    public static (RetentionModel, Cmd<RetentionMsg>) Update(RetentionModel model, RetentionMsg message) => (message switch
    {
        RetentionMsg.Cover c => model with { Covered = c.Value },
        RetentionMsg.Edit e => model with { Text = e.Text },
        RetentionMsg.Remove => model with { Exists = false },
        RetentionMsg.Restart => new RetentionModel(),
        RetentionMsg.HideList h => model with { ListHidden = h.Value },
        _ => model
    }, Cmd<RetentionMsg>.None);
}
sealed class RetentionView(RetentionModel model) : Kumiko.Runtime.Screen<RetentionModel, RetentionMsg>(model)
{
    protected override void Render()
    {
        C.Text("Retained page state", key: "title");
        if (Model.Exists)
        {
            using var page = C.RetainedScope(key: "draft-page", visible: !Model.Covered);
            if (page.Visible)
            {
                using var child = C.RetainedScope(key: "nested");
                Ui.TextField("draft", "Draft", Model.Text, text => new RetentionMsg.Edit(text));
                if (!Model.ListHidden)
                {
                    using var list = C.Div(key: "scroll").FillWidth().Height(160).Scroll().Semantics(Role.ScrollArea, "Page scroll");
                    for (var row = 0; row < 30; row++)
                        C.Text($"Row {row}", key: "row-" + row);
                }
            }
        }
        if (Model.Covered)
            Ui.Button("Return", new RetentionMsg.Cover(false), key: "return");
        C.Text("Sibling content", key: "sibling");
    }
}
[System.Text.Json.Serialization.JsonSerializable(typeof(List<string>))]
partial class ReportJson : System.Text.Json.Serialization.JsonSerializerContext;
