using System.Runtime.InteropServices;

namespace Kumiko.Rendering;

// Vtable slots are checked against the Windows SDK interface declarations (d2d1.h, d2d1_3.h, wincodec.h).
internal static unsafe class WindowsSvg
{
    static readonly Guid Factory1 = new("bb12d362-daee-4b9a-aa1d-14ba401cfa1f");
    static readonly Guid DeviceContext5 = new("7836d248-68cc-4df6-b9e8-de991bf62eb7");
    static readonly Guid ImagingFactoryClass = new("cacaf262-9370-4615-a13b-9f5539da4c0a");
    static readonly Guid ImagingFactory = new("ec5ec8a9-c395-4314-9c77-54d7a935ff70");
    static readonly Guid PremultipliedBgra = new("6fddc324-4e03-4bfe-b185-3d77768dc910");
    const int QueryInterface = 0, Release = 2;
    const int CreateWicBitmapRenderTarget = 13, Clear = 47, BeginDraw = 48, EndDraw = 49, CreateSvgDocument = 115, DrawSvgDocument = 116;
    const int CreateBitmap = 17, CopyPixels = 7;
    const int GetRoot = 7, IsAttributeSpecified = 19, SetAttributeValue = 28, GetAttributeValue = 31;
    const int ViewBoxType = 12, LengthType = 13, Number = 0, Percentage = 1;
    [StructLayout(LayoutKind.Sequential)]
    readonly record struct RenderTargetProperties(int Type, int Format, int AlphaMode, float DpiX, float DpiY, int Usage, int MinLevel);
    [StructLayout(LayoutKind.Sequential)]
    readonly record struct SizeF(float Width, float Height);
    [StructLayout(LayoutKind.Sequential)]
    record struct Length(float Value, int Units);
    [StructLayout(LayoutKind.Sequential)]
    readonly record struct ViewBox(float X, float Y, float Width, float Height);
    [StructLayout(LayoutKind.Sequential)]
    readonly record struct ColorF(float R, float G, float B, float A);
    [DllImport("d2d1.dll")]
    static extern int D2D1CreateFactory(int type, Guid* riid, void* options, nint* factory);
    [DllImport("ole32.dll")]
    static extern int CoInitializeEx(nint reserved, uint mode);
    [DllImport("ole32.dll")]
    static extern void CoUninitialize();
    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* value);
    [DllImport("shlwapi.dll")]
    static extern nint SHCreateMemStream(byte* data, uint length);
    internal static void Render(ReadOnlySpan<byte> bytes, Pixels pixels)
    {
        // Balance only our own initialization; RPC_E_CHANGED_MODE means COM is already usable here.
        var com = CoInitializeEx(0, 2);
        nint imaging = 0, bitmap = 0, factory = 0, target = 0, context = 0, stream = 0, document = 0;
        try
        {
            Guid factoryId = Factory1, imagingClass = ImagingFactoryClass, imagingId = ImagingFactory, format = PremultipliedBgra, contextId = DeviceContext5;
            Check(D2D1CreateFactory(0, &factoryId, null, &factory), "Direct2D factory creation failed.");
            Check(CoCreateInstance(&imagingClass, 0, 1, &imagingId, &imaging), "WIC factory creation failed.");
            Check(
                ((delegate* unmanaged<nint, uint, uint, Guid*, int, nint*, int>)Slot(imaging, CreateBitmap))(imaging, (uint)pixels.Width, (uint)pixels.Height, &format, 2, &bitmap),
                "SVG bitmap creation failed."
            );
            // 96 DPI makes Direct2D units equal pixels; B8G8R8A8 premultiplied matches Pixels.
            var properties = new RenderTargetProperties(0, 87, 1, 96, 96, 0, 0);
            Check(
                ((delegate* unmanaged<nint, nint, RenderTargetProperties*, nint*, int>)Slot(factory, CreateWicBitmapRenderTarget))(factory, bitmap, &properties, &target),
                "SVG render target creation failed."
            );
            if (((delegate* unmanaged<nint, Guid*, nint*, int>)Slot(target, QueryInterface))(target, &contextId, &context) < 0)
                throw new PlatformNotSupportedException("SVG rendering on Windows needs Direct2D SVG support (Windows 10 1703 or later).");
            fixed (byte* p = bytes)
                stream = SHCreateMemStream(p, (uint)bytes.Length);
            if (stream == 0)
                throw new InvalidOperationException("SVG stream creation failed.");
            var viewport = new SizeF(pixels.Width, pixels.Height);
            if (((delegate* unmanaged<nint, nint, SizeF, nint*, int>)Slot(context, CreateSvgDocument))(context, stream, viewport, &document) < 0 || document == 0)
                throw new InvalidDataException("SVG decode failed.");
            FitRoot(document);
            var clear = new ColorF(0, 0, 0, 0);
            ((delegate* unmanaged<nint, void>)Slot(context, BeginDraw))(context);
            ((delegate* unmanaged<nint, ColorF*, void>)Slot(context, Clear))(context, &clear);
            ((delegate* unmanaged<nint, nint, void>)Slot(context, DrawSvgDocument))(context, document);
            ulong tag1, tag2;
            Check(((delegate* unmanaged<nint, ulong*, ulong*, int>)Slot(context, EndDraw))(context, &tag1, &tag2), "SVG render failed.");
            fixed (byte* p = pixels.Bytes)
            {
                Check(
                    ((delegate* unmanaged<nint, void*, uint, uint, byte*, int>)Slot(bitmap, CopyPixels))(bitmap, null, (uint)pixels.Width * 4, (uint)pixels.Bytes.Length, p),
                    "SVG pixel copy failed."
                );
            }
        }
        finally
        {
            foreach (var value in (ReadOnlySpan<nint>)[document, stream, context, target, bitmap, imaging, factory])
                if (value != 0)
                    ((delegate* unmanaged<nint, uint>)Slot(value, Release))(value);
            if (com >= 0)
                CoUninitialize();
        }
    }

    // Direct2D draws a root width/height at 1:1 and ignores the viewport unless sizes are relative.
    // Match AppKit and librsvg: derive a missing viewBox from the root size, then fill the viewport.
    static void FitRoot(nint document)
    {
        nint root = 0;
        ((delegate* unmanaged<nint, nint*, void>)Slot(document, GetRoot))(document, &root);
        if (root == 0)
            return;
        try
        {
            Length width = default, height = default;
            fixed (char* widthName = "width", heightName = "height", viewBoxName = "viewBox")
            {
                var get = (delegate* unmanaged<nint, char*, int, void*, uint, int>)Slot(root, GetAttributeValue);
                if (get(root, widthName, LengthType, &width, (uint)sizeof(Length)) < 0 || get(root, heightName, LengthType, &height, (uint)sizeof(Length)) < 0)
                    return;
                if (width.Units != Number || height.Units != Number || width.Value <= 0 || height.Value <= 0)
                    return;
                var set = (delegate* unmanaged<nint, char*, int, void*, uint, int>)Slot(root, SetAttributeValue);
                int inherited;
                if (((delegate* unmanaged<nint, char*, int*, int>)Slot(root, IsAttributeSpecified))(root, viewBoxName, &inherited) == 0)
                {
                    var viewBox = new ViewBox(0, 0, width.Value, height.Value);
                    Check(set(root, viewBoxName, ViewBoxType, &viewBox, (uint)sizeof(ViewBox)), "SVG viewBox update failed.");
                }

                var full = new Length(100, Percentage);
                Check(set(root, widthName, LengthType, &full, (uint)sizeof(Length)), "SVG width update failed.");
                Check(set(root, heightName, LengthType, &full, (uint)sizeof(Length)), "SVG height update failed.");
            }
        }
        finally
        {
            ((delegate* unmanaged<nint, uint>)Slot(root, Release))(root);
        }
    }

    static void* Slot(nint value, int index) => (*(void***)value)[index];
    static void Check(int result, string message)
    {
        if (result < 0)
            throw new InvalidOperationException($"{message} (HRESULT 0x{result:X8})");
    }
}
