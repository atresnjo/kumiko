using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DiskMap.Core;

/// <summary>
/// Reads a folder with getattrlistbulk: one system call returns name, type, modification time, file ID, link
/// count, length and allocated size for a whole batch of entries, instead of a readdir plus an lstat per entry.
/// Nothing is followed: links report themselves and mount points are recognised by their device.
/// </summary>
[SupportedOSPlatform("macos")]
static unsafe partial class MacDirectory
{
    const uint ReturnedAttrs = 0x80000000;
    const uint Name = 0x00000001;
    const uint ObjType = 0x00000008;
    const uint ModTime = 0x00000400;
    const uint FileId = 0x02000000;
    const uint Error = 0x20000000;
    const uint LinkCount = 0x00000001;
    const uint TotalSize = 0x00000002;
    const uint AllocSize = 0x00000004;
    const int ReadOnly = 0;
    const int NoFollow = 0x100;
    const int Directory = 0x100000;
    const int CloseOnExec = 0x1000000;
    const uint Folder = 2;
    const uint SymbolicLink = 5;
    const int BufferSize = 256 * 1024;

    [StructLayout(LayoutKind.Sequential)]
    struct AttrList
    {
        public ushort BitmapCount;
        public ushort Reserved;
        public uint Common;
        public uint Volume;
        public uint Folder;
        public uint File;
        public uint Fork;
    }

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "close")]
    private static partial int Close(int descriptor);

    [LibraryImport("libc", EntryPoint = "fstat", SetLastError = true)]
    private static partial int FStat(int descriptor, byte* buffer);

    [LibraryImport("libc", EntryPoint = "getattrlistbulk", SetLastError = true)]
    private static partial int GetAttrListBulk(int descriptor, AttrList* list, byte* buffer, nuint size, ulong options);

    [ThreadStatic]
    static byte[]? buffer;

    /// <summary>The device a path lives on, or -1 when it cannot be opened.</summary>
    public static long Device(string path)
    {
        int descriptor = Open(path, ReadOnly | Directory | CloseOnExec);
        if (descriptor < 0)
            return -1;
        try
        {
            return DeviceOf(descriptor);
        }
        finally
        {
            Close(descriptor);
        }
    }

    static long DeviceOf(int descriptor)
    {
        // struct stat starts with a 32-bit dev_t in both inode layouts; the buffer covers the whole structure.
        byte* stat = stackalloc byte[256];
        return FStat(descriptor, stat) == 0 ? *(int*)stat : -1;
    }

    /// <summary>
    /// Lists one folder. Returns false when it cannot be opened or read; a folder on another device than
    /// <paramref name="devices"/> allows sets <paramref name="foreign"/> and is not read.
    /// </summary>
    public static bool Read(string path, IReadOnlySet<long> devices, Entries entries, out bool foreign)
    {
        foreign = false;
        int descriptor = Open(path, ReadOnly | Directory | NoFollow | CloseOnExec);
        if (descriptor < 0)
            return false;
        try
        {
            if (!devices.Contains(DeviceOf(descriptor)))
            {
                foreign = true;
                return true;
            }
            var list = new AttrList
            {
                BitmapCount = 5,
                Common = ReturnedAttrs | Name | ObjType | ModTime | FileId | Error,
                File = LinkCount | TotalSize | AllocSize
            };
            buffer ??= GC.AllocateUninitializedArray<byte>(BufferSize, pinned: true);
            fixed (byte* start = buffer)
            {
                while (true)
                {
                    int count = GetAttrListBulk(descriptor, &list, start, BufferSize, 0);
                    if (count < 0)
                        return false;
                    if (count == 0)
                        return true;
                    byte* entry = start;
                    for (int i = 0; i < count; i++)
                    {
                        uint length = *(uint*)entry;
                        Parse(entry, entries);
                        entry += length;
                    }
                }
            }
        }
        finally
        {
            Close(descriptor);
        }
    }

    static void Parse(byte* entry, Entries entries)
    {
        byte* at = entry + 4;
        uint common = *(uint*)at;
        uint file = *(uint*)(at + 12);
        at += 20;
        if ((common & Error) != 0)
        {
            if (*(uint*)at != 0)
            {
                entries.Errors++;
                return;
            }
            at += 4;
        }
        string name = "";
        if ((common & Name) != 0)
        {
            int offset = *(int*)at;
            int bytes = (int)*(uint*)(at + 4);
            // Names are at most 255 UTF-8 bytes; decode on the stack and share one string per distinct name.
            Span<char> chars = stackalloc char[512];
            int length = System.Text.Encoding.UTF8.GetChars(new ReadOnlySpan<byte>(at + offset, Math.Clamp(bytes - 1, 0, 1020)), chars);
            name = entries.Names.Get(chars[..length]);
            at += 8;
        }
        uint type = 0;
        if ((common & ObjType) != 0)
        {
            type = *(uint*)at;
            at += 4;
        }
        long modified = 0;
        if ((common & ModTime) != 0)
        {
            modified = Unaligned<long>(at);
            at += 16;
        }
        ulong id = 0;
        if ((common & FileId) != 0)
        {
            id = Unaligned<ulong>(at);
            at += 8;
        }
        uint links = 1;
        if ((file & LinkCount) != 0)
        {
            links = *(uint*)at;
            at += 4;
        }
        long apparent = 0;
        if ((file & TotalSize) != 0)
        {
            apparent = Unaligned<long>(at);
            at += 8;
        }
        long allocated = apparent;
        if ((file & AllocSize) != 0)
            allocated = Unaligned<long>(at);
        if (name.Length == 0)
            return;
        if (type == Folder)
            entries.Folders.Add(new(name, modified));
        else
            entries.Add(name, type == SymbolicLink ? ItemKind.Link : ItemKind.File, allocated, apparent, modified, links > 1 ? id : 0);
    }

    static T Unaligned<T>(byte* at) where T : unmanaged => System.Runtime.CompilerServices.Unsafe.ReadUnaligned<T>(at);
}
