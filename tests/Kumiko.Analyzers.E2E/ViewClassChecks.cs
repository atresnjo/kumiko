using System.Collections.Immutable;
using System.Text.Json;
using Kumiko.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

static class ViewClassChecks
{
    const string Header = "using System; using Kumiko.UI; using Kumiko.Runtime; public sealed record Model(int Count); public sealed record Msg(int Count); public sealed class NumberView(int value) : View<int,int>(value) { protected override void Render() {} } ";
    const string Open = "public sealed class Screen(Model model) : View<Model, Msg>(model) { ";

    const string Entry = "public sealed record Entry(int Id, string Route) : INavigationEntry<string, Entry> { public static Entry Create(int id, string route) => new(id,route); public static bool IsValidPath(System.Collections.Immutable.ImmutableArray<Entry> entries) => true; }";

    public static async Task Run(string output, IEnumerable<MetadataReference> references)
    {
        var cases = new (string Name, string Source, string[] Expected)[]
        {
            ("valid", Open + "protected override void Render() => Ui.Button(Model.Count.ToString(), new Msg(1), key: \"add\"); }", []),
            ("uncalled-impure", Open + "protected override void Render() => System.IO.File.WriteAllText(\"x\", \"x\"); }", ["KUM005"]),
            ("initializer-impure", Open + "readonly string text = System.IO.File.ReadAllText(\"x\"); protected override void Render() => C.Text(text); }", ["KUM005"]),
            ("mutable-field", Open + "int value; protected override void Render() { value++; C.Text(value.ToString()); } }", ["KUM012"]),
            ("unsealed", "public class Screen(Model model) : View<Model, Msg>(model) { protected override void Render() {} }", ["KUM012"]),
            ("helper-plumbing", Open + "protected override void Render() {} void Label(Model value) {} }", ["KUM012"]),
            ("static-plumbing", Open + "protected override void Render() => Tools.Draw(Ui, Model); } static class Tools { public static void Draw(Ui<Msg> ui, Model model) => ui.Context.Text(model.Count.ToString()); }", ["KUM012"]),
            ("mapped-this", Open + "protected override void Render() { Ui.Child(new NumberView(0), \"number\", n => new Msg(Model.Count + n)); } }", ["KUM013"]),
            ("desktop-palette-captured-view", Open + "protected override void Render() { var actions = Ui.DesktopActions(System.Array.Empty<Kumiko.Desktop.DesktopAction<Msg>>()); actions.Palette(\"\", text => new Msg(Model.Count), new Msg(0)); } }", ["KUM013"]),
            ("desktop-palette-local", Open + "protected override void Render() { var count = Model.Count; var actions = Ui.DesktopActions(System.Array.Empty<Kumiko.Desktop.DesktopAction<Msg>>()); actions.Palette(\"\", text => new Msg(count), new Msg(0)); } }", []),
            ("desktop-delegate-initializer", Open + "protected override void Render() { var actions = Ui.DesktopActions(System.Array.Empty<Kumiko.Desktop.DesktopAction<Msg>>()); Func<string,Msg> query = text => new Msg(Model.Count); actions.Palette(\"\", query, new Msg(0)); } }", ["KUM013"]),
            ("desktop-delegate-reassigned", Open + "protected override void Render() { var actions = Ui.DesktopActions(System.Array.Empty<Kumiko.Desktop.DesktopAction<Msg>>()); Func<string,Msg> query = static text => new Msg(0); query = text => new Msg(Model.Count); actions.Palette(\"\", query, new Msg(0)); } }", ["KUM013"]),
            ("desktop-delegate-chain", Open + "protected override void Render() { var actions = Ui.DesktopActions(System.Array.Empty<Kumiko.Desktop.DesktopAction<Msg>>()); Func<string,Msg> query = text => new Msg(Model.Count); var alias = query; actions.Palette(\"\", alias, new Msg(0)); } }", ["KUM013"]),
            ("desktop-delegate-local-function", Open + "protected override void Render() { var actions = Ui.DesktopActions(System.Array.Empty<Kumiko.Desktop.DesktopAction<Msg>>()); Msg Convert(string text) => new Msg(Model.Count); Func<string,Msg> query = Convert; actions.Palette(\"\", query, new Msg(0)); } }", ["KUM013"]),
            ("desktop-delegate-method-group", Open + "protected override void Render() { var actions = Ui.DesktopActions(System.Array.Empty<Kumiko.Desktop.DesktopAction<Msg>>()); Func<string,Msg> query = Rename; actions.Palette(\"\", query, new Msg(0)); } Msg Rename(string text) => new Msg(Model.Count); }", ["KUM013"]),
            ("desktop-delegate-safe-local", Open + "protected override void Render() { var actions = Ui.DesktopActions(System.Array.Empty<Kumiko.Desktop.DesktopAction<Msg>>()); var count = Model.Count; Func<string,Msg> query = text => new Msg(count); actions.Palette(\"\", query, new Msg(0)); } }", []),
            ("desktop-delegate-cycle", Open + "protected override void Render() { var actions = Ui.DesktopActions(System.Array.Empty<Kumiko.Desktop.DesktopAction<Msg>>()); Func<string,Msg> query = static text => new Msg(0); var alias = query; query = alias; actions.Palette(\"\", alias, new Msg(0)); } }", []),
            ("ui-delegate-alias", Open + "protected override void Render() { Func<string,Msg> query = text => new Msg(Model.Count); Ui.TextField(\"field\", \"Field\", \"\", query); } }", ["KUM013"]),
            ("callback-this", Open + "protected override void Render() { Ui.TextField(\"name\", \"Name\", \"\", text => new Msg(Model.Count)); } }", ["KUM013"]),
            ("mapped-local", Open + "protected override void Render() { var count = Model.Count; Ui.Child(new NumberView(0), \"number\", n => new Msg(count + n)); } }", []),
            ("mapped-alias", Open + "protected override void Render() { var self = this; Ui.Child(new NumberView(0), \"number\", n => new Msg(self.Model.Count + n)); } }", ["KUM013"]),
            ("mapped-ui-alias", Open + "protected override void Render() { var bound = Ui; Ui.Child(new NumberView(0), \"number\", n => { bound.Dispatch(new Msg(n)); return new Msg(n); }); } }", ["KUM013"]),
            ("callback-method", Open + "protected override void Render() => Ui.TextField(\"name\", \"Name\", \"\", Rename); Msg Rename(string value) => new(Model.Count); }", ["KUM013"]),
            ("callback-local-function", Open + "protected override void Render() { Msg Convert(int n) => new(Model.Count+n); Ui.Child(new NumberView(0), \"number\", Convert); } }", ["KUM013"]),
            ("callback-local-value", Open + "protected override void Render() { var count = Model.Count; Msg Convert(int n) => new(count+n); Ui.Child(new NumberView(0), \"number\", Convert); } }", []),
            ("mutable-primary-prop", "public sealed class Child(Model model, int width) : View<Model, Msg>(model) { protected override void Render() { width++; C.Text(width.ToString()); } }", ["KUM012"]),
            ("callback-primary-prop", "public sealed class Child(Model model, int width) : View<Model, Msg>(model) { protected override void Render() => Ui.TextField(\"name\", \"Name\", \"\", text => new Msg(width)); }", ["KUM013"]),
            ("callback-primary-model", Open + "protected override void Render() => Ui.TextField(\"name\", \"Name\", \"\", text => new Msg(model.Count)); }", ["KUM013"]),
            ("callback-primary-local", "public sealed class Child(Model model, int width) : View<Model, Msg>(model) { protected override void Render() { var value = width; Ui.TextField(\"name\", \"Name\", \"\", text => new Msg(value)); } }", []),
            ("stack-owned-ui", Open + "protected override void Render() => Ui.Stack(NavigationPath<string,Entry>.Start(\"root\"), Model, static (bound, props, entry) => bound.Context.Text(entry.Route)); }" + Entry, []),
            ("stack-owned-alias", Open + "protected override void Render() => Ui.Stack(NavigationPath<string,Entry>.Start(\"root\"), Model, static (bound, props, entry) => { var current = bound; current.Context.Text(entry.Route); }); }" + Entry, []),
            ("stack-captured-ui", Open + "protected override void Render() => Ui.Stack(NavigationPath<string,Entry>.Start(\"root\"), Model, (bound, props, entry) => Ui.Context.Text(entry.Route)); }" + Entry, ["KUM013"]),
            ("keyed-child", Open + "protected override void Render() { for(int i=0;i<2;i++) Ui.Child(new NumberView(i), i, static n => new Msg(n)); } }", []),
            ("shell-valid", "public sealed class Frame(Model model) : NavigationShell<Model, Msg>(model) { protected override void Render() { var count = Model.Count; Ui.TextField(\"q\", \"Query\", \"\", text => new Msg(count)); Outlet(); } }", []),
            ("shell-unsealed", "public class Frame(Model model) : NavigationShell<Model, Msg>(model) { protected override void Render() => Outlet(); }", ["KUM012"]),
            ("shell-helper-plumbing", "public sealed class Frame(Model model) : NavigationShell<Model, Msg>(model) { protected override void Render() => Outlet(); void Label(Model value) {} }", ["KUM012"]),
            ("shell-callback-this", "public sealed class Frame(Model model) : NavigationShell<Model, Msg>(model) { protected override void Render() { Ui.TextField(\"q\", \"Query\", \"\", text => new Msg(Model.Count)); Outlet(); } }", ["KUM013"]),
            ("partial-view", "public sealed partial class Partial(Model model) : View<Model,Msg>(model) { protected override void Render() => Label(); } public sealed partial class Partial { void Label() => C.Text(Model.Count.ToString()); }", [])
        };
        var report = new List<object>();
        foreach (var item in cases)
        {
            var compilation = Compile("ViewFixture", Header + item.Source, references);
            var actual = await Diagnostics(compilation);
            if (!actual.SequenceEqual(item.Expected.Order()))
                throw new Exception($"view-{item.Name}: expected {string.Join(',', item.Expected)}, got {string.Join(',', actual)}");
            File.WriteAllText(Path.Combine(output, $"view-{item.Name}.cs.txt"), Header + item.Source);
            report.Add(new
            {
                item.Name,
                Diagnostics = actual
            });
        }
        var library = Compile("ViewLibrary", Header + Open + "protected override void Render() => C.Text(Model.Count.ToString()); }", references);
        using var bytes = new MemoryStream();
        if (!library.Emit(bytes).Success)
            throw new Exception("Could not compile view library");
        var consumer = Compile("ViewConsumer", "using Kumiko.UI; using Kumiko.Runtime; public sealed class Consumer(Model model) : View<Model, Msg>(model) { protected override void Render() => Ui.Child(new Screen(Model), \"screen\", static message => message); }",
            references.Append(MetadataReference.CreateFromImage(bytes.ToArray())));
        if ((await Diagnostics(consumer)).Length != 0)
            throw new Exception("Referenced view library rejected");
        File.WriteAllText(Path.Combine(output, "views.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() }));
        Console.WriteLine($"PASS: {cases.Length + 1} view-class compiler E2E fixtures");
    }

    static CSharpCompilation Compile(string name, string source, IEnumerable<MetadataReference> references)
    {
        var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length > 0)
            throw new Exception(string.Join('\n', errors.Select(d => d.ToString())));
        return compilation;
    }

    static async Task<string[]> Diagnostics(CSharpCompilation compilation) =>
        (await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new KumikoAnalyzer(), new ViewAnalyzer())).GetAnalyzerDiagnosticsAsync())
        .Select(d => d.Id).Distinct().Order().ToArray();
}
