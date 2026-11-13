using System.Collections.Immutable;
using Kumiko.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>Reusable components are checked where they are compiled and trusted where they are used.</summary>
static class ComponentChecks
{
    const string Usings = "using System; using System.Threading.Tasks; using Kumiko.Runtime; using Kumiko.UI; ";
    const string Types = "public sealed record Model(int Count = 0); public sealed record Out; " +
        "public abstract partial record Msg { public sealed record Add : Msg; public sealed record Reset : Msg; } ";
    const string Valid = "public sealed partial class Counter : IKumikoComponent<Model, Msg, Out> { " +
        "public static Outcome<Model, Msg, Out> Update(Model m, Msg msg) => msg switch { Msg.Add => new(m with { Count = m.Count + 1 }), " +
        "Msg.Reset => new(m, outMessage: new Out()), _ => throw new ArgumentOutOfRangeException(nameof(msg)) }; " +
        "public static void View(Ui<Msg> ui, Model m, string title, NodeKey key = default) { using var row = ui.Context.Div(key: key); " +
        "ui.Button(title, new Msg.Add(), key: \"add\"); } } public sealed class CounterView(Model model) : View<Model, Msg>(model) { protected override void Render() => Ui.Button(\"Add\", new Msg.Add(), key: \"add\"); } public static class Loud { public static int Shout() { Console.WriteLine(); return 1; } }";

    public static async Task Run(string output, IEnumerable<MetadataReference> references)
    {
        var cases = new (string Name, string Code, string[] Expected)[]
        {
            ("component-valid", Valid, []),
            ("component-update-io", "public sealed class C : IKumikoComponent<Model, Msg, Out> { public static Outcome<Model, Msg, Out> Update(Model m, Msg msg) " +
                "{ System.IO.File.WriteAllText(\"x\", \"x\"); return new(m); } }", ["KUM006"]),
            ("component-update-missing", "public sealed class C : IKumikoComponent<Model, Msg> { public static (Model, Cmd<Msg>) Update(Model m, Msg msg) => " +
                "msg switch { Msg.Add => (m, Cmd<Msg>.None), _ => (m, Cmd<Msg>.None) }; }", ["KUM007"]),
            ("component-view-mutation", "public sealed class C : IKumikoComponent<Model, Msg> { static int clicks; public static (Model, Cmd<Msg>) Update(Model m, Msg msg) => " +
                "(m, Cmd<Msg>.None); public static void View(Ui<Msg> ui, Model m, string title) { clicks++; } }", ["KUM005"]),
            ("component-mutable-model", "public sealed class Bag { public int Count; } public sealed class C : IKumikoComponent<Bag, Msg> { " +
                "public static (Bag, Cmd<Msg>) Update(Bag m, Msg msg) => (m, Cmd<Msg>.None); }", ["KUM008"])
        };
        foreach (var item in cases)
            await Expect(item.Name, Compile("ComponentFixture", Usings + Types + item.Code, references), item.Expected, output);

        var library = Compile("ComponentLibrary", Usings + Types + Valid, references);
        await Expect("component-library", library, [], output);
        using var image = new MemoryStream();
        var emitted = library.Emit(image);
        if (!emitted.Success)
            throw new Exception(string.Join('\n', emitted.Diagnostics));
        var withLibrary = references.Append(MetadataReference.CreateFromImage(image.ToArray())).ToArray();
        const string app = "public sealed record AppModel(Model Counter); public abstract partial record AppMsg { public sealed record GotCounter(Msg Message) : AppMsg; } " +
            "public static class App { public static (AppModel, Cmd<AppMsg>) Update(AppModel m, AppMsg msg) => msg switch { AppMsg.GotCounter got => " +
            "Counter.Update(m.Counter, got.Message).Fold(counter => m with { Counter = counter }, Wrap, (next, _) => (next with { Counter = new() }, Cmd<AppMsg>.None)), " +
            "_ => throw new ArgumentOutOfRangeException(nameof(msg)) }; public static void View(Ui<AppMsg> ui, AppModel m) { " +
            "ui.Child(new CounterView(m.Counter), \"counter\", Wrap); } static AppMsg Wrap(Msg message) => new AppMsg.GotCounter(message); }";
        await Expect("component-used-from-library", Compile("ComponentApp", Usings + app, withLibrary), [], output);
        const string loud = "public sealed record AppModel(int Count); public abstract partial record AppMsg { public sealed record Add : AppMsg; } " +
            "public static class App { public static (AppModel, Cmd<AppMsg>) Update(AppModel m, AppMsg msg) => msg switch { " +
            "AppMsg.Add => (m with { Count = Loud.Shout() }, Cmd<AppMsg>.None), _ => throw new ArgumentOutOfRangeException(nameof(msg)) }; }";
        await Expect("library-helper-untrusted", Compile("LoudApp", Usings + loud, withLibrary), ["KUM006"], output);
        Console.WriteLine($"PASS: {cases.Length + 3} component compiler end-to-end fixtures; artifacts: {output}");
    }

    static CSharpCompilation Compile(string name, string source, IEnumerable<MetadataReference> references)
    {
        var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length > 0)
            throw new Exception($"{name}:\n" + string.Join('\n', errors.Select(d => d.ToString())));
        return compilation;
    }

    static async Task Expect(string name, CSharpCompilation compilation, string[] expected, string output)
    {
        var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new KumikoAnalyzer())).GetAnalyzerDiagnosticsAsync();
        var actual = diagnostics.Select(d => d.Id).Order().ToArray();
        if (!actual.SequenceEqual(expected.Order()))
            throw new Exception($"{name}: expected {string.Join(',', expected)}, got {string.Join(',', diagnostics.Select(d => d.ToString()))}");
        File.WriteAllText(Path.Combine(output, "component-" + name + ".cs.txt"), compilation.SyntaxTrees[0].ToString());
    }
}
