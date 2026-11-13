using System.Runtime.InteropServices;

namespace Kumiko.Platform;

internal sealed unsafe class WglContext : IGpuContext
{
    nint window, dc, context;
    readonly bool owned;
    readonly nint library = NativeLibrary.Load("opengl32.dll");
    [StructLayout(LayoutKind.Sequential)]
    struct Format
    {
        public ushort Size, Version;
        public uint Flags;
        public byte PixelType, ColorBits, RedBits, RedShift, GreenBits, GreenShift, BlueBits, BlueShift, AlphaBits, AlphaShift,
            AccumBits, AccumRed, AccumGreen, AccumBlue, AccumAlpha, DepthBits, StencilBits, AuxBuffers, LayerType, Reserved;
        public uint LayerMask, VisibleMask, DamageMask;
    }
    [DllImport("user32.dll")] static extern nint GetDC(nint window);
    [DllImport("user32.dll")] static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] static extern int ChoosePixelFormat(nint dc, ref Format format);
    [DllImport("gdi32.dll")] static extern int SetPixelFormat(nint dc, int index, ref Format format);
    [DllImport("gdi32.dll")] static extern int SwapBuffers(nint dc);
    [DllImport("opengl32.dll")] static extern nint wglCreateContext(nint dc);
    [DllImport("opengl32.dll")] static extern int wglDeleteContext(nint context);
    [DllImport("opengl32.dll")] static extern int wglMakeCurrent(nint dc, nint context);
    [DllImport("opengl32.dll", CharSet = CharSet.Ansi)] static extern nint wglGetProcAddress(string name);
    public WglContext(nint window = 0)
    {
        owned = window == 0;
        this.window = window != 0 ? window : Win32.CreateWindowExW(0, "STATIC", "Kumiko offscreen GPU", 0x80000000, 0, 0, 1, 1, 0, 0, Win32.GetModuleHandleW(null), 0);
        try
        {
            dc = GetDC(this.window);
            var format = new Format { Size = (ushort)sizeof(Format), Version = 1, Flags = 4 | 0x20 | 1, ColorBits = 32, AlphaBits = 8 };
            int index = ChoosePixelFormat(dc, ref format);
            if (index == 0 || SetPixelFormat(dc, index, ref format) == 0)
                throw new InvalidOperationException("OpenGL window pixel format is unavailable.");
            context = wglCreateContext(dc);
            MakeCurrent();
            var pointer = wglGetProcAddress("wglCreateContextAttribsARB");
            if (pointer == 0)
                throw new InvalidOperationException("OpenGL 3.3 is unavailable.");
            int* attributes = stackalloc int[] { 0x2091, 3, 0x2092, 3, 0x9126, 1, 0 };
            var core = ((delegate* unmanaged<nint, nint, int*, nint>)pointer)(dc, 0, attributes);
            if (core == 0)
                throw new InvalidOperationException("OpenGL 3.3 context creation failed.");
            wglMakeCurrent(0, 0);
            wglDeleteContext(context);
            context = core;
            MakeCurrent();
        }
        catch
        {
            Dispose();
            throw;
        }
    }
    public void MakeCurrent()
    {
        if (context == 0 || wglMakeCurrent(dc, context) == 0)
            throw new InvalidOperationException("OpenGL window context binding failed.");
    }
    public nint Load(string name)
    {
        var pointer = wglGetProcAddress(name);
        return pointer is 0 or 1 or 2 or 3 or -1 ? NativeLibrary.GetExport(library, name) : pointer;
    }
    public void Swap() => SwapBuffers(dc);
    public void Dispose()
    {
        if (window == 0)
            return;
        wglMakeCurrent(0, 0);
        if (context != 0)
            wglDeleteContext(context);
        if (dc != 0)
            ReleaseDC(window, dc);
        if (owned)
            Win32.DestroyWindow(window);
        window = dc = context = 0;
        NativeLibrary.Free(library);
    }
}
