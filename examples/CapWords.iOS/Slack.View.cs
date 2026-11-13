using Kumiko.iOS;
using Kumiko.UI;
using static SlackApp;

internal sealed partial class SlackScreen(SlackModel model) : Kumiko.Runtime.Screen<SlackModel, SlackMessage>(model)
{
    protected override void Render()
    {
        var ui = Ui;
        var model = Model;
        var c = ui.Context;
        using var page = c.NativeLinearGradient(Color.Hex(0x401147), Palette.Background, key: "slack").SizeFull().ItemsCenter();
        using var content = c.Div(key: "slack-content").Width(Math.Min(c.Size.Width, 600)).FillHeight().Padding(new Edges(12, 20, 8, 20)).Gap(12);
        using (c.Div(key: "slack-header").Row().FillWidth().Height(44).ItemsCenter())
        {
            using (ui.NativePressable("All examples", new SlackMessage.Back(), haptics: true, key: "slack-back").Width(60).Height(44).Items(Align.Start).Justify(Justify.Center))
                c.NativeSymbol(model.Index < 0 ? "xmark" : "chevron.left", Palette.Text, 22, key: "back-symbol");
            c.Spacer();
            if (model.Index >= 0)
                c.NativeFlipCounter(model.Index + 1, "Left", new(Size: 17, Weight: Weight.Bold, Color: Palette.Text), key: "slack-counter");
            else
                using (c.Div(key: "counter-space").Width(120).Height(44))
                {
                }
            c.Spacer();
            using (ui.NativePressable("Undo", new SlackMessage.Undo(), enabled: model.Index < 4 && !model.Undoing,
                haptics: true, key: "slack-undo").Width(60).Height(44).Items(Align.End).Justify(Justify.Center))
                c.NativeLabel(model.Index < 4 ? "Undo" : "", new(Size: 16, Weight: Weight.Bold, Color: Palette.Text), key: "undo-label");
        }
        using (ui.NativeSwipeDeck(Ids, model.Index, model.Direction, swipe => new SlackMessage.Swipe(swipe.Id, swipe.Direction),
            Palette, disabled: model.Busy, key: "slack-deck").FillWidth().Grow().Margin(LayoutEdge.Bottom, 12))
            for (var i = 0; i < Ids.Length; i++)
                using (c.NativeSwipeCard(Ids[i]))
                    Chat(Ids[i]);
        if (model.Index >= 0)
        {
            using var footer = c.Div(key: "slack-footer").Row().FillWidth().Gap(20).Height(52);
            Action("Keep Unread", new SlackMessage.KeepUnread(), Palette.Surface, Palette.Border, model.Busy, "slack-unread");
            Action("Mark as Read", new SlackMessage.MarkRead(), Color.Hex(0x064e3b), Color.Hex(0x065f46), model.Busy, "slack-read");
        }
        else
        {
            using (c.Div(key: "slack-completion").Absolute().Offset(20, 68).Width(Math.Min(c.Size.Width, 600) - 40)
                .Height(Math.Max(0, c.Size.Height - 140)).Center().Gap(14).Enter(new(Opacity: .5f, Scale: .8f, Motion: Motion.Standard)))
            {
                c.NativeLabel("💥", new(Size: 58), key: "slack-boom");
                using (c.Div(key: "done-copy").ItemsCenter())
                {
                    c.NativeLabel("Boom. You're up to", new(Size: 30, Weight: Weight.Bold, Color: Palette.Text), key: "slack-done-title");
                    c.NativeLabel("date.", new(Size: 30, Weight: Weight.Bold, Color: Palette.Text), key: "slack-done-title-end");
                }
            }
            using var footer = c.Div(key: "slack-footer").Row().FillWidth().Height(52);
            Action("Done", new SlackMessage.Done(), Color.Hex(0x064e3b), Color.Hex(0x065f46), false, "slack-done");
        }
    }

    void Action(string title, SlackMessage message, Color fill, Color border, bool disabled, string key)
    {
        var ui = Ui;
        using var button = ui.NativePressable(title, message, enabled: !disabled, haptics: true, key: key).Flex().Height(52);
        using var surface = ui.Context.Div(key: "surface").SizeFull().Background(fill).Border(border, 1).Rounded(16).Center();
        ui.Context.NativeLabel(title, new(Size: 16, Weight: Weight.Semibold, Color: Palette.Text), key: "title");
    }
}

internal sealed partial class SlackApp
{
    internal static NativePalette Palette => new(Color.Hex(0x20031f), Color.Hex(0x171717), Color.Hex(0x262626),
        Color.Hex(0xe5e5e5), Color.Hex(0xa3a3a3), Color.Hex(0x34d399), Color.Hex(0x38bdf8), Color.Hex(0x303030));
}
