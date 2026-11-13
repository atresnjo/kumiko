using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

/// <summary>Language-only consumer fixtures: deliberately never attach custom analyzers.</summary>
static class CompositionCompilerChecks
{
    const string Library = """
        using Kumiko.Runtime;
        namespace Reusable;
        public sealed record DiffProps(string Text);
        public sealed record DiffMsg(string Text);
        public sealed class DiffView(DiffProps props) : View<DiffProps, DiffMsg>(props)
        {
            protected override void Render() => Ui.TextField("text", "Diff text", Model.Text, static text => new DiffMsg(text));
        }
        """;
    const string Header = """
        using System;
        using Kumiko.Runtime;
        using Kumiko.UI;
        using Reusable;
        public sealed record FlintModel(string Text, int PrivateAppState);
        public sealed record AppMsg(DiffMsg Message);
        """;
    const string Open = "public sealed class Parent(FlintModel model) : View<FlintModel, AppMsg>(model) { protected override void Render() { ";

    public static void Run(string output, IEnumerable<MetadataReference> references)
    {
        var refs = references.ToArray();
        var library = Compile("ReusableViews", Library, refs);
        using var bytes = new MemoryStream();
        var emit = library.Emit(bytes);
        if (!emit.Success)
            throw new Exception(string.Join('\n', emit.Diagnostics));
        File.WriteAllBytes(Path.Combine(output, "ReusableViews.dll"), bytes.ToArray());
        File.WriteAllText(Path.Combine(output, "ReusableViews.cs.txt"), Library);
        var consumerRefs = refs.Append(MetadataReference.CreateFromImage(bytes.ToArray())).ToArray();
        var cases = new (string Name, string Body, string? Diagnostic)[]
        {
            ("valid", "Ui.Child(new DiffView(new DiffProps(Model.Text)), \"diff\", static message => new AppMsg(message));", null),
            ("wrong-props", "Ui.Child(new DiffView(Model), \"diff\", static message => new AppMsg(message));", "CS1503"),
            ("wrong-mapping-input", "Ui.Child(new DiffView(new(\"\")), \"diff\", (int message) => new AppMsg(new(message.ToString())));", "CS0411"),
            ("wrong-mapping-output", "Ui.Child(new DiffView(new(\"\")), \"diff\", static message => message);", "CS0029"),
            ("missing-key", "Ui.Child(new DiffView(new(\"\")), toParent: static message => new AppMsg(message));", "CS7036"),
            ("missing-mapping", "Ui.Child(new DiffView(new(\"\")), \"diff\");", "CS7036"),
            ("protected-render", "new DiffView(new(\"\")).Render();", "CS0122"),
            ("internal-invocation", "new DiffView(new(\"\")).Invoke(null!);", "CS1061"),
            ("no-map-bypass", "Ui.Map<DiffMsg>(static message => new AppMsg(message));", "CS1061"),
            ("no-ui-construction", "_ = new Ui<AppMsg>(C, static message => {});", "CS1729")
        };
        var report = new List<string> { "Custom analyzers: disabled. Library references only framework/platform assemblies, never the consumer." };
        foreach (var item in cases)
            Check(item.Name, Header + Open + item.Body + "} }", consumerRefs, item.Diagnostic, output, report);
        Check("wrong-dispatch", Header + "public sealed class Child(DiffProps props) : View<DiffProps, DiffMsg>(props) { protected override void Render() => Ui.Dispatch(new AppMsg(new(\"\"))); }",
            consumerRefs, "CS1503", output, report);
        Check("private-parent", Header + "class Parent { private static int secret; } public sealed class Child(DiffProps props) : View<DiffProps, DiffMsg>(props) { protected override void Render() => C.Text(Parent.secret.ToString()); }",
            consumerRefs, "CS0122", output, report);
        Check("missing-root", Header + "public partial class App : IKumiko<FlintModel,AppMsg> { public static FlintModel Init => new(\"\",0); public static (FlintModel,Cmd<AppMsg>) Update(FlintModel m,AppMsg msg) => (m,Cmd<AppMsg>.None); public static IProgram Create() => null!; }",
            consumerRefs, "CS0535", output, report);
        Check("raw-program-bypass", Header + "class App { void Start() { _ = new Program<FlintModel,AppMsg>(new(\"\",0), static (m,msg) => (m,Cmd<AppMsg>.None), static (c,m,send) => {}, null!,null!); } }",
            consumerRefs, "CS1593", output, report);
        File.WriteAllLines(Path.Combine(output, "composition-language-report.txt"), report);
        Console.WriteLine($"PASS: {report.Count - 1} language-only typed composition fixtures");
    }

    static CSharpCompilation Compile(string name, string source, IEnumerable<MetadataReference> references) =>
        CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    static void Check(string name, string source, IEnumerable<MetadataReference> references, string? expected,
        string output, List<string> report)
    {
        var errors = Compile("AppConsumer", source, references).GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        File.WriteAllText(Path.Combine(output, "language-" + name + ".cs.txt"), source);
        File.WriteAllLines(Path.Combine(output, "language-" + name + ".log"), errors.Select(d => d.ToString()));
        if (expected is null ? errors.Length != 0 : !errors.Any(d => d.Id == expected))
            throw new Exception($"{name}: expected {expected ?? "success"}; got {string.Join('\n', errors.Select(d => d.ToString()))}");
        report.Add($"PASS {name}: {string.Join(',', errors.Select(d => d.Id).Distinct())}");
    }
}
