using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Kumiko.Platform;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Desktop;

internal interface ISecurityItems
{
    int Copy(nint query, out nint result);
    int Add(nint attributes);
    int Update(nint query, nint attributes);
    int Delete(nint query);
}

internal sealed unsafe class MacCredentialBackend(CredentialScope scope, ISecurityItems? fixture = null) : ICredentialBackend
{
    internal const string Security = "/System/Library/Frameworks/Security.framework/Security";
    const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    static readonly nint library = NativeLibrary.Load(Security);
    readonly ISecurityItems api = fixture ?? new SecurityItems();
    internal static nint Key(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(library, name));
    internal static string Service(CredentialScope scope) => $"Kumiko/v1/{scope.ApplicationId}/{scope.Environment}";

    public CredentialReadResult Execute(CredentialOperation operation, string key, ReadOnlySpan<byte> secret, CancellationToken cancellation)
    {
        using var pool = new AutoreleasePool();
        var query = O.New("NSMutableDictionary");
        var attributes = O.New("NSMutableDictionary");
        nint data = 0;
        try
        {
            Set(query, "kSecClass", Key("kSecClassGenericPassword"));
            Set(query, "kSecAttrService", O.TempString(Service(scope)));
            Set(query, "kSecAttrAccount", O.TempString(key));
            // No authentication UI, including from a worker or an unbundled application.
            Set(query, "kSecUseAuthenticationUI", Key("kSecUseAuthenticationUIFail"));
            cancellation.ThrowIfCancellationRequested();
            if (operation == CredentialOperation.Delete)
                return new(Map(api.Delete(query)));
            if (operation == CredentialOperation.Read)
            {
                Set(query, "kSecReturnData", O.O(O.Class("NSNumber"), "numberWithBool:", (byte)1));
                Set(query, "kSecMatchLimit", Key("kSecMatchLimitOne"));
                int status = api.Copy(query, out var result);
                try
                {
                    if (status != 0)
                        return new(Map(status));
                    if (result == 0 || CFGetTypeID(result) != CFDataGetTypeID())
                        return new(CredentialStatus.Failed);
                    nint length = CFDataGetLength(result);
                    if (length < 0 || length > CredentialStore.MaximumSecretBytes)
                        return new(CredentialStatus.Failed);
                    return new(CredentialStatus.Success, new(new ReadOnlySpan<byte>(CFDataGetBytePtr(result), (int)length)));
                }
                finally
                {
                    if (result != 0)
                        CFRelease(result);
                }
            }
            data = CFDataCreateMutable(0, secret.Length);
            if (data == 0)
                throw new OutOfMemoryException();
            CFDataSetLength(data, secret.Length);
            secret.CopyTo(new Span<byte>(CFDataGetMutableBytePtr(data), secret.Length));
            Set(attributes, "kSecValueData", data);
            int updated = api.Update(query, attributes);
            if (updated != -25300)
                return new(Map(updated));
            cancellation.ThrowIfCancellationRequested();
            Set(query, "kSecValueData", data);
            int added = api.Add(query);
            if (added == -25299)
            {
                // A competing writer inserted after Update. Do not delete/recreate an item.
                cancellation.ThrowIfCancellationRequested();
                O.V(query, "removeObjectForKey:", Key("kSecValueData"));
                added = api.Update(query, attributes);
            }
            return new(Map(added));
        }
        finally
        {
            if (data != 0)
            {
                CryptographicOperations.ZeroMemory(new Span<byte>(CFDataGetMutableBytePtr(data), (int)CFDataGetLength(data)));
                CFRelease(data);
            }
            O.Release(attributes);
            O.Release(query);
        }
    }

    static void Set(nint dictionary, string key, nint value) => O.V(dictionary, "setObject:forKey:", value, Key(key));
    internal static CredentialStatus Map(int status) => status switch
    {
        0 => CredentialStatus.Success,
        -25300 => CredentialStatus.Missing,
        -25308 => CredentialStatus.Locked,
        -25294 or -25295 or -25291 => CredentialStatus.Unavailable,
        -128 => CredentialStatus.Cancelled,
        -25293 or -25243 or -34018 or -25292 => CredentialStatus.Denied,
        _ => CredentialStatus.Failed
    };

    sealed class SecurityItems : ISecurityItems
    {
        public int Copy(nint query, out nint result) => SecItemCopyMatching(query, out result);
        public int Add(nint attributes) => SecItemAdd(attributes, 0);
        public int Update(nint query, nint attributes) => SecItemUpdate(query, attributes);
        public int Delete(nint query) => SecItemDelete(query);
    }

    [DllImport(Security)]
    static extern int SecItemCopyMatching(nint query, out nint result);
    [DllImport(Security)]
    static extern int SecItemAdd(nint attributes, nint result);
    [DllImport(Security)]
    static extern int SecItemUpdate(nint query, nint attributes);
    [DllImport(Security)]
    static extern int SecItemDelete(nint query);
    [DllImport(CF)]
    internal static extern nint CFDataCreate(nint allocator, byte* bytes, nint length);
    [DllImport(CF)]
    static extern nint CFDataCreateMutable(nint allocator, nint capacity);
    [DllImport(CF)]
    static extern void CFDataSetLength(nint data, nint length);
    [DllImport(CF)]
    static extern byte* CFDataGetMutableBytePtr(nint data);
    [DllImport(CF)]
    internal static extern byte* CFDataGetBytePtr(nint data);
    [DllImport(CF)]
    internal static extern nint CFDataGetLength(nint data);
    [DllImport(CF)]
    static extern nuint CFGetTypeID(nint value);
    [DllImport(CF)]
    static extern nuint CFDataGetTypeID();
    [DllImport(CF)]
    internal static extern void CFRelease(nint value);
}
