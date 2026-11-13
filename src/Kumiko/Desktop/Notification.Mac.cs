using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Kumiko.Platform;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Desktop;

internal sealed unsafe class MacNotificationBackend : INotificationBackend
{
    static readonly ConcurrentDictionary<nint, MacNotificationBackend> delegates = new();
    readonly App app;
    readonly Action<NotificationClick> click;
    readonly nint center, target, previous;
    readonly Dictionary<string, NotificationClick> identities = new();
    readonly HashSet<TaskCompletionSource> pending = new();
    readonly Dictionary<string, long> versions = new();
    readonly object responseGate = new();
    readonly List<nint> responses = new();
    long nextVersion;
    bool disposed, cleared;
    internal MacNotificationBackend(App app, Action<NotificationClick> click, nint fixtureCenter = 0)
    {
        this.app = app;
        this.click = click;
        using var pool = new AutoreleasePool();
        if (fixtureCenter == 0)
        {
            var bundle = O.O(O.Class("NSBundle"), "mainBundle");
            if (O.O(bundle, "bundleIdentifier") == 0 || !O.Text(O.O(bundle, "bundlePath")).EndsWith(".app", StringComparison.Ordinal))
                throw new PlatformNotSupportedException("macOS notifications require a packaged .app with a CFBundleIdentifier. Unbundled dotnet run is unsupported; see PACKAGING.md.");
        }
        NativeLibrary.Load("/System/Library/Frameworks/UserNotifications.framework/UserNotifications");
        var shared = fixtureCenter != 0 ? fixtureCenter : O.O(O.Class("UNUserNotificationCenter"), "currentNotificationCenter");
        if (delegates.Values.Any(owner => owner.center == shared))
            throw new InvalidOperationException("macOS supports one live Notification owner per process. Reuse the existing owner for multiple notification IDs and groups.");
        center = O.O(shared, "retain");
        previous = O.O(O.O(center, "delegate"), "retain");
        var cls = O.Define("KumikoNotificationDelegate", "NSObject",
            ("userNotificationCenter:didReceiveNotificationResponse:withCompletionHandler:", (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, void>)&Responded, "v@:@@@"),
            ("userNotificationCenter:willPresentNotification:withCompletionHandler:", (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, void>)&Present, "v@:@@@"));
        O.class_addProtocol(cls, O.objc_getProtocol("UNUserNotificationCenterDelegate"));
        target = O.O(cls, "new");
        delegates[target] = this;
        O.V(center, "setDelegate:", target);
    }

    static string Key(string id, string group) => group.Length + ":" + group + id;
    static bool Decode(string key, out NotificationClick notification)
    {
        notification = new("", "");
        int colon = key.IndexOf(':');
        if (colon < 1 || !int.TryParse(key.AsSpan(0, colon), out int length) || length < 0 || length >= key.Length - colon - 1)
            return false;
        notification = new(key[(colon + 1 + length)..], key.Substring(colon + 1, length));
        return true;
    }
    public Task Show(NotificationOptions options)
    {
        app.CheckThread();
        string key = Key(options.Id, options.Group);
        long version = ++nextVersion;
        versions[key] = version;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pending.Add(completion);
        using var permission = NativeBlock.Permission((granted, error) =>
        {
            try
            {
                app.Dispatch(() =>
                {
                    if (disposed || versions.GetValueOrDefault(key) != version)
                    {
                        pending.Remove(completion);
                        completion.TrySetCanceled();
                        return;
                    }

                    if (!granted || error is not null)
                    {
                        pending.Remove(completion);
                        completion.TrySetException(new InvalidOperationException(error ?? "Notification authorization was denied."));
                        return;
                    }

                    Send(options, version, completion);
                });
            }
            catch (ObjectDisposedException)
            {
                completion.TrySetCanceled();
            }
        });
        O.V(center, "requestAuthorizationWithOptions:completionHandler:", (nuint)6, permission.Handle);
        return completion.Task;
    }

