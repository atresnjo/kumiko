using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using Kumiko.Desktop;
using Kumiko.UI;

namespace Kumiko.Platform;

internal sealed unsafe partial class WindowsWindow
{
    public void StartDrag(TransferData data, Rect bounds)
    {
        int initialized = OleInitialize(0);
        Marshal.ThrowExceptionForHR(initialized);
        try
        {
            using var snapshot = new TransferSnapshot(window.App, data);
            var source = (nint)ComInterfaceMarshaller<IDragSource>.ConvertToUnmanaged(new OleDragSource());
            var transfer = (nint)ComInterfaceMarshaller<IDragData>.ConvertToUnmanaged(new OleDragData(snapshot));
            try
            {
                Marshal.ThrowExceptionForHR(DoDragDrop(transfer, source, 1, out _));
            }
            finally
            {
                Marshal.Release(transfer);
                Marshal.Release(source);
            }
        }
        finally
        {
            OleUninitialize();
        }
    }

    [DllImport("ole32.dll")]
    static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll")]
    static extern void OleUninitialize();
    [DllImport("ole32.dll")]
    static extern int DoDragDrop(nint data, nint source, uint allowed, out uint performed);
}

[StructLayout(LayoutKind.Sequential)]
internal struct DragFormat
{
    public ushort Format;
    public nint Device;
    public uint Aspect;
    public int Index;
    public uint Medium;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DragMedium
{
    public uint Type;
    public nint Handle, Release;
}

[GeneratedComInterface, Guid("00000121-0000-0000-C000-000000000046")]
internal partial interface IDragSource
{
    [PreserveSig]
    int QueryContinueDrag(int escape, uint keyState);
    [PreserveSig]
    int GiveFeedback(uint effect);
}

[GeneratedComClass]
internal sealed partial class OleDragSource : IDragSource
{
    public int QueryContinueDrag(int escape, uint keyState) => escape != 0 ? 0x40101 : (keyState & 1) == 0 ? 0x40100 : 0;
    public int GiveFeedback(uint effect) => 0x40102;
}

[GeneratedComInterface, Guid("0000010e-0000-0000-C000-000000000046")]
internal unsafe partial interface IDragData
{
    [PreserveSig]
    int GetData(in DragFormat format, out DragMedium medium);
    [PreserveSig]
    int GetDataHere(in DragFormat format, ref DragMedium medium);
    [PreserveSig]
    int QueryGetData(in DragFormat format);
    [PreserveSig]
    int GetCanonicalFormatEtc(in DragFormat input, out DragFormat output);
    [PreserveSig]
    int SetData(in DragFormat format, in DragMedium medium, int release);
    [PreserveSig]
    int EnumFormatEtc(uint direction, out nint enumerator);
    [PreserveSig]
    int Advise(in DragFormat format, uint flags, nint sink, out uint connection);
    [PreserveSig]
    int Unadvise(uint connection);
    [PreserveSig]
    int EnumAdvise(out nint enumerator);
}

[GeneratedComClass]
internal sealed unsafe partial class OleDragData : IDragData
{
    readonly TransferSnapshot snapshot;
    readonly Dictionary<ushort, string> formats = new();
    internal OleDragData(TransferSnapshot snapshot)
    {
        this.snapshot = snapshot;
        foreach (var format in snapshot.Data.Formats)
        {
            uint native = format switch
            {
                TransferFormats.Text => 13,
                TransferFormats.FileList => 15,
                TransferFormats.HTML => RegisterClipboardFormatW("HTML Format"),
                TransferFormats.PNG => RegisterClipboardFormatW("PNG"),
                _ => RegisterClipboardFormatW(format)
            };
            if (native == 0 || native > ushort.MaxValue)
                throw new IOException("Could not register a drag format.");
            formats[(ushort)native] = format;
        }
    }

