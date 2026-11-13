#pragma warning disable KUM001, KUM002 // Intentional invalid views exercise runtime diagnostics.

using System.Globalization;
using System.Text.Json;
using Kumiko.UI;

static class AuthoringChecks
{
    public static void Run(string output, Action<bool, string> check)
    {
        var saved = 0;
        var reverse = false;
        void View(Context c)
        {
            using var root = c.Div(key: "page").SizeFull().Padding(12).Gap(8);
            c.Button("Save", key: "save");
            using var row = c.Div(key: "actions").Row().Gap(8);
            foreach (var name in reverse ? new[]
            {
                "B",
                "A"
            }

            : new[]
            {
                "A",
                "B"
            }

            )
                if (c.Button(name, key: name).Clicked)
                    saved++;
        }

        using var t = new Tester(View);
        var first = t.Dump();
        t.Frame();
        check(first == t.Dump(), "tree dumps are stable after layout");
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            check(first == t.Dump(), "tree dumps use invariant numbers");
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }

        var a = t.Get(Role.Button, "A");
        var id = a.Id;
        reverse = true;
        t.Frame();
        a.Click();
        check(a.Id == id && saved == 1, "semantic locators preserve keyed identity after reorder");
        t.Screenshot(Path.Combine(output, "authoring.png"));
        File.WriteAllText(Path.Combine(output, "authoring-tree.txt"), t.Dump());
        using var warnings = new Tester(c =>
        {
            using var root = c.Div(key: "warnings").SizeFull();
            using (c.Div(key: "small").Width(20).Height(10))
                c.Text("Text past its box");
            using (var empty = c.Div(key: "empty"))
                empty.Control();
            for (var i = 0; i < 2; i++)
            {
                using var duplicate = c.Div(key: "duplicate").Width(100).Height(30).Absolute();
                duplicate.Control();
            }

            for (var i = 0; i < 2; i++)
            {
                using var unkeyed = c.Div().Width(10).Height(10);
            }

            using (c.Div(key: "contrast").Background(Color.White))
                c.Text("Faint", new(Color: Color.Hex(0xeeeeee)));
        }, new(DevMode: true));
        var codes = warnings.Context.Warnings.Select(w => w.Code).ToHashSet();
        check(
            new[] { "overflow", "text-overflow", "empty-control", "overlap", "duplicate-key", "unkeyed-loop", "contrast" }.All(codes.Contains),
            "dev diagnostics expose layout, identity, interaction and contrast failures"
        );
        warnings.WriteWarnings(Path.Combine(output, "authoring-warnings.json"));
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "authoring-warnings.json")));
        check(json.RootElement.GetProperty("warnings").GetArrayLength() >= 7, "warnings are machine-readable JSON");
        using var percent = new Tester(c =>
        {
            using var root = c.Div(key: "percent").SizeFull().Padding(10);
            using var bar = c.Div(key: "bar").Width(Length.Percent(25)).Height(20);
        }, new(Width: 420));
        check(percent.Context.Nodes.ToArray().Single(n => n.Key == "bar").Rect.Width == 100, "percent widths use parent content size");
        percent.Resize(820, 600);
        check(percent.Context.Nodes.ToArray().Single(n => n.Key == "bar").Rect.Width == 200, "percent widths follow resize");
        using (var relative = new Tester(c =>
        {
            using var parent = c.Div(key: "relative").SizeFull().Padding(10);
            using var child = c.Div(key: "absolute").Absolute().Width(Length.Percent(50)).Height(Length.Percent(25));
        }, new(Width: 420, Height: 420)))
        {
            var rect = relative.Context.Nodes.ToArray().Single(n => n.Key == "absolute").Rect;
            check(rect == new Rect(10, 10, 200, 100), "absolute percentage dimensions exclude parent padding on both axes");
        }
        using (var rows = new Tester(c => c.UniformList("rows", 1000, 30, (ctx, i) => ctx.Text($"Row {i}")), new(DevMode: true)))
            check(rows.Context.Warnings.Any(w => w.Code == "unkeyed-row"), "virtual lists report missing explicit row identity");
        using (var hidden = new Tester(c =>
        {
            using var box = c.Div(key: "hidden").Width(80).Height(30).Opacity(0).Semantics(Role.Button, "Hidden");
            box.Control();
        }))
        {
            try
            {
                hidden.Get(Role.Button, "Hidden");
                check(false, "transparent controls are excluded from semantic lookup");
            }
            catch (InvalidOperationException) { check(true, "transparent controls are excluded from semantic lookup"); }
        }
        using (var escaped = new Tester(c => c.Text("Quoted \"text\"\nsecond line")))
            check(escaped.Dump().Contains("\\\"text\\\"\\nsecond line", StringComparison.Ordinal), "tree text escapes quotes and line breaks");
        using (var scrolling = new Tester(c =>
        {
            using var root = c.Div(key: "scroll").SizeFull().Scroll();
            for (var i = 0; i < 20; i++)
                using (c.Div(key: i).FillWidth().Height(30))
                    c.Text($"Item {i}");
        }, new(Width: 200, Height: 100, DevMode: true)))
        {
            scrolling.MoveTo(50, 50);
            scrolling.Scroll(60);
            check(scrolling.Dump().Contains("scroll=y:60/500", StringComparison.Ordinal), "tree reports scroll offsets after input");
            check(!scrolling.Context.Warnings.Any(w => w.Code is "overflow" or "overlap"), "clipped scroll content avoids false layout warnings");
        }

        var x = 0f;
        var y = 0f;
        var width = 10f;
        var reorder = false;
        using (var motion = new Tester(c =>
        {
            using var root = c.Div(key: "motion").SizeFull();
            if (reorder)
            {
                using var sibling = c.Div(key: "other").Height(1);
            }

            using var card = c.Div(key: "animated").Absolute().Offset(x, y, animate: Motion.Quick).Width(width, animate: Motion.Quick).Height(20);
        }))
        {
            x = 100;
            y = 60;
            width = 80;
            motion.Frame();
            var firstMotion = motion.Context.Nodes.ToArray().Single(n => n.Key == "animated").Rect;
            check(
                firstMotion.X is > 0 and < 100 && firstMotion.Y is > 0 and < 60 && firstMotion.Width is > 10 and < 80,
                "node motion animates independent property channels"
            );
            reorder = true;
            motion.Frame();
            var afterReorder = motion.Context.Nodes.ToArray().Single(n => n.Key == "animated").Rect;
            check(afterReorder.X > firstMotion.X && afterReorder.X < x, "keyed motion survives sibling reorder");
            motion.Context.ReduceMotion = true;
            motion.Frame();
            var finalMotion = motion.Context.Nodes.ToArray().Single(n => n.Key == "animated").Rect;
            check(finalMotion.X == x && finalMotion.Y == y && finalMotion.Width == width, "reduced motion reaches all targets immediately");
        }

        var cached = false;
        using (var controls = new Tester(c =>
        {
            using var box = c.Div(key: "cached").Width(80).Height(30).Semantics(Role.Button, "Cached");
            var firstHit = box.Control(false);
            var again = box.Control(true);
            cached = firstHit == again;
        }))
            check(cached && !controls.Get(Role.Button, "Cached").Enabled, "Control caches interaction and enabled state once per build");
        using (var covered = new Tester(c =>
        {
            using (var under = c.Div(key: "under").Absolute().Width(80).Height(30).Semantics(Role.Button, "Under"))
                under.Control();
            using (var over = c.Div(key: "over").Absolute().Width(80).Height(30).Semantics(Role.Button, "Over"))
                over.Control();
        }))
        {
            try
            {
                covered.Get(Role.Button, "Under").Click();
                check(false, "covered semantic click rejected");
            }
            catch (InvalidOperationException)
            {
                check(true, "covered semantic click rejected");
            }
        }

        var edgeClicks = 0;
        using (var edge = new Tester(c =>
        {
            using (var under = c.Div(key: "under").Absolute().Width(100).Height(30).Semantics(Role.Button, "Edge"))
                if (under.Control().Clicked)
                    edgeClicks++;
            using (var over = c.Div(key: "over").Absolute().Width(80).Height(30).Semantics(Role.Button, "Cover"))
                over.Control();
        }))
        {
            edge.Get(Role.Button, "Edge").Click();
            check(edgeClicks == 1, "semantic clicks find narrow unoccluded areas");
        }
        var focusEnabled = true;
        using (var focusTest = new Tester(c => c.Button("Focus target", new(Disabled: !focusEnabled), key: "focus")))
        {
            focusTest.Get(Role.Button, "Focus target").Click();
            focusEnabled = false;
            focusTest.Frame();
            check(focusTest.Context.Focus == 0 && !focusTest.Context.Accessibility.Any(n => n.Focused), "disabling the focused control clears semantic focus in the same frame");
        }
        using var duplicateNames = new Tester(c =>
        {
            c.Button("Same");
            c.Button("Same");
        });
        try
        {
            duplicateNames.Get(Role.Button, "Same").Click();
            check(false, "ambiguous locator rejected");
        }
        catch (InvalidOperationException)
        {
            check(true, "ambiguous locator rejected");
        }

        using var disabled = new Tester(c => c.Button("Disabled", new(Disabled: true)));
        try
        {
            disabled.Get(Role.Button, "Disabled").Click();
            check(false, "disabled semantic click rejected");
        }
        catch (InvalidOperationException)
        {
            check(true, "disabled semantic click rejected");
        }

        try
        {
            using var broken = new Tester(c =>
            {
                var box = c.Div(key: "forgotten");
            });
            check(false, "scope error names the offending key");
        }
        catch (InvalidOperationException e)
        {
            check(e.Message.Contains("forgotten", StringComparison.Ordinal), "scope error names the offending key");
        }
    }
}