    void Send(NotificationOptions options, long version, TaskCompletionSource completion)
    {
        using var pool = new AutoreleasePool();
        var content = O.New("UNMutableNotificationContent");
        try
        {
            O.V(content, "setTitle:", O.TempString(options.Title));
            O.V(content, "setBody:", O.TempString(options.Body));
            O.V(content, "setSubtitle:", O.TempString(options.Subtitle));
            O.V(content, "setThreadIdentifier:", O.TempString(options.Group));
            string id = Key(options.Id, options.Group);
            identities[id] = new(options.Id, options.Group);
            var request = O.O(O.Class("UNNotificationRequest"), "requestWithIdentifier:content:trigger:", O.TempString(id), content, 0);
            using var block = NativeBlock.Completion(error =>
            {
                try
                {
                    app.Dispatch(() =>
                    {
                        pending.Remove(completion);
                        if (disposed || versions.GetValueOrDefault(id) != version)
                        {
                            if (!disposed && !identities.ContainsKey(id))
                                RemoveRequests(id);
                            completion.TrySetCanceled();
                        }
                        else if (error is not null)
                            completion.TrySetException(new IOException(error));
                        else
                            completion.TrySetResult();
                    });
                }
                catch (ObjectDisposedException)
                {
                    completion.TrySetCanceled();
                }
            });
            O.V(center, "addNotificationRequest:withCompletionHandler:", request, block.Handle);
        }
        finally
        {
            O.Release(content);
        }
    }

    [UnmanagedCallersOnly]
    static void Present(nint self, nint cmd, nint center, nint notification, nint completion)
    {
        // UNNotificationPresentationOptionList | UNNotificationPresentationOptionBanner.
        NativeBlock.Complete(completion, delegates.ContainsKey(self) ? 24u : 0u);
    }

    [UnmanagedCallersOnly]
    static void Responded(nint self, nint cmd, nint center, nint response, nint completion)
    {
        try
        {
            if (!delegates.TryGetValue(self, out var owner))
                return;
            lock (owner.responseGate)
            {
                if (owner.disposed)
                    return;
                var retained = O.O(response, "retain");
                owner.responses.Add(retained);
                try
                {
                    owner.app.Dispatch(() =>
                    {
                        lock (owner.responseGate)
                            if (!owner.responses.Remove(retained))
                                return;
                        try
                        {
                            if (owner.disposed)
                                return;
                            var request = O.O(O.O(retained, "notification"), "request");
                            var id = O.Text(O.O(request, "identifier"));
                            if (owner.identities.TryGetValue(id, out var notification) ||
                                (!owner.cleared && !owner.versions.ContainsKey(id) && Decode(id, out notification)))
                                owner.click(notification);
                        }
                        finally
                        {
                            O.Release(retained);
                        }
                    });
                }
                catch
                {
                    owner.responses.Remove(retained);
                    O.Release(retained);
                    throw;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }
        finally
        {
            NativeBlock.Complete(completion);
        }
    }

    public void Close(string id, string group)
    {
        using var pool = new AutoreleasePool();
        string key = Key(id, group);
        versions[key] = ++nextVersion;
        identities.Remove(key);
        RemoveRequests(key);
    }

    void RemoveRequests(string key)
    {
        using var pool = new AutoreleasePool();
        var ids = O.O(O.Class("NSArray"), "arrayWithObject:", O.TempString(key));
        O.V(center, "removePendingNotificationRequestsWithIdentifiers:", ids);
        O.V(center, "removeDeliveredNotificationsWithIdentifiers:", ids);
    }

    public void Clear()
    {
        cleared = true;
        foreach (string key in versions.Keys.ToArray())
            versions[key] = ++nextVersion;
        foreach (var identity in identities.Values.ToArray())
            Close(identity.Id, identity.Group);
        O.V(center, "removeAllPendingNotificationRequests");
        O.V(center, "removeAllDeliveredNotifications");
    }

    public void Dispose()
    {
        lock (responseGate)
        {
            disposed = true;
            foreach (var response in responses)
                O.Release(response);
            responses.Clear();
        }
        foreach (var completion in pending)
            completion.TrySetCanceled();
        pending.Clear();
        if (O.O(center, "delegate") == target)
            O.V(center, "setDelegate:", previous);
        delegates.TryRemove(target, out _);
        O.Release(target);
        O.Release(previous);
        O.Release(center);
    }
}
