using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kumiko;
using Kumiko.Desktop;
using Kumiko.Rendering;
using Kumiko.UI;
using Kumiko.Web;

var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/e2e");
Directory.CreateDirectory(output);
var checks = new List<string>();
void Check(bool value, string name)
{
    if (!value)
        throw new InvalidOperationException(name);
    checks.Add(name);
}

if (args.Contains("--headless-only", StringComparer.Ordinal))
{
    HeadlessChecks.Run(output, Check);
    WriteReport();
    return;
}

if (args.Contains("--headless-default-only", StringComparer.Ordinal))
{
    HeadlessDefaults.Run(output, Check);
    WriteReport();
    return;
}

if (args.Contains("--rtl-only", StringComparer.Ordinal))
{
    ResponsivePhysicalPaddingChecks.Run(output, Check);
    LocalizationChecks.Run(output, Check);
    WriteReport();
    return;
}

if (args.Contains("--responsive-only", StringComparer.Ordinal))
{
    ResponsiveChecks.Run(output, Check);
    WriteReport();
    return;
}

if (args.Contains("--hugeicons-only", StringComparer.Ordinal))
{
    HugeiconChecks.Run(output, Check);
    WriteReport();
    return;
}

if (args.Contains("--navigation-path-only", StringComparer.Ordinal))
{
    NavigationPathChecks.Run(output, Check);
    WriteReport();
    return;
}

if (args.Contains("--text-overflow-only", StringComparer.Ordinal))
{
    TextOverflowChecks.Run(output, Check);
    WriteReport();
    return;
}

if (args.Contains("--virtual-list-only", StringComparer.Ordinal))
{
    VirtualListChecks.Run(Check);
    KeyRoutingChecks.Run(Check);
    if (OperatingSystem.IsMacOS())
        AtlasUploadChecks.Run(Check);
    WriteReport();
    return;
}

if (args.Contains("--idle-frames-only", StringComparer.Ordinal))
{
    if (OperatingSystem.IsMacOS())
        IdleFrameChecks.Run(Check);
    WriteReport();
    return;
}

if (args.Contains("--long-line-only", StringComparer.Ordinal))
{
    LongLineChecks.Run(output, Check);
    WriteReport();
    return;
}

if (args.Contains("--yoga-upstream-only", StringComparer.Ordinal))
{
    YogaUpstreamChecks.Run(output, Check);
    WriteReport();
    return;
}

if (args.Contains("--flexbox-only", StringComparer.Ordinal))
{
    FlexboxChecks.Run(output, Check);
    YogaChecks.Run(output, Check);
    YogaFeatureChecks.Run(output, Check);
    YogaUpstreamChecks.Run(output, Check);
    IncrementalLayoutChecks.Run(output, Check);
    WriteReport();
    return;
}

ResponsiveChecks.Run(output, Check);
TextOverflowChecks.Run(output, Check);
LongLineChecks.Run(output, Check);
VirtualListChecks.Run(Check);
KeyRoutingChecks.Run(Check);
NavigationPathChecks.Run(output, Check);
FlexboxChecks.Run(output, Check);
YogaChecks.Run(output, Check);
YogaFeatureChecks.Run(output, Check);
YogaUpstreamChecks.Run(output, Check);
IncrementalLayoutChecks.Run(output, Check);
KumikoEnforcementChecks.Run(output, Check);
KumikoChecks.Run(output, Check);
SubmodelChecks.Run(output, Check);
NavigationPathChecks.Run(output, Check);
CompositionChecks.Run(output, Check);
HotReloadChecks.Run(output, Check);
DebugChecks.Run(output, Check);
AuthoringChecks.Run(output, Check);
AuthoringPatterns.Run(output, Check);
StyleReuseChecks.Run(output, Check);
SvgChecks.Run(output, Check);
HugeiconChecks.Run(output, Check);
ScrollChecks.Run(Check);
ControlChecks.Run(output, Check);
HeadlessChecks.Run(output, Check);
MotionChecks.Run(output, Check);
DropdownChecks.Run(output, Check);
DebounceChecks.Run(Check);
HeldCommandChecks.Run(output, Check);
TransitionChecks.Run(output, Check);
DragChecks.Run(output, Check);
DataGridChecks.Run(output, Check);
LocalizationChecks.Run(output, Check);
AccessibilityChecks.Run(output, Check);
DragDropChecks.Run(output, Check);
WindowStateChecks.Run(output, Check);
var count = 0;
var enabled = false;
var value = 0.25f;
var buffer = new TextBuffer();
void View(Context c)
{
    using var root = c.Div().SizeFull().Padding(24).Gap(12);
    c.Text($"Count: {count}", new(Size: 28, Weight: Weight.Bold));
    if (c.Button("Increment", key: "increment").Clicked)
        count++;
    if (c.Button("Disabled", new(Disabled: true)).Clicked)
        throw new Exception("Disabled button activated");
    c.Checkbox("Enabled", ref enabled);
    c.Slider(ref value, new(Width: 240));
    c.TextField(buffer, new(Placeholder: "Write here", Width: 300));
    using (c.Div().Row().Width(240).Gap(8))
    {
        using (c.Div().Width(40).Height(30).Background(Color.Hex(0xff0000)))
        {
        }

        c.Text("Wrapping text must fit beside a fixed box and remain readable.", new(Wrap: true));
    }
}

