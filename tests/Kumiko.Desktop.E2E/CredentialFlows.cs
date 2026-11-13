using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Kumiko;
using Kumiko.Desktop;
using Kumiko.Platform;

static unsafe class CredentialFlows
{
    internal static void Run(string output, Action<bool, string> check)
    {
        Directory.CreateDirectory(output);
        using var stream = File.Create(Path.Combine(output, "credentials.json"));
        using var json = new Utf8JsonWriter(stream, new()
        {
            Indented = true
        });
        json.WriteStartObject();
        json.WriteString("platform", RuntimeInformation.OSDescription);
        json.WriteString("evidence", "Public async service and real Foundation/Security constants across a private SecItem-call fixture (SecItem P/Invoke and daemon not executed); no credential store contacted");
        json.WriteStartArray("checks");
        void Verify(bool condition, string description)
        {
            check(condition, description);
            json.WriteStringValue(description);
            json.Flush();
        }
        using var app = new App(new("Native services", QuitOnLastWindow: false, Hidden: true));
        bool rejected = false;
        try
        {
            using var forbidden = new CredentialStore(app, new("dev.kumiko.fixture", "isolated"));
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        Verify(rejected, "hidden App rejects implicit credential storage");
        if (OperatingSystem.IsMacOS())
            Mac(app, Verify);
        else
            json.WriteStringValue("macOS Security-call fixture unavailable on this host; native Windows/Linux storage not executed");
        json.WriteEndArray();
        json.WriteString("notExecuted", "Real login/data-protection Keychain; Credential Manager; Secret Service daemon; OS authorization prompts");
        json.WriteString("status", "passed");
        json.WriteEndObject();
    }

    static void Mac(App app, Action<bool, string> verify)
    {
        using var api = new PrivateItems();
        var scope = new CredentialScope("dev.kumiko.credentials", "test");
        using var store = new CredentialStore(app, scope, new MacCredentialBackend(scope, api));
        var otherScope = scope with
        {
            Environment = "development"
        };
        using var other = new CredentialStore(app, otherScope, new MacCredentialBackend(otherScope, api));
        var otherAppScope = scope with
        {
            ApplicationId = "dev.kumiko.other"
        };
        using var otherApp = new CredentialStore(app, otherAppScope, new MacCredentialBackend(otherAppScope, api));
        using (var missing = store.ReadAsync("refresh").GetAwaiter().GetResult())
            verify(missing.Status == CredentialStatus.Missing && missing.Secret is null, "missing entry is distinct from a readable token");
        var bytes = RandomNumberGenerator.GetBytes(64);
        var replacement = RandomNumberGenerator.GetBytes(31);
        bytes[0] = 0;
        bytes[1] = 255;
        try
        {
            verify(store.PutAsync("refresh", bytes).GetAwaiter().GetResult() == CredentialStatus.Success, "store an opaque binary token");
            verify(store.PutAsync("api", replacement).GetAwaiter().GetResult() == CredentialStatus.Success, "store an independent sibling key");
            using (var read = store.ReadAsync("refresh").GetAwaiter().GetResult())
                verify(read.Status == CredentialStatus.Success && read.Secret!.Bytes.SequenceEqual(bytes), "binary token round-trips through native CFData without UTF8 conversion");
            using (var absent = other.ReadAsync("refresh").GetAwaiter().GetResult())
                verify(absent.Status == CredentialStatus.Missing, "development namespace cannot read the test token");
            using (var absent = otherApp.ReadAsync("refresh").GetAwaiter().GetResult())
                verify(absent.Status == CredentialStatus.Missing, "another application cannot read the token");
            verify(other.PutAsync("refresh", bytes).GetAwaiter().GetResult() == CredentialStatus.Success, "another namespace can own the same key");
            verify(store.PutAsync("refresh", replacement).GetAwaiter().GetResult() == CredentialStatus.Success, "replace an existing token without delete/recreate");
            using (var read = store.ReadAsync("refresh").GetAwaiter().GetResult())
                verify(read.Secret!.Bytes.SequenceEqual(replacement), "read sees the replacement");
            verify(store.DeleteAsync("refresh").GetAwaiter().GetResult() == CredentialStatus.Success, "delete the selected entry");
            using (var read = store.ReadAsync("api").GetAwaiter().GetResult())
                verify(read.Secret!.Bytes.SequenceEqual(replacement), "deletion preserves a sibling token");
            using (var read = other.ReadAsync("refresh").GetAwaiter().GetResult())
                verify(read.Secret!.Bytes.SequenceEqual(bytes), "deletion preserves another namespace");
            verify(store.DeleteAsync("refresh").GetAwaiter().GetResult() == CredentialStatus.Missing, "deleting an absent entry reports Missing");
            verify(store.PutAsync("empty", []).GetAwaiter().GetResult() == CredentialStatus.Success, "store an empty binary value");
            var empty = store.ReadAsync("empty").GetAwaiter().GetResult();
            verify(empty.Status == CredentialStatus.Success && empty.Secret!.Bytes.IsEmpty, "empty value is distinct from missing");
            var retained = empty.Secret!;
            empty.Dispose();
            bool disposed = false;
            try
            {
                _ = retained.Bytes.Length;
            }
            catch (ObjectDisposedException)
            {
                disposed = true;
            }
            verify(disposed && retained.ToString() == "[secret]", "disposed secret rejects access and string conversion is redacted");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            verify(store.PutAsync("cancelled", bytes, cancelled.Token).GetAwaiter().GetResult() == CredentialStatus.Cancelled, "pre-cancelled write does not commit");
            using (var absent = store.ReadAsync("cancelled").GetAwaiter().GetResult())
                verify(absent.Status == CredentialStatus.Missing, "cancelled write left no entry");
            bool invalid = false;
            try
            {
                _ = store.ReadAsync("../production/token");
            }
            catch (ArgumentException)
            {
                invalid = true;
            }
            verify(invalid, "keys cannot escape their namespace with path separators");
            bool wrongThread = Task.Run(() =>
            {
                try
                {
                    _ = store.ReadAsync("api");
                    return false;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            }).GetAwaiter().GetResult();
            verify(wrongThread, "credential service enforces the App owner thread at its public boundary");
            OwnershipAndCancellation(store, api, verify);
            Failures(store, api, verify);
            Disposal(app, scope, api, bytes, verify);
            verify(api.NoUI && api.WorkerOnly, "native requests forbid authentication UI and leave the App thread unblocked");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            CryptographicOperations.ZeroMemory(replacement);
        }
    }

    static void OwnershipAndCancellation(CredentialStore store, PrivateItems api, Action<bool, string> verify)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var input = RandomNumberGenerator.GetBytes(48);
        var expected = input.ToArray();
        api.Entered = entered;
        api.Release = release;
        try
        {
            var write = store.PutAsync("copied", input);
            verify(entered.Wait(TimeSpan.FromSeconds(5)), "queued write reaches the native boundary before caller buffer reuse");
            CryptographicOperations.ZeroMemory(input);
            release.Set();
            verify(write.GetAwaiter().GetResult() == CredentialStatus.Success, "caller can clear its input after PutAsync returns");
            api.Entered = null;
            api.Release = null;
            using (var read = store.ReadAsync("copied").GetAwaiter().GetResult())
                verify(read.Secret!.Bytes.SequenceEqual(expected), "clearing the caller buffer does not corrupt the stored credential");
            entered.Reset();
            release.Reset();
            api.Entered = entered;
            api.Release = release;
            api.PauseRead = true;
            using var cancellation = new CancellationTokenSource();
            var pending = store.ReadAsync("copied", cancellation.Token);
            verify(entered.Wait(TimeSpan.FromSeconds(5)), "cancellable read reaches the native boundary");
            cancellation.Cancel();
            release.Set();
            using var cancelled = pending.GetAwaiter().GetResult();
            verify(cancelled.Status == CredentialStatus.Cancelled && cancelled.Secret is null, "late read after cancellation exposes no secret buffer");
        }
        finally
        {
            release.Set();
            api.PauseRead = false;
            api.Entered = null;
            api.Release = null;
            CryptographicOperations.ZeroMemory(input);
            CryptographicOperations.ZeroMemory(expected);
        }
    }

    static void Failures(CredentialStore store, PrivateItems api, Action<bool, string> verify)
    {
        api.Failure = -25308;
        using (var read = store.ReadAsync("api").GetAwaiter().GetResult())
            verify(read.Status == CredentialStatus.Locked && read.Secret is null, "locked store preserves the signed-in credential as inaccessible rather than missing");
        api.Failure = -25293;
        using (var read = store.ReadAsync("api").GetAwaiter().GetResult())
            verify(read.Status == CredentialStatus.Denied && read.Secret is null, "access denial cannot be confused with a missing token");
        api.Failure = -25291;
        using (var read = store.ReadAsync("api").GetAwaiter().GetResult())
            verify(read.Status == CredentialStatus.Unavailable && read.Secret is null, "unavailable native store produces no token");
        api.Failure = -128;
        verify(store.DeleteAsync("api").GetAwaiter().GetResult() == CredentialStatus.Cancelled, "native cancellation is reported without pretending to delete");
        api.Failure = -50;
        using (var read = store.ReadAsync("api").GetAwaiter().GetResult())
            verify(read.Status == CredentialStatus.Failed, "unexpected native failure is explicit and sanitized");
        api.Failure = 0;
    }

    static void Disposal(App app, CredentialScope scope, PrivateItems api, byte[] bytes, Action<bool, string> verify)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var store = new CredentialStore(app, scope, new MacCredentialBackend(scope, api));
        verify(store.PutAsync("committed", bytes).GetAwaiter().GetResult() == CredentialStatus.Success, "prepare an existing token for in-flight replacement");
        api.Entered = entered;
        api.Release = release;
        var pending = store.PutAsync("committed", bytes);
        verify(entered.Wait(TimeSpan.FromSeconds(5)), "write reaches the private native boundary");
        store.Dispose();
        release.Set();
        verify(pending.GetAwaiter().GetResult() == CredentialStatus.Success, "disposal during a committed native write does not falsely report cancellation");
        bool rejected = false;
        try
        {
            _ = store.ReadAsync("committed");
        }
        catch (ObjectDisposedException)
        {
            rejected = true;
        }
        verify(rejected, "disposed service rejects new work");
        api.Entered = null;
        api.Release = null;
    }

