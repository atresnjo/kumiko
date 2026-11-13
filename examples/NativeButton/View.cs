using Kumiko.Runtime;
using Kumiko.UI;

internal sealed record NativeButtonProps(int Count, bool Enabled, bool Visible);

internal sealed class NativeButtonView(NativeButtonProps props) : Screen<NativeButtonProps, NativeButtonMessage>(props)
{
    protected override void Render()
    {
        using var column = Ui.Context.Div(key: "content").SizeFull().Center().Padding(Space.S5);
        if (Model.Visible)
            Ui.Button($"Tapped {Model.Count} times", new NativeButtonMessage.Increment(),
                new(Disabled: !Model.Enabled), key: "counter");
    }
}
