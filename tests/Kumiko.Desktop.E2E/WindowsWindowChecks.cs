using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text.Json;
using Kumiko;
using Kumiko.Desktop;
using Kumiko.Platform;
using Kumiko.Rendering;
using Kumiko.UI;

/// <summary>
/// Windows window parity: OLE file drops with hover, composition-paced frames, layout-aware key routing, EXIF
/// orientation, and changed-row atlas uploads. The OpenGL upload checks run on every host with OpenGL; the rest
/// need Windows and use hidden windows only, so they never show, focus or synthesize system input.
/// </summary>
static class WindowsWindowChecks
{
    public static void Run(string output, Action<bool, string> check)
    {
        using var file = File.Create(Path.Combine(output, "windows-window.json"));
        using var json = new Utf8JsonWriter(file, new()
        {
            Indented = true
        });
        json.WriteStartObject();
        OpenGlAtlas(json, check);
        if (!OperatingSystem.IsWindows())
        {
            json.WriteBoolean("windows", false);
            json.WriteString("coverage", "Windows CI runs the drop target, frame pacing, key routing, EXIF and Direct3D 11 checks");
            json.WriteEndObject();
            Console.WriteLine("Windows window checks unavailable: not a Windows host");
            return;
        }
        json.WriteBoolean("windows", true);
        using (var d3d = new D3D11(forceWarp: true))
            AtlasScenario("Direct3D 11", c =>
            {
                d3d.Render(c, false);
                return d3d.Readback();
            }, () => (d3d.FullAtlasUploads, d3d.AtlasRowUploads), check);
        Orientation(check);
        using var app = new App(new("Windows window verification", QuitOnLastWindow: false, Hidden: true));
        Drop(app, output, check);
        Frames(app, json, check);
        Keys(app, check);
        json.WriteEndObject();
    }

    static void OpenGlAtlas(Utf8JsonWriter json, Action<bool, string> check)
    {
        IGpuContext context;
        OpenGl renderer;
        try
        {
            context = OperatingSystem.IsMacOS() ? new CglContext() : OperatingSystem.IsWindows() ? new WglContext() : new EglContext();
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            json.WriteString("openGlUnavailable", ex.Message);
            Console.WriteLine("OpenGL atlas uploads unavailable: " + ex.Message);
            return;
        }
        using (context)
        {
            renderer = new(context.Load);
            try
            {
                json.WriteString("openGlDriver", renderer.Driver);
                AtlasScenario("OpenGL", c =>
                {
                    context.MakeCurrent();
                    renderer.Render(c, false);
                    return renderer.Readback();
                }, () => (renderer.FullAtlasUploads, renderer.AtlasRowUploads), check);
                json.WriteNumber("openGlFullAtlasUploads", renderer.FullAtlasUploads);
                json.WriteNumber("openGlRowUploads", renderer.AtlasRowUploads);
            }
            finally
            {
                context.MakeCurrent();
                renderer.Dispose();
            }
        }
    }

