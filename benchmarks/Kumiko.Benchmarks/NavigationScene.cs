using Kumiko.UI;

static class NavigationScene
{
    internal static void Render(Context c)
    {
        var active = (int)Math.Min(4, Math.Max(0, c.FrameId - 1));
        for (var i = 0; i < 5; i++)
        {
            using var page = c.RetainedScope(key: i, visible: active == i);
            if (!page.Visible)
                continue;
            using var content = c.Div(key: "content").SizeFull().Padding(16).Gap(8);
            c.TextField("A retained draft", _ => { }, new(Name: "Draft"), key: "draft");
            using var list = c.Div(key: "list").FillWidth().Grow().Scroll();
            for (var row = 0; row < 100; row++)
                c.Text($"Page {i} row {row}", key: row);
        }
    }
}