    sealed class PrivateItems : ISecurityItems, IDisposable
    {
        readonly Dictionary<(string Service, string Account), byte[]> items = new();
        readonly int owner = Environment.CurrentManagedThreadId;
        internal int Failure;
        internal bool PauseRead;
        internal bool NoUI = true;
        internal bool WorkerOnly = true;
        internal ManualResetEventSlim? Entered;
        internal ManualResetEventSlim? Release;
        (string, string) Identity(nint query)
        {
            WorkerOnly &= Environment.CurrentManagedThreadId != owner;
            NoUI &= ObjC.O(query, "objectForKey:", MacCredentialBackend.Key("kSecUseAuthenticationUI")) == MacCredentialBackend.Key("kSecUseAuthenticationUIFail");
            return (ObjC.Text(ObjC.O(query, "objectForKey:", MacCredentialBackend.Key("kSecAttrService"))),
                ObjC.Text(ObjC.O(query, "objectForKey:", MacCredentialBackend.Key("kSecAttrAccount"))));
        }
        static byte[] Bytes(nint attributes)
        {
            var data = ObjC.O(attributes, "objectForKey:", MacCredentialBackend.Key("kSecValueData"));
            return new ReadOnlySpan<byte>(MacCredentialBackend.CFDataGetBytePtr(data), (int)MacCredentialBackend.CFDataGetLength(data)).ToArray();
        }
        public int Copy(nint query, out nint result)
        {
            var key = Identity(query);
            result = 0;
            if (PauseRead)
            {
                Entered?.Set();
                Release?.Wait(TimeSpan.FromSeconds(10));
            }
            if (Failure != 0)
                return Failure;
            if (!items.TryGetValue(key, out var bytes))
                return -25300;
            fixed (byte* pointer = bytes)
                result = MacCredentialBackend.CFDataCreate(0, pointer, bytes.Length);
            return 0;
        }
        public int Add(nint attributes)
        {
            var key = Identity(attributes);
            if (items.ContainsKey(key))
                return -25299;
            items.Add(key, Bytes(attributes));
            return 0;
        }
        public int Update(nint query, nint attributes)
        {
            var key = Identity(query);
            Entered?.Set();
            Release?.Wait(TimeSpan.FromSeconds(10));
            if (Failure != 0)
                return Failure;
            if (!items.Remove(key, out var old))
                return -25300;
            CryptographicOperations.ZeroMemory(old);
            items.Add(key, Bytes(attributes));
            return 0;
        }
        public int Delete(nint query)
        {
            var key = Identity(query);
            if (Failure != 0)
                return Failure;
            if (!items.Remove(key, out var bytes))
                return -25300;
            CryptographicOperations.ZeroMemory(bytes);
            return 0;
        }
        public void Dispose()
        {
            foreach (var bytes in items.Values)
                CryptographicOperations.ZeroMemory(bytes);
            items.Clear();
        }
    }
}
