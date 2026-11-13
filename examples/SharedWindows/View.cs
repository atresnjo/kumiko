using Kumiko.Runtime;
using Kumiko.UI;

namespace SharedWindows;

sealed class WorkspaceView(WorkspaceModel model, WorkspaceWindow window)
    : WindowScreen<WorkspaceModel, WorkspaceMessage, WorkspaceWindow>(model, window)
{
    protected override void Render()
    {
        C.Theme = Model.Document.Dark ? Theme.DarkTheme : Theme.Light;
        using var page = C.Div(key: "workspace").SizeFull().Padding(Space.S5).Gap(Space.S4).Background(C.Theme.Background);
        switch (Window)
        {
            case WorkspaceWindow.Editor editor:
                Editor(editor.Id);
                break;
            case WorkspaceWindow.Inspector:
                Inspector();
                break;
            case WorkspaceWindow.Settings:
                Settings();
                break;
        }
        C.Text(Model.Status, Typography.Caption with
        {
            Color = C.Theme.Muted
        }, key: "status");
    }

    void Editor(int id)
    {
        C.Text($"Document {id}", Typography.Heading, key: "title");
        C.Text("Changes appear in both editors. Selection belongs to this window.", Typography.Body with
        {
            Wrap = true
        }, key: "intro");
        Ui.TextField("document", "Document text", Model.Document.Text, static text => new WorkspaceMessage.Edit(text), new(Width: 540));
        C.Text("Selection: " + (id == 1 ? Model.FirstSelection : Model.SecondSelection), key: "selection");
        using (C.Div(key: "actions").Row().Gap(Space.S2))
        {
            Ui.Button("Select", new WorkspaceMessage.Select(id), key: "select");
            Ui.Button("Inspector", new WorkspaceMessage.OpenInspector(), key: "inspector");
            Ui.Button("Settings", new WorkspaceMessage.OpenSettings(), key: "settings");
            Ui.Button("Close window", new WorkspaceMessage.Close(new WorkspaceWindow.Editor(id)), key: "close");
        }
    }

    void Inspector()
    {
        C.Text("Shared document", Typography.Heading, key: "title");
        C.Text(Model.Document.Text, Typography.Body with
        {
            Wrap = true
        }, key: "text");
        C.Text($"{Model.Document.Text.Length} characters", key: "length");
        C.Text($"Selections: {Model.FirstSelection} / {Model.SecondSelection}", key: "selections");
        Ui.Button("Open editors", new WorkspaceMessage.OpenEditors(), key: "editors");
        Ui.Button("Settings", new WorkspaceMessage.OpenSettings(), key: "settings");
    }

    void Settings()
    {
        C.Text("Shared settings", Typography.Heading, key: "title");
        Ui.Toggle("Dark theme", Model.Document.Dark, static dark => new WorkspaceMessage.SetDark(dark), key: "theme");
        C.Text("The theme is part of the shared document model.", Typography.Body with
        {
            Wrap = true
        }, key: "intro");
    }
}
