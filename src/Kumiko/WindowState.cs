using System.Text.Json;
using Kumiko.Desktop;

namespace Kumiko;
/// <summary>
/// A window's normal placement (top-left origin, y down) and whether it is maximized; maximized windows keep their
/// normal bounds so restoring returns to that size. X/Y locate the outer frame and Width/Height the content, in
/// the platform's screen units: points on macOS and Linux, virtual-screen pixels on Windows (as
/// <see cref = "Desktop.Screens"/> reports them). Store and restore them on the same platform.
/// </summary>
public sealed record WindowState(float X, float Y, float Width, float Height, bool Maximized, bool FullScreen)
{
    public WindowState(float X, float Y, float Width, float Height, bool Maximized = false) : this(X, Y, Width, Height, Maximized, false)
    {
    }

    public void Deconstruct(out float X, out float Y, out float Width, out float Height, out bool Maximized)
    {
        X = this.X;
        Y = this.Y;
        Width = this.Width;
        Height = this.Height;
        Maximized = this.Maximized;
    }

    const float MinimumSize = 100;
    public float Right => X + Width;
    public float Bottom => Y + Height;

    /// <summary>
    /// Keeps the window usable on the connected <paramref name = "screens"/>: a window whose monitor is gone moves to
    /// the primary work area, an oversized window shrinks to fit, and the title bar stays reachable.
    /// </summary>
    public WindowState Fit(IReadOnlyList<Screen> screens)
    {
        if (screens.Count == 0)
            return this;
        var width = float.IsFinite(Width) && Width >= MinimumSize ? Width : MinimumSize;
        var height = float.IsFinite(Height) && Height >= MinimumSize ? Height : MinimumSize;
        var home = screens.MaxBy(s => Overlap(s.WorkArea, X, Y, width, height));
        if (home is null || Overlap(home.WorkArea, X, Y, width, height) <= 0)
            home = screens.FirstOrDefault(s => s.Primary) ?? screens[0];
        var area = home.WorkArea;
        width = Math.Min(width, area.Width);
        height = Math.Min(height, area.Height);
        var x = float.IsFinite(X) ? Math.Clamp(X, area.X, area.Right - width) : area.X;
        var y = float.IsFinite(Y) ? Math.Clamp(Y, area.Y, area.Bottom - height) : area.Y;
        return new(x, y, width, height, Maximized, FullScreen);
    }

    static float Overlap(UI.Rect area, float x, float y, float width, float height)
    {
        var w = Math.Min(area.Right, x + width) - Math.Max(area.X, x);
        var h = Math.Min(area.Bottom, y + height) - Math.Max(area.Y, y);
        return w > 0 && h > 0 ? w * h : 0;
    }
}

/// <summary>
/// Stores window placements as small JSON files in one directory, written atomically. Use your own directory in
/// tests; <see cref = "App.WindowStates"/> defaults to a folder named after the app under the user's app data.
/// </summary>
public sealed class WindowStateStore(string directory)
{
    public string Directory { get; } = directory;

    public WindowState? Load(string name)
    {
        try
        {
            var bytes = File.ReadAllBytes(PathOf(name));
            var reader = new Utf8JsonReader(bytes);
            float x = float.NaN, y = float.NaN, width = float.NaN, height = float.NaN;
            var maximized = false;
            var fullScreen = false;
            string? property = null;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                    property = reader.GetString();
                else if (reader.TokenType == JsonTokenType.Number)
                    _ = property switch
                    {
                        "x" => x = reader.GetSingle(),
                        "y" => y = reader.GetSingle(),
                        "width" => width = reader.GetSingle(),
                        "height" => height = reader.GetSingle(),
                        _ => 0
                    };
                else if (reader.TokenType is JsonTokenType.True or JsonTokenType.False && property == "maximized")
                    maximized = reader.GetBoolean();
                else if (reader.TokenType is JsonTokenType.True or JsonTokenType.False && property == "fullScreen")
                    fullScreen = reader.GetBoolean();
            }

            return float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(width) && float.IsFinite(height) ? new(x, y, width, height, maximized, fullScreen) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    public void Save(string name, WindowState state)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = PathOf(name);
        var temporary = path + ".tmp";
        using (var stream = File.Create(temporary))
        using (var json = new Utf8JsonWriter(stream, new()
        {
            Indented = true
        }))
        {
            json.WriteStartObject();
            json.WriteNumber("x", state.X);
            json.WriteNumber("y", state.Y);
            json.WriteNumber("width", state.Width);
            json.WriteNumber("height", state.Height);
            json.WriteBoolean("maximized", state.Maximized);
            json.WriteBoolean("fullScreen", state.FullScreen);
            json.WriteEndObject();
        }

        File.Move(temporary, path, true);
    }

    string PathOf(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or "..")
            throw new ArgumentException("Window state names must be plain file names.", nameof(name));
        return Path.Combine(Directory, name + ".json");
    }
}