    public int QueryGetData(in DragFormat format) => format.Aspect == 1 && format.Index == -1 && (format.Medium & 1) != 0 && formats.ContainsKey(format.Format) ? 0 : unchecked((int)0x80040064);
    public int GetData(in DragFormat format, out DragMedium medium)
    {
        medium = default;
        int status = QueryGetData(format);
        if (status != 0)
            return status;
        try
        {
            string portable = formats[format.Format];
            var bytes = snapshot.Read(portable);
            if (format.Format == 13)
                bytes = Encoding.Unicode.GetBytes(Encoding.UTF8.GetString(bytes) + "\0");
            else if (format.Format == 15)
            {
                var paths = Encoding.UTF8.GetString(bytes).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(s => new Uri(s)).Where(u => u.IsFile && (u.Host.Length == 0 || u.IsLoopback)).Select(u => u.LocalPath);
                var list = Encoding.Unicode.GetBytes(string.Join('\0', paths) + "\0\0");
                bytes = new byte[20 + list.Length];
                BitConverter.GetBytes(20).CopyTo(bytes, 0);
                BitConverter.GetBytes(1).CopyTo(bytes, 16);
                list.CopyTo(bytes, 20);
            }
            else if (portable == TransferFormats.HTML)
                bytes = WindowsClipboardBackend.EncodeHtml(bytes);
            var memory = GlobalAlloc(0x42, (nuint)Math.Max(1, bytes.Length));
            if (memory == 0)
                return unchecked((int)0x8007000e);
            var destination = GlobalLock(memory);
            if (destination == 0)
            {
                GlobalFree(memory);
                return unchecked((int)0x8007000e);
            }

            Marshal.Copy(bytes, 0, destination, bytes.Length);
            GlobalUnlock(memory);
            medium = new()
            {
                Type = 1,
                Handle = memory
            };
            return 0;
        }
        catch (Exception ex)
        {
            return Marshal.GetHRForException(ex);
        }
    }

    public int GetDataHere(in DragFormat format, ref DragMedium medium) => unchecked((int)0x80040069);
    public int GetCanonicalFormatEtc(in DragFormat input, out DragFormat output)
    {
        output = input;
        output.Device = 0;
        return 0x40130;
    }

    public int SetData(in DragFormat format, in DragMedium medium, int release) => unchecked((int)0x80004001);
    public int EnumFormatEtc(uint direction, out nint enumerator)
    {
        enumerator = 0;
        if (direction != 1)
            return unchecked((int)0x80004001);
        try
        {
            var entries = formats.Keys.Select(key => new DragFormat { Format = key, Aspect = 1, Index = -1, Medium = 1 }).ToArray();
            enumerator = (nint)ComInterfaceMarshaller<IEnumDragFormats>.ConvertToUnmanaged(new OleFormatEnumerator(entries));
            return 0;
        }
        catch (Exception ex)
        {
            return Marshal.GetHRForException(ex);
        }
    }

    public int Advise(in DragFormat format, uint flags, nint sink, out uint connection)
    {
        connection = 0;
        return unchecked((int)0x80040003);
    }

    public int Unadvise(uint connection) => unchecked((int)0x80040003);
    public int EnumAdvise(out nint enumerator)
    {
        enumerator = 0;
        return unchecked((int)0x80040003);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint RegisterClipboardFormatW(string name);
    [DllImport("kernel32.dll")]
    static extern nint GlobalAlloc(uint flags, nuint length);
    [DllImport("kernel32.dll")]
    static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")]
    static extern int GlobalUnlock(nint memory);
    [DllImport("kernel32.dll")]
    static extern nint GlobalFree(nint memory);
}

[GeneratedComInterface, Guid("00000103-0000-0000-C000-000000000046")]
internal unsafe partial interface IEnumDragFormats
{
    [PreserveSig]
    int Next(uint count, DragFormat* formats, uint* fetched);
    [PreserveSig]
    int Skip(uint count);
    [PreserveSig]
    int Reset();
    [PreserveSig]
    int Clone(out nint enumerator);
}

[GeneratedComClass]
internal sealed unsafe partial class OleFormatEnumerator(DragFormat[] entries) : IEnumDragFormats
{
    int position;
    public int Next(uint count, DragFormat* formats, uint* fetched)
    {
        if (formats == null || (fetched == null && count != 1))
            return unchecked((int)0x80070057);
        uint copied = 0;
        while (copied < count && position < entries.Length)
            formats[copied++] = entries[position++];
        if (fetched != null)
            *fetched = copied;
        return copied == count ? 0 : 1;
    }

    public int Skip(uint count)
    {
        int skipped = (int)Math.Min(count, (uint)(entries.Length - position));
        position += skipped;
        return skipped == count ? 0 : 1;
    }

    public int Reset()
    {
        position = 0;
        return 0;
    }

    public int Clone(out nint enumerator)
    {
        try
        {
            enumerator = (nint)ComInterfaceMarshaller<IEnumDragFormats>.ConvertToUnmanaged(new OleFormatEnumerator(entries) { position = position });
            return 0;
        }
        catch (Exception ex)
        {
            enumerator = 0;
            return Marshal.GetHRForException(ex);
        }
    }
}
