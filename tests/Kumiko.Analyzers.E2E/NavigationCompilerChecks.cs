using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

/// <summary>Compile actual external consumers without custom analyzers.</summary>
static class NavigationCompilerChecks
{
    public static void Run(string output, IEnumerable<MetadataReference> references)
    {
        const string header = """
            using System;
            using Kumiko.Runtime;
            using Kumiko.UI;
            public sealed record Props(string Name);
            public sealed record AppModel(int Secret);
            public sealed record Output(string Name);
            public sealed record AppMessage(Output Output);
            public sealed class Page(Props props) : Screen<Props, Output>(props)
            {
                protected override void Render() => Ui.Button(Model.Name, new Output(Model.Name));
            }
            """;
        const string host = "NavigationRoot<AppMessage>.Create(NavigationHistory<string>.Start(\"home\"), \"app\", static route => new Page(new Props(route)).Map<AppMessage>(static m => new AppMessage(m)), static request => new AppMessage(new Output(request.Stack)))";
        var cases = new (string Name, string Source, string? Error)[]
        {
            ("valid-navigation", "class App { NavigationRoot<AppMessage> Root() => " + host + "; }", null),
            ("screen-as-child", "sealed class Parent(Props p) : View<Props,AppMessage>(p) { protected override void Render() => Ui.Child(new Page(Model), \"page\", static m => new AppMessage(m)); }", "CS0411"),
            ("screen-as-root", "class App { NavigationRoot<AppMessage> Root() => new Page(new Props(\"x\")); }", "CS0029"),
            ("component-as-screen", "sealed class Component(Props p) : View<Props,Output>(p) { protected override void Render() {} } class App { Screen<Output> Select() => new Component(new Props(\"x\")); }", "CS0029"),
            ("protected-screen-render", "class App { void Render() => new Page(new Props(\"x\")).Render(); }", "CS0122"),
            ("internal-screen-invoke", "class App { void Render() => new Page(new Props(\"x\")).Invoke(null!); }", "CS1061"),
            ("wrong-screen-props", "class App { object Create() => new Page(new AppModel(1)); }", "CS1503"),
            ("wrong-screen-message", "class App { Screen<AppMessage> Select() => new Page(new Props(\"x\")).Map<AppMessage>(static m => m); }", "CS0029"),
            ("missing-history", "class App { object Root() => NavigationRoot<AppMessage>.Create(key: \"app\", resolve: (string r) => new Page(new Props(r)).Map<AppMessage>(static m => new AppMessage(m)), onNavigate: static request => new AppMessage(new Output(\"\"))); }", "CS1501"),
            ("missing-host-key", "class App { object Root() => NavigationRoot<AppMessage>.Create(history: NavigationHistory<string>.Start(\"home\"), resolve: (string r) => new Page(new Props(r)).Map<AppMessage>(static m => new AppMessage(m)), onNavigate: (HistoryRequest request) => new AppMessage(new Output(request.Stack))); }", "CS1501"),
            ("missing-navigation-mapping", "class App { object Root() => NavigationRoot<AppMessage>.Create(NavigationHistory<string>.Start(\"home\"), \"app\", static r => new Page(new Props(r)).Map<AppMessage>(static m => new AppMessage(m))); }", "CS1501"),
            ("closed-root-implementation", "class Bypass : NavigationRoot<AppMessage> {}", "CS0122")
        };
        var report = new List<string> { "Custom analyzers disabled; ordinary external C# consumer." };
        foreach (var item in cases)
        {
            var compilation = CSharpCompilation.Create("NavigationConsumer", [CSharpSyntaxTree.ParseText(header + item.Source)], references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            File.WriteAllText(Path.Combine(output, item.Name + ".cs.txt"), header + item.Source);
            File.WriteAllLines(Path.Combine(output, item.Name + ".log"), errors.Select(e => e.ToString()));
            if (item.Error is null ? errors.Length != 0 : !errors.Any(e => e.Id == item.Error))
                throw new Exception($"{item.Name}: expected {item.Error ?? "success"}; got {string.Join('\n', errors.Select(e => e.ToString()))}");
            report.Add($"PASS {item.Name}: {string.Join(',', errors.Select(e => e.Id).Distinct())}");
        }
        File.WriteAllLines(Path.Combine(output, "navigation-language-report.txt"), report);
        Console.WriteLine($"PASS: {cases.Length} language-only navigation fixtures");
    }
}
