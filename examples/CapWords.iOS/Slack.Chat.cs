using Kumiko.iOS;
using Kumiko.UI;
using static SlackApp;

internal sealed partial class SlackScreen
{
    void Chat(string id)
    {
        var ui = Ui;
        var c = ui.Context;
        using var chat = c.Div(key: "chat").SizeFull().Rounded(24).Clip();
        using (c.Div(key: "channel-header").Row().FillWidth().Height(48).Center().Gap(4).Border(Palette.Border, 1))
        {
            c.NativeLabel("#" + id, new(Size: 16, Weight: Weight.Semibold, Color: Color.Hex(0xd4d4d4)), key: "channel-name");
            c.NativeSymbol("chevron.down", Color.Hex(0xd4d4d4), 12, key: "channel-menu");
        }
        using (c.Div(key: "messages").FillWidth().Grow().Scroll())
        using (c.Div(key: "message-list").FillWidth())
            for (var i = 0; i < 20; i++)
            {
                using var row = c.Div(key: "message-" + i).Row().FillWidth().Padding(20).Gap(16).Border(Palette.Border, .5f);
                using (c.Div(key: "avatar").Width(48).Height(48).Rounded(12).Background(Palette.Raised))
                {
                }
                using var lines = c.Div(key: "lines").Flex().Gap(8);
                SlackApp.Bar(c, 80, 16, "name");
                SlackApp.Bar(c, null, 12, "line-one");
                SlackApp.Bar(c, null, 12, "line-two");
                using (c.Div(key: "short-line").Width(Length.Percent(75)).Height(12).Rounded(6).Background(Palette.Raised))
                {
                }
            }
        using (c.Div(key: "composer").FillWidth().Height(70).Row().ItemsCenter().Padding(12).Gap(12).Border(Palette.Border, 1).Rounded(24))
        {
            using (c.Div(key: "plus-circle").Width(40).Height(40).Rounded(20).Background(Palette.Raised).Center())
                c.NativeSymbol("plus", Color.Hex(0x737373), 22, key: "add");
            using (c.Div(key: "placeholder").Flex())
                c.NativeLabel("Message #" + id, new(Size: 14, Color: Palette.Muted), key: "message-placeholder");
            c.NativeSymbol("mic", Color.Hex(0x737373), 18, key: "mic");
        }
    }

}

internal sealed partial class SlackApp
{
    internal static void Preview(Context c)
    {
        using var deck = c.NativeSwipeDeck(Ids, 4, NativeSwipeDirection.Right, Palette, preview: true, key: "slack-preview-deck")
            .Absolute().Offset(32, 20).Width(Math.Min(c.Size.Width - 104, 320)).Height(190);
        foreach (var id in Ids)
        {
            using var card = c.NativeSwipeCard(id);
            using (c.Div(key: "preview-chat").SizeFull().Padding(16).Gap(12))
            {
                c.NativeLabel("#" + id, new(Size: 14, Weight: Weight.Semibold, Color: Palette.Text), key: "channel-name");
                for (var i = 0; i < 3; i++)
                {
                    using var row = c.Div(key: "row-" + i).Row().FillWidth().Gap(12);
                    using (c.Div(key: "avatar").Width(30).Height(30).Rounded(8).Background(Palette.Raised))
                    {
                    }
                    using (c.Div(key: "copy").Flex().Gap(6))
                    {
                        Bar(c, 70, 10, "name");
                        Bar(c, null, 8, "message");
                    }
                }
            }
        }
    }

    internal static void Bar(Context c, float? width, float height, string key)
    {
        using var bar = c.Div(key: key).Height(height).Rounded(height / 2).Background(Palette.Raised);
        if (width is { } fixedWidth)
            bar.Width(fixedWidth);
        else
            bar.FillWidth();
    }
}
