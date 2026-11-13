using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Kumiko.Platform;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Web;

public sealed partial class Page
{
    sealed record CallScope(Page Page, long Generation);
    sealed class CallLease(CallScope? previous) : IDisposable
    {
        public void Dispose() => currentCall.Value = previous;
    }
    static readonly AsyncLocal<CallScope?> currentCall = new();
    readonly HashSet<string> retiredDocuments = new(StringComparer.Ordinal);
    string? documentIdentity, documentRoot, retainedRoot, recoveryTicket;
    long nativeNavigationKey;
    bool navigating;
    IDisposable EnterCall(long generation)
    {
        var previous = currentCall.Value;
        currentCall.Value = new(this, generation);
        return new CallLease(previous);
    }
    internal long EventGeneration => currentCall.Value is { } scope && scope.Page == this ? scope.Generation : Volatile.Read(ref epoch);
    internal void EnqueueEvent(string json, long generation)
    {
        if (generation == epoch)
            Enqueue(json, true);
    }
    bool SameDocumentTarget(string target, out bool unchanged)
    {
        unchanged = false;
        if ((navigating ? retainedRoot : documentIdentity) is null || !target.Contains('#'))
            return false;
        string source = CurrentDocumentUrl();
        // The retained DOM can change history during a held load; query its live committed URL.
        // A real provisional start still retires the generation if the engine creates another document.
        var destination = new Uri(target);
        if (!Uri.TryCreate(source, UriKind.Absolute, out var current) ||
            Uri.Compare(current, destination, UriComponents.SchemeAndServer | UriComponents.PathAndQuery,
                UriFormat.UriEscaped, StringComparison.Ordinal) != 0)
            return false;
        unchanged = Uri.Compare(current, destination, UriComponents.AbsoluteUri, UriFormat.UriEscaped, StringComparison.Ordinal) == 0;
        return true;
    }
    string CurrentDocumentUrl()
    {
        if (adapter is not null)
            return adapter.Url;
        using var pool = new AutoreleasePool();
        // The history item remains the committed document during a provisional load.
        var item = O.O(O.O(webview, "backForwardList"), "currentItem");
        var url = item == 0 ? O.O(webview, "URL") : O.O(item, "URL");
        return O.Text(O.O(url, "absoluteString"));
    }
    void StopRetainedNavigation()
    {
        // Fence abandoned native callbacks before stopping; recovery is an explicit host transition.
        nativeNavigationKey = 0;
        recoveryTicket = null;
        if (adapter is not null)
            adapter.Stop();
        else
        {
            using var pool = new AutoreleasePool();
            O.V(webview, "stopLoading");
        }
    }
    internal void NativeNavigationStarted(long key = 0)
    {
        window.App.CheckThread();
        if (disposed)
            return;
        nativeNavigationKey = key;
        recoveryTicket = null;
        if (navigating)
            return;
        navigating = true;
        retainedRoot = documentRoot;
        IsReady = false;
        if (documentIdentity is { } identity)
            retiredDocuments.Add(identity);
        documentIdentity = null;
        CancelStreams();
        outbox.Clear();
        held.Clear();
        scheduled = false;
    }
    internal void NativeNavigationFailed(long? key = null) => RecoverRetainedDocument(key);
    void RecoverRetainedDocument(long? key = null)
    {
        window.App.CheckThread();
        if (disposed || !navigating || (key is not null && key != nativeNavigationKey) || retainedRoot is null)
            return;
        // Target the retained DOM's immutable root, never an error page/newer document.
        recoveryTicket = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        Evaluate("window.__kumiko?.renew(" + JsonSerializer.Serialize(retainedRoot, WebJson.Default.String) + "," +
            JsonSerializer.Serialize(recoveryTicket, WebJson.Default.String) + ")");
    }
    static bool BridgeIdentity(string? identity) => identity is { Length: 32 } && identity.All(Uri.IsHexDigit);
    void AcceptDocument(string? identity, string? root, string? recovery)
    {
        if (!BridgeIdentity(identity) || !BridgeIdentity(root) || retiredDocuments.Contains(identity!))
            return;
        if (recovery is not null && (recovery != recoveryTicket || root != retainedRoot || !navigating))
            return;
        if (documentIdentity == identity)
            return;
        // A restored back/forward-cache document also starts a fresh generation.
        if (!navigating)
            NativeNavigationStarted();
        documentIdentity = identity;
        documentRoot = root;
        retainedRoot = null;
        recoveryTicket = null;
        navigating = false;
    }
    [UnmanagedCallersOnly]
    static void NavigationFailed(nint self, nint cmd, nint view, nint navigation, nint error)
    {
        if (!handlers.TryGetValue(self, out var page))
            return;
        try
        {
            page.NativeNavigationFailed((long)navigation);
        }
        catch (Exception failure)
        {
            page.window.App.Report(failure);
        }
    }
    [UnmanagedCallersOnly]
    static void NavigationStarted(nint self, nint cmd, nint view, nint navigation)
    {
        if (!handlers.TryGetValue(self, out var page))
            return;
        try
        {
            page.NativeNavigationStarted((long)navigation);
        }
        catch (Exception error)
        {
            page.window.App.Report(error);
        }
    }
}
