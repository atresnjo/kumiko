using System.Globalization;
using System.Runtime.InteropServices;
using Kumiko.UI;

namespace Kumiko.Rendering;

// IIDs, enum values, struct layouts and vtable slots are checked against the Windows SDK declarations
// (dwrite.h, d2d1.h, dcommon.h, wincodec.h as mirrored by mingw-w64-headers).
/// <summary>Process-wide DirectWrite, Direct2D and WIC state for Windows text, and the Uniscribe fallback decision.</summary>
internal static unsafe class DirectWrite
{
    static readonly Guid WriteFactoryId = new("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");
    static readonly Guid DrawFactoryId = new("06152247-6f50-465a-9245-118bfd3b6007");
    static readonly Guid ImagingFactoryClass = new("cacaf262-9370-4615-a13b-9f5539da4c0a");
    static readonly Guid ImagingFactoryId = new("ec5ec8a9-c395-4314-9c77-54d7a935ff70");
    static readonly Guid PremultipliedBgra = new("6fddc324-4e03-4bfe-b185-3d77768dc910");
    const int Release = 2;
    // IDWriteFactory; IDWriteTextFormat; IDWriteTextLayout continues IDWriteTextFormat's 28 slots.
    const int CreateCustomRenderingParams = 12, CreateTextFormat = 15, CreateTextLayout = 18;
    const int SetWordWrapping = 5;
    internal const int GetLineMetrics = 59, GetMetrics = 60, HitTestPoint = 64, HitTestTextPosition = 65, HitTestTextRange = 66;
    // ID2D1Factory; ID2D1RenderTarget (after ID2D1Resource::GetFactory); IWICImagingFactory; IWICBitmapSource.
    const int CreateWicBitmapRenderTarget = 13;
    const int CreateSolidColorBrush = 8, DrawTextLayout = 28, SetTextAntialiasMode = 34, SetTextRenderingParams = 36, Clear = 47, BeginDraw = 48, EndDraw = 49;
    const int CreateBitmap = 17, CopyPixels = 7;
    internal const int NotSufficientBuffer = unchecked((int)0x8007007A);
    // Unwrapped single lines never approach this extent; it only bounds the layout box.
    const float MaxExtent = 1 << 20;
    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct LineMetrics(uint Length, uint TrailingWhitespaceLength, uint NewlineLength, float Height, float Baseline, int IsTrimmed);
    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct TextMetrics(float Left, float Top, float Width, float WidthIncludingTrailingWhitespace, float Height,
        float LayoutWidth, float LayoutHeight, uint MaxBidiReorderingDepth, uint LineCount);
    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct HitTestMetrics(uint TextPosition, uint Length, float Left, float Top, float Width, float Height,
        uint BidiLevel, int IsText, int IsTrimmed);
    [StructLayout(LayoutKind.Sequential)]
    readonly record struct RenderTargetProperties(int Type, int Format, int AlphaMode, float DpiX, float DpiY, int Usage, int MinLevel);
    [StructLayout(LayoutKind.Sequential)]
    readonly record struct ColorF(float R, float G, float B, float A);
    [StructLayout(LayoutKind.Sequential)]
    readonly record struct PointF(float X, float Y);
    [DllImport("dwrite.dll")]
    static extern int DWriteCreateFactory(int type, Guid* riid, nint* factory);
    [DllImport("d2d1.dll")]
    static extern int D2D1CreateFactory(int type, Guid* riid, void* options, nint* factory);
    [DllImport("ole32.dll")]
    static extern int CoInitializeEx(nint reserved, uint mode);
    [DllImport("ole32.dll")]
    static extern void CoUninitialize();
    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* value);

    static readonly Lock gate = new();
    static bool initialized;
    static nint writeFactory, drawFactory, renderingParams;
    static string? fallbackReason;

    /// <summary>Why Windows text uses Uniscribe instead of DirectWrite, or null while DirectWrite is active.</summary>
    internal static string? FallbackReason
    {
        get
        {
            Initialize();
            return fallbackReason;
        }
    }

    /// <summary>The active Windows text backend, naming the reason when it fell back to Uniscribe.</summary>
    internal static string Backend => FallbackReason is { } reason ? "Uniscribe fallback: " + reason : "DirectWrite";

    internal static INativeTextLayout Create(string text, TextStyle style, string? family) =>
        Initialize() ? new DirectWriteText(text, style, family) : new UniscribeText(text, style, family);

    // Decided once per process so every layout, caret and raster comes from the same backend.
    static bool Initialize()
    {
        if (Volatile.Read(ref initialized))
            return fallbackReason is null;
        lock (gate)
        {
            if (initialized)
                return fallbackReason is null;
            try
            {
                CreateFactories();
            }
            catch (Exception ex)
            {
                fallbackReason = ex.Message;
                Free(renderingParams);
                Free(drawFactory);
                Free(writeFactory);
                renderingParams = drawFactory = writeFactory = 0;
            }
            Volatile.Write(ref initialized, true);
            return fallbackReason is null;
        }
    }

    static void CreateFactories()
    {
        Guid writeId = WriteFactoryId, drawId = DrawFactoryId;
        nint write = 0, draw = 0, parameters = 0;
        Check(DWriteCreateFactory(0, &writeId, &write), "DirectWrite factory creation failed.");
        writeFactory = write;
        Check(D2D1CreateFactory(1, &drawId, null, &draw), "Direct2D factory creation failed.");
        drawFactory = draw;
        // Gamma 1 and no contrast enhancement yield plain coverage like Pango/cairo masks; the compositor blends it.
        // Natural symmetric rendering keeps DirectWrite's fractional glyph positions instead of GDI-snapped advances.
        Check(
            ((delegate* unmanaged<nint, float, float, float, int, int, nint*, int>)Slot(write, CreateCustomRenderingParams))(write, 1, 0, 0, 0, 5, &parameters),
            "DirectWrite rendering parameters creation failed."
        );
        renderingParams = parameters;
        // WIC is COM-activated per raster; probing it here falls back before any layout exists.
        var com = CoInitializeEx(0, 2);
        try
        {
            Free(CreateImagingFactory());
        }
        finally
        {
            if (com >= 0)
                CoUninitialize();
        }
    }

    internal static nint Layout(string text, TextStyle style, string? family)
    {
        if (!Initialize())
            throw new InvalidOperationException("DirectWrite is unavailable: " + fallbackReason);
        nint format = 0, layout = 0;
        string locale = CultureInfo.CurrentUICulture.Name is { Length: > 0 } name ? name : "en-us";
        int weight = style.Weight switch
        {
            Weight.Bold => 700,
            Weight.Semibold => 600,
            Weight.Medium => 500,
            _ => 400
        };
        try
        {
            fixed (char* familyName = family ?? (style.Mono ? "Consolas" : "Segoe UI"), localeName = locale, chars = text)
            {
                // Sizes are DIPs: the same logical size the GDI path requested as -size pixels at 96 DPI.
                Check(
                    ((delegate* unmanaged<nint, char*, nint, int, int, int, float, char*, nint*, int>)Slot(writeFactory, CreateTextFormat))(
                        writeFactory, familyName, 0, weight, style.Italic ? 2 : 0, 5, style.Size, localeName, &format),
                    "DirectWrite text format creation failed."
                );
                Check(((delegate* unmanaged<nint, int, int>)Slot(format, SetWordWrapping))(format, 1), "DirectWrite word wrapping could not be disabled.");
                Check(
                    ((delegate* unmanaged<nint, char*, uint, nint, float, float, nint*, int>)Slot(writeFactory, CreateTextLayout))(
                        writeFactory, chars, (uint)text.Length, format, MaxExtent, MaxExtent, &layout),
                    "DirectWrite text layout creation failed."
                );
            }
            return layout;
        }
        finally
        {
            Free(format);
        }
    }

    /// <summary>Draws a layout in white with grayscale antialiasing into a top-down premultiplied BGRA buffer.</summary>
    internal static byte[] Draw(nint layout, int width, int height, float scale, float x, float y)
    {
        // Balance only our own initialization; RPC_E_CHANGED_MODE means COM is already usable here.
        var com = CoInitializeEx(0, 2);
        nint imaging = 0, bitmap = 0, target = 0, brush = 0;
        try
        {
            imaging = CreateImagingFactory();
            Guid format = PremultipliedBgra;
            Check(
                ((delegate* unmanaged<nint, uint, uint, Guid*, int, nint*, int>)Slot(imaging, CreateBitmap))(imaging, (uint)width, (uint)height, &format, 2, &bitmap),
                "Text bitmap creation failed."
            );
            // Software, B8G8R8A8 premultiplied; 96 × scale DPI maps layout DIPs to device pixels so glyphs rasterize at device size.
            var properties = new RenderTargetProperties(1, 87, 1, 96 * scale, 96 * scale, 0, 0);
            Check(
                ((delegate* unmanaged<nint, nint, RenderTargetProperties*, nint*, int>)Slot(drawFactory, CreateWicBitmapRenderTarget))(drawFactory, bitmap, &properties, &target),
                "Text render target creation failed."
            );
            var white = new ColorF(1, 1, 1, 1);
            Check(((delegate* unmanaged<nint, ColorF*, void*, nint*, int>)Slot(target, CreateSolidColorBrush))(target, &white, null, &brush), "Text brush creation failed.");
            ((delegate* unmanaged<nint, int, void>)Slot(target, SetTextAntialiasMode))(target, 2);
            ((delegate* unmanaged<nint, nint, void>)Slot(target, SetTextRenderingParams))(target, renderingParams);
            var clear = new ColorF(0, 0, 0, 0);
            ((delegate* unmanaged<nint, void>)Slot(target, BeginDraw))(target);
            ((delegate* unmanaged<nint, ColorF*, void>)Slot(target, Clear))(target, &clear);
            ((delegate* unmanaged<nint, PointF, nint, nint, int, void>)Slot(target, DrawTextLayout))(target, new(x, y), layout, brush, 0);
            ulong tag1, tag2;
            Check(((delegate* unmanaged<nint, ulong*, ulong*, int>)Slot(target, EndDraw))(target, &tag1, &tag2), "Text render failed.");
            var bytes = new byte[checked(width * height * 4)];
            fixed (byte* p = bytes)
            {
                Check(
                    ((delegate* unmanaged<nint, void*, uint, uint, byte*, int>)Slot(bitmap, CopyPixels))(bitmap, null, (uint)width * 4, (uint)bytes.Length, p),
                    "Text pixel copy failed."
                );
            }
            return bytes;
        }
        finally
        {
            foreach (var value in (ReadOnlySpan<nint>)[brush, target, bitmap, imaging])
                Free(value);
            if (com >= 0)
                CoUninitialize();
        }
    }

    static nint CreateImagingFactory()
    {
        Guid imagingClass = ImagingFactoryClass, imagingId = ImagingFactoryId;
        nint imaging = 0;
        Check(CoCreateInstance(&imagingClass, 0, 1, &imagingId, &imaging), "WIC factory creation failed.");
        return imaging;
    }

    internal static void* Slot(nint value, int index) => (*(void***)value)[index];
    internal static void Free(nint value)
    {
        if (value != 0)
            ((delegate* unmanaged<nint, uint>)Slot(value, Release))(value);
    }
    internal static void Check(int result, string message)
    {
        if (result < 0)
            throw new InvalidOperationException($"{message} (HRESULT 0x{result:X8})");
    }
}
