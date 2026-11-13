using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Kumiko.Desktop;

internal sealed unsafe class WindowsCredentialBackend(CredentialScope scope) : ICredentialBackend
{
    public CredentialReadResult Execute(CredentialOperation operation, string key, ReadOnlySpan<byte> secret, CancellationToken cancellation)
    {
        var target = $"Kumiko/v1/{scope.ApplicationId}/{scope.Environment}/{key}";
        cancellation.ThrowIfCancellationRequested();
        if (operation == CredentialOperation.Delete)
            return new(CredDeleteW(target, 1, 0) != 0 ? CredentialStatus.Success : Error());
        if (operation == CredentialOperation.Put)
        {
            fixed (byte* bytes = secret)
            fixed (char* name = target)
            {
                var credential = new Credential
                {
                    Type = 1,
                    Target = (nint)name,
                    BlobSize = (uint)secret.Length,
                    Blob = (nint)bytes,
                    Persist = 2 // Local machine, current user's credential set; never enterprise-roamed.
                };
                return new(CredWriteW(ref credential, 0) != 0 ? CredentialStatus.Success : Error());
            }
        }
        if (CredReadW(target, 1, 0, out var result) == 0)
            return new(Error());
        try
        {
            var credential = (Credential*)result;
            if (credential->BlobSize > CredentialStore.MaximumSecretBytes || (credential->BlobSize != 0 && credential->Blob == 0))
                return new(CredentialStatus.Failed);
            return new(CredentialStatus.Success, new(new ReadOnlySpan<byte>((void*)credential->Blob, (int)credential->BlobSize)));
        }
        finally
        {
            var credential = (Credential*)result;
            if (credential->Blob != 0 && credential->BlobSize <= CredentialStore.MaximumSecretBytes)
                CryptographicOperations.ZeroMemory(new Span<byte>((void*)credential->Blob, (int)credential->BlobSize));
            CredFree(result);
        }
    }

    static CredentialStatus Error() => Marshal.GetLastPInvokeError() switch
    {
        1168 => CredentialStatus.Missing,
        5 => CredentialStatus.Denied,
        1312 or 1058 => CredentialStatus.Unavailable,
        1223 => CredentialStatus.Cancelled,
        50 or 120 => CredentialStatus.Unsupported,
        _ => CredentialStatus.Failed
    };

    [StructLayout(LayoutKind.Sequential)]
    struct Credential
    {
        public uint Flags;
        public uint Type;
        public nint Target;
        public nint Comment;
        public long LastWritten;
        public uint BlobSize;
        public nint Blob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public nint Alias;
        public nint UserName;
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int CredWriteW(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int CredReadW(string target, uint type, uint flags, out nint result);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int CredDeleteW(string target, uint type, uint flags);
    [DllImport("advapi32.dll")]
    static extern void CredFree(nint buffer);
}
