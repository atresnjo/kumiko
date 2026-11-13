using System.Text.Json;
using System.Text.Json.Serialization;
using Kumiko.Components;
using Kumiko.Icons;
using Kumiko.Rendering;
using Kumiko.UI;

static class HugeiconChecks
{
    public static void Run(string output, Action<bool, string> check)
    {
        var names = Hugeicons.Names;
        check(names.Length > 6000, "Hugeicons includes the full free catalogue");
        check(Throws<KeyNotFoundException>(() => Hugeicons.Get("not-an-icon")), "unknown Hugeicons names fail explicitly");
        File.WriteAllText(Path.Combine(output, "hugeicons-star.svg"), Hugeicons.GetSvg("star"));
        var entries = new List<IconCoverage>();
        const int columns = 40;
        const int cell = 32;
        using (var catalog = new Tester(c =>
        {
            using var root = c.Div(key: "catalog").SizeFull().Background(Color.White);
            for (int start = 0; start < names.Length; start += columns)
            {
                using var row = c.Div(key: start).Row().Height(cell);
                foreach (var name in names.Skip(start).Take(columns))
                {
                    using var tile = c.Div(key: name).Width(cell).Height(cell).Center();
                    c.Icon(Hugeicons.Get(name), 24, Color.Black, key: "glyph");
                }
            }
        }, new(Width: columns * cell, Height: (names.Length + columns - 1) / columns * cell, Scale: 1)))
        {
            var pixels = catalog.Render();
            for (int i = 0; i < names.Length; i++)
            {
                int x = i % columns * cell;
                int y = i / columns * cell;
                int ink = 0;
                for (int yy = y + 4; yy < y + 28; yy++)
                    for (int xx = x + 4; xx < x + 28; xx++)
                        if (pixels.Bytes[(yy * pixels.Width + xx) * 4] < 240)
                            ink++;
                check(ink > 0, $"Hugeicons {names[i]} renders visible coverage");
                entries.Add(new(names[i], x, y, ink));
            }
            Png.Write(Path.Combine(output, "hugeicons-catalog.png"), pixels);
        }
        File.WriteAllText(Path.Combine(output, "hugeicons-catalog.json"),
            JsonSerializer.Serialize(entries, IconJson.Default.ListIconCoverage));
        Tint(output, check);
        Kit(output, check);
    }

    static void Tint(string output, Action<bool, string> check)
    {
        var foreground = Color.Hex(0xe04030);
        using var view = new Tester(c =>
        {
            using var root = c.Div(key: "tint").SizeFull().Background(Color.Black);
            c.Icon(Hugeicons.Star, 40, foreground, key: "star", accessibleName: "Favorite");
        }, new(Width: 40, Height: 40, Scale: 1));
        foreach (var scale in new[] { 1f, 2f, 3f, 1f })
        {
            view.Context.Scale = scale;
            view.Context.TextSystem.Scale = scale;
            view.Settle();
            var red = view.Render();
            check(red.Width == 40 * scale && red.Height == 40 * scale, "Hugeicons respects the current device scale");
            check(CountTint(red, red: true) > 20 * scale, "Hugeicons takes a red foreground on a dark surface");
            foreground = Color.Hex(0x3050e0);
            view.Settle();
            var blue = view.Render();
            check(CountTint(blue, red: false) > 20 * scale && CountTint(blue, red: true) == 0,
                "cached Hugeicons changes tint without reusing its previous foreground");
            view.Screenshot(Path.Combine(output, $"hugeicons-tint-{scale}.png"));
            foreground = Color.Hex(0xe04030);
        }
        check(view.Context.Accessibility.Any(n => n.Role == Role.Image && n.Name == "Favorite"),
            "Hugeicons preserves meaningful image semantics");
        SvgIcons.Register("e2e-invalid", _ => "not SVG");
        check(Throws<InvalidDataException>(() =>
        {
            using var invalid = new Tester(c => c.Icon("e2e-invalid:test"));
        }), "registered icon documents pass through SVG validation");
        check(Throws<InvalidOperationException>(() => SvgIcons.Register("e2e-invalid", _ => "<svg/>")),
            "SVG icon sources cannot overwrite another registered prefix");
    }

    static void Kit(string output, Action<bool, string> check)
    {
        int invoked = 0;
        using var view = new Tester(c =>
        {
            using var root = c.Div(key: "kit").SizeFull().Padding(16).Gap(12);
            if (c.Kit.Button("Favorite", new(Icon: Hugeicons.Star), key: "favorite").Clicked)
                invoked++;
            if (c.Kit.MenuItem("Open company", new(Hugeicons.ArrowUpRight01), key: "open").Clicked)
                invoked++;
            c.Icon("arrow.right", key: "built-in");
        }, new(Width: 260, Height: 160));
        view.Get(Role.Button, "Favorite").Click();
        view.Get(Role.MenuItem, "Open company").Click();
        check(invoked == 2, "Hugeicons works with Kit buttons and menu actions through semantic input");
        view.Screenshot(Path.Combine(output, "hugeicons-kit.png"));
    }

    static int CountTint(Pixels pixels, bool red)
    {
        int count = 0;
        for (int i = 0; i < pixels.Bytes.Length; i += 4)
            if (pixels.Bytes[i + (red ? 2 : 0)] > 80 && pixels.Bytes[i + (red ? 2 : 0)] > pixels.Bytes[i + (red ? 0 : 2)] * 2)
                count++;
        return count;
    }

    static bool Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (T)
        {
            return true;
        }
    }
}

sealed record IconCoverage(string Name, int X, int Y, int InkPixels);

[JsonSerializable(typeof(List<IconCoverage>))]
partial class IconJson : JsonSerializerContext;