    /// <summary>
    /// Glyphs that appear after the first frame must look exactly as the CPU draws them while only their rows are
    /// sent; a grown atlas and a different context's atlas must be sent whole.
    /// </summary>
    static void AtlasScenario(string name, Func<Context, Pixels> render, Func<(int Full, int Rows)> uploads, Action<bool, string> check)
    {
        var text = "abc";
        Image? picture = null;
        void View(Context c)
        {
            using var page = c.Div(key: "page").SizeFull().Padding(8).Background(Color.White);
            c.Text(text, new(14, Color: Color.Black), key: "a");
            c.Text(text.ToUpperInvariant() + " 0123", new(12, Weight.Bold, Color.Black, Mono: true), key: "b");
            if (picture is not null)
                c.Image(picture, 150, 150, key: "picture");
        }
        using var t = new Tester(View, new(Width: 360, Height: 240, Scale: 2));
        // Pixels off by more than a quarter of the range: a glyph uploaded to the wrong rows shows hundreds.
        int Error(Tester tester)
        {
            var gpu = render(tester.Context);
            var cpu = tester.Render();
            if (gpu.Width != cpu.Width || gpu.Height != cpu.Height)
                return int.MaxValue;
            int wrong = 0;
            for (int i = 0; i < gpu.Bytes.Length; i += 4)
                for (int k = 0; k < 4; k++)
                    if (Math.Abs(gpu.Bytes[i + k] - cpu.Bytes[i + k]) > 64)
                    {
                        wrong++;
                        break;
                    }
            return wrong;
        }
        t.Settle();
        render(t.Context);
        check(uploads() == (1, 0), $"{name}: the first frame sends the whole atlas");
        var atlas = t.Context.TextSystem.Atlas;
        var width = atlas.Width;
        foreach (var next in new[] { "xyz — new glyphs", "{ quick: brown, fox }", "jumps => over(lazy, dog);" })
        {
            text = next;
            t.Settle();
            var error = Error(t);
            check(error < 16, $"{name}: glyphs added after the first frame render as on the CPU (\"{next}\", {error} pixels differ)");
        }
        var rows = uploads();
        check(rows.Full == 1 && rows.Rows >= 3 && atlas.Width == width,
            $"{name}: later glyphs reach the GPU as changed-row uploads without re-sending the atlas ({rows.Full} full, {rows.Rows} row uploads)");
        render(t.Context);
        check(uploads() == rows, $"{name}: an unchanged atlas uploads nothing");

        // 300 × 300 pixels cannot fit a 256-pixel atlas, so it grows and every texel moves.
        var pixels = Pixels.Create(300, 300);
        for (int i = 0; i < pixels.Bytes.Length; i += 4)
        {
            var at = i / 4;
            pixels.Bytes[i] = (byte)(at % 300 * 255 / 299);
            pixels.Bytes[i + 1] = (byte)(at / 300 * 255 / 299);
            pixels.Bytes[i + 2] = 96;
            pixels.Bytes[i + 3] = 255;
        }
        picture = new(pixels);
        t.Settle();
        var grown = Error(t);
        check(atlas.Width > width && uploads().Full == 2 && grown < 16, $"{name}: a grown atlas is sent whole ({grown} pixels differ)");

        using var other = new Tester(c =>
        {
            using var page = c.Div(key: "page").SizeFull().Padding(8).Background(Color.White);
            c.Text("A second context: 9876", new(16, Color: Color.Black), key: "text");
        }, new(Width: 360, Height: 240, Scale: 2));
        other.Settle();
        var second = Error(other);
        var back = Error(t);
        check(uploads().Full == 4 && second < 16 && back < 16,
            $"{name}: switching between two contexts' atlases sends each whole ({second} and {back} pixels differ)");
    }

    // A 32 × 16 JPEG whose quadrants are red, green (top) and blue, black (bottom), with no EXIF segment.
    const string Quadrants =
        "/9j/7QA4UGhvdG9zaG9wIDMuMAA4QklNBAQAAAAAAAA4QklNBCUAAAAAABDUHYzZjwCyBOmACZjs+EJ+/8AAEQgAEAAgAwEiAAIRAQMRAf/EAB8A" +
        "AAEFAQEBAQEBAAAAAAAAAAABAgMEBQYHCAkKC//EALUQAAIBAwMCBAMFBQQEAAABfQECAwAEEQUSITFBBhNRYQcicRQygZGhCCNCscEVUtHwJDNi" +
        "coIJChYXGBkaJSYnKCkqNDU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6g4SFhoeIiYqSk5SVlpeYmZqio6Slpqeoqaqys7S1" +
        "tre4ubrCw8TFxsfIycrS09TV1tfY2drh4uPk5ebn6Onq8fLz9PX29/j5+v/EAB8BAAMBAQEBAQEBAQEAAAAAAAABAgMEBQYHCAkKC//EALURAAIB" +
        "AgQEAwQHBQQEAAECdwABAgMRBAUhMQYSQVEHYXETIjKBCBRCkaGxwQkjM1LwFWJy0QoWJDThJfEXGBkaJicoKSo1Njc4OTpDREVGR0hJSlNUVVZX" +
        "WFlaY2RlZmdoaWpzdHV2d3h5eoKDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uLj5OXm5+jp6vLz" +
        "9PX29/j5+v/bAEMAAQEBAQEBAgEBAgICAgICAwICAgIDBAMDAwMDBAUEBAQEBAQFBQUFBQUFBQYGBgYGBgcHBwcHCAgICAgICAgICP/bAEMBAQEB" +
        "AgICAwICAwgFBQUICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICP/dAAQAAv/aAAwDAQACEQMRAD8A/F+i" +
        "vyHor/VD/i2X/wCrkf8AmN/+/wA/2o/4uGf+qA/8yH/3if3kUV/mn0V/yv8A/FM7/qpP/Lb/AO+D/ir/AOKPf/VW/wDlp/8AfR//2Q==";

