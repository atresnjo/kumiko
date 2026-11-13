using Kumiko.Runtime;
using Kumiko.UI;

namespace ReactVite;

public sealed record ShellProps(int Count, string LastSource);

/// <summary>A native Kumiko toolbar above the embedded React app.</summary>
public sealed class ShellScreen(ShellProps props) : Screen<ShellProps, CounterMsg>(props)
{
    public const float ToolbarHeight = 56;

    protected override void Render()
    {
        using var page = C.Div(key: "page").SizeFull().Background(C.Theme.Background);
        Toolbar();
        float width = MathF.Max(1, C.Size.Width);
        float height = MathF.Max(1, C.Size.Height - ToolbarHeight);
        C.WebView(Host.WebViewId, width, height, "react", "React app");
    }

    void Toolbar()
    {
        using var bar = C.Div(key: "toolbar").Row().ItemsCenter().FillWidth().Height(ToolbarHeight)
            .Px(Space.S4).Gap(Space.S3).Background(C.Theme.Surface).Border(C.Theme.Border);
        using (C.Div(key: "summary").Flex().Gap(Space.S1))
        {
            C.Text("Native Kumiko toolbar", Typography.Label, key: "title");
            C.Text($"Count {Model.Count} · last change from {Model.LastSource}", Typography.Caption with
            {
                Color = C.Theme.Muted
            }, key: "status");
        }
        Ui.Button("Add from Kumiko", new CounterMsg.Add(1, "Kumiko"), key: "add");
        Ui.Button("Reset", new CounterMsg.Reset(), key: "reset");
    }
}