using var t = new Tester(View, new(Width: 520, Height: 520, Scale: 1));
t.ClickText("Increment");
Check(count == 1 && t.HasText("Count: 1"), "click updates the displayed frame immediately");
t.ClickText("Disabled");
Check(count == 1, "disabled controls ignore clicks");
t.ClickText("Enabled");
Check(enabled, "checkbox responds through the input pipeline");
t.ClickText("Write here");
t.TypeText("A👩‍💻é");
t.Key(Key.Backspace);
t.Key(Key.Backspace);
Check(buffer.Text == "A", "editing deletes whole grapheme clusters");
t.TypeText("da");
t.Key(Key.A, Modifiers.Primary);
t.Key(Key.C, Modifiers.Primary);
Check(t.Clipboard == "Ada", "selection copies through the isolated clipboard");
t.Key(Key.X, Modifiers.Primary);
t.Key(Key.V, Modifiers.Primary);
Check(buffer.Text == "Ada", "cut and paste preserve selected text");
t.Compose("日本", false);
t.Compose("日本語", true);
Check(buffer.Text == "Ada日本語", "IME commits composition once");
t.Screenshot(Path.Combine(output, "controls.png"));
var png = Png.Read(File.ReadAllBytes(Path.Combine(output, "controls.png")));
Check(png.Width == 520 && png.Height == 520, "PNG artifacts round trip through the C# codec");
t.Resize(360, 620);
t.Screenshot(Path.Combine(output, "resized.png"));
Check(t.Context.Nodes.ToArray().All(n => float.IsFinite(n.Rect.Width) && n.Rect.Width >= 0), "resizing produces finite layout");
var built = 0;
void ListView(Context c)
{
    using var root = c.Div().SizeFull();
    c.UniformList("items", 100_000, 28, (ctx, row) =>
    {
        built++;
        ctx.Text($"Row {row}");
    });
}

using var list = new Tester(ListView, new(Width: 400, Height: 280, Scale: 1));
Check(built < 100 && list.HasText("Row 0"), "100,000-row list builds only the viewport");
list.MoveTo(100, 100);
list.Scroll(2800);
list.Settle();
Check(list.HasText("Row 100") && !list.HasText("Row 0"), "virtual scrolling updates visible rows");
list.Screenshot(Path.Combine(output, "list.png"));
var headed = 0;
void HeadedListView(Context c)
{
    using var root = c.Div().SizeFull();
    c.UniformList("headed", 100_000, 28, header: ctx =>
    {
        using var header = ctx.Div(key: "header").FillWidth().Height(200);
        ctx.Text("List header");
    }, row: (ctx, row) =>
    {
        headed++;
        ctx.Text($"Item {row}");
    }, footer: ctx => ctx.Text("List footer", key: "footer"), rowKey: row => (ulong)row);
}

using var headedList = new Tester(HeadedListView, new(Width: 400, Height: 280, Scale: 1));
Check(headed < 100 && headedList.HasText("List header") && headedList.HasText("Item 0"), "a list header shares the scroller and builds only the viewport");
headedList.MoveTo(100, 100);
headed = 0;
headedList.Scroll(200 + 2800);
headedList.Settle();
Check(headed < 100 && !headedList.HasText("List header") && headedList.Get(Role.Text, "Item 100").Rect.Y == 0, "rows below a header virtualize from the header's end");
headedList.Scroll(10_000_000);
headedList.Settle();
Check(headedList.HasText("Item 99999") && headedList.HasText("List footer") && !headedList.HasText("Item 0"), "a list footer follows the last row");
void GlassView(Context c)
{
    using var root = c.Div().SizeFull().Padding(16).Gap(8);
    for (var i = 0; i < 5; i++)
        using (c.Div(key: i).Width(280).Height(40).Background(Color.Hex((uint)(0x2050a0 + i * 0x201000))))
        {
        }

    using (c.Div().Absolute().Offset(28, 40).Width(240).Height(160).Rounded(24).Glass())
        c.Text("Liquid Glass");
}

