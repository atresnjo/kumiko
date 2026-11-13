using System.Collections.Immutable;
using System.Text.Json;
using Kumiko.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/analyzers");
Directory.CreateDirectory(output);
var cases = new (string Name, string Body, string[] Expected)[]
{
    ("native-stack-unowned", "var stack=NativeStack();", ["KUM001"]),
    ("native-page-unowned", "using var stack=NativeStack(); stack.Page(c,1);", ["KUM001"]),
    ("native-page-owned", "using var stack=NativeStack(); using var page=stack.Page(c,1);", []),
    ("retained-return", "RetainedScope Page()=>c.RetainedScope(key: \"page\"); using var page=Page();", []),
    ("retained-unowned", "c.RetainedScope(key: \"page\");", ["KUM001"]),
    ("retained-owned", "using var page = c.RetainedScope(key: \"page\", visible:false);", []),
    ("discarded", "c.Div().Padding(12);", ["KUM001"]),
    ("stored", "var box = c.Div();", ["KUM001"]),
    ("leaked-return", "ulong Leak(){var box=c.Div(); return box.Id;} _=Leak();", ["KUM001"]),
    ("scope-helper", "Div Card(){var box=c.Div(); return box.Width(30);} using var card=Card();", []),
    ("style-extension-owned", "using var box=c.Div(key:\"box\").CardSurface(c.Theme);", []),
    ("style-extension-unowned", "c.Div(key:\"box\").CardSurface(c.Theme);", ["KUM001"]),
    ("using", "using var box = c.Div(key: \"box\");", []),
    ("loop", "for(int i=0;i<2;i++) { using var box=c.Div(); }", ["KUM002"]),
    ("keyed", "for(int i=0;i<2;i++) { using var box=c.Div(key:i); c.Text(\"Child\"); }", []),
    ("positional-keyed", "for(int i=0;i<2;i++){using var box=c.Div(i); c.Text(\"Child\");}", []),
    ("tuple-loop", "foreach(var (label,index) in new[]{(\"A\",1),(\"B\",2)}) c.Button(label);", ["KUM002"]),
    ("app-worker", "Task.Run(()=>app.RunFor(TimeSpan.FromMilliseconds(1)));", ["KUM003"]),
    ("scheduled-ui", "Task.Run(()=>app.Schedule(TimeSpan.Zero,()=>c.Text(\"Good\")));", []),
    ("widget-loop", "foreach(var label in new[]{\"A\",\"B\"}) c.Button(label);", ["KUM002"]),
    ("worker", "Task.Run(() => c.Text(\"Bad\"));", ["KUM003"]),
    ("thread", "new System.Threading.Thread(() => c.Text(\"Bad\"));", ["KUM003"]),
    ("dispatch", "Task.Run(() => app.Dispatch(() => c.Text(\"Good\")));", []),
    ("computed-animation", "c.Animate(Hash(\"card.\" + name), 10);", ["KUM004"]),
    ("node-animation", "using var box=c.Div(key:name); box.Offset(0, 10, animate:Motion.Quick);", []),
    ("legacy-key-string", "c.Key(name); using var box=c.Div();", []),
    ("legacy-key-number", "c.Key(1UL); using var box=c.Div();", []),
    ("typography-implicit-system", "c.Text(name, Typography.Body);", []),
    ("helper-loop-runtime", "void Avatar(){using var circle=c.Div();} for(int i=0;i<2;i++) Avatar();", []),
    ("unrelated", "var x = new Other(); x.Div(); Task.Run(() => x.Text());", [])
};
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
    .Select(p => MetadataReference.CreateFromFile(p))
    .Append(MetadataReference.CreateFromFile(typeof(Kumiko.UI.Context).Assembly.Location));
await KumikoAnalyzerChecks.Run(output, references);
await StrictKumikoChecks.Run(output, references);
await ImmutableStateChecks.Run(output, references);
await HostingChecks.Run(output, references);
await ComponentChecks.Run(output, references);
await ViewClassChecks.Run(output, references);
CompositionCompilerChecks.Run(output, references);
NavigationCompilerChecks.Run(output, references);
await GeneratorViewChecks.Run(output, references);
await RouteGeneratorChecks.Run(output, references);
await GuardGeneratorChecks.Run(output, references);
foreach (var item in cases)
{
    var source = "using System; using System.Threading.Tasks; using Kumiko; using Kumiko.UI; class Fixture { static Kumiko.iOS.NativeStackScope<int> NativeStack()=>new(); static ulong Hash(string v)=>1; void View(Context c, App app, string name) { " + item.Body + " } } class Other { public void Div(){} public void Text(){} } " +
        "namespace Kumiko.iOS { sealed class NativeStackScope<T> : IDisposable { public Div Page(Context c,int id)=>c.Div(key:id); public void Dispose(){} } } " +
        "static class Styles { extension(Div div) { public Div CardSurface(Theme theme) => div.Padding(theme.Space.S4).Background(theme.Surface); } }";
    var compilation = CSharpCompilation.Create(
        "Fixture",
        [CSharpSyntaxTree.ParseText(source)],
        references,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
    );
    var compiler = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
    if (compiler.Length > 0)
        throw new Exception(string.Join("\n", compiler.Select(d => d.ToString())));
    var obsolete = compilation.GetDiagnostics().Count(d => d.Id == "CS0618");
    var expectedObsolete = item.Name is "legacy-key-string" or "legacy-key-number" or "computed-animation" ? 1 : 0;
    if (obsolete != expectedObsolete)
        throw new Exception($"{item.Name}: expected {expectedObsolete} obsolete warnings, got {obsolete}");
    var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ViewAnalyzer())).GetAnalyzerDiagnosticsAsync();
    var actual = diagnostics.Select(d => d.Id).Order().ToArray();
    if (!actual.SequenceEqual(item.Expected.Order()))
        throw new Exception($"{item.Name}: expected {string.Join(',', item.Expected)}, got {string.Join(',', actual)}");
    File.WriteAllText(Path.Combine(output, item.Name + ".cs.txt"), source);
}

using var stream = File.Create(Path.Combine(output, "report.json"));
using var writer = new Utf8JsonWriter(stream, new() { Indented = true });
writer.WriteStartArray();
foreach (var item in cases)
{
    writer.WriteStartObject();
    writer.WriteString("case", item.Name);
    writer.WriteString("diagnostics", string.Join(',', item.Expected));
    writer.WriteEndObject();
}

writer.WriteEndArray();
Console.WriteLine($"PASS: {cases.Length} compiler end-to-end fixtures; artifacts: {output}");
