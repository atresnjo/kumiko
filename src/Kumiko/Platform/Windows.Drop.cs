using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Kumiko.UI;

namespace Kumiko.Platform;

/// <summary>
/// Files dragged in from Explorer and other apps through an OLE drop target, with hover like the macOS
/// NSDraggingDestination. WM_DROPFILES remains only as the fallback when RegisterDragDrop fails.
/// </summary>
internal sealed unsafe partial class WindowsWindow
{
    // The registered IDropTarget; RegisterDragDrop holds its own reference until RevokeDragDrop.
    nint dropTarget;
    string[] dragFiles = [];
    Point dragPoint;

    /// <summary>The registered drop target's IDropTarget pointer, or 0 when WM_DROPFILES is the fallback.</summary>
    internal nint DropTargetPointer => dropTarget;

    /// <summary>The live backend that owns <paramref name="handle"/>, for verification.</summary>
    internal static WindowsWindow? Find(nint handle) => windows.GetValueOrDefault(handle);

    internal void Report(Exception ex) => window.App.Report(ex);

    void RegisterDrop()
    {
        var target = (nint)ComInterfaceMarshaller<IDropTargetNative>.ConvertToUnmanaged(new OleDropTarget(this));
        if (RegisterDragDrop(handle, target) >= 0)
        {
            dropTarget = target;
            return;
        }
        Marshal.Release(target);
        DragAcceptFiles(handle, 1);
    }

    /// <summary>Runs before DestroyWindow: OLE keeps the target only while the window exists.</summary>
    void RevokeDrop()
    {
        if (dropTarget == 0)
            return;
        RevokeDragDrop(handle);
        Marshal.Release(dropTarget);
        dropTarget = 0;
        dragFiles = [];
    }

    internal uint DropEnter(nint data, UiaNative.ScreenPoint screen, uint allowed)
    {
        dragFiles = DraggedFiles(data);
        dragPoint = ClientPoint(screen);
        var files = dragFiles;
        var point = dragPoint;
        window.Input(i =>
        {
            i.Pointer = point;
            i.DragFiles = files.Length > 0 ? files : null;
        });
        return DropEffect(allowed);
    }

    /// <summary>OLE repeats DragOver while the pointer rests; only a move becomes input.</summary>
    internal uint DropOver(UiaNative.ScreenPoint screen, uint allowed)
    {
        var point = ClientPoint(screen);
        if (point != dragPoint && dragFiles.Length > 0)
        {
            dragPoint = point;
            window.Input(i => i.Pointer = point);
        }
        return DropEffect(allowed);
    }

    internal void DropLeave()
    {
        dragFiles = [];
        window.Input(i => i.DragFiles = null);
    }

    internal uint DropFinish(nint data, UiaNative.ScreenPoint screen, uint allowed)
    {
        var files = DraggedFiles(data);
        var point = ClientPoint(screen);
        dragFiles = files;
        var effect = DropEffect(allowed);
        dragFiles = [];
        window.Input(i =>
        {
            i.Pointer = point;
            i.DragFiles = null;
            i.DroppedFiles = files.Length > 0 ? files : null;
        });
        return effect;
    }

    /// <summary>DROPEFFECT_COPY while files are offered and the source allows copying, else DROPEFFECT_NONE.</summary>
    uint DropEffect(uint allowed) => dragFiles.Length > 0 && (allowed & 1) != 0 ? 1u : 0u;

    /// <summary>POINTL arrives in screen pixels; input uses client coordinates in logical units.</summary>
    Point ClientPoint(UiaNative.ScreenPoint screen)
    {
        ScreenToClient(handle, ref screen);
        float scale = Math.Max(1, Win32.GetDpiForWindow(handle) / 96f);
        return new(screen.X / scale, screen.Y / scale);
    }

    /// <summary>CF_HDROP paths from an IDataObject; other formats are ignored, as on macOS.</summary>
    static string[] DraggedFiles(nint data)
    {
        if (data == 0)
            return [];
        // FORMATETC { CF_HDROP, null, DVASPECT_CONTENT, -1, TYMED_HGLOBAL }; IDataObject::GetData is slot 3.
        var format = new DragFormat
        {
            Format = 15,
            Aspect = 1,
            Index = -1,
            Medium = 1
        };
        DragMedium medium;
        if (((delegate* unmanaged[Stdcall]<nint, DragFormat*, DragMedium*, int>)(*(nint**)data)[3])(data, &format, &medium) < 0)
            return [];
        try
        {
            if (medium.Type != 1 || medium.Handle == 0)
                return [];
            var count = DragQueryFileW(medium.Handle, uint.MaxValue, null, 0);
            var files = new List<string>();
            for (uint i = 0; i < count; i++)
            {
                var length = DragQueryFileW(medium.Handle, i, null, 0);
                if (length == 0)
                    continue;
                var buffer = new char[length + 1];
                fixed (char* text = buffer)
                    length = DragQueryFileW(medium.Handle, i, text, length + 1);
                if (length > 0)
                    files.Add(new string(buffer, 0, (int)length));
            }
            return [.. files];
        }
        finally
        {
            ReleaseStgMedium(&medium);
        }
    }

    [DllImport("ole32.dll")]
    static extern int RegisterDragDrop(nint window, nint target);
    [DllImport("ole32.dll")]
    static extern int RevokeDragDrop(nint window);
    [DllImport("ole32.dll")]
    static extern void ReleaseStgMedium(DragMedium* medium);
    [DllImport("user32.dll")]
    static extern int ScreenToClient(nint window, ref UiaNative.ScreenPoint point);
}

/// <summary>IDropTarget from oleidl.idl: DragEnter, DragOver, DragLeave and Drop after IUnknown; POINTL is by value.</summary>
[GeneratedComInterface, Guid("00000122-0000-0000-C000-000000000046")]
internal partial interface IDropTargetNative
{
    [PreserveSig]
    int DragEnter(nint data, uint keyState, UiaNative.ScreenPoint point, ref uint effect);
    [PreserveSig]
    int DragOver(uint keyState, UiaNative.ScreenPoint point, ref uint effect);
    [PreserveSig]
    int DragLeave();
    [PreserveSig]
    int Drop(nint data, uint keyState, UiaNative.ScreenPoint point, ref uint effect);
}

/// <summary>Forwards OLE drag callbacks to the window; exceptions are reported and never reach OLE.</summary>
[GeneratedComClass]
internal sealed partial class OleDropTarget(WindowsWindow owner) : IDropTargetNative
{
    public int DragEnter(nint data, uint keyState, UiaNative.ScreenPoint point, ref uint effect)
    {
        var allowed = effect;
        effect = 0;
        try
        {
            effect = owner.DropEnter(data, point, allowed);
        }
        catch (Exception ex)
        {
            owner.Report(ex);
        }
        return 0;
    }

    public int DragOver(uint keyState, UiaNative.ScreenPoint point, ref uint effect)
    {
        var allowed = effect;
        effect = 0;
        try
        {
            effect = owner.DropOver(point, allowed);
        }
        catch (Exception ex)
        {
            owner.Report(ex);
        }
        return 0;
    }

    public int DragLeave()
    {
        try
        {
            owner.DropLeave();
        }
        catch (Exception ex)
        {
            owner.Report(ex);
        }
        return 0;
    }

    public int Drop(nint data, uint keyState, UiaNative.ScreenPoint point, ref uint effect)
    {
        var allowed = effect;
        effect = 0;
        try
        {
            effect = owner.DropFinish(data, point, allowed);
        }
        catch (Exception ex)
        {
            owner.Report(ex);
        }
        return 0;
    }
}