using var glass = new Tester(GlassView, new(Width: 320, Height: 260, Scale: 1));
glass.Screenshot(Path.Combine(output, "glass.png"));
Check(glass.Render().Bytes.Distinct().Count() > 100, "glass renders its colored backdrop");
var tile = Pixels.Create(8, 8);
for (int i = 0; i < tile.Bytes.Length; i += 4)
{
    tile.Bytes[i + 2] = 128;
    tile.Bytes[i + 3] = 128;
}

tile.Bytes[2] = tile.Bytes[3] = 0;
using var encodedImage = new MemoryStream();
Png.Write(encodedImage, tile);
var picture = Image.Decode(encodedImage.ToArray());
Check(Enumerable.Range(0, 64).Any(i => picture.Pixels.Bytes[i * 4 + 3] == 0), "decoded pictures preserve transparency");
using var graphics = new Tester(c =>
{
    using var root = c.Div().SizeFull().Padding(20).Gap(12);
    using (c.Div().Row().Gap(12))
    {
        c.Image(picture, 96, 96, 18);
        c.Icon("magnifyingglass", 40);
        c.Icon("star.fill", 40);
        c.Icon("terminal", 40);
    }

    using (c.Div().Width(240).Height(64).Rounded(24).Clip().Background(Color.Hex(0x23a581)))
    using (c.Div().Absolute().Offset(-20, 20).Width(280).Height(80).Background(Color.Hex(0x2876d5)))
    {
    }

    c.RichText("Bold blue underlined", [new(0, 4, Weight: Weight.Bold), new(5, 9, Color: Color.Hex(0x0088ff)), new(10, 20, Underline: true)]);
}, new(Width: 380, Height: 280, Scale: 1));
graphics.Screenshot(Path.Combine(output, "graphics.png"));
if (OperatingSystem.IsMacOS())
{
    // Fast shaping may fail on kerning, fallback fonts, ligatures, or contextual pairs.
    // Compare complete rendered scenes against CoreText's normal path.
    using (var shaping = new Tester(c =>
    {
        using var root = c.Div().SizeFull().Padding(12).Gap(8);
        foreach (var sample in new[]
        {
            "AVATAR To Ty",
            "fi fl ffi",
            "1234 9876",
            "Hello, World!",
            "(x) = [y]",
            "WAVE Typography",
            "Καλημέρα · 日本語 · 👩‍💻"
        }

        )
            c.Text(sample, new(Size: 18), key: sample);
    }, new(Width: 500, Height: 300, Scale: 1)))
    {
        shaping.Context.TextSystem.AsciiFastPathEnabled = false;
        shaping.Frame();
        var nativeText = shaping.Render();
        shaping.Context.TextSystem.AsciiFastPathEnabled = true;
        shaping.Frame();
        var fastText = shaping.Render();
        double difference = 0;
        for (int i = 0; i < fastText.Bytes.Length; i++)
            difference += Math.Abs(fastText.Bytes[i] - nativeText.Bytes[i]);
        Check(difference / fastText.Bytes.Length < .1, "ASCII optimization preserves CoreText kerning, ligatures and Unicode fallback");
        Png.Write(Path.Combine(output, "text-native.png"), nativeText);
        Png.Write(Path.Combine(output, "text-fast.png"), fastText);
    }

    // Glyph bitmaps must land on whole device pixels: resampling a bitmap at a fractional offset smears every
    // stem across an extra pixel and makes text read a weight heavier. Weights must also stay distinct.
    static double Ink(Tester tester)
    {
        tester.Frame();
        double ink = 0;
        var pixels = tester.Render();
        for (int i = 0; i < pixels.Bytes.Length; i += 4)
            ink += 255 - pixels.Bytes[i];
        return ink;
    }
    foreach (var scale in new[] { 1f, 2f })
    {
        var weight = Weight.Regular;
        using var crisp = new Tester(c =>
        {
            using var root = c.Div().SizeFull().Padding(new Edges(7.3f, 0, 0, 11.6f)).Background(Color.White);
            c.Text("People, Northwind Labs · 1,480", new(Size: 13, Weight: weight, Color: Color.Black), key: "sample");
        }, new(Width: 260, Height: 40, Scale: scale));
        var regular = Ink(crisp);
        var glyphs = crisp.Context.Instances.Where(i => i.Kind == 1).ToArray();
        Check(glyphs.Length > 20 && glyphs.All(i => MathF.Abs(i.Rect.X * scale - MathF.Round(i.Rect.X * scale)) < .001f &&
            MathF.Abs(i.Rect.Y * scale - MathF.Round(i.Rect.Y * scale)) < .001f), $"glyphs are pixel aligned at {scale}x");
        weight = Weight.Semibold;
        Check(regular < Ink(crisp) * .85, $"regular text is visibly lighter than semibold at {scale}x");
    }

    IdleFrameChecks.Run(Check);
    AtlasUploadChecks.Run(Check);
    using var metal = new MetalRenderer();
    var gpu = metal.Render(t.Context.Instances, t.Context.TextSystem.Atlas, t.Context.Size, 1, t.Context.Theme.Background);
    var cpu = t.Render();
    double error = 0;
    for (var i = 0; i < gpu.Bytes.Length; i++)
        error += Math.Abs(gpu.Bytes[i] - cpu.Bytes[i]);
    error /= gpu.Bytes.Length;
    Check(error < 3, $"Metal and CPU render the same view (mean byte error {error:F3})");
    Png.Write(Path.Combine(output, "metal.png"), gpu);
    var gpuGraphics = metal.Render(
        graphics.Context.Instances,
        graphics.Context.TextSystem.Atlas,
        graphics.Context.Size,
        1,
        graphics.Context.Theme.Background
    );
    var cpuGraphics = graphics.Render();
    double graphicsError = 0;
    for (int i = 0; i < gpuGraphics.Bytes.Length; i++)
        graphicsError += Math.Abs(gpuGraphics.Bytes[i] - cpuGraphics.Bytes[i]);
    Check(graphicsError / gpuGraphics.Bytes.Length < 3, "images, symbols, rich text and rounded clipping agree across renderers");
    Png.Write(Path.Combine(output, "graphics-metal.png"), gpuGraphics);
    using var app = new App(new(Name: "Kumiko E2E", Hidden: true));
    var nativeErrors = new List<Exception>();
    app.UnhandledException += nativeErrors.Add;
    using var window = app.NewWindow(new(Title: "Hidden verification", Width: 400, Height: 300, Visible: false), View);
    app.RunFor(TimeSpan.FromMilliseconds(80));
    window.Resize(440, 320);
    window.Invalidate();
    app.RunFor(TimeSpan.FromMilliseconds(80));
    Check(!window.IsVisible, "native lifecycle verification never shows a window");
    NativeAccessibilityChecks.Run(app, "Hidden verification", output, Check);
    var called = false;
    Task.Run(() => app.Dispatch(() => called = true)).GetAwaiter().GetResult();
    app.RunFor(TimeSpan.FromMilliseconds(40));
    Check(called, "worker dispatch reaches the native main loop");
    using (var server = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None))
    using (var client = new AnonymousPipeClientStream(PipeDirection.Out, server.GetClientHandleAsString()))
    {
        bool read = false;
        Watch? watch = null;
        watch = Watch.Readable(app, server.SafePipeHandle.DangerousGetHandle().ToInt32(), () =>
        {
            read = server.ReadByte() == 42;
            watch!.Dispose();
        });
        client.WriteByte(42);
        client.Flush();
        app.RunFor(TimeSpan.FromMilliseconds(80));
        watch.Dispose();
        Check(read, "file descriptor readiness dispatches without polling");
    }

    app.Services.Bind(
        "Greeter",
        "greet",
        static (Greeting a) => $"Hello, {a.Name}!",
        E2EJson.Default.Greeting,
        E2EJson.Default.String,
        "{ name: string }",
        "string"
    );
    var notification = new NanoEvent<string>("greeted", E2EJson.Default.String, "string");
    app.Services.RegisterEvent(notification);
    using var pageWindow = app.NewPage(new(Title: "Hidden WebKit verification", Visible: false), new(Assets: [Asset.Text("/index.html", """
        <!doctype html><html><body><script>
        kumiko.on('greeted',p=>kumiko.log('event:'+p));
        </script><script type="module">
        import {Greeter} from '/kumiko.js';
        kumiko.log(await Greeter.greet({name:'Ada'}));
        try {await kumiko.call('Unknown.method',{});}catch(e){kumiko.log('rejected');}
        </script></body></html>
        """)]));
    var logs = new List<string>();
    pageWindow.Page!.Logged += logs.Add;
    notification.Emit(pageWindow, "queued");
    var deadline = System.Diagnostics.Stopwatch.StartNew();
    while (deadline.Elapsed < TimeSpan.FromSeconds(10) && logs.Count < 3)
        app.RunFor(TimeSpan.FromMilliseconds(40));
    Check(
        logs.Contains("Hello, Ada!") && logs.Contains("rejected") && logs.Contains("event:queued"),
        "real WKWebView loads assets, generated client, replies and held events"
    );
    Check(!pageWindow.IsVisible, "WebKit end-to-end verification stays hidden");
    var gpuGlass = metal.Render(glass.Context.Instances, glass.Context.TextSystem.Atlas, glass.Context.Size, 1, glass.Context.Theme.Background);
    var cpuGlass = glass.Render();
    double glassError = 0;
    for (int i = 0; i < gpuGlass.Bytes.Length; i++)
        glassError += Math.Abs(gpuGlass.Bytes[i] - cpuGlass.Bytes[i]);
    glassError /= gpuGlass.Bytes.Length;
    Check(glassError < 3, $"Metal glass matches CPU optics (mean byte error {glassError:F3})");
    Png.Write(Path.Combine(output, "glass-metal.png"), gpuGlass);
    Check(nativeErrors.Count == 0, "native callbacks complete without exceptions");
}
else
{
    using var app = new App(new(Name: "Kumiko E2E", Hidden: true));
    using var window = app.NewWindow(new(Title: "Hidden verification", Width: 400, Height: 300, Visible: false), View);
    var errors = new List<Exception>();
    app.UnhandledException += errors.Add;
    app.RunFor(TimeSpan.FromMilliseconds(100));
    window.Resize(440, 320);
    app.RunFor(TimeSpan.FromMilliseconds(80));
    Check(!window.IsVisible && errors.Count == 0, "native lifecycle remains hidden and error-free");
    NativeAccessibilityChecks.Run(app, "Hidden verification", output, Check);
    bool called = false;
    Task.Run(() => app.Dispatch(() => called = true)).GetAwaiter().GetResult();
    app.RunFor(TimeSpan.FromMilliseconds(50));
    Check(called, "worker dispatch reaches the native main loop");
}

