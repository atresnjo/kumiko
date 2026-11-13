using Kumiko.Runtime;
using Kumiko.UI;

namespace KumikoTemplate;

internal sealed class KumikoTemplateView(Model model) : Kumiko.Runtime.Screen<Model, Msg>(model)
{
    protected override void Render()
    {
        var c = Ui.Context;
        using var page = c.Div(key: "page").SizeFull().Padding(Space.S5).Gap(Space.S3);
        c.Text($"Count: {Model.Count}", Typography.Title);
        Ui.Button("Increment", new Msg.Increment(), key: "increment");
        Ui.TextField(key: "name", label: "Name", value: Model.Name, onChange: text => new Msg.Rename(text), options: new(Placeholder: "Name"));
        Ui.Checkbox("Email updates", Model.Updates, on => new Msg.SetUpdates(on), key: "updates");
    }
}
