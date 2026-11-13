using System.Globalization;
using Kumiko.UI;

internal static class ResponsivePhysicalPaddingChecks
{
    internal static void Run(string output, Action<bool, string> check)
    {
        EdgeMatrixChecks.Run(output, check);
        var clicks = 0;
        using var tester = new Tester(c =>
        {
            using var root = c.Div(key: "physical-shell").SizeFull()
                .Padding(LayoutEdge.Left, 40).Padding(LayoutEdge.Right, 12)
                .Padding(LayoutEdge.Top, 20).Padding(LayoutEdge.Bottom, 24);
            using (c.Div(key: "physical-content").FillWidth().Height(100).Row())
            {
                if (c.Button("Leading action").Clicked)
                    clicks++;
                c.Button("Trailing action");
            }
            using (c.Div(key: "ltr-island").Flow(FlowDirection.LeftToRight).FillWidth().Height(100)
                .Padding(LayoutEdge.Left, 30).Padding(LayoutEdge.Right, 10))
            using (c.Div(key: "island-content").SizeFull().Background(c.Theme.Surface))
                c.Text("Physical padding inside an LTR island");
        }, new(1024, 400));
        foreach (var culture in new[] { "en-US", "he-IL", "en-US", "he-IL" })
        {
            tester.Context.Culture = CultureInfo.GetCultureInfo(culture);
            tester.Settle();
            var rtl = tester.Context.Culture.TextInfo.IsRightToLeft;
            ResponsiveEnvironmentChecks.Save(tester, output, $"physical-padding-{culture}");
            var body = ResponsiveEnvironmentChecks.Box(tester, "physical-content");
            check(body.X == 40 && body.Right == 1012 && body.Y == 20,
                "physical safe-area padding stays on the same edges after culture changes");
            var leading = tester.Get(Role.Button, "Leading action").Rect;
            var trailing = tester.Get(Role.Button, "Trailing action").Rect;
            check(rtl ? leading.X >= trailing.Right : leading.Right <= trailing.X,
                "physical padding preserves inherited reading order");
            var island = ResponsiveEnvironmentChecks.Box(tester, "ltr-island");
            var content = ResponsiveEnvironmentChecks.Box(tester, "island-content");
            check(content.X == island.X + 30 && content.Right == island.Right - 10,
                "nested direction overrides preserve physical padding");
            tester.Get(Role.Button, "Leading action").Click();
        }
        check(clicks == 4, "hit testing follows mirrored actions after culture-only reflow");
    }
}