    /// <summary>
    /// Every EXIF orientation must decode upright: the expected corners (top-left, top-right, bottom-left,
    /// bottom-right) are what ImageIO's transform shows for the same files on macOS.
    /// </summary>
    static void Orientation(Action<bool, string> check)
    {
        string[] corners = ["", "RGBK", "GRKB", "KBGR", "BKRG", "RBGK", "BRKG", "KGBR", "GKRB"];
        var jpeg = Convert.FromBase64String(Quadrants);
        for (int orientation = 1; orientation <= 8; orientation++)
        {
            // APP1 "Exif\0\0", a big-endian TIFF header and one IFD entry: Orientation (0x0112), SHORT, count 1.
            byte[] tiff = [0x4d, 0x4d, 0, 0x2a, 0, 0, 0, 8, 0, 1, 1, 0x12, 0, 3, 0, 0, 0, 1, 0, (byte)orientation, 0, 0, 0, 0, 0, 0];
            byte[] body = [.. "Exif\0\0"u8, .. tiff];
            byte[] segment = [0xff, 0xe1, (byte)((body.Length + 2) >> 8), (byte)(body.Length + 2), .. body];
            var image = Image.Decode([.. jpeg.AsSpan(0, 2), .. segment, .. jpeg.AsSpan(2)]).Pixels;
            bool turned = orientation >= 5;
            string seen = image.Width == (turned ? 16 : 32) && image.Height == (turned ? 32 : 16)
                ? string.Concat(Corner(image, 3, 3), Corner(image, image.Width - 4, 3), Corner(image, 3, image.Height - 4), Corner(image, image.Width - 4, image.Height - 4))
                : $"{image.Width}x{image.Height}";
            check(seen == corners[orientation], $"EXIF orientation {orientation} decodes upright with turned dimensions ({seen})");
        }
    }

    static char Corner(Pixels image, int x, int y)
    {
        var i = (y * image.Width + x) * 4;
        // Pixels are BGRA.
        int b = image.Bytes[i], g = image.Bytes[i + 1], r = image.Bytes[i + 2];
        return r > 160 && g < 96 && b < 96 ? 'R' : g > 160 && r < 96 && b < 96 ? 'G' : b > 160 && r < 96 && g < 96 ? 'B' : r < 64 && g < 64 && b < 64 ? 'K' : '?';
    }

    [DllImport("user32.dll")]
    static extern int ClientToScreen(nint window, ref UiaNative.ScreenPoint point);

