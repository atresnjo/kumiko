using Kumiko.UI;

/// <summary>In-window drag and drop and files dropped from the operating system, through Kumiko input only.</summary>
static class DragDropChecks
{
    public static void Run(string output, Action<bool, string> check)
    {
        string[] cards = ["a", "b", "c"];
        var drops = new List<(string Target, string Value)>();
        string[] files = [];
        var clicks = 0;
        var laneOver = false;
        var filesOver = false;
        void View(Context c)
        {
            using var page = c.Div(key: "page").SizeFull().Padding(16).Gap(12);
            using (c.Div(key: "cards").Row().Gap(8))
            {
                foreach (var card in cards)
                {
                    using var source = c.Div(key: card).Width(80).Height(40).Center().Background(c.Theme.Surface)
                        .DragSource("card", card, card.ToUpperInvariant());
                    var hit = source.Control();
                    c.Text("Card " + card, key: "label");
                    if (hit.Clicked)
                        clicks++;
                }
            }
            using (var lane = c.Div(key: "lane").Width(300).Height(120).Padding(8).Background(c.Theme.SurfaceHover))
            {
                var drop = lane.DropTarget("card");
                laneOver = drop.IsOver;
                if (drop.Dropped is { Item: { } item })
                    drops.Add(("lane", item.Value));
                using var inner = c.Div(key: "inner").Width(100).Height(50).Background(c.Theme.Surface);
                if (inner.DropTarget("card").Dropped is { Item: { } nested })
                    drops.Add(("inner", nested.Value));
            }
            using (var zone = c.Div(key: "files").Width(300).Height(50).Background(c.Theme.SurfaceHover))
            {
                var drop = zone.DropTarget(DragKinds.Files);
                filesOver = drop.IsOver;
                if (drop.Dropped is { } dropped)
                    files = [.. dropped.Files];
            }
            using (var tasks = c.Div(key: "tasks").Width(100).Height(40).Background(c.Theme.SurfaceHover))
            {
                if (tasks.DropTarget("task").Dropped is { Item: { } task })
                    drops.Add(("tasks", task.Value));
            }
        }
        using var t = new Tester(View, new(Width: 420, Height: 420));
        Rect Of(string key) => t.Context.Nodes.ToArray().Single(n => n.Key == key).Rect;
        Point Center(string key) => new(Of(key).X + Of(key).Width / 2, Of(key).Y + Of(key).Height / 2);
        void Drag(string from, Point to, bool cancel = false)
        {
            var start = Center(from);
            t.Press(start.X, start.Y);
            t.MoveTo(start.X + 8, start.Y + 8);
            t.MoveTo(to.X, to.Y);
            if (cancel)
            {
                t.Context.Input.Down = true;
                t.Key(Key.Escape);
            }
            t.Release(to.X, to.Y);
            t.Settle();
        }

        var a = Center("a");
        t.Press(a.X, a.Y);
        t.MoveTo(a.X + 2, a.Y + 1);
        t.Release(a.X + 2, a.Y + 1);
        t.Settle();
        check(clicks == 1 && drops.Count == 0, "a press that barely moves clicks instead of dragging");

        var lane = Of("lane");
        var laneSpot = new Point(lane.Right - 20, lane.Bottom - 20);
        var start = Center("a");
        t.Press(start.X, start.Y);
        t.MoveTo(start.X + 8, start.Y + 8);
        t.MoveTo(laneSpot.X, laneSpot.Y);
        t.Settle();
        check(laneOver && t.HasText("A"), "dragging over an accepting target highlights it and shows a preview");
        t.Screenshot(Path.Combine(output, "dragdrop-over.png"));
        t.Release(laneSpot.X, laneSpot.Y);
        t.Settle();
        check(drops.SequenceEqual([("lane", "a")]) && clicks == 1 && !t.HasText("A"), "dropping delivers the payload once without a click");

        Drag("b", Center("inner"));
        check(drops.Count == 2 && drops[1] == ("inner", "b"), "the innermost accepting target receives the drop");
        Drag("c", Center("tasks"));
        check(drops.Count == 2, "targets ignore payload kinds they do not accept");
        Drag("c", laneSpot, cancel: true);
        check(drops.Count == 2 && !laneOver, "Escape cancels a drag");
        Drag("c", Center("c"));
        check(drops.Count == 2 && clicks == 1, "a drag released on its source neither drops nor clicks");
        var clear = new Point(410, 410);
        Drag("c", clear);
        t.Click(laneSpot.X, laneSpot.Y);
        t.Settle();
        check(drops.Count == 2, "releasing outside every target ends the drag without a stale drop");

        var zone = Center("files");
        t.DragFiles(zone.X, zone.Y, ["/tmp/report.pdf"]);
        check(filesOver, "files dragged in from the system highlight a file target");
        t.DropFiles(laneSpot.X, laneSpot.Y, ["/tmp/report.pdf"]);
        check(files.Length == 0 && drops.Count == 2, "a target that accepts only cards ignores dropped files");
        t.DropFiles(zone.X, zone.Y, ["/tmp/report.pdf", "/tmp/data.csv"]);
        check(files.SequenceEqual(["/tmp/report.pdf", "/tmp/data.csv"]), "a file target receives every dropped path once");
        t.Settle();
        check(!filesOver, "the file highlight ends after the drop");
        t.Screenshot(Path.Combine(output, "dragdrop-files.png"));
        File.WriteAllText(Path.Combine(output, "dragdrop-files.txt"), t.Dump());
    }
}
