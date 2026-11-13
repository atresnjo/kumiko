using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Kumiko.Desktop;

/// <summary>Crash-released OS ownership shared by CoreCLR and NativeAOT processes.</summary>
internal static class InstanceOwnership
{
    internal static IDisposable? Acquire(string name)
    {
        if (OperatingSystem.IsWindows())
        {
            var mutex = new Mutex(false, "Kumiko.Instance." + name);
            bool acquired;
            try
            {
                acquired = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }
            if (acquired)
                return new MutexOwnership(mutex);
            mutex.Dispose();
            return null;
        }

        // Never unlink this file on release: another contender may already have
        // its inode open. A stable inode prevents two simultaneous lock owners.
        string path = Path.Combine(Path.GetTempPath(), "kumiko-instance-" + name + ".lock");
        if (!File.Exists(path))
        {
            try
            {
                using var created = new FileStream(path, new FileStreamOptions
                {
                    Mode = FileMode.OpenOrCreate,
                    Access = FileAccess.ReadWrite,
                    Share = FileShare.ReadWrite | FileShare.Delete,
                    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                });
            }
            catch (IOException creationError) when ((creationError.HResult & 0xffff) == 32)
            {
                // Another launch created and locked the same inode first.
            }
        }
        // open without O_CREAT needs no variadic mode argument. This keeps the
        // call ABI correct on Apple arm64 as well as x64 and Linux.
        int flags = 2 | (OperatingSystem.IsMacOS() ? 0x1000000 : 0x80000);
        int descriptor = open(path, flags);
        if (descriptor < 0)
            throw new IOException($"Cannot open single-instance lock: errno {Marshal.GetLastPInvokeError()}.");
        var handle = new SafeFileHandle((nint)descriptor, ownsHandle: true);
        if (flock(descriptor, 2 | 4) == 0)
            return handle;
        int error = Marshal.GetLastPInvokeError();
        handle.Dispose();
        if (error is 11 or 35)
            return null;
        throw new IOException($"Cannot acquire single-instance file lock: errno {error}.");
    }

    sealed class MutexOwnership(Mutex mutex) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                mutex.ReleaseMutex();
            }
            finally
            {
                mutex.Dispose();
            }
        }
    }

    [DllImport("libc", SetLastError = true)]
    static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
    [DllImport("libc", SetLastError = true)]
    static extern int flock(int descriptor, int operation);
}