    /// <summary>
    /// Drives the window's registered IDropTarget through its COM vtable, as OLE does during a drag from Explorer,
    /// with an IDataObject offering CF_HDROP.
    /// </summary>
    static unsafe void Drop(App app, string output, Action<bool, string> check)
    {
        bool over = false;
        string[] dropped = [];
        using var window = app.NewWindow(new(Width: 300, Height: 200, Visible: false), c =>
        {
            using var page = c.Div(key: "page").SizeFull().Row();
            using var zone = c.Div(key: "files").Width(150).Height(200);
            var drop = zone.DropTarget(DragKinds.Files);
            over = drop.IsOver;
            if (drop.Dropped is { } d)
                dropped = [.. d.Files];
        });
        app.RunFor(TimeSpan.FromMilliseconds(30));
        var backend = WindowsWindow.Find(window.NativeHandle)!;
        var target = backend.DropTargetPointer;
        check(target != 0, "RegisterDragDrop accepts the window's OLE drop target, so WM_DROPFILES stays unused");
        if (target == 0)
            return;
        var folder = Directory.CreateDirectory(Path.Combine(output, "windows-drop")).FullName;
        string[] paths = [Path.Combine(folder, "one.txt"), Path.Combine(folder, "two words.txt")];
        foreach (var path in paths)
            File.WriteAllText(path, "drop");
        float scale = Win32.GetDpiForWindow(window.NativeHandle) / 96f;
        UiaNative.ScreenPoint Screen(float x, float y)
        {
            var point = new UiaNative.ScreenPoint { X = (int)(x * Math.Max(1, scale)), Y = (int)(y * Math.Max(1, scale)) };
            ClientToScreen(window.NativeHandle, ref point);
            return point;
        }
        var vtable = *(nint**)target;
        var enter = (delegate* unmanaged[Stdcall]<nint, nint, uint, UiaNative.ScreenPoint, uint*, int>)vtable[3];
        var move = (delegate* unmanaged[Stdcall]<nint, uint, UiaNative.ScreenPoint, uint*, int>)vtable[4];
        var leave = (delegate* unmanaged[Stdcall]<nint, int>)vtable[5];
        var finish = (delegate* unmanaged[Stdcall]<nint, nint, uint, UiaNative.ScreenPoint, uint*, int>)vtable[6];
        using var files = new TransferSnapshot(app, TransferData.Files(paths));
        using var text = new TransferSnapshot(app, TransferData.Text("not a file"));
        var fileData = (nint)ComInterfaceMarshaller<IDragData>.ConvertToUnmanaged(new OleDragData(files));
        var textData = (nint)ComInterfaceMarshaller<IDragData>.ConvertToUnmanaged(new OleDragData(text));
        try
        {
            uint effect = 7;
            int status = enter(target, fileData, 1, Screen(40, 50), &effect);
            var hovering = window.Context!.Input.DragFiles;
            check(status == 0 && effect == 1 && over && hovering is { Length: 2 } && hovering.SequenceEqual(paths),
                "files hovering over a file target are offered for copying and reach Input.DragFiles");
            effect = 7;
            move(target, 1, Screen(220, 50), &effect);
            check(!over && window.Context.Input.Pointer.X > 150, "hover follows DragOver in client points; outside the target it is not over");
            effect = 2;
            move(target, 1, Screen(40, 60), &effect);
            check(effect == 0 && over, "a source that only allows moving gets DROPEFFECT_NONE");
            leave(target);
            check(!over && window.Context.Input.DragFiles is null, "DragLeave clears the hover");

            effect = 7;
            enter(target, textData, 1, Screen(40, 50), &effect);
            check(effect == 0 && !over && window.Context.Input.DragFiles is null, "data without CF_HDROP is refused");
            leave(target);

            effect = 7;
            enter(target, fileData, 1, Screen(40, 50), &effect);
            effect = 7;
            status = finish(target, fileData, 1, Screen(42, 52), &effect);
            check(status == 0 && effect == 1 && dropped.SequenceEqual(paths) && window.Context.Input.DragFiles is null,
                "dropping delivers the CF_HDROP paths to the target under the pointer");
        }
        finally
        {
            Marshal.Release(textData);
            Marshal.Release(fileData);
        }
    }

