using Kumiko.RichText;
using Kumiko.Runtime;
using Kumiko.UI;

namespace Kumiko.Examples.RichEditor;

public sealed record EditorModel(RichDocument Editor, NavigationHistory<string> Navigation);
public abstract partial record EditorMessage
{
    public sealed record Edit(RichCommand Command) : EditorMessage;
    public sealed record Navigate(HistoryRequest Request) : EditorMessage;
}
public sealed partial class EditorApp : IKumiko<EditorModel, EditorMessage>
{
    public static EditorModel Init => new(RichDocument.FromValue(RichExchange.ParseMarkdown(
        "# A small rich editor\nSelect text and format it.\n- [ ] A model-owned task\n- [x] Native text rendering\n**Bold**, *italic*, `code` and [links](https://example.com).")), NavigationHistory<string>.Start("editor"));
    public static NavigationRoot<EditorMessage> Root(EditorModel model) => NavigationRoot<EditorMessage>.Create(model.Navigation, "app",
        _ => new EditorScreen(model.Editor).Map<EditorMessage>(static m => m), static r => new EditorMessage.Navigate(r));
    public static (EditorModel, Cmd<EditorMessage>) Update(EditorModel model, EditorMessage message) => (message switch
    {
        EditorMessage.Edit edit => model with { Editor = model.Editor.Apply(edit.Command) },
        EditorMessage.Navigate navigate => model with { Navigation = model.Navigation.Move(navigate.Request, "app") },
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    }, Cmd<EditorMessage>.None);
}
public sealed class EditorScreen(RichDocument document) : Screen<RichDocument, EditorMessage>(document)
{
    protected override void Render()
    {
        using var page = C.Div(key: "page").SizeFull().Padding(Space.S5).Gap(Space.S3).Background(C.Theme.Background);
        C.Text("Rich text", Typography.Heading, key: "title");
        using (C.Div(key: "toolbar").Row().Gap(Space.S2))
        {
            Ui.Button("Bold", new EditorMessage.Edit(new RichCommand.Toggle(RichFormat.Bold)), key: "bold");
            Ui.Button("Italic", new EditorMessage.Edit(new RichCommand.Toggle(RichFormat.Italic)), key: "italic");
            Ui.Button("Heading", new EditorMessage.Edit(new RichCommand.Paragraph(new(Heading: 2))), key: "heading");
            Ui.Button("Task", new EditorMessage.Edit(new RichCommand.Paragraph(new(List: RichList.Task))), key: "task");
            Ui.Button("Undo", new EditorMessage.Edit(new RichCommand.Undo()), key: "undo");
            Ui.Button("Redo", new EditorMessage.Edit(new RichCommand.Redo()), key: "redo");
        }
        Ui.RichTextEditor("editor", "Document text", Model, static command => new EditorMessage.Edit(command),
            new(Width: C.Size.Width - Space.S5 * 2, Height: C.Size.Height - 150));
        C.Text("Formatting, selection and undo belong to the model.", Typography.Caption, key: "status");
    }
}
