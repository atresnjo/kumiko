using System.Collections.Immutable;
using Kumiko.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

// Compile real consumers: enforcement must not depend on attributes or project opt-outs.
static class StrictKumikoChecks
{
    public static async Task Run(string output, IEnumerable<MetadataReference> references)
    {
        var cases = new (string Name, string Body, string Extra, string[] Expected)[]
        {
            ("navigation-policy-pure", "record Entry(int Id, int Route) : INavigationEntry<int, Entry> { public static Entry Create(int id, int route) => new(id, route); public static bool IsValidPath(System.Collections.Immutable.ImmutableArray<Entry> entries) => entries.Length > 0; }", "", []),
            ("navigation-policy-io", "record Entry(int Id, int Route) : INavigationEntry<int, Entry> { public static Entry Create(int id, int route) => new(id, route); public static bool IsValidPath(System.Collections.Immutable.ImmutableArray<Entry> entries) { System.IO.File.WriteAllText(\"x\", \"x\"); return true; } }", "", ["KUM006"]),
            ("navigation-entry-clock", "record Entry(int Id, int Route) : INavigationEntry<int, Entry> { public static Entry Create(int id, int route) => new(DateTime.UtcNow.Second, route); public static bool IsValidPath(System.Collections.Immutable.ImmutableArray<Entry> entries) => entries.Length > 0; }", "", ["KUM006"]),
            ("guard-pure", "void Start() { _ = RouteGuard.BeforeLoad(new Model(0), new[] { \"home\" }, BeforeLoad); } static RouteDecision<string> BeforeLoad(Model m, string route) => new RouteDecision<string>.Allow();", "", []),
            ("guard-null-callback", "void Start() { _ = RouteGuard.BeforeLoad<Model, string>(new Model(0), new[] { \"home\" }, null!); }", "", ["KUM009"]),
            ("guard-clock-helper", "void Start() { _ = RouteGuard.BeforeLoad(new Model(0), new[] { \"home\" }, BeforeLoad); } static RouteDecision<string> BeforeLoad(Model m, string route) { _ = DateTime.UtcNow; return new RouteDecision<string>.Allow(); }", "", ["KUM006"]),
            ("guard-io-lambda", "void Start() { _ = RouteGuard.BeforeLoad(new Model(0), new[] { \"home\" }, static (m, route) => { System.IO.File.WriteAllText(\"x\", \"x\"); return new RouteDecision<string>.Allow(); }); }", "", ["KUM006"]),
            ("guard-instance", "void Start() { _ = RouteGuard.BeforeLoad(new Model(0), new[] { \"home\" }, BeforeLoad); } RouteDecision<string> BeforeLoad(Model m, string route) => new RouteDecision<string>.Allow();", "", ["KUM009"]),
            ("guard-named-callback", "void Start() { _ = RouteGuard.BeforeLoad(beforeLoad: BeforeLoad, model: new Model(0), path: new[] { \"home\" }); } static RouteDecision<string> BeforeLoad(Model m, string route) { _ = DateTime.UtcNow; return new RouteDecision<string>.Allow(); }", "", ["KUM006"]),
            ("update-provider", "static (Model, Cmd<Msg>) Update(Model m, Msg msg) { _ = Resolve().GetService(typeof(string)); return (m, Cmd<Msg>.None); } static IServiceProvider Resolve() => null!;", "", ["KUM006"]),
            ("view-provider", "static void View(Context c, Model m, Dispatch<Msg> send) { _ = Resolve().GetService(typeof(string)); } static IServiceProvider Resolve() => null!;", "", ["KUM005"]),
            ("unmarked-view", "static void View(Context c, Model m, Dispatch<Msg> send) { state++; }", "", ["KUM005"]),
            ("unmarked-update", "static (Model, Cmd<Msg>) Update(Model m, Msg msg) => (m with { Count = DateTime.UtcNow.Second }, Cmd<Msg>.None);", "", ["KUM006"]),
            ("helper-mutation", "static void View(Context c, Model m, Dispatch<Msg> send) => Helper(); static void Helper() { state++; }", "", ["KUM005"]),
            ("helper-clock", "static (Model, Cmd<Msg>) Update(Model m, Msg msg) => (m with { Count = Clock() }, Cmd<Msg>.None); static int Clock() => DateTime.UtcNow.Second;", "", ["KUM006"]),
            ("property-clock", "static (Model, Cmd<Msg>) Update(Model m, Msg msg) => (m with { Count = Clock }, Cmd<Msg>.None); static int Clock => DateTime.UtcNow.Second;", "", ["KUM006"]),
            ("instance-view", "void View(Context c, Model m, Dispatch<Msg> send) { c.Text(m.Count.ToString()); }", "", ["KUM009"]),
            ("instance-update", "(Model, Cmd<Msg>) Update(Model m, Msg msg) => (m, Cmd<Msg>.None);", "", ["KUM009"]),
            ("view-io", "static void View(Context c, Model m, Dispatch<Msg> send) => System.IO.File.WriteAllText(\"x\", \"x\");", "", ["KUM005"]),
            ("command-ui", "static (Model, Cmd<Msg>) Update(Model m, Msg msg) => (m, Cmd<Msg>.Run(\"bad\", _ => { ReadUI(); return Task.FromResult<Msg>(new Msg.Add()); }, _ => new Msg.Add())); static void ReadUI() { var c = new Context(); c.Text(\"bad\"); }", "", ["KUM006"]),
            ("readonly-mutable-capture", "static readonly System.Collections.Generic.List<int> shared = new(); static void View(Context c, Model m, Dispatch<Msg> send) => c.Text(shared.Count.ToString());", "", ["KUM009"]),
            ("property-setter-effect", "static void View(Context c, Model m, Dispatch<Msg> send) { State = 1; } static int State { set { System.IO.File.WriteAllText(\"x\", \"x\"); } }", "", ["KUM005"]),
            ("static-lambda-view", "void Start() { using var p = new Program<Model,Msg>(new(0), static (m,msg) => (m,Cmd<Msg>.None), static (c,m,send) => { state++; }, null!, null!); }", "", ["KUM005"]),
            ("interface-helper", "static void View(Context c, Model m, Dispatch<Msg> send) { IHidden helper = new Hidden(); helper.Change(); } interface IHidden { void Change(); } sealed class Hidden : IHidden { public Hidden() {} public void Change() { state++; } }", "", ["KUM005"]),
            ("init-accessor-effect", "void Start() { using var p = new Program<Payload,Msg>(new(), Reduce, Draw, null!, null!); } static (Payload,Cmd<Msg>) Reduce(Payload m, Msg msg) => (m,Cmd<Msg>.None); static void Draw(Context c, Payload m, Dispatch<Msg> send) {} record Payload { readonly int count; public int Count { get => count; init { count = value; state++; } } }", "", ["KUM006"]),
            ("constructor-effect", "void Start() { using var p = new Program<Payload,Msg>(new(), Reduce, Draw, null!, null!); } static (Payload,Cmd<Msg>) Reduce(Payload m, Msg msg) => (m,Cmd<Msg>.None); static void Draw(Context c, Payload m, Dispatch<Msg> send) {} record Payload { public Payload() { System.IO.File.WriteAllText(\"x\", \"x\"); } }", "", ["KUM006"]),
            ("initializer-clock", "void Start() { using var p = new Program<Payload,Msg>(new(), Reduce, Draw, null!, null!); } static (Payload,Cmd<Msg>) Reduce(Payload m, Msg msg) => (m,Cmd<Msg>.None); static void Draw(Context c, Payload m, Dispatch<Msg> send) {} record Payload { public DateTime Time {get;init;} = DateTime.UtcNow; }", "", ["KUM006"]),
            ("pure-helper", "static void View(Context c, Model m, Dispatch<Msg> send) => c.Text(Format(m.Count)); static string Format(int n) => (n + 1).ToString();", "", []),
            ("style-extension-pure", "static void View(Context c, Model m, Dispatch<Msg> send) { using var card = c.Div(key: \"card\").CardSurface(c.Theme).Width(320); c.Text(\"Card\"); }",
                "static class Styles { extension(Div div) { public Div CardSurface(Theme theme) => div.Padding(theme.Space.S4).Background(theme.Surface); } }", []),
            ("style-extension-mutation", "static void View(Context c, Model m, Dispatch<Msg> send) { using var card = c.Div(key: \"card\").CardSurface(c.Theme); }",
                "static class Styles { static int hits; extension(Div div) { public Div CardSurface(Theme theme) { hits++; return div.Background(theme.Surface); } } }", ["KUM005"]),
            ("captured-field", "static void View(Context c, Model m, Dispatch<Msg> send) => c.Text(state.ToString());", "", ["KUM009"]),
            ("immutable", "static (Model, Cmd<Msg>) Update(Model m, Msg msg) => (m with { Count = m.Count + 1 }, Cmd<Msg>.None);", "", [])
        };
        var report = new List<string>();
        foreach (var item in cases)
        {
            var source = "using System; using System.Threading.Tasks; using Kumiko.UI; using Kumiko.Runtime; " +
                "class Fixture { static int state; " + item.Body + " } " +
                "record Model(int Count); abstract record Msg { public sealed record Add : Msg; } " + item.Extra;
            var compilation = CSharpCompilation.Create("Kumiko.E2E", [CSharpSyntaxTree.ParseText(source)], references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length > 0)
                throw new Exception(string.Join('\n', errors.Select(d => d.ToString())));
            var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new KumikoAnalyzer())).GetAnalyzerDiagnosticsAsync();
            var actual = diagnostics.Select(d => d.Id).Distinct().Order();
            if (!actual.SequenceEqual(item.Expected.Order()))
                throw new Exception($"{item.Name}: expected {string.Join(',', item.Expected)}, got {string.Join('\n', diagnostics)}");
            File.WriteAllText(Path.Combine(output, "strict-" + item.Name + ".cs.txt"), source);
            report.Add($"PASS {item.Name}: {string.Join(',', item.Expected)}");
        }
        File.WriteAllLines(Path.Combine(output, "strict-report.txt"), report);
    }
}
