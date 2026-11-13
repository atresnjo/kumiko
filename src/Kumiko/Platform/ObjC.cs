using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Kumiko.Platform;

internal static unsafe partial class ObjC
{
    internal static NativeRect R(
        nint obj,
        string selector,
        NativeRect rect,
        nint view
    ) => ((delegate* unmanaged<nint, nint, NativeRect, nint, NativeRect>)structSend)(obj, Sel(selector), rect, view);
    internal static nint O(
        nint obj,
        string selector,
        double a,
        double b
    ) => ((delegate* unmanaged<nint, nint, double, double, nint>)send)(obj, Sel(selector), a, b);
    internal static void V(
        nint obj,
        string selector,
        NativeRect a,
        NativeRect b,
        nuint operation,
        double fraction
    ) => ((delegate* unmanaged<nint, nint, NativeRect, NativeRect, nuint, double, void>)send)(obj, Sel(selector), a, b, operation, fraction);
    internal static nint WakeEvent(nint cls) => ((delegate* unmanaged<nint, nint, nuint, NativePoint, nuint, double, nint, nint, short, nint, nint, nint>)send)(
        cls,
        Sel("otherEventWithType:location:modifierFlags:timestamp:windowNumber:context:subtype:data1:data2:"),
        15,
        default,
        0,
        0,
        0,
        0,
        0,
        0,
        0
    );
    internal static nint O(
        nint obj,
        string selector,
        double value
    ) => ((delegate* unmanaged<nint, nint, double, nint>)send)(obj, Sel(selector), value);
    internal static byte B(
        nint obj,
        string selector,
        nint a,
        nint b
    ) => ((delegate* unmanaged<nint, nint, nint, nint, byte>)send)(obj, Sel(selector), a, b);
    internal static byte B(
        nint obj,
        string selector,
        nint a,
        nint b,
        nint c
    ) => ((delegate* unmanaged<nint, nint, nint, nint, nint, byte>)send)(obj, Sel(selector), a, b, c);
    internal static nint O(
        nint obj,
        string selector,
        nuint a,
        nuint b
    ) => ((delegate* unmanaged<nint, nint, nuint, nuint, nint>)send)(obj, Sel(selector), a, b);
    internal static nint O(
        nint obj,
        string selector,
        nint a,
        nuint b,
        nint c,
        nint d
    ) => ((delegate* unmanaged<nint, nint, nint, nuint, nint, nint, nint>)send)(obj, Sel(selector), a, b, c, d);
    internal static NativeRect R(
        nint obj,
        string selector,
        NativeRect rect
    ) => ((delegate* unmanaged<nint, nint, NativeRect, NativeRect>)structSend)(obj, Sel(selector), rect);
    internal static NativePoint P(
        nint obj,
        string selector,
        NativePoint point,
        nint view
    ) => ((delegate* unmanaged<nint, nint, NativePoint, nint, NativePoint>)send)(obj, Sel(selector), point, view);
    internal static NativeSize S(nint obj, string selector) => ((delegate* unmanaged<nint, nint, NativeSize>)send)(obj, Sel(selector));
    internal static nint O(
        nint obj,
        string selector,
        NativeRect rect,
        nuint options,
        nint owner,
        nint user
    ) => ((delegate* unmanaged<nint, nint, NativeRect, nuint, nint, nint, nint>)send)(obj, Sel(selector), rect, options, owner, user);
    internal static nint O(
        nint obj,
        string selector,
        nuint a,
        nuint b,
        nuint c,
        byte d
    ) => ((delegate* unmanaged<nint, nint, nuint, nuint, nuint, byte, nint>)send)(obj, Sel(selector), a, b, c, d);
    internal static nint O(
        nint obj,
        string selector,
        nint a,
        byte b,
        byte c
    ) => ((delegate* unmanaged<nint, nint, nint, byte, byte, nint>)send)(obj, Sel(selector), a, b, c);
    internal static void V(
        nint obj,
        string selector,
        ThreadSize a,
        ThreadSize b
    ) => ((delegate* unmanaged<nint, nint, ThreadSize, ThreadSize, void>)send)(obj, Sel(selector), a, b);
    const string Lib = "/usr/lib/libobjc.A.dylib";
    static readonly ConcurrentDictionary<string, nint> selectors = new();
    static readonly nint library = NativeLibrary.Load(Lib);
    static readonly nint send = NativeLibrary.GetExport(library, "objc_msgSend");
    static readonly nint structSend = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? NativeLibrary.GetExport(library, "objc_msgSend_stret") : send;
    internal static nint Sel(string name) => selectors.GetOrAdd(name, static n => sel_registerName(n));
    [DllImport(Lib)]
    static extern nint sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Lib)]
    internal static extern nint objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Lib)]
    internal static extern nint objc_allocateClassPair(nint superclass, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nuint size);
    [DllImport(Lib)]
    internal static extern void objc_registerClassPair(nint cls);
    [DllImport(Lib)]
    internal static extern byte class_addMethod(nint cls, nint selector, nint imp, [MarshalAs(UnmanagedType.LPUTF8Str)] string types);
    [DllImport(Lib)]
    internal static extern nint objc_getProtocol([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Lib)]
    internal static extern byte class_addProtocol(nint cls, nint protocol);
    [DllImport(Lib)]
    internal static extern nint objc_autoreleasePoolPush();
    [DllImport(Lib)]
    internal static extern void objc_autoreleasePoolPop(nint pool);
    internal static nint Class(string name) => objc_getClass(name) is var cls && cls != 0 ? cls : throw new InvalidOperationException($"Missing Objective-C class: {name}");
    internal static nint New(string name) => O(Class(name), "new");
    internal static void Release(nint obj)
    {
        if (obj != 0)
            V(obj, "release");
    }

    internal static nint String(string value)
    {
        fixed (char* p = value)
            return O(O(Class("NSString"), "alloc"), "initWithCharacters:length:", (nint)p, (nuint)value.Length);
    }

    internal static nint TempString(string value)
    {
        var obj = String(value);
        return O(obj, "autorelease");
    }

    internal static string Text(nint value) => value == 0 ? "" : Marshal.PtrToStringUTF8(O(value, "UTF8String")) ?? "";
    internal static nint Define(string name, string parent, params (string Name, nint Callback, string Types)[] methods)
    {
        var existing = objc_getClass(name);
        if (existing != 0)
            return existing;
        var cls = objc_allocateClassPair(Class(parent), name, 0);
        foreach (var method in methods)
            if (class_addMethod(cls, Sel(method.Name), method.Callback, method.Types) == 0)
                throw new InvalidOperationException(method.Name);
        objc_registerClassPair(cls);
        return cls;
    }

    internal static nint O(nint obj, string selector) => ((delegate* unmanaged<nint, nint, nint>)send)(obj, Sel(selector));
    internal static nint O(nint obj, string selector, nint a0) => ((delegate* unmanaged<nint, nint, nint, nint>)send)(obj, Sel(selector), a0);
    internal static nint O(
        nint obj,
        string selector,
        nint a0,
        nint a1
    ) => ((delegate* unmanaged<nint, nint, nint, nint, nint>)send)(obj, Sel(selector), a0, a1);
    internal static nint O(
        nint obj,
        string selector,
        nint a0,
        nint a1,
        nint a2
    ) => ((delegate* unmanaged<nint, nint, nint, nint, nint, nint>)send)(obj, Sel(selector), a0, a1, a2);
    internal static nint O(
        nint obj,
        string selector,
        nint a0,
        nint a1,
        nint a2,
        nint a3
    ) => ((delegate* unmanaged<nint, nint, nint, nint, nint, nint, nint>)send)(obj, Sel(selector), a0, a1, a2, a3);
    internal static nint O(nint obj, string selector, nuint a0) => ((delegate* unmanaged<nint, nint, nuint, nint>)send)(obj, Sel(selector), a0);
    internal static nint O(
        nint obj,
        string selector,
        nuint a0,
        nuint a1,
        nuint a2
    ) => ((delegate* unmanaged<nint, nint, nuint, nuint, nuint, nint>)send)(obj, Sel(selector), a0, a1, a2);
    internal static nint O(
        nint obj,
        string selector,
        nint a0,
        nuint a1
    ) => ((delegate* unmanaged<nint, nint, nint, nuint, nint>)send)(obj, Sel(selector), a0, a1);
    internal static nint O(
        nint obj,
        string selector,
        nint a0,
        nuint a1,
        nuint a2
    ) => ((delegate* unmanaged<nint, nint, nint, nuint, nuint, nint>)send)(obj, Sel(selector), a0, a1, a2);
    internal static nint O(
        nint obj,
        string selector,
        nint a0,
        nuint a1,
        nuint a2,
        nint a3
    ) => ((delegate* unmanaged<nint, nint, nint, nuint, nuint, nint, nint>)send)(obj, Sel(selector), a0, a1, a2, a3);
    internal static nint O(
        nint obj,
        string selector,
        nint a0,
        nint a1,
        nint a2,
        nuint a3
    ) => ((delegate* unmanaged<nint, nint, nint, nint, nint, nuint, nint>)send)(obj, Sel(selector), a0, a1, a2, a3);
    internal static nint O(
        nint obj,
        string selector,
        nint a0,
        double a1
    ) => ((delegate* unmanaged<nint, nint, nint, double, nint>)send)(obj, Sel(selector), a0, a1);
    internal static nint O(
        nint obj,
        string selector,
        nint a0,
        double a1,
        double a2
    ) => ((delegate* unmanaged<nint, nint, nint, double, double, nint>)send)(obj, Sel(selector), a0, a1, a2);
    internal static nint O(
        nint obj,
        string selector,
        NativeRect a0
    ) => ((delegate* unmanaged<nint, nint, NativeRect, nint>)send)(obj, Sel(selector), a0);
    internal static nint O(
        nint obj,
        string selector,
        NativeRect a0,
        nuint a1,
        nuint a2,
        byte a3
    ) => ((delegate* unmanaged<nint, nint, NativeRect, nuint, nuint, byte, nint>)send)(obj, Sel(selector), a0, a1, a2, a3);
    internal static nint O(
        nint obj,
        string selector,
        NativeRect a0,
        nint a1
    ) => ((delegate* unmanaged<nint, nint, NativeRect, nint, nint>)send)(obj, Sel(selector), a0, a1);
    internal static nint O(
        nint obj,
        string selector,
        nint a0,
        nint a1,
        nuint a2
    ) => ((delegate* unmanaged<nint, nint, nint, nint, nuint, nint>)send)(obj, Sel(selector), a0, a1, a2);
    internal static nint O(
        nint obj,
        string selector,
        nint a0,
        nint a1,
        nint a2,
        nint a3,
        nint a4
    ) => ((delegate* unmanaged<nint, nint, nint, nint, nint, nint, nint, nint>)send)(obj, Sel(selector), a0, a1, a2, a3, a4);
    internal static void V(nint obj, string selector) => ((delegate* unmanaged<nint, nint, void>)send)(obj, Sel(selector));
    internal static void V(nint obj, string selector, nint a0) => ((delegate* unmanaged<nint, nint, nint, void>)send)(obj, Sel(selector), a0);
    internal static void V(nint obj, string selector, nuint a0) => ((delegate* unmanaged<nint, nint, nuint, void>)send)(obj, Sel(selector), a0);
    internal static void V(nint obj, string selector, byte a0) => ((delegate* unmanaged<nint, nint, byte, void>)send)(obj, Sel(selector), a0);
    internal static void V(nint obj, string selector, double a0) => ((delegate* unmanaged<nint, nint, double, void>)send)(obj, Sel(selector), a0);
    internal static void V(
        nint obj,
        string selector,
        nint a0,
        nint a1
    ) => ((delegate* unmanaged<nint, nint, nint, nint, void>)send)(obj, Sel(selector), a0, a1);
    internal static void V(
        nint obj,
        string selector,
        nint a0,
        nint a1,
        nint a2,
        nint a3
    ) => ((delegate* unmanaged<nint, nint, nint, nint, nint, nint, void>)send)(obj, Sel(selector), a0, a1, a2, a3);
    internal static void V(
        nint obj,
        string selector,
        nuint a0,
        nuint a1
    ) => ((delegate* unmanaged<nint, nint, nuint, nuint, void>)send)(obj, Sel(selector), a0, a1);
    internal static void V(
        nint obj,
        string selector,
        nint a0,
        nuint a1,
        nuint a2
    ) => ((delegate* unmanaged<nint, nint, nint, nuint, nuint, void>)send)(obj, Sel(selector), a0, a1, a2);
    internal static void V(
        nint obj,
        string selector,
        nint a0,
        nuint a1
    ) => ((delegate* unmanaged<nint, nint, nint, nuint, void>)send)(obj, Sel(selector), a0, a1);
    internal static void V(
        nint obj,
        string selector,
        nint a0,
        nint a1,
        nuint a2
    ) => ((delegate* unmanaged<nint, nint, nint, nint, nuint, void>)send)(obj, Sel(selector), a0, a1, a2);
    internal static void V(
        nint obj,
        string selector,
        NativeRect a0,
        byte a1
    ) => ((delegate* unmanaged<nint, nint, NativeRect, byte, void>)send)(obj, Sel(selector), a0, a1);
    internal static void V(
        nint obj,
        string selector,
        NativeRect a0
    ) => ((delegate* unmanaged<nint, nint, NativeRect, void>)send)(obj, Sel(selector), a0);
    internal static void V(
        nint obj,
        string selector,
        NativeSize a0
    ) => ((delegate* unmanaged<nint, nint, NativeSize, void>)send)(obj, Sel(selector), a0);
    internal static void V(
        nint obj,
        string selector,
        NativePoint a0
    ) => ((delegate* unmanaged<nint, nint, NativePoint, void>)send)(obj, Sel(selector), a0);
    internal static void V(
        nint obj,
        string selector,
        ClearColor a0
    ) => ((delegate* unmanaged<nint, nint, ClearColor, void>)send)(obj, Sel(selector), a0);
    internal static void V(
        nint obj,
        string selector,
        nuint a0,
        nuint a1,
        nuint a2,
        nuint a3,
        nuint a4
    ) => ((delegate* unmanaged<nint, nint, nuint, nuint, nuint, nuint, nuint, void>)send)(obj, Sel(selector), a0, a1, a2, a3, a4);
    internal static void V(
        nint obj,
        string selector,
        Region a0,
        nuint a1,
        nint a2,
        nuint a3
    ) => ((delegate* unmanaged<nint, nint, Region, nuint, nint, nuint, void>)send)(obj, Sel(selector), a0, a1, a2, a3);
    internal static void V(
        nint obj,
        string selector,
        nint a0,
        nuint a1,
        Region a2,
        nuint a3
    ) => ((delegate* unmanaged<nint, nint, nint, nuint, Region, nuint, void>)send)(obj, Sel(selector), a0, a1, a2, a3);
    /// <summary>Encodes a buffer-to-texture copy of <paramref name="size"/> rows starting at row <paramref name="y"/>.</summary>
    internal static void CopyToTexture(nint blit, nint buffer, nuint bytesPerRow, Region size, nint texture, nuint y) =>
        ((delegate* unmanaged<nint, nint, nint, nuint, nuint, nuint, NativeSize3, nint, nuint, nuint, NativeSize3, void>)send)(blit,
            Sel("copyFromBuffer:sourceOffset:sourceBytesPerRow:sourceBytesPerImage:sourceSize:toTexture:destinationSlice:destinationLevel:destinationOrigin:"),
            buffer, 0, bytesPerRow, bytesPerRow * size.Height, new(size.Width, size.Height, size.Depth), texture, 0, 0, new(0, y, 0));
    internal static byte B(nint obj, string selector) => ((delegate* unmanaged<nint, nint, byte>)send)(obj, Sel(selector));
    internal static byte B(nint obj, string selector, nint a0) => ((delegate* unmanaged<nint, nint, nint, byte>)send)(obj, Sel(selector), a0);
    internal static nuint N(nint obj, string selector) => ((delegate* unmanaged<nint, nint, nuint>)send)(obj, Sel(selector));
    internal static double D(nint obj, string selector) => ((delegate* unmanaged<nint, nint, double>)send)(obj, Sel(selector));
    internal static NativeRect R(nint obj, string selector) => ((delegate* unmanaged<nint, nint, NativeRect>)structSend)(obj, Sel(selector));
    internal static NativePoint P(nint obj, string selector) => ((delegate* unmanaged<nint, nint, NativePoint>)send)(obj, Sel(selector));
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativePoint(double X, double Y);
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativeSize(double Width, double Height);
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativeRect(NativePoint Origin, NativeSize Size)
{
    internal NativeRect(double x, double y, double w, double h) : this(new(x, y), new(w, h))
    {
    }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct ClearColor(double R, double G, double B, double A);
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct Region(nuint X, nuint Y, nuint Z, nuint Width, nuint Height, nuint Depth);
/// <summary>MTLSize or MTLOrigin.</summary>
internal readonly record struct NativeSize3(nuint A, nuint B, nuint C);
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct ThreadSize(nuint Width, nuint Height, nuint Depth);
internal readonly struct AutoreleasePool : IDisposable
{
    readonly nint handle;
    public AutoreleasePool() => handle = ObjC.objc_autoreleasePoolPush();
    public void Dispose() => ObjC.objc_autoreleasePoolPop(handle);
}