using (var selected = new Tester(c =>
{
    using var root = c.Div().SizeFull().Padding(20).Selectable();
    c.Text("Selectable text");
}, new(Width: 300, Height: 100, Scale: 1)))
{
    var r = selected.FindText("Selectable text")!.Value;
    selected.Press(r.X, r.Y + r.Height / 2);
    selected.MoveTo(r.Right, r.Y + r.Height / 2);
    selected.Release(r.Right, r.Y + r.Height / 2);
    selected.Key(Key.C, Modifiers.Primary);
    Check(selected.Clipboard == "Selectable text", "native text can be selected and copied without a text field");
    selected.Screenshot(Path.Combine(output, "selection.png"));
}

var router = new ServiceRegistry();
router.Bind(
    "Greeter",
    "greet",
    static (Greeting a) => $"Hello, {a.Name}!",
    E2EJson.Default.Greeting,
    E2EJson.Default.String,
    "{ name: string }",
    "string"
);
var reply = router.Invoke("Greeter.greet", JsonDocument.Parse("{\"name\":\"Ada\"}").RootElement);
Check(reply == "\"Hello, Ada!\"", "typed IPC invokes C# without reflection");
try
{
    router.Invoke("Unknown.call", JsonDocument.Parse("{}").RootElement);
    throw new Exception("Unknown IPC accepted");
}
catch (KeyNotFoundException)
{
    checks.Add("unknown IPC methods reject");
}

Check(router.TypeScript().Contains("Promise<string>"), "typed client generation describes the service");
WriteReport();

void WriteReport()
{
    var files = Directory.GetFiles(output)
        .Where(p => Path.GetFileName(p) != "report.json" && Path.GetExtension(p) is ".png" or ".txt" or ".json")
        .Order()
        .Select(p => new Artifact(Path.GetFileName(p), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))))
        .ToArray();
    var report = new Report(
        System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "NativeAOT",
        checks.ToArray(),
        files
    );
    File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, E2EJson.Default.Report));
    Console.WriteLine($"PASS: {checks.Count} end-to-end checks; artifacts: {output}");
}
record Greeting(string Name);
record Artifact(string File, string Sha256);
record Report(string Runtime, string Compilation, string[] Checks, Artifact[] Artifacts);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(Greeting))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Report))]
partial class E2EJson : JsonSerializerContext;
