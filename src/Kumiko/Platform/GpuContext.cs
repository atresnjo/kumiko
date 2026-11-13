using System.Runtime.InteropServices;

namespace Kumiko.Platform;

internal interface IGpuContext : IDisposable
{
    void MakeCurrent();
    nint Load(string name);
    void Swap();
}

internal sealed unsafe class EglContext : IGpuContext
{
    const string Library = "libEGL.so.1";
    static nint sharedDisplay;
    static int owners;
    nint display, surface, context;
    readonly nint gl = NativeLibrary.Load("libGL.so.1");
    [DllImport(Library)] static extern nint eglGetDisplay(nint display);
    [DllImport(Library)] static extern nint eglGetPlatformDisplay(uint platform, nint display, nint attributes);
    [DllImport(Library)] static extern int eglInitialize(nint display, out int major, out int minor);
    [DllImport(Library)] static extern int eglChooseConfig(nint display, int* attributes, out nint config, int size, out int count);
    [DllImport(Library)] static extern int eglBindAPI(uint api);
    [DllImport(Library)] static extern nint eglCreateContext(nint display, nint config, nint shared, int* attributes);
    [DllImport(Library)] static extern nint eglCreatePbufferSurface(nint display, nint config, int* attributes);
    [DllImport(Library)] static extern int eglMakeCurrent(nint display, nint draw, nint read, nint context);
    [DllImport(Library)] static extern int eglDestroySurface(nint display, nint surface);
    [DllImport(Library)] static extern int eglDestroyContext(nint display, nint context);
    [DllImport(Library)] static extern int eglTerminate(nint display);
    [DllImport(Library)] static extern nint eglGetProcAddress([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    public EglContext()
    {
        try
        {
            if (sharedDisplay == 0)
            {
                var candidate = eglGetPlatformDisplay(0x31dd, 0, 0);
                if (candidate == 0 || eglInitialize(candidate, out _, out _) == 0)
                {
                    candidate = eglGetDisplay(0);
                    if (candidate == 0 || eglInitialize(candidate, out _, out _) == 0)
                        throw new InvalidOperationException("EGL display is unavailable.");
                }
                sharedDisplay = candidate;
            }
            display = sharedDisplay;
            owners++;
            int* configAttributes = stackalloc int[] { 0x3033, 1, 0x3040, 8, 0x3024, 8, 0x3023, 8, 0x3022, 8, 0x3021, 8, 0x3038 };
            if (eglChooseConfig(display, configAttributes, out var config, 1, out int count) == 0 || count == 0 || eglBindAPI(0x30a2) == 0)
                throw new InvalidOperationException("EGL OpenGL configuration is unavailable.");
            int* attributes = stackalloc int[] { 0x3098, 3, 0x30fb, 3, 0x30fd, 1, 0x3038 };
            context = eglCreateContext(display, config, 0, attributes);
            int* size = stackalloc int[] { 0x3057, 1, 0x3056, 1, 0x3038 };
            surface = eglCreatePbufferSurface(display, config, size);
            if (context == 0 || surface == 0)
                throw new InvalidOperationException("EGL OpenGL 3.3 context creation failed.");
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
        if (eglMakeCurrent(display, surface, surface, context) == 0)
            throw new InvalidOperationException("EGL context binding failed.");
    }
    public nint Load(string name)
    {
        var pointer = eglGetProcAddress(name);
        return pointer != 0 ? pointer : NativeLibrary.GetExport(gl, name);
    }
    public void Swap()
    {
    }
    public void Dispose()
    {
        if (display == 0)
            return;
        eglMakeCurrent(display, 0, 0, 0);
        if (context != 0)
            eglDestroyContext(display, context);
        if (surface != 0)
            eglDestroySurface(display, surface);
        if (--owners == 0)
        {
            eglTerminate(display);
            sharedDisplay = 0;
        }
        display = context = surface = 0;
        NativeLibrary.Free(gl);
    }
}

internal sealed unsafe class CglContext : IGpuContext
{
    const string Library = "/System/Library/Frameworks/OpenGL.framework/OpenGL";
    readonly nint library = NativeLibrary.Load(Library);
    nint context;
    // Desktop-only exports must not add an OpenGL framework dependency to untrimmed iOS builds.
    int CGLChoosePixelFormat(int* attributes, out nint format, out int count)
    {
        fixed (nint* formatPointer = &format)
        fixed (int* countPointer = &count)
            return ((delegate* unmanaged<int*, nint*, int*, int>)Load("CGLChoosePixelFormat"))(attributes, formatPointer, countPointer);
    }
    int CGLCreateContext(nint format, nint shared, out nint value)
    {
        fixed (nint* pointer = &value)
            return ((delegate* unmanaged<nint, nint, nint*, int>)Load("CGLCreateContext"))(format, shared, pointer);
    }
    int CGLDestroyPixelFormat(nint format) => ((delegate* unmanaged<nint, int>)Load("CGLDestroyPixelFormat"))(format);
    int CGLSetCurrentContext(nint value) => ((delegate* unmanaged<nint, int>)Load("CGLSetCurrentContext"))(value);
    int CGLDestroyContext(nint value) => ((delegate* unmanaged<nint, int>)Load("CGLDestroyContext"))(value);
    public CglContext()
    {
        int* attributes = stackalloc int[] { 99, 0x4100, 73, 8, 24, 11, 8, 0 };
        if (CGLChoosePixelFormat(attributes, out var format, out _) != 0 || format == 0)
            throw new InvalidOperationException("Offscreen OpenGL pixel format is unavailable.");
        try
        {
            if (CGLCreateContext(format, 0, out context) != 0)
                throw new InvalidOperationException("Offscreen OpenGL context is unavailable.");
            MakeCurrent();
        }
        finally
        {
            CGLDestroyPixelFormat(format);
        }
    }
    public void MakeCurrent()
    {
        if (CGLSetCurrentContext(context) != 0)
            throw new InvalidOperationException("OpenGL context binding failed.");
    }
    public nint Load(string name) => NativeLibrary.GetExport(library, name);
    public void Swap()
    {
    }
    public void Dispose()
    {
        if (context == 0)
            return;
        CGLSetCurrentContext(0);
        CGLDestroyContext(context);
        context = 0;
        NativeLibrary.Free(library);
    }
}
