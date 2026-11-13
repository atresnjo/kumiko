using System.Collections.Immutable;
using Kumiko.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

static class ImmutableStateChecks
{
    public static async Task Run(string output, IEnumerable<MetadataReference> references)
    {
        var cases = new (string Name, string State, string Extra, bool Reject)[]
        {
            ("record", "record Model(int Count);", "", false),
            ("init", "record Model { public string Name {get;init;} = \"\"; }", "", false),
            ("setter", "record Model { public int Count {get;set;} }", "", true),
            ("list", "record Model(List<int> Items);", "", true),
            ("dictionary", "record Model(Dictionary<string,int> Items);", "", true),
            ("array", "record Model(int[] Items);", "", true),
            ("nested-class", "record Model(Hidden Value); class Hidden { public int Count; }", "", true),
            ("nested-record", "record Model(Hidden Value); record Hidden(List<int> Items);", "", true),
            ("immutable-array", "record Model(ImmutableArray<int> Items);", "", false),
            ("immutable-map", "record Model(ImmutableDictionary<string,ImmutableArray<int>> Items);", "", false),
            ("message-payload", "record Model(int Count);", "public sealed record Mutable(List<int> Items) : Msg;", true),
            ("base-setter", "record Model(int Count) : Base; record Base { public int Mutable {get;set;} }", "", true)
        };
        var report = new List<string>();
        foreach (var item in cases)
        {
            var source = "using System; using System.Collections.Generic; using System.Collections.Immutable; using Kumiko.Runtime; using Kumiko.UI; " +
                item.State + " abstract record Msg { public sealed record Add : Msg; " + item.Extra + " } " +
                "class Consumer { static void View(Context c, Model m, Dispatch<Msg> send) { c.Text(\"OK\"); } " +
                "static (Model,Cmd<Msg>) Update(Model m, Msg msg) => (m,Cmd<Msg>.None); " +
                "void Start(Model m) { using var p = new Program<Model,Msg>(m,Update,View,null!,null!); } }";
            var compilation = CSharpCompilation.Create("Kumiko.E2E", [CSharpSyntaxTree.ParseText(source)], references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var compiler = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            if (compiler.Length > 0)
                throw new Exception(string.Join('\n', compiler.Select(d => d.ToString())));
            var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new KumikoAnalyzer())).GetAnalyzerDiagnosticsAsync();
            if (diagnostics.Any(d => d.Id == "KUM008") != item.Reject || diagnostics.Any(d => d.Id != "KUM008"))
                throw new Exception($"{item.Name}: unexpected diagnostics: {string.Join('\n', diagnostics)}");
            File.WriteAllText(Path.Combine(output, "immutable-" + item.Name + ".cs.txt"), source);
            report.Add($"PASS {item.Name}: {(item.Reject ? "KUM008" : "immutable")}");
        }
        File.WriteAllLines(Path.Combine(output, "immutable-report.txt"), report);
    }
}
