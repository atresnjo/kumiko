using CRMLite;
using Kumiko.UI;

/// <summary>The sidebar collapses to an icon rail with an eased transition and resizes from its edge.</summary>
static class SidebarFlows
{
    public static void Run(CrmSession s, string output, Action<bool, string> check)
    {
        float Width() => s.Node(Role.Group, "Sidebar").Rect.Width;
        check(MathF.Abs(Width() - s.Model.SidebarWidth) < .5f && s.Model.SidebarWidth == 232, "the sidebar opens at its default width");

        var handle = s.Node(Role.Separator, "Sidebar width");
        var (x, y) = (handle.Rect.X + handle.Rect.Width / 2, handle.Rect.Y + handle.Rect.Height / 2);
        s.Tester.Press(x, y);
        s.Tester.MoveTo(x + 30, y);
        s.Tester.MoveTo(x + 60, y);
        s.Tester.Release(x + 60, y);
        s.Tester.Settle();
        check(s.Model.SidebarWidth == 292 && MathF.Abs(Width() - 292) < .5f, "dragging the edge resizes the sidebar");
        s.Tester.Press(x + 60, y);
        s.Tester.MoveTo(x + 400, y);
        s.Tester.Release(x + 400, y);
        s.Tester.Settle();
        check(s.Model.SidebarWidth == 360, "the sidebar stops at its maximum width");
        s.Key(Key.Left);
        check(s.Model.SidebarWidth == 350, "arrow keys step the focused edge");
        s.Key(Key.Home);
        check(s.Model.SidebarWidth == 200 && MathF.Abs(Width() - 200) < .5f, "Home jumps to the minimum width");
        s.Tester.Screenshot(Path.Combine(output, "sidebar-narrow.png"));

        s.Click(Role.Button, "Collapse sidebar");
        check(s.Model.SidebarCollapsed && MathF.Abs(Width() - 52) < .5f, "collapsing leaves a 52 px icon rail");
        check(!s.Exists(Role.Separator, "Sidebar width"), "the collapsed rail cannot be resized");
        check(!s.Tester.HasText("Favorites") && !s.Tester.HasText("Hot accounts"), "rail labels are hidden once collapsed");
        var deals = s.Node(Role.Button, "Deals").Rect;
        check(deals.Width <= 32 && deals.X >= 10, "rail items shrink to icon buttons inside the rail");
        s.Click(Role.Button, "Deals");
        check(ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Deals, "rail items still navigate");
        s.Tester.Screenshot(Path.Combine(output, "sidebar-collapsed.png"));
        File.WriteAllText(Path.Combine(output, "sidebar-collapsed.txt"), s.Tester.Dump());
        check(s.Tester.Context.Warnings.Count == 0, "the rail renders without warnings: " +
            string.Join("; ", s.Tester.Context.Warnings.Select(w => $"{w.Code} {w.Path} {w.Message}")));

        s.Tester.Context.ReduceMotion = false;
        s.Click(Role.Button, "Expand sidebar");
        s.Tester.Frame(1 / 60f);
        var opening = Width();
        check(opening > 52 && opening < 190, $"expanding eases out instead of jumping ({opening:0} px after one frame)");
        s.Tester.Frame(1 / 60f);
        check(Width() > opening, "each frame moves the sidebar further open");
        for (var frame = 0; frame < 6; frame++)
            s.Tester.Frame(1 / 60f);
        s.Tester.Screenshot(Path.Combine(output, "sidebar-opening.png"));
        s.Tester.Advance(1);
        s.Tester.Settle();
        check(MathF.Abs(Width() - 200) < .5f && s.Tester.HasText("Favorites"), "the eased sidebar settles at its saved width with labels");
        s.Tester.Context.ReduceMotion = true;
    }
}
