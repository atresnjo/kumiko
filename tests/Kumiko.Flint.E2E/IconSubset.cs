using System.Reflection;
using System.Text.Json.Serialization;
using Kumiko.Icons;
using Kumiko.Rendering;
using Kumiko.Runtime;
using Kumiko.UI;

/// <summary>Flint embeds only its own Hugeicons (examples/Flint/Hugeicons.props); every one must still draw.</summary>
static class IconSubset
{
    public static void Run(string output, Action<bool, string> check)
    {
        string[] drawn = [.. typeof(Flint.Icons).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (string)field.GetValue(null)!).Distinct().Order(StringComparer.Ordinal)];
        check(Hugeicons.Names.Select(Hugeicons.Get).Order(StringComparer.Ordinal).SequenceEqual(drawn),
            "KumikoHugeicon lists exactly the Hugeicons Flint draws");
        const int cell = 32;
        using (var sheet = View(c =>
        {
            using var root = c.Div(key: "icons").SizeFull().Row().Background(Color.White);
            foreach (var icon in drawn)
            {
                using var tile = c.Div(key: icon).Width(cell).Height(cell).Center();
                c.Icon(icon, 24, Color.Black, key: "glyph");
            }
        }, drawn.Length * cell, cell))
        {
            var pixels = sheet.Render();
            for (int i = 0; i < drawn.Length; i++)
            {
                int ink = 0;
                for (int y = 4; y < 28; y++)
                    for (int x = i * cell + 4; x < i * cell + 28; x++)
                        if (pixels.Bytes[(y * pixels.Width + x) * 4] < 240)
                            ink++;
                check(ink > 0, $"Subset icon {drawn[i]} renders visible coverage");
            }
            Png.Write(Path.Combine(output, "icons.png"), pixels);
        }
        check(Fails(() => Hugeicons.Get("star")), "Get of an icon outside the subset fails naming KumikoHugeicon");
        check(Fails(() => Hugeicons.GetSvg("star")), "GetSvg of an icon outside the subset fails naming KumikoHugeicon");
        check(Fails(() =>
        {
            using var missing = View(c => c.Icon(Hugeicons.Star, 24, Color.Black, key: "star"), cell, cell);
            missing.Render();
        }), "Drawing an icon outside the subset fails on first render instead of drawing a placeholder");
    }

    sealed class Sheet(Program<int, int> program, Tester tester) : IDisposable
    {
        public Pixels Render() => tester.Render();
        public void Dispose()
        {
            tester.Dispose();
            program.Dispose();
        }
    }

    static Sheet View(Action<Context> draw, int width, int height)
    {
        var program = new Program<int, int>(0, (model, _) => (model, Cmd<int>.None), model => NavigationRoot<int>.Create(NavigationHistory<string>.Start("icons"), "app",
                _ => new IconView(model, draw).Map<int>(static m => m), static _ => throw new InvalidOperationException()),
            IconSubsetJson.Default.Int32, IconSubsetJson.Default.Int32);
        try
        {
            return new(program, new Tester(program, new(Width: width, Height: height, Scale: 1)));
        }
        catch
        {
            program.Dispose();
            throw;
        }
    }

    sealed class IconView(int model, Action<Context> draw) : Kumiko.Runtime.Screen<int, int>(model)
    {
        protected override void Render() => draw(C);
    }

    static bool Fails(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (KeyNotFoundException e)
        {
            return e.Message.Contains("KumikoHugeicon", StringComparison.Ordinal);
        }
    }
}

[JsonSerializable(typeof(int))]
partial class IconSubsetJson : JsonSerializerContext;
