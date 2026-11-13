using Kumiko.UI;

static class TextFlows
{
    public static void Run(string output, Action<bool, string> check)
    {
        var text = new TextBuffer("ab👩‍💻cd");
        var secret = new TextBuffer();
        var notes = new TextBuffer("First line\n第二行\nمرحبا");
        void View(Context c)
        {
            using var root = c.Div(key: "text-flows").SizeFull().Padding(24).Gap(16);
            c.TextField(text, new(Width: 400, Name: "Composition"), key: "composition");
            c.TextField(secret, new(Width: 400, Name: "Secret", Password: true), key: "secret");
            c.TextField(notes, new(Width: 280, Name: "Notes", Multiline: true, Height: 100), key: "notes");
        }
        using var ui = new Tester(View, new(Width: 560, Height: 420));
        ui.Get(Role.TextField, "Composition").Click();
        ui.Key(Key.Home);
        ui.Key(Key.Right);
        ui.Key(Key.Right);
        ui.Compose("日本", false);
        check(text.Text == "ab👩‍💻cd" && ui.HasText("ab日本👩‍💻cd"), "IME preview appears at the insertion point without changing committed text");
        ui.Compose("日本語", true);
        check(text.Text == "ab日本語👩‍💻cd", "IME composition commits once at the insertion point");
        ui.Get(Role.TextField, "Secret").Click();
        ui.Compose("private日本語", false);
        check(!ui.Context.Nodes.ToArray().Any(n => n.Text?.Contains("private", StringComparison.Ordinal) == true), "password IME preview uses masked glyphs");
        ui.Compose("private日本語", true);
        check(secret.Text == "private日本語" && ui.Context.Accessibility.All(n => n.Value?.Contains("private", StringComparison.Ordinal) != true), "committed passwords remain absent from semantic values");
        ui.Get(Role.TextField, "Notes").Click();
        ui.Key(Key.End);
        var before = ui.Context.CaretRectangle;
        ui.Key(Key.Up);
        var after = ui.Context.CaretRectangle;
        check(before.HasValue && after.HasValue && after.Value.Y < before.Value.Y, "multiline keyboard navigation moves to the preceding visual line");
        ui.Screenshot(Path.Combine(output, "text-composition-multiline.png"));
        File.WriteAllText(Path.Combine(output, "text-composition-multiline.txt"), ui.Dump());
    }
}
