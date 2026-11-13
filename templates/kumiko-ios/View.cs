using System.Collections.Immutable;
using Kumiko.iOS;
using Kumiko.Runtime;
using Kumiko.UI;

namespace KumikoiOSTemplate;

internal sealed record HomeProps(int Count, string Name);
internal sealed record SettingsProps(bool Updates, ImmutableArray<string> Log, HistoryRequest? Back);

internal sealed class HomeScreen(HomeProps props) : Screen<HomeProps, Msg>(props)
{
    protected override void Render()
    {
        using var page = C.Div(key: "page").SizeFull().Padding(Space.S5).Gap(Space.S3);
        C.Text($"Count: {Model.Count}", Typography.Title, key: "count");
        Ui.Button("Increment", new Msg.Increment(), new(ButtonVariant.Primary), key: "increment");
        Ui.TextField(key: "name", label: "Name", value: Model.Name, onChange: static text => new Msg.Rename(text), options: new(Placeholder: "Name"));
        Ui.Button("Settings", new Msg.Open(Route.Settings), key: "settings");
    }
}

internal sealed class SettingsScreen(SettingsProps props) : Screen<SettingsProps, Msg>(props)
{
    protected override void Render()
    {
        using var page = C.Div(key: "page").SizeFull().Padding(Space.S5).Gap(Space.S3);
        C.Heading("Settings", key: "title");
        using (C.Div(key: "updates-row").Row().FillWidth().ItemsCenter().Gap(Space.S3))
        {
            using (C.Div(key: "updates-label").Flex())
                C.Text("Email updates", key: "text");
            Ui.NativeSwitch("Email updates", Model.Updates, static on => new Msg.SetUpdates(on), key: "updates");
        }
        C.Label(Model.Log.IsEmpty ? "No lifecycle changes yet" : string.Join(" → ", Model.Log), key: "log");
        if (Model.Back is { } back)
            Ui.Button("Back", new Msg.Navigate(back), key: "back");
    }
}
