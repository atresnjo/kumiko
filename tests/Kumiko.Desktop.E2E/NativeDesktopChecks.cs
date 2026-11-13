using Kumiko;
using Kumiko.Rendering;
using Kumiko.UI;

static partial class NativeDesktopChecks
{
    public static void Run(string output, Action<bool, string> check)
    {
        var buffer = new TextBuffer("Initial text");
        var secret = new TextBuffer("private-password");
        float value = 20;
        bool enabled = false;
        void View(Context c)
        {
            using var root = c.Div(key: "native").SizeFull().Padding(24).Gap(12);
            c.TextField(buffer, new(Width: 300, Name: "Native editor"), key: "editor");
            c.TextField(secret, new(Password: true, Name: "Native password"), key: "secret");
            c.Slider(ref value, new(Min: 0, Max: 100, Width: 300), key: "range", name: "Native amount");
            c.Checkbox("Native toggle", ref enabled, key: "toggle");
            c.Button("Native disabled", new(Disabled: true), key: "disabled");
        }
        using var app = new App(new("Desktop foundation verification", Hidden: true));
        using var window = app.NewWindow(new(Title: "Desktop foundation verification", Width: 540, Height: 360, Visible: false), View);
        check(window.Context!.ReadClipboard is null && window.Context.WriteClipboard is null,
            "hidden native windows retain their own clipboard without installing system callbacks");
        app.RunFor(TimeSpan.FromMilliseconds(100));
        check(!window.IsVisible, "native desktop checks never show a window");
        if (OperatingSystem.IsMacOS())
            Mac(app, window, check);
        else if (OperatingSystem.IsLinux())
            Linux(app, check);
        else if (OperatingSystem.IsWindows())
            Windows(app, window, check);
        app.RunFor(TimeSpan.FromMilliseconds(50));
        check(buffer.Text == "Native updated 日本語", "native accessibility edits the real field");
        check(value == 65, "native accessibility sets the real range value");
        check(enabled, "native accessibility toggles the real checkbox");
        Png.Write(Path.Combine(output, "native-hidden.png"), window.Capture());
        File.WriteAllText(Path.Combine(output, "native-renderer.txt"), window.Renderer);
        check(window.Renderer.Contains("OpenGL", StringComparison.Ordinal) || window.Renderer.StartsWith("Direct3D 11", StringComparison.Ordinal) ||
            window.Renderer.StartsWith("Software", StringComparison.Ordinal) || window.Renderer == "Metal",
            "native capture reports its GPU renderer or explicit software fallback");
    }
}
