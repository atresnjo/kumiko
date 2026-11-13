using Kumiko;
using Kumiko.UI;

static class AuthoringPatterns
{
    public static void Run(string output, Action<bool, string> check)
    {
        void View(Context c)
        {
            using var page = c.Div(key: "page").SizeFull().Padding(Space.S3).Gap(Space.S2);
            using (c.Div(key: "toolbar").Row().FillWidth().Gap(Space.S2))
            {
                using (c.Div(key: "title").Flex().Clip())
                    c.Text(new string('W', 200), Typography.Body);
                c.Button("Save", key: "save");
            }
            using (c.Div(key: "columns").Row().FillWidth().Height(60).Gap(Space.S2))
            {
                using (c.Div(key: "one").Flex().Background(c.Theme.Surface))
                {
                }
                using (c.Div(key: "two").Flex(2).Background(c.Theme.Track))
                {
                }
            }
            c.Icon("star", key: "favorite", accessibleName: "Favorite");
            c.Icon("plus", key: "decoration");
        }

        using var tester = new Tester(View, new(Width: 420, Height: 240, DevMode: true));
        foreach (var width in new[] { 420, 640 })
        {
            tester.Resize(width, 240);
            var nodes = tester.Context.Nodes.ToArray();
            var toolbar = nodes.Single(n => n.Key == "toolbar").Rect;
            var save = tester.Get(Role.Button, "Save").Rect;
            var one = nodes.Single(n => n.Key == "one").Rect;
            var two = nodes.Single(n => n.Key == "two").Rect;
            check(save.Right <= toolbar.Right && save.X > toolbar.X, "flex content preserves the fixed toolbar action on resize");
            check(Math.Abs(two.Width - one.Width * 2) < .01f, "flex columns divide available space by weight");
        }
        check(tester.Get(Role.Image, "Favorite").Id != 0, "named icons are semantic images");
        var decoration = tester.Context.Nodes.ToArray().Single(n => n.Key == "decoration");
        check(decoration.Source == "/tests/Kumiko.E2E/AuthoringPatterns.cs", "caller source paths are mapped independently of the checkout location");
        check(!tester.Context.Accessibility.Any(n => n.Id == decoration.Id), "decorative images are omitted from the semantic tree");
        tester.Screenshot(Path.Combine(output, "authoring-patterns.png"));
        File.WriteAllText(Path.Combine(output, "authoring-patterns.txt"), tester.Dump());
        tester.WriteWarnings(Path.Combine(output, "authoring-patterns.warnings.json"));

        void Avatar(Context c, NodeKey key = default)
        {
            using var avatar = c.Div(key: key).Width(20).Height(20);
        }
        foreach (var keyed in new[] { false, true })
        {
            using var helpers = new Tester(c =>
            {
                using var row = c.Div(key: "helpers").Row();
                for (var i = 0; i < 2; i++)
                    Avatar(c, keyed ? new NodeKey($"person.{i}") : default);
            }, new(DevMode: true));
            check(helpers.Context.Warnings.Any(w => w.Code == "unkeyed-loop") != keyed, "runtime identity diagnostics cover repeated helpers and accept explicit keys");
        }
    }
}