    /// <summary>One request is one composition-paced frame; an idle worker delivers nothing; disposal joins it.</summary>
    static void Frames(App app, Utf8JsonWriter json, Action<bool, string> check)
    {
        var window = app.NewWindow(new(Width: 200, Height: 120, Visible: false), c =>
        {
            using var page = c.Div(key: "page").SizeFull();
        });
        try
        {
            app.RunFor(TimeSpan.FromMilliseconds(30));
            var backend = WindowsWindow.Find(window.NativeHandle)!;
            check(!backend.RequestFrame(), "a hidden app's window leaves frame pacing to its caller's timer");
            var before = backend.PacedFrames;
            var paced = backend.Pace();
            if (!paced)
            {
                json.WriteString("framePacing", "unavailable");
                Console.WriteLine("Composition-paced frames unavailable: DWM composition is off on this host");
                return;
            }
            check(backend.Pace() && backend.Pace(), "requests while a frame is pending are accepted and coalesced");
            var elapsed = Stopwatch.StartNew();
            while (backend.PacedFrames == before && elapsed.Elapsed < TimeSpan.FromSeconds(1))
                app.RunFor(TimeSpan.FromMilliseconds(5));
            if (backend.PacedFrames == before)
            {
                // A host whose compositor never ticks (no display session) must not strand frames.
                json.WriteString("framePacing", "stalled");
                check(!backend.Pace(), "a frame worker that delivers no composition hands frames back to the caller's timer");
                return;
            }
            json.WriteString("framePacing", "paced");
            json.WriteNumber("firstFrameMilliseconds", elapsed.Elapsed.TotalMilliseconds);
            app.RunFor(TimeSpan.FromMilliseconds(100));
            check(backend.PacedFrames - before == 1, $"three requests before a composition draw exactly one frame ({backend.PacedFrames - before})");
            var idle = backend.PacedFrames;
            app.RunFor(TimeSpan.FromMilliseconds(150));
            check(backend.PacedFrames == idle, "without a request the frame worker delivers nothing");
            check(backend.Pace(), "a later request starts another frame");
            app.RunFor(TimeSpan.FromMilliseconds(100));
            check(backend.PacedFrames == idle + 1, "the later request draws one more frame");
        }
        finally
        {
            var disposing = Stopwatch.StartNew();
            window.Dispose();
            check(disposing.Elapsed < TimeSpan.FromSeconds(1), $"disposing a window joins its frame worker promptly ({disposing.Elapsed.TotalMilliseconds:F0} ms)");
        }
    }

    /// <summary>
    /// Key-downs posted to the hidden window take the real TranslateMessage and routing path. The expectations
    /// assume a Latin keyboard layout, where the J key types j.
    /// </summary>
    static void Keys(App app, Action<bool, string> check)
    {
        var hits = new List<string>();
        var buffer = new TextBuffer();
        bool field = false;
        using var window = app.NewWindow(new(Width: 300, Height: 120, Visible: false), c =>
        {
            using var page = c.Div(key: "page").SizeFull();
            if (c.Shortcut('j'))
                hits.Add("j");
            if (c.Shortcut(Key.Down, Modifiers.None))
                hits.Add("down");
            if (field)
                c.TextField(buffer, new(Name: "Search")
                {
                    AutoFocus = true
                }, key: "search");
        });
        app.RunFor(TimeSpan.FromMilliseconds(30));
        var handle = window.NativeHandle;
        // WM_KEYDOWN lParam: repeat count 1, set-1 scan code in bits 16-23, extended flag in bit 24.
        void Press(uint code, int scan, bool extended = false)
        {
            Win32.PostMessageW(handle, 0x100, code, 1 | (scan << 16) | (extended ? 1 << 24 : 0));
            app.RunFor(TimeSpan.FromMilliseconds(30));
        }
        Press(0x4a, 0x24);
        Press(0x28, 0x50, extended: true);
        check(string.Join(" ", hits) == "j down", $"outside text input a letter arrives as its layout character and an arrow as a key ({string.Join(" ", hits)})");
        check(buffer.Text.Length == 0, "a key delivered as a shortcut does not also type");
        field = true;
        window.Invalidate();
        app.RunFor(TimeSpan.FromMilliseconds(50));
        check(window.Context!.IsEditing, "the search field takes focus");
        hits.Clear();
        Press(0x4a, 0x24);
        check(buffer.Text == "j" && hits.Count == 0, $"while a field has focus the letter is text, not a shortcut (\"{buffer.Text}\", {hits.Count} shortcuts)");
    }
}
