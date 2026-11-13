using System.Globalization;
using Flint;
using Kumiko.UI;

static class ResizeStability
{
    internal static void Run(Harness h, string output)
    {
        var tester = h.Tester;
        var trace = new List<string> { "page,width,height,frame,key,x,y,w,h,animating,next_frame" };
        var differences = new List<string>();
        var savedSize = tester.Context.Size;
        var motion = tester.Context.ReduceMotion;
        var original = h.Tab;
        var sidebarSize = h.Model.SidebarSize;
        tester.Context.ReduceMotion = false;
        foreach (var page in new[] { "All Commits", "Local Changes" })
        {
            h.Click(page);
            tester.Advance(1);
            var tab = h.Tab;
            var sidebar = h.Model.SidebarSize;
            foreach (var (width, height) in new[] { (1440f, 900f), (1000f, 700f), (900f, 560f), (1001f, 701f), (1440f, 900f), (1440.5f, 900.5f) })
            {
                // Do not use Tester.Resize: it settles two frames and hides resize feedback.
                tester.Context.Size = new(width, height);
                Dictionary<ulong, Rect>? first = null;
                for (var frame = 0; frame < 8; frame++)
                {
                    tester.Frame();
                    var nodes = tester.Context.Nodes.ToArray();
                    foreach (var node in nodes.Where(n => n.Key is "sidebar-reveal" or "main" or "header" or "history" or "details" or "local-changes" or "index" or "working" or "composer" or "unstaged" or "staged" or "sections-resize" || n.Role == Role.TextField))
                        trace.Add(string.Create(CultureInfo.InvariantCulture, $"{page},{width},{height},{frame},{node.Key},{node.Rect.X},{node.Rect.Y},{node.Rect.Width},{node.Rect.Height},{tester.Context.Animating},{tester.Context.NextFrame}"));
                    first ??= nodes.ToDictionary(n => n.Id, n => n.Rect);
                    foreach (var node in nodes)
                        if (first.TryGetValue(node.Id, out var rect) && rect != node.Rect)
                            differences.Add($"{page} {width}x{height} frame {frame}: {node.Key} {rect} -> {node.Rect}");
                }
                h.Shot($"resize-{page.Replace(' ', '-')}-{width:0}-{height:0}");
            }
            h.Check(h.Tab.File == tab.File && h.Tab.Sha == tab.Sha && h.Tab.Patch == tab.Patch && h.Tab.Subject == tab.Subject &&
                h.Tab.HistoryHeight == tab.HistoryHeight && h.Tab.WorkingFilesWidth == tab.WorkingFilesWidth && h.Tab.UnstagedFraction == tab.UnstagedFraction && h.Model.SidebarSize == sidebar,
                "Window resizing preserves selection, patch, draft and saved divider preferences");
        }
        File.WriteAllLines(Path.Combine(output, "resize-frames.csv"), trace);
        File.WriteAllLines(Path.Combine(output, "resize-differences.txt"), differences);
        h.Check(differences.Count == 0, "Window resize pane geometry is stable from its first rendered frame");
        tester.Resize(1440, 900);
        var dividerTrace = new List<string> { "handle,step,frame,x,y,messages,passes,build_ms,layout_ms,paint_ms" };
        var repeated = 0;
        foreach (var (page, name, vertical) in new[] { ("All Commits", "Resize history", false), ("Local Changes", "Resize sidebar", true), ("Local Changes", "Resize local files", true), ("Local Changes", "Resize unstaged files", false) })
        {
            h.Click(page);
            var handle = tester.Get(Role.Separator, name).Rect;
            var x = handle.X + handle.Width / 2;
            var y = handle.Y + handle.Height / 2;
            tester.Press(x, y);
            for (var step = 0; step < 12; step++)
            {
                var delta = (step + 1) / 3f;
                tester.MoveTo(x + (vertical ? delta : 0), y + (vertical ? 0 : delta));
                var start = h.Program.History.Count;
                for (var frame = 0; frame < 5; frame++)
                {
                    tester.Frame();
                    var rect = tester.Get(Role.Separator, name).Rect;
                    var messages = h.Program.History.Skip(start).Count(s => s.Message is Msg.ResizeHistory or Msg.ResizeSidebar or Msg.ResizeWorkingFiles or Msg.ResizeUnstaged);
                    repeated = Math.Max(repeated, messages);
                    var stats = tester.Context.Stats;
                    dividerTrace.Add(string.Create(CultureInfo.InvariantCulture, $"{name},{step},{frame},{rect.X},{rect.Y},{messages},{stats.Passes},{stats.BuildMilliseconds},{stats.LayoutMilliseconds},{stats.PaintMilliseconds}"));
                }
            }
            tester.Release(x + (vertical ? 4 : 0), y + (vertical ? 0 : 4));
        }
        File.WriteAllLines(Path.Combine(output, "divider-frames.csv"), dividerTrace);
        h.Check(repeated == 0, "Holding a divider still sends no repeated resize messages or layout feedback");
        h.Program.Send(new Msg.ResizeHistory(original.HistoryHeight));
        h.Program.Send(new Msg.ResizeSidebar(sidebarSize));
        h.Program.Send(new Msg.ResizeWorkingFiles(original.WorkingFilesWidth));
        h.Program.Send(new Msg.ResizeUnstaged(original.UnstagedFraction));
        h.Click(original.Page == Page.History ? "All Commits" : "Local Changes");
        tester.Context.ReduceMotion = motion;
        tester.Resize(savedSize.Width, savedSize.Height);
        NativeResize.Run(h, output);
    }
}
