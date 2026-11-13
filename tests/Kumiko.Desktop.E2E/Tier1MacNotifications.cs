using System.Runtime.InteropServices;
using Kumiko;
using Kumiko.Desktop;
using Kumiko.Platform;

static partial class Tier1Checks
{
    static void MacNotificationFlows(App app, Action<bool, string> check)
    {
        using var center = new NotificationCenterFixture();
        using var service = new Notification(app, callback => new MacNotificationBackend(app, callback, center.Handle));
        int clicked = 0, clickThread = 0;
        var shown = service.Show(new("native", "Native title", "Native body", "Native subtitle", "native group"), () =>
        {
            clicked++;
            clickThread = Environment.CurrentManagedThreadId;
        });
        center.Grant();
        Pump(app, () => shown.IsCompleted, "fixture notification completion");
        shown.GetAwaiter().GetResult();
        var content = ObjC.O(center.Request, "content");
        check(ObjC.Text(ObjC.O(content, "title")) == "Native title" && ObjC.Text(ObjC.O(content, "body")) == "Native body" &&
            ObjC.Text(ObjC.O(content, "subtitle")) == "Native subtitle" && ObjC.Text(ObjC.O(content, "threadIdentifier")) == "native group",
            "native notification requests preserve title, body, subtitle and grouping through copied authorization blocks");
        check(center.Present() == 24, "native foreground notification delivery completes with banner and list presentation options");
        center.Click(fromWorker: true);
        Pump(app, () => clicked == 1, "fixture native notification click");
        check(center.ClickCompletions == 1 && clickThread == Environment.CurrentManagedThreadId, "native notification response dispatches worker delivery to the main thread and completes its callback");
        var replaced = service.Show(new("native", "Replacement", Group: "native group"), () => clicked += 10);
        center.Grant();
        Pump(app, () => replaced.IsCompleted, "replacement native notification");
        replaced.GetAwaiter().GetResult();
        center.Click();
        Pump(app, () => clicked == 11, "replacement native notification click");
        check(center.Additions == 2 && ObjC.Text(ObjC.O(ObjC.O(center.Request, "content"), "title")) == "Replacement",
            "native notification replacement preserves request identity while updating content and click action");
        service.Close("native", "native group");
        center.Click();
        app.RunFor(TimeSpan.FromMilliseconds(25));
        check(clicked == 11 && center.Removals == 2, "native close removes delivered/pending requests and rejects stale responses");
        center.DelayAdd = true;
        var closing = service.Show(new("closing", "Pending add"));
        center.Grant();
        Pump(app, () => center.Additions == 3, "native delayed add fixture");
        service.Close("closing");
        center.CompleteAdds();
        Pump(app, () => closing.IsCompleted, "obsolete add completion");
        check(closing.IsCanceled && center.Removals == 6, "late native add completions remove requests that were closed while being scheduled");
        var late = service.Show(new("late", "Must never display"));
        service.ClearNotifications();
        center.Grant();
        Pump(app, () => late.IsCompleted, "canceled native authorization");
        check(late.IsCanceled && center.Additions == 3, "clearing native notifications invalidates authorization callbacks before they can enqueue requests");
        center.DelayAdd = false;
        var persistent = service.Show(new("persistent", "Survives its owner", Group: "history"));
        center.Grant();
        Pump(app, () => persistent.IsCompleted, "persistent notification fixture");
        persistent.GetAwaiter().GetResult();
        center.Click(fromWorker: true);
        service.Dispose();
        app.RunFor(TimeSpan.FromMilliseconds(25));
        check(center.Delegate == 0 && clicked == 11, "native notification disposal releases queued responses and restores the previous weak delegate");
        using var reopened = new Notification(app, callback => new MacNotificationBackend(app, callback, center.Handle));
        NotificationClick? received = null;
        reopened.Clicked += value => received = value;
        center.Click(fromWorker: true);
        Pump(app, () => received is not null, "notification from a previous owner");
        check(received == new NotificationClick("persistent", "history"), "a new native notification owner recovers stable ID/group clicks from previously delivered requests");
    }

    sealed unsafe class NotificationCenterFixture : IDisposable
    {
        static readonly Dictionary<nint, NotificationCenterFixture> centers = new();
        static readonly Dictionary<nint, nint> links = new();
        static int clickCompletions;
        static nuint presentation;
        readonly Queue<nint> permissions = new();
        readonly Queue<nint> additions = new();
        readonly List<nint> ownedLinks = new();
        internal nint Handle
        {
            get;
        }
        internal nint Delegate, Request;
        internal int Additions, Removals;
        internal bool DelayAdd;
        internal int ClickCompletions => clickCompletions;

