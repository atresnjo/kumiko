using Kumiko;
using Kumiko.Documents;
using Kumiko.Examples.DocumentEditor;
using Kumiko.Runtime;

// Explicit local/private root only. Snapshot mode never creates a native window.
using var store = new FileDocumentStore();
using var program = DocumentApp.CreateProgram(new KumikoServices().Add<IDocumentStore>(store));
if (args.Any(arg => arg.StartsWith("--kumiko-", StringComparison.Ordinal)))
    Dev.Run("Document Editor", 820, 640, program, args);
else
{
    using var app = new App(new("Document Editor"));
    using var quit = DocumentHost.BindQuit(app, program, static model => model.Workspace, new DocumentMessage.Quit());
    app.NewWindows(program);
    app.Run();
}
