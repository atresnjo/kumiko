using static Kumiko.Rendering.D3D11Native;

namespace Kumiko.Rendering;

// Window presentation: the frame texture is copied into a DXGI swap chain's back buffer and
// presented with vsync. Flip model is preferred; the blt model remains for older RDP hosts.
internal sealed unsafe partial class D3D11
{
    nint swapChain;
    int swapWidth, swapHeight;

    void CreateSwapChain(nint window)
    {
        nint dxgi = Query(device, DxgiDevice), adapter = 0, factory = 0;
        try
        {
            Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(dxgi, DxgiSlot.GetAdapter))(dxgi, &adapter), "DXGI adapter query failed.");
            var factoryId = DxgiFactory2;
            Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(adapter, GetParent))(adapter, &factoryId, &factory), "DXGI 1.2 factory is unavailable.");
            // FLIP_DISCARD with two buffers; sizes are set by the first ResizeBuffers in Present.
            var description = new SwapChainDescription
            {
                Width = 8,
                Height = 8,
                Format = Bgra,
                SampleCount = 1,
                BufferUsage = 0x20,
                BufferCount = 2,
                SwapEffect = 4
            };
            var create = (delegate* unmanaged[Stdcall]<nint, nint, nint, SwapChainDescription*, void*, nint, nint*, int>)Slot(factory, DxgiSlot.CreateSwapChainForHwnd);
            nint chain = 0;
            int result = create(factory, device, window, &description, null, 0, &chain);
            if (result < 0 && !IsDeviceLost(result))
            {
                description.BufferCount = 1;
                description.SwapEffect = 0;
                result = create(factory, device, window, &description, null, 0, &chain);
            }
            Check(result, "Direct3D 11 swap chain creation failed.");
            swapChain = chain;
            // DXGI_MWA_NO_ALT_ENTER: Kumiko windows do not switch to exclusive fullscreen.
            ((delegate* unmanaged[Stdcall]<nint, nint, uint, int>)Slot(factory, DxgiSlot.MakeWindowAssociation))(factory, window, 2);
        }
        finally
        {
            ReleaseObject(ref factory);
            ReleaseObject(ref adapter);
            ReleaseObject(ref dxgi);
        }
    }

    void Present()
    {
        if (swapChain == 0)
            return;
        if (swapWidth != width || swapHeight != height)
        {
            // No back-buffer reference survives a Present, so the buffers may be resized here.
            Check(
                ((delegate* unmanaged[Stdcall]<nint, uint, uint, uint, int, uint, int>)Slot(swapChain, DxgiSlot.ResizeBuffers))(swapChain, 0, (uint)width, (uint)height, 0, 0),
                "Direct3D 11 swap chain resize failed."
            );
            swapWidth = width;
            swapHeight = height;
        }
        nint back = 0;
        var textureId = Texture2D;
        Check(((delegate* unmanaged[Stdcall]<nint, uint, Guid*, nint*, int>)Slot(swapChain, DxgiSlot.GetBuffer))(swapChain, 0, &textureId, &back), "Direct3D 11 back buffer is unavailable.");
        try
        {
            ((delegate* unmanaged[Stdcall]<nint, nint, nint, void>)Slot(context, ContextSlot.CopyResource))(context, back, frameTexture);
        }
        finally
        {
            ReleaseObject(ref back);
        }
        // Success codes such as DXGI_STATUS_OCCLUDED are positive and need no handling.
        Check(((delegate* unmanaged[Stdcall]<nint, uint, uint, int>)Slot(swapChain, DxgiSlot.Present))(swapChain, 1, 0), "Direct3D 11 present failed.");
    }

    void DisposeSwapChain()
    {
        ReleaseObject(ref swapChain);
        swapWidth = swapHeight = 0;
    }
}