        internal NotificationCenterFixture()
        {
            clickCompletions = 0;
            var cls = ObjC.Define("KumikoNotificationCenterFixture", "NSObject",
                ("delegate", (nint)(delegate* unmanaged<nint, nint, nint>)&GetDelegate, "@@:"),
                ("setDelegate:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&SetDelegate, "v@:@"),
                ("requestAuthorizationWithOptions:completionHandler:", (nint)(delegate* unmanaged<nint, nint, nuint, nint, void>)&Authorize, "v@:Q@?"),
                ("addNotificationRequest:withCompletionHandler:", (nint)(delegate* unmanaged<nint, nint, nint, nint, void>)&Add, "v@:@@?"),
                ("removePendingNotificationRequestsWithIdentifiers:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&Remove, "v@:@"),
                ("removeDeliveredNotificationsWithIdentifiers:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&Remove, "v@:@"),
                ("removeAllPendingNotificationRequests", (nint)(delegate* unmanaged<nint, nint, void>)&RemoveAll, "v@:"),
                ("removeAllDeliveredNotifications", (nint)(delegate* unmanaged<nint, nint, void>)&RemoveAll, "v@:"));
            Handle = ObjC.O(cls, "new");
            centers.Add(Handle, this);
        }

        [UnmanagedCallersOnly]
        static nint GetDelegate(nint self, nint cmd) => centers[self].Delegate;
        [UnmanagedCallersOnly]
        static void SetDelegate(nint self, nint cmd, nint value) => centers[self].Delegate = value;
        [UnmanagedCallersOnly]
        static void Authorize(nint self, nint cmd, nuint options, nint block) => centers[self].permissions.Enqueue(BlockCopy(block));
        [UnmanagedCallersOnly]
        static void Add(nint self, nint cmd, nint request, nint block)
        {
            var fixture = centers[self];
            ObjC.Release(fixture.Request);
            fixture.Request = ObjC.O(request, "retain");
            fixture.Additions++;
            if (fixture.DelayAdd)
                fixture.additions.Enqueue(BlockCopy(block));
            else
                ((delegate* unmanaged<nint, nint, void>)Marshal.ReadIntPtr(block, 16))(block, 0);
        }

        [UnmanagedCallersOnly]
        static void Remove(nint self, nint cmd, nint ids) => centers[self].Removals++;
        [UnmanagedCallersOnly]
        static void RemoveAll(nint self, nint cmd) => centers[self].Removals++;

        internal void Grant()
        {
            while (permissions.TryDequeue(out var block))
            {
                ((delegate* unmanaged<nint, byte, nint, void>)Marshal.ReadIntPtr(block, 16))(block, 1, 0);
                BlockRelease(block);
            }
        }

        internal void CompleteAdds()
        {
            while (additions.TryDequeue(out var block))
            {
                ((delegate* unmanaged<nint, nint, void>)Marshal.ReadIntPtr(block, 16))(block, 0);
                BlockRelease(block);
            }
        }

        nint Link(nint value)
        {
            var cls = ObjC.Define("KumikoNotificationLinkFixture", "NSObject",
                ("notification", (nint)(delegate* unmanaged<nint, nint, nint>)&GetLink, "@@:"),
                ("request", (nint)(delegate* unmanaged<nint, nint, nint>)&GetLink, "@@:"));
            var link = ObjC.O(cls, "new");
            links.Add(link, value);
            ownedLinks.Add(link);
            return link;
        }

        [UnmanagedCallersOnly]
        static nint GetLink(nint self, nint cmd) => links[self];
        [UnmanagedCallersOnly]
        static void ClickCompleted(nint block) => clickCompletions++;
        [UnmanagedCallersOnly]
        static void Presented(nint block, nuint options) => presentation = options;

        internal nuint Present()
        {
            nint* block = stackalloc nint[5];
            block[2] = (nint)(delegate* unmanaged<nint, nuint, void>)&Presented;
            presentation = 0;
            ObjC.V(Delegate, "userNotificationCenter:willPresentNotification:withCompletionHandler:", Handle, (nint)0, (nint)block);
            return presentation;
        }

        internal void Click(bool fromWorker = false)
        {
            // The production response callback invokes this no-argument completion synchronously.
            nint* block = stackalloc nint[5];
            block[2] = (nint)(delegate* unmanaged<nint, void>)&ClickCompleted;
            var selector = ObjC.Sel("userNotificationCenter:didReceiveNotificationResponse:withCompletionHandler:");
            var response = Link(Link(Request));
            var target = Delegate;
            var completion = (nint)block;
            var callback = ObjC.O(target, "methodForSelector:", selector);
            void Invoke() => ((delegate* unmanaged<nint, nint, nint, nint, nint, void>)callback)(target, selector, Handle, response, completion);
            if (fromWorker)
                Task.Run(Invoke).GetAwaiter().GetResult();
            else
                Invoke();
        }

        public void Dispose()
        {
            foreach (var block in permissions)
                BlockRelease(block);
            foreach (var block in additions)
                BlockRelease(block);
            foreach (var link in ownedLinks)
            {
                links.Remove(link);
                ObjC.Release(link);
            }
            centers.Remove(Handle);
            ObjC.Release(Request);
            ObjC.Release(Handle);
        }
    }
}
