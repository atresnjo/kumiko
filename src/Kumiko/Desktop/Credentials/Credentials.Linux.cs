using System.Runtime.InteropServices;
using S = Kumiko.Desktop.SecretVariant;

namespace Kumiko.Desktop;

/// <summary>Explicit SecretService proxy (never SecretBackend/file fallback); no Unlock or Prompt calls.</summary>
internal sealed unsafe class LinuxCredentialBackend(CredentialScope scope, Func<nint, ILinuxSecretSession>? fixtureFactory = null) : ICredentialBackend
{
    const string Service = "org.freedesktop.Secret.Service";
    const string Root = "/org/freedesktop/secrets";

    public CredentialReadResult Execute(CredentialOperation operation, string key, ReadOnlySpan<byte> secret, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsLinux() && fixtureFactory is null)
            return new(CredentialStatus.Unsupported);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        nint cancellable = g_cancellable_new();
        using var registration = deadline.Token.Register(() => g_cancellable_cancel(cancellable));
        ILinuxSecretSession? service = null;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            service = fixtureFactory is null ? new LinuxSecretSession(cancellable) : fixtureFactory(cancellable);
            if (service.Algorithms != "dh-ietf1024-sha256-aes128-cbc-pkcs7")
                return new(CredentialStatus.Unsupported);
            var connection = service.Connection;
            using var app = S.String(scope.ApplicationId);
            using var environment = S.String(scope.Environment);
            using var entry = S.String(key);
            using var version = S.String("1");
            using var attributes = S.Dictionary("{ss}", ("kumiko.application", app), ("kumiko.environment", environment), ("kumiko.key", entry), ("kumiko.version", version));
            using var searchArgs = S.Tuple(attributes);
            using var search = Call(connection, Root, Service, "SearchItems", searchArgs, cancellable);
            using var unlocked = search.Child(0);
            using var locked = search.Child(1);
            if (locked.Count != 0)
                return new(CredentialStatus.Locked);
            if (unlocked.Count > 1)
                return new(CredentialStatus.Failed); // Ambiguous data must not return/delete an arbitrary secret.
            string? path = null;
            if (unlocked.Count == 1)
            {
                using var item = unlocked.Child(0);
                path = item.Text;
            }
            if (operation == CredentialOperation.Put)
                return Store(connection, service, attributes, path, secret, cancellable);
            if (path is null)
                return new(CredentialStatus.Missing);
            if (operation == CredentialOperation.Delete)
            {
                using var args = S.Tuple();
                using var deleted = Call(connection, path, "org.freedesktop.Secret.Item", "Delete", args, cancellable);
                return PromptResult(connection, deleted, cancellable);
            }
            // Item.GetSecret preserves IsLocked/NoSuchObject. libsecret's bulk GetSecrets helper can silently omit locked items.
            using var sessionPath = S.Path(service.Path);
            using var readArgs = S.Tuple(sessionPath);
            using var read = Call(connection, path, "org.freedesktop.Secret.Item", "GetSecret", readArgs, cancellable);
            using var encoded = read.Child(0);
            var secretBuffer = service.Decode(encoded);
            return secretBuffer is null ? new(CredentialStatus.Failed) : new(CredentialStatus.Success, secretBuffer);

        }
        catch (SecretBusException ex)
        {
            return new(ex.Status == CredentialStatus.Cancelled && !cancellation.IsCancellationRequested ||
                ex.Status == CredentialStatus.Failed && service is null ? CredentialStatus.Unavailable : ex.Status);
        }
        finally
        {
            service?.Dispose();
            registration.Dispose(); // Stop the callback before freeing GCancellable.
            g_object_unref(cancellable);
        }
    }

    static CredentialReadResult Store(nint connection, ILinuxSecretSession service, S attributes, string? existingPath, ReadOnlySpan<byte> secret, nint cancellation)
    {
        if (existingPath is not null)
        {
            using var encoded = service.Encode(secret);
            using var updateArgs = S.Tuple(encoded);
            using var updated = Call(connection, existingPath, "org.freedesktop.Secret.Item", "SetSecret", updateArgs, cancellation);
            return new(CredentialStatus.Success);
        }
        using var alias = S.String("default");
        using var aliasArgs = S.Tuple(alias);
        using var collection = Call(connection, Root, Service, "ReadAlias", aliasArgs, cancellation);
        using var collectionPath = collection.Child(0);
        if (collectionPath.Text == "/")
            return new(CredentialStatus.Unavailable); // Never create a collection or prompt.
        using var iface = S.String("org.freedesktop.Secret.Collection");
        using var property = S.String("Locked");
        using var lockedArgs = S.Tuple(iface, property);
        using var state = Call(connection, collectionPath.Text, "org.freedesktop.DBus.Properties", "Get", lockedArgs, cancellation);
        using var boxed = state.Child(0);
        using var locked = boxed.Unbox();
        if (locked.Boolean)
            return new(CredentialStatus.Locked);
        using var encrypted = service.Encode(secret);
        using var label = S.String("Kumiko application credential");
        using var labelBox = S.Box(label);
        using var attributesBox = S.Box(attributes);
        using var properties = S.Dictionary("{sv}", ("org.freedesktop.Secret.Item.Label", labelBox), ("org.freedesktop.Secret.Item.Attributes", attributesBox));
        using var replace = S.Bool(true);
        using var args = S.Tuple(properties, encrypted, replace);
        using var result = Call(connection, collectionPath.Text, "org.freedesktop.Secret.Collection", "CreateItem", args, cancellation);
        using var prompt = result.Child(1);
        return DismissPrompt(connection, prompt.Text, cancellation);
    }

    static CredentialReadResult PromptResult(nint connection, S result, nint cancellation)
    {
        using var prompt = result.Child(0);
        return DismissPrompt(connection, prompt.Text, cancellation);
    }

    static CredentialReadResult DismissPrompt(nint connection, string path, nint cancellation)
    {
        if (path == "/")
            return new(CredentialStatus.Success);
        // A race may lock the store after the preflight. Never show the returned prompt.
        using var args = S.Tuple();
        using var dismissed = Call(connection, path, "org.freedesktop.Secret.Prompt", "Dismiss", args, cancellation);
        return new(CredentialStatus.Denied);
    }

    static S Call(nint connection, string path, string iface, string method, S args, nint cancellation)
    {
        var result = g_dbus_connection_call_sync(connection, "org.freedesktop.secrets", path, iface, method, args.Handle, 0, 0, 10000, cancellation, out var error);
        SecretBusException.Throw(error);
        return S.Adopt(result);
    }

    [DllImport(S.Gio)]
    static extern nint g_cancellable_new();
    [DllImport(S.Gio)]
    static extern void g_cancellable_cancel(nint cancellation);
    [DllImport(S.GObject)]
    static extern void g_object_unref(nint value);
    [DllImport(S.Gio)]
    static extern nint g_dbus_connection_call_sync(nint connection, [MarshalAs(UnmanagedType.LPUTF8Str)] string bus, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string iface, [MarshalAs(UnmanagedType.LPUTF8Str)] string method, nint parameters, nint replyType, int flags, int timeout, nint cancellation, out nint error);
}
