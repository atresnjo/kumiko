using System.Collections.Immutable;
using Kumiko.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

static class HostingChecks
{
    public static async Task Run(string output, IEnumerable<MetadataReference> references)
    {
        var cases = new (string Name, string Code)[]
        {
            ("dev", "Dev.Run(\"bad\", 400, 300, Draw, []);"),
            ("window", "using var app = new App(); app.NewWindow(new(), Draw);"),
            ("tester", "using var tester = new Tester(Draw);"),
            ("checkbox", "bool value = false; c.Checkbox(\"bad\", ref value);"),
            ("toggle", "bool value = false; c.Toggle(\"bad\", ref value);"),
            ("slider", "float value = 0; c.Slider(ref value);"),
            ("buffer", "c.TextField(new TextBuffer());"),
            ("changed", "c.Changed = true;")
        };
        var report = new List<string>();
        foreach (var item in cases)
        {
            var source = "using Kumiko; using Kumiko.UI; class Consumer { static void Draw(Context c) {} void Start(Context c) { " + item.Code + " } }";
            var compilation = CSharpCompilation.Create("ExternalHost", [CSharpSyntaxTree.ParseText(source)], references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var compiler = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new KumikoAnalyzer())).GetAnalyzerDiagnosticsAsync();
            if (compiler.Length == 0 || !diagnostics.Any(d => d.Id == "KUM010"))
                throw new Exception($"{item.Name}: free-style API escaped: {string.Join('\n', diagnostics)}");
            File.WriteAllText(Path.Combine(output, "hosting-" + item.Name + ".cs.txt"), source);
            report.Add($"PASS {item.Name}: KUM010 and inaccessible API");
        }
        // A whole-window web page has no Kumiko view callback, so it is a supported host without a program.
        var page = "using Kumiko; class Consumer { void Start() { using var app = new App(); using var window = app.NewPage(new(), new()); } }";
        var pageCompilation = CSharpCompilation.Create("PageHost", [CSharpSyntaxTree.ParseText(page)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var pageErrors = pageCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        var pageDiagnostics = await pageCompilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new KumikoAnalyzer())).GetAnalyzerDiagnosticsAsync();
        if (pageErrors.Length > 0 || pageDiagnostics.Any(d => d.Id == "KUM010"))
            throw new Exception("program-free page host rejected: " + string.Join('\n', pageErrors.Concat(pageDiagnostics)));
        File.WriteAllText(Path.Combine(output, "hosting-page.cs.txt"), page);
        report.Add("PASS page: program-free web page host compiles without KUM010");
        const string fake = "using System; using Kumiko; using Kumiko.UI; using Kumiko.Runtime; sealed class Fake : IProgram { " +
            "public bool DevMode {get;set;} public bool DebugEnabled {get;set;} public void View(Context c) {} public void Attach(Window w) {} " +
            "public void Record(string p) {} public void Save(string p) {} public void Replay(string p) {} public void Step(int n) {} " +
            "public event Action? ModelChanged; public void SaveModel(string p) {} public bool RestoreModel(string p) => false; " +
            "public void Dispose() {} public void RequireKumikoRuntime() {} }";
        var forged = CSharpCompilation.Create("ForgedProgram", [CSharpSyntaxTree.ParseText(fake)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        if (!forged.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error && d.GetMessage().Contains("RequireKumikoRuntime", StringComparison.Ordinal)))
            throw new Exception("External applications can forge a free-style IProgram");
        File.WriteAllText(Path.Combine(output, "hosting-forged-program.cs.txt"), fake);
        report.Add("PASS forged-program: external implementations cannot satisfy the runtime-only contract");
        File.WriteAllLines(Path.Combine(output, "hosting-report.txt"), report);
    }
}
