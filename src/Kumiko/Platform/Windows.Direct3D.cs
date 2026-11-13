using Kumiko.Rendering;
using Kumiko.UI;

namespace Kumiko.Platform;

// Opt-in Direct3D 11 presentation: KUMIKO_RENDERER=d3d11 prefers hardware, then WARP; =warp forces
// WARP. A lost device is recreated once; any other failure, or a second loss, falls back to the
// retained software renderer and records the reason in Renderer.
internal sealed partial class WindowsWindow
{
    D3D11? direct3D;
    bool direct3DWarp, direct3DRecreated;

    void StartDirect3D(bool warp)
    {
        direct3DWarp = warp;
        try
        {
            direct3D = new(warp, handle);
            Renderer = direct3D.Driver;
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            Renderer = "Software fallback: " + ex.Message;
        }
    }

    bool DrawDirect3D(Context context, bool present)
    {
        while (direct3D is not null)
        {
            try
            {
                direct3D.Render(context, present);
                pixels = null;
                return true;
            }
            catch (InvalidOperationException ex)
            {
                Recover(ex);
            }
        }
        return false;
    }

    Pixels CaptureDirect3D(Context context)
    {
        while (DrawDirect3D(context, false))
        {
            try
            {
                return direct3D!.Readback();
            }
            catch (InvalidOperationException ex)
            {
                Recover(ex);
            }
        }
        return canvas.Capture(context);
    }

    void Recover(InvalidOperationException ex)
    {
        direct3D?.Dispose();
        direct3D = null;
        if (ex is D3D11DeviceLostException && !direct3DRecreated)
        {
            direct3DRecreated = true;
            StartDirect3D(direct3DWarp);
            if (direct3D is not null)
                Renderer += " (recreated after device loss)";
            return;
        }
        Renderer = "Software fallback: " + ex.Message;
    }
}
