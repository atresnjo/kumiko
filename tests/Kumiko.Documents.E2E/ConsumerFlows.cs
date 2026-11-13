using Kumiko.Documents;
using Kumiko.Examples.DocumentEditor;
using Kumiko.RichText;
using Kumiko.Runtime;
using Kumiko.UI;
using Minimal = Kumiko.Examples.RichEditor;

static class ConsumerFlows
{
    internal static void Run(string output, Action<bool, string> check)
    {
        try
        {
            using var missing = DocumentApp.CreateProgram();
            check(false, "generated document factory requires store");
        }
        catch (InvalidOperationException error)
        {
            check(error.Message.Contains("IDocumentStore", StringComparison.Ordinal), "generated service validation rejects missing document store before rendering");
        }
        using (var simple = Minimal.EditorApp.CreateProgram())
        using (var ui = new Tester(simple, new(Width: 820, Height: 640, DevMode: true)))
        {
            check(!ui.Context.Warnings.Any(w => w.Code == "contrast"), "minimal consumer links retain readable theme text contrast");
            ui.Get(Role.TextField, "Document text").Click();
            ui.Key(Key.A, Modifiers.Primary);
            ui.TypeText("Minimal consumer\nSelected text");
            ui.Key(Key.A, Modifiers.Primary);
            ui.Get(Role.Button, "Bold").Click();
            check(simple.Model.Editor.Value.Paragraphs.All(p => p.Runs.All(r => r.Attributes.Bold)), "minimal consumer formats through public rich editor commands");
            Capture(ui, output, "minimal-consumer");
        }
        var store = new HeldStore();
        using var program = DocumentApp.CreateProgram(new KumikoServices().Add<IDocumentStore>(store));
        using var tester = new Tester(program, new(Width: 820, Height: 640, DevMode: true));
        tester.Get(Role.TextField, "Explicit save destination").Click();
        tester.TypeText(Path.Combine(output, "consumer.kumiko"));
        tester.Get(Role.TextField, "Document text").Click();
        tester.Key(Key.A, Modifiers.Primary);
        tester.TypeText("Captured version");
        tester.Get(Role.Button, "Save").Click();
        check(store.Started.Wait(TimeSpan.FromSeconds(5)), "document consumer starts injected asynchronous save");
        tester.Get(Role.TextField, "Document text").Click();
        tester.Key(Key.End, Modifiers.Primary);
        tester.TypeText(" plus newer typing");
        store.Release.TrySetResult();
        Drain(program, tester);
        var document = program.Model.Workspace.Documents.Single();
        check(document.Dirty && document.SavedValue.PlainText == "Captured version" && document.Value.PlainText.EndsWith("plus newer typing", StringComparison.Ordinal),
            "document consumer preserves edit made during held save");
        Capture(tester, output, "document-save-race");
        store.ExternalConflict = true;
        tester.Get(Role.Button, "Save").Click();
        Drain(program, tester);
        check(program.Model.Workspace.Documents.Single().Conflict && program.Model.Error is not null, "document consumer reports external-file conflict without clearing local edits");
        Capture(tester, output, "document-external-conflict");
        tester.Get(Role.Button, "Duplicate").Click();
        check(tester.Windows.Count == 2, "document consumer projects a second independent document window");
        var copy = program.Model.Workspace.Documents.Last();
        Capture(tester.In(new DocumentWindow(copy.Id)), output, "document-copy");
    }
    static void Drain(Program<DocumentModel, DocumentMessage> program, Tester tester)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (program.PendingCommands > 0 && deadline.Elapsed < TimeSpan.FromSeconds(5))
        {
            Thread.Yield();
            tester.Settle();
        }
        tester.Settle();
        if (program.PendingCommands > 0)
            throw new TimeoutException("Document effect did not complete.");
    }
    static void Capture(Tester tester, string output, string name)
    {
        tester.Screenshot(Path.Combine(output, name + ".png"));
        File.WriteAllText(Path.Combine(output, name + ".txt"), tester.Dump());
        tester.WriteWarnings(Path.Combine(output, name + ".warnings.json"));
    }
    sealed class HeldStore : IDocumentStore
    {
        internal readonly ManualResetEventSlim Started = new();
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool ExternalConflict;
        public Task<DocumentRead<T>> ReadAsync<T>(string path, IDocumentCodec<T> codec, CancellationToken token) where T : notnull => throw new NotSupportedException();
        public async Task<DocumentSaved<T>> SaveAsync<T>(DocumentRequest<T> request, IDocumentCodec<T> codec, CancellationToken token) where T : notnull
        {
            Started.Set();
            await Release.Task.WaitAsync(token);
            if (ExternalConflict)
                throw new DocumentConflictException(request.Path);
            return new(request, new(true, "fixture-captured-content"));
        }
        public Task<FileVersion> VersionAsync(string path, CancellationToken token) => throw new NotSupportedException();
        public Task MoveAsync(string source, string destination, FileVersion expected, CancellationToken token) => throw new NotSupportedException();
    }
}
