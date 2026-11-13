using System.Collections.Immutable;
using Kumiko.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

static class KumikoAnalyzerChecks
{
    public static async Task Run(string output, IEnumerable<MetadataReference> references)
    {
        var cases = new (string Name, string Code, string[] Expected)[]
        {
            ("typed-view-field", "static void View(Ui<Msg> ui, Model m) { state++; }", ["KUM005"]),
            ("typed-view-valid", "static void View(Ui<Msg> ui, Model m) { using var form = ui.Form(key: \"form\", onSubmit: new Msg.Add()); ui.TextField(key: \"name\", label: \"Name\", value: \"\", onChange: _ => new Msg.Reset()); ui.Submit(key: \"submit\", label: \"Add\"); }", []),
            ("typed-view-clock", "static void View(Ui<Msg> ui, Model m) { var now = DateTime.UtcNow; }", ["KUM005"]),
            ("view-field", "[KumikoView] static void View(Context c) { state++; }", ["KUM005"]),
            ("view-property", "[KumikoView] static void View(Context c) { model.Count=2; }", ["KUM005", "KUM009"]),
            ("view-collection", "[KumikoView] static void View(Context c) { items.Add(1); }", ["KUM005", "KUM009"]),
            ("view-alias", "[KumikoView] static void View(Context c) { var alias=items; alias.Clear(); }", ["KUM005", "KUM009"]),
            ("view-local", "[KumikoView] static void View(Context c, Dispatch<Msg> send) { int n=0; n++; c.TitleBarHeight=30; if(c.Button(\"Add\").Clicked) send(new Msg.Add()); }", []),
            ("update-io", "[KumikoUpdate] static Model Update(Model m, Msg msg) { System.IO.File.WriteAllText(\"x\",\"x\"); return m; }", ["KUM006"]),
            ("update-task", "[KumikoUpdate] static Model Update(Model m, Msg msg) { Task.Run(()=>{}); return m; }", ["KUM006"]),
            ("update-clock", "[KumikoUpdate] static Model Update(Model m, Msg msg) { var now=DateTime.UtcNow; return m; }", ["KUM006"]),
            ("update-mutation", "[KumikoUpdate] static Model Update(Model m, Msg msg) { m.Count++; return m; }", ["KUM006"]),
            ("update-command", "[KumikoUpdate] static Cmd<Msg> Update(Model m, Msg msg) => Cmd<Msg>.Run(\"read\", async _ => { await Task.Yield(); return new Msg.Add(); }, _ => new Msg.Reset());", []),
            ("update-debounce", "[KumikoUpdate] static Cmd<Msg> Update(Model m, Msg msg) => Cmd<Msg>.Debounce(\"search\", TimeSpan.FromMilliseconds(200), async token => { await Task.Delay(10, token).ConfigureAwait(false); return new Msg.Add(); }, e => e.Message.Length > 0 ? new Msg.Reset() : new Msg.Add());", []),
            ("document-effects-valid", "[KumikoUpdate] static Cmd<Msg> Update(Model m, Msg msg) => DocumentEffects.Save<string, Msg>(Document<string>.Create(\"id\",\"title\",\"value\").BeginSave(\"/tmp/explicit\").Request, new Utf8DocumentCodec(), _ => new Msg.Add(), error => error.Message.Length > 0 ? new Msg.Reset() : new Msg.Add());", []),
            ("document-effects-eager-clock", "[KumikoUpdate] static Cmd<Msg> Update(Model m, Msg msg) => DocumentEffects.Open<string, Msg>(DateTime.UtcNow.ToString(), new Utf8DocumentCodec(), _ => new Msg.Add(), _ => new Msg.Reset());", ["KUM006"]),
            ("document-effects-ui-callback", "[KumikoUpdate] static Cmd<Msg> Update(Model m, Msg msg, Context c) => DocumentEffects.Open<string, Msg>(\"/tmp/explicit\", new Utf8DocumentCodec(), _ => { c.Text(\"bad\"); return new Msg.Add(); }, _ => new Msg.Reset());", ["KUM006"]),
            ("document-effects-eager-io", "[KumikoUpdate] static Cmd<Msg> Update(Model m, Msg msg) => DocumentEffects.Open<string, Msg>(System.IO.File.ReadAllText(\"bad\"), new Utf8DocumentCodec(), _ => new Msg.Add(), _ => new Msg.Reset());", ["KUM006"]),
            ("message-missing", "[KumikoUpdate] static Model Update(Model m, Msg msg) => msg switch { Msg.Add => m, _ => m };", ["KUM007"]),
            ("message-guarded", "[KumikoUpdate] static Model Update(Model m, Msg msg) => msg switch { Msg.Add when m.Count>0 => m, Msg.Reset => m, _ => m };", ["KUM007"]),
            ("message-complete", "[KumikoUpdate] static Model Update(Model m, Msg msg) => msg switch { Msg.Add => m, Msg.Reset => m, _ => throw new ArgumentOutOfRangeException() };", []),
            ("message-statement", "[KumikoUpdate] static Model Update(Model m, Msg msg) { switch(msg) { case Msg.Add: return m; default: return m; } }", ["KUM007"])
        };
        foreach (var item in cases)
        {
            var source = "using System; using System.Collections.Generic; using System.Threading.Tasks; using Kumiko.Runtime; using Kumiko.UI; using Kumiko.Documents; " +
                "class Fixture { static int state; static readonly Model model=new(); static readonly List<int> items=new(); " + item.Code +
                " } class Model { public int Count {get;set;} } abstract record Msg { public sealed record Add : Msg; public sealed record Reset : Msg; }";
            var compilation = CSharpCompilation.Create("Kumiko.E2E", [CSharpSyntaxTree.ParseText(source)], references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var compiler = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            if (compiler.Length > 0)
                throw new Exception(string.Join('\n', compiler.Select(d => d.ToString())));
            var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new KumikoAnalyzer())).GetAnalyzerDiagnosticsAsync();
            var actual = diagnostics.Select(d => d.Id).Order().ToArray();
            if (!actual.SequenceEqual(item.Expected.Order()))
                throw new Exception($"{item.Name}: expected {string.Join(',', item.Expected)}, got {string.Join(',', diagnostics.Select(d => d.ToString()))}");
            File.WriteAllText(Path.Combine(output, "kumiko-" + item.Name + ".cs.txt"), source);
        }
        File.WriteAllLines(Path.Combine(output, "kumiko-report.txt"), cases.Select(c => $"PASS {c.Name}: {string.Join(',', c.Expected)}"));
        Console.WriteLine($"PASS: {cases.Length} Kumiko compiler end-to-end fixtures; artifacts: {output}");
    }
}
