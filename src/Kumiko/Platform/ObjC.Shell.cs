namespace Kumiko.Platform;

internal static unsafe partial class ObjC
{
    internal static nint O(nint obj, string selector, uint a, uint b, nint c, short d, int e) => ((delegate* unmanaged<nint, nint, uint, uint, nint, short, int, nint>)send)(obj, Sel(selector), a, b, c, d, e);
    internal static void V(nint obj, string selector, NativeRect a, nint b) => ((delegate* unmanaged<nint, nint, NativeRect, nint, void>)send)(obj, Sel(selector), a, b);
    internal static void V(nint obj, string selector, nuint a, nint b) => ((delegate* unmanaged<nint, nint, nuint, nint, void>)send)(obj, Sel(selector), a, b);
    internal static void V(nint obj, string selector, nint a, nint b, nuint c, nuint d) => ((delegate* unmanaged<nint, nint, nint, nint, nuint, nuint, void>)send)(obj, Sel(selector), a, b, c, d);
    internal static void V(nint obj, string selector, nint a, nint b, nint c) => ((delegate* unmanaged<nint, nint, nint, nint, nint, void>)send)(obj, Sel(selector), a, b, c);
}
