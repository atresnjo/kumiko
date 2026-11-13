using System.Globalization;
using Kumiko.Rendering;

namespace Kumiko.UI;

public readonly record struct TesterOptions(float Width = 800, float Height = 600, float Scale = 1, Theme? Theme = null, bool DevMode = false, ApplicationIconCache? Icons = null);
public sealed partial class Tester : IDisposable
{
    readonly Action<Context> view;
    public Context Context
    {
        get;
    }
    public string Clipboard
    {
        get => Context.Clipboard; set => Context.Clipboard = value;
    }

    internal Tester(Action<Context> view, TesterOptions options = default)
    {
        this.view = view;
        Context = new()
        {
            DevMode = options.DevMode,
            ApplicationIcons = options.Icons ?? new(bundle => Rendering.Image.ApplicationIcon(bundle)),
            Size = new(options.Width > 0 ? options.Width : 800, options.Height > 0 ? options.Height : 600),
            Scale = options.Scale > 0 ? options.Scale : 1,
            Theme = options.Theme ?? Theme.Light
        };
        Context.TextSystem.Scale = Context.Scale;
        try
        {
            Settle();
        }
        catch
        {
            Context.Dispose();
            throw;
        }
    }

    public void Frame(float dt = 1 / 60f)
    {
        ObjectDisposedException.ThrowIf(testerDisposed, this);
        Context.Frame(view, dt);
        FrameWindows(dt);
    }
    public void Settle(int frames = 2)
    {
        for (int i = 0; i < frames; i++)
            Frame();
    }

    public Rect? FindText(string text)
    {
        foreach (ref readonly var node in Context.Nodes)
            if (node.Text == text && node.Rect.Intersect(node.ClipRect) is { Width: > 0, Height: > 0 })
                return node.Rect.Intersect(node.ClipRect);
        return null;
    }

    public bool HasText(string text) => FindText(text).HasValue;
    public bool ContainsText(string text)
    {
        foreach (ref readonly var node in Context.Nodes)
            if (node.Text?.Contains(text, StringComparison.Ordinal) == true)
                return true;
        return false;
    }

    public void MoveTo(float x, float y)
    {
        Context.Input.Pointer = new(x, y);
        Frame();
    }

    public void Press(float x, float y)
    {
        Context.Input.Pointer = new(x, y);
        Context.Input.Down = Context.Input.Pressed = true;
        Frame();
    }

    public void Release(float x, float y)
    {
        Context.Input.Pointer = new(x, y);
        Context.Input.Down = false;
        Context.Input.Released = true;
        Frame();
    }

    public void Click(float x, float y)
    {
        Context.Input.Button = PointerButton.Primary;
        Press(x, y);
        Release(x, y);
    }

    public void RightClick(float x, float y)
    {
        Context.Input.Button = PointerButton.Secondary;
        Press(x, y);
        Release(x, y);
        Context.Input.Button = PointerButton.Primary;
    }

    public void ClickText(string text)
    {
        var r = FindText(text) ?? throw new KeyNotFoundException($"Visible text not found: {text}");
        Click(r.X + r.Width / 2, r.Y + r.Height / 2);
    }

    public void Key(Key key, Modifiers modifiers = Modifiers.None)
    {
        Context.Input.Key = key;
        Context.Input.Modifiers = modifiers;
        Frame();
    }

    /// <summary>
    /// Presses the key that produces <paramref name="character"/> outside text input, as the desktop backends
    /// deliver it: an uppercase letter also holds Shift. Typing into a focused field uses TypeText.
    /// </summary>
    public void Keystroke(char character, Modifiers modifiers = Modifiers.None)
    {
        Context.Input.Key = KeyCharacters.Key(character);
        Context.Input.Character = character;
        Context.Input.Modifiers = modifiers | (char.IsUpper(character) ? Modifiers.Shift : Modifiers.None);
        Frame();
    }

    public void TypeText(string text)
    {
        Context.Input.Text = text;
        Frame();
    }

    public void Compose(string text, bool commit)
    {
        Context.Input.Composition = text;
        Context.Input.CommitComposition = commit;
        Frame();
    }

    /// <summary>Simulates files from the system hovering over the window at a point, through Kumiko input.</summary>
    public void DragFiles(float x, float y, string[] paths)
    {
        Context.Input.Pointer = new(x, y);
        Context.Input.DragFiles = paths;
        Settle();
    }

    /// <summary>Simulates dropping files from the system at a point; no real files or system drag are involved.</summary>
    public void DropFiles(float x, float y, string[] paths)
    {
        Context.Input.Pointer = new(x, y);
        Context.Input.DragFiles = null;
        Context.Input.DroppedFiles = paths;
        Frame();
    }

    public void Scroll(float dy)
    {
        Context.Input.ScrollY = dy;
        Frame();
    }

    /// <summary>One mouse-wheel notch of <paramref name="dy"/> pixels; unlike <see cref="Scroll"/> it glides.</summary>
    internal void Wheel(float dy)
    {
        Context.Input.ScrollY = dy;
        Context.Input.ScrollPrecise = false;
        Frame();
    }

    public void Resize(float width, float height)
    {
        Context.Size = new(width, height);
        Settle();
    }

    public void Advance(float seconds) => Frame(seconds);
    public Pixels Render() => CpuRenderer.Render(Context.Instances, Context.TextSystem.Atlas, Context.Size, Context.Scale, Context.Theme.Background);
    public void Screenshot(string path) => Png.Write(path, Render());
    public void RunScript(string script)
    {
        foreach (var raw in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = raw.IndexOf(':');
            string op = colon >= 0 ? raw[..colon] : raw, arg = colon >= 0 ? raw[(colon + 1)..] : "";
            switch (op)
            {
                case "click":
                    ClickText(arg);
                    break;
                case "move":
                    var r = FindText(arg) ?? throw new KeyNotFoundException(arg);
                    MoveTo(r.X + r.Width / 2, r.Y + r.Height / 2);
                    break;
                case "type":
                    TypeText(arg);
                    break;
                case "key":
                    Key(Enum.Parse<Key>(arg, true));
                    break;
                case "back":
                    Navigate(NavigationDirection.Back);
                    break;
                case "forward":
                    Navigate(NavigationDirection.Forward);
                    break;
                case "scroll":
                    Scroll(float.Parse(arg, CultureInfo.InvariantCulture));
                    break;
                case "wait":
                    Advance(float.Parse(arg, CultureInfo.InvariantCulture));
                    break;
                case "resize":
                    var parts = arg.Split('x');
                    Resize(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture));
                    break;
                default:
                    throw new ArgumentException($"Unknown script operation: {op}");
            }

            Settle();
        }
    }

    public void Dispose()
    {
        if (testerDisposed)
            return;
        testerDisposed = true;
        DisposeWindows();
        Context.Dispose();
    }
}
