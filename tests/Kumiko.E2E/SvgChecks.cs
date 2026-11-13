using System.Text;
using Kumiko.Rendering;
using Kumiko.UI;

static class SvgChecks
{
    const string Badge = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
          <rect width="24" height="12" fill="#e5484d"/>
          <circle cx="12" cy="17" r="5" fill="#3e63dd"/>
        </svg>
        """;
    const string Wide = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 10">
          <rect width="20" height="10" fill="#30a46c"/>
        </svg>
        """;
    const string Sized = """
        <svg xmlns="http://www.w3.org/2000/svg" width="10" height="10">
          <rect width="10" height="10" fill="#e5484d"/>
        </svg>
        """;
    const string Icon = """
        <svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 24 24">
          <rect width="24" height="24" fill="#3e63dd"/>
        </svg>
        """;
    const string Gradient = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
          <defs>
            <linearGradient id="g">
              <stop offset="0" stop-color="#e5484d"/>
              <stop offset="1" stop-color="#3e63dd"/>
            </linearGradient>
          </defs>
          <rect width="10" height="10" rx="2" fill="url(#g)"/>
        </svg>
        """;

    public static void Run(string output, Action<bool, string> check)
    {
        var badge = Encoding.UTF8.GetBytes(Badge);
        var image = Image.Svg(badge, 24, 24, scale: 2).Pixels;
        check(image.Width == 48 && image.Height == 48, "SVG rasterizes at the requested size and scale");
        var top = Pixel(image, 24, 2);
        var corner = Pixel(image, 2, 46);
        var circle = Pixel(image, 24, 34);
        check(top.R > 200 && top.B < 100 && top.A == 255, "SVG keeps its top edge at the top of the image");
        check(corner.A == 0, "SVG keeps transparent regions transparent");
        check(circle.B > 200 && circle.R < 100, "SVG paints shapes in document order and color");
        var sharp = Image.Svg(badge, 24, 24, scale: 4).Pixels;
        var partial = Enumerable.Range(0, sharp.Width).Count(x => Pixel(sharp, x, 68).A is > 0 and < 255);
        check(sharp.Width == 96 && partial <= 4, "higher scales re-rasterize vectors instead of upscaling pixels");
        var wide = Image.Svg(Encoding.UTF8.GetBytes(Wide), 40, 40, scale: 1).Pixels;
        check(
            Pixel(wide, 20, 2).A == 0 && Pixel(wide, 20, 37).A == 0 && Pixel(wide, 20, 20).A == 255 && Pixel(wide, 1, 20).A == 255,
            "non-square SVGs fit centered inside the requested box"
        );
        var sized = Image.Svg(Encoding.UTF8.GetBytes(Sized), 40, 40, scale: 1).Pixels;
        check(Pixel(sized, 2, 2).A == 255 && Pixel(sized, 37, 37).A == 255, "SVGs sized without a viewBox scale to the requested box");
        var icon = Image.Svg(Encoding.UTF8.GetBytes(Icon), 40, 40, scale: 1).Pixels;
        check(Pixel(icon, 2, 2).A == 255 && Pixel(icon, 37, 37).A == 255, "SVGs with a fixed width, height and viewBox scale to the requested box");
        var again = Image.Svg(badge, 24, 24, scale: 2).Pixels;
        check(again.Bytes.AsSpan().SequenceEqual(image.Bytes), "SVG rendering is repeatable");
        var path = Path.Combine(output, "badge.svg");
        File.WriteAllText(path, Badge);
        check(Image.LoadSvg(path, 24, 24, 2).Pixels.Bytes.AsSpan().SequenceEqual(image.Bytes), "SVG files load like in-memory SVG");
        check(Throws<InvalidDataException>(() => Image.Svg([], 24, 24)), "empty SVG input is rejected");
        check(Throws<InvalidDataException>(() => Image.Svg([1, 2, 3, 4], 24, 24)), "binary garbage is rejected as SVG");
        check(Throws<InvalidDataException>(() => Image.Svg("<svg xmlns=\"http://www.w3.org/2000/svg\""u8, 24, 24)), "truncated SVG is rejected");
        using (var encoded = new MemoryStream())
        {
            Png.Write(encoded, image);
            check(Throws<InvalidDataException>(() => Image.Svg(encoded.ToArray(), 24, 24)), "raster bytes are rejected as SVG");
        }

        check(
            Throws<ArgumentOutOfRangeException>(() => Image.Svg(badge, 0, 24))
            && Throws<ArgumentOutOfRangeException>(() => Image.Svg(badge, 24, -1))
            && Throws<ArgumentOutOfRangeException>(() => Image.Svg(badge, float.NaN, 24))
            && Throws<ArgumentOutOfRangeException>(() => Image.Svg(badge, 24, 24, float.PositiveInfinity))
            && Throws<ArgumentOutOfRangeException>(() => Image.Svg(badge, 100_000, 100_000)),
            "invalid SVG dimensions are rejected"
        );
        var badgeImage = Image.Svg(badge, 64, 64);
        var gradient = Image.Svg(Encoding.UTF8.GetBytes(Gradient), 96, 96);
        var banner = Image.Svg(Encoding.UTF8.GetBytes(Wide), 160, 64);
        using var view = new Tester(c =>
        {
            using var root = c.Div().SizeFull().Padding(16).Gap(12).Row();
            c.Image(badgeImage, 64, 64);
            c.Image(gradient, 96, 96, 24);
            using (c.Div().Width(160).Height(64).Rounded(20).Clip())
                c.Image(banner, 160, 64);
        }, new(Width: 380, Height: 140, Scale: 1));
        view.Screenshot(Path.Combine(output, "svg.png"));
        if (!OperatingSystem.IsMacOS())
            return;
        var cpu = view.Render();
        using var metal = new MetalRenderer();
        var gpu = metal.Render(view.Context.Instances, view.Context.TextSystem.Atlas, view.Context.Size, 1, view.Context.Theme.Background);
        double error = 0;
        for (var i = 0; i < gpu.Bytes.Length; i++)
            error += Math.Abs(gpu.Bytes[i] - cpu.Bytes[i]);
        error /= gpu.Bytes.Length;
        check(error < 3, $"Metal and CPU render SVG images the same (mean byte error {error:F3})");
        Png.Write(Path.Combine(output, "svg-metal.png"), gpu);
    }

    static (byte B, byte G, byte R, byte A) Pixel(Pixels pixels, int x, int y)
    {
        var i = (y * pixels.Width + x) * 4;
        return (pixels.Bytes[i], pixels.Bytes[i + 1], pixels.Bytes[i + 2], pixels.Bytes[i + 3]);
    }

    static bool Throws<T>(Action action)
        where T : Exception
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
