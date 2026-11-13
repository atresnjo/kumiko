using System.Security.Cryptography;

namespace Kumiko.Desktop;

/// <summary>Native outcomes; Missing is never used for access or backend failures.</summary>
public enum CredentialStatus
{
    Success,
    Missing,
    Locked,
    Denied,
    Unavailable,
    Unsupported,
    Cancelled,
    Failed
}

/// <summary>Stable reverse-domain application identity and an explicit environment (for example production or development).</summary>
public sealed record CredentialScope(string ApplicationId, string Environment);

/// <summary>A read result owns its secret. Dispose it after use; never put its bytes in model state or session logs.</summary>
public sealed class CredentialReadResult : IDisposable
{
    public CredentialStatus Status
    {
        get;
    }
    public SecretBuffer? Secret
    {
        get;
    }
    internal CredentialReadResult(CredentialStatus status, SecretBuffer? secret = null)
    {
        Status = status;
        Secret = secret;
    }
    public void Dispose() => Secret?.Dispose();
    public override string ToString() => Status.ToString();
}

internal enum CredentialOperation
{
    Read, Put, Delete
}
internal interface ICredentialBackend
{
    CredentialReadResult Execute(CredentialOperation operation, string key, ReadOnlySpan<byte> secret, CancellationToken cancellation);
}

/// <summary>
/// Application/environment-scoped native credentials. Start calls on the App thread; native storage runs on workers.
/// Put replaces an existing value. No prompts or plaintext fallback. Dispose cancels queued work, not committed writes.
/// </summary>
public sealed class CredentialStore : IDisposable
{
    public const int MaximumSecretBytes = 2560;
    readonly App app;
    readonly ICredentialBackend backend;
    readonly CancellationTokenSource lifetime = new();
    readonly object gate = new();
    bool disposed;
    int pending;
    public CredentialScope Scope
    {
        get;
    }

    public CredentialStore(App app, CredentialScope scope) : this(app, scope, null)
    {
    }

    internal CredentialStore(App app, CredentialScope scope, ICredentialBackend? backend)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(scope);
        app.CheckThread();
        Validate(scope.ApplicationId, nameof(scope.ApplicationId), 128);
        Validate(scope.Environment, nameof(scope.Environment), 64);
        if (backend is null && app.Options.Hidden)
            throw new InvalidOperationException("Hidden apps require an isolated credential backend.");
        this.app = app;
        Scope = scope;
        this.backend = backend ?? (OperatingSystem.IsMacOS() ? new MacCredentialBackend(scope) :
            OperatingSystem.IsWindows() ? new WindowsCredentialBackend(scope) : new LinuxCredentialBackend(scope));
        app.Disposing += Dispose;
    }

    internal static void Validate(string value, string name, int maximum)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (value.Length > maximum || value.Any(c => !(char.IsAsciiDigit(c) || c is >= 'a' and <= 'z') && c is not '.' and not '-' and not '_'))
            throw new ArgumentException("Use bounded lowercase ASCII letters, digits, dots, hyphens or underscores.", name);
    }

    public Task<CredentialReadResult> ReadAsync(string key, CancellationToken cancellation = default) => Start(CredentialOperation.Read, key, [], cancellation);

    /// <summary>Copies bytes before returning. The caller remains responsible for clearing its input.</summary>
    public Task<CredentialStatus> PutAsync(string key, ReadOnlySpan<byte> secret, CancellationToken cancellation = default)
    {
        if (secret.Length > MaximumSecretBytes)
            throw new ArgumentOutOfRangeException(nameof(secret));
        return Status(Start(CredentialOperation.Put, key, secret, cancellation));
    }

    public Task<CredentialStatus> DeleteAsync(string key, CancellationToken cancellation = default) => Status(Start(CredentialOperation.Delete, key, [], cancellation));

    static async Task<CredentialStatus> Status(Task<CredentialReadResult> task)
    {
        using var result = await task.ConfigureAwait(false);
        return result.Status;
    }

    Task<CredentialReadResult> Start(CredentialOperation operation, string key, ReadOnlySpan<byte> secret, CancellationToken cancellation)
    {
        app.CheckThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        Validate(key, nameof(key), 128);
        var input = secret.ToArray();
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
        lock (gate)
            pending++;
        return Task.Run(() =>
        {
            try
            {
                if (linked.IsCancellationRequested)
                    return new CredentialReadResult(CredentialStatus.Cancelled);
                var result = backend.Execute(operation, key, input, linked.Token);
                // Never suppress a committed mutation. Reads may be safely discarded after cancellation.
                if (operation == CredentialOperation.Read && linked.IsCancellationRequested)
                {
                    result.Dispose();
                    return new CredentialReadResult(CredentialStatus.Cancelled);
                }
                return result;
            }
            catch (OperationCanceledException)
            {
                return new CredentialReadResult(CredentialStatus.Cancelled);
            }
            catch (DllNotFoundException)
            {
                return new CredentialReadResult(CredentialStatus.Unavailable);
            }
            catch (EntryPointNotFoundException)
            {
                return new CredentialReadResult(CredentialStatus.Unsupported);
            }
            catch (PlatformNotSupportedException)
            {
                return new CredentialReadResult(CredentialStatus.Unsupported);
            }
            catch (InvalidOperationException)
            {
                return new CredentialReadResult(CredentialStatus.Failed);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(input);
                linked.Dispose();
                lock (gate)
                    if (--pending == 0 && disposed)
                        lifetime.Dispose();
            }
        });
    }

    public void Dispose()
    {
        app.CheckThread();
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            app.Disposing -= Dispose;
            lifetime.Cancel();
            if (pending == 0)
                lifetime.Dispose();
        }
    }
}
