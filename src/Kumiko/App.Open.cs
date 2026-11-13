namespace Kumiko;

public sealed record SecondInstanceEvent(IReadOnlyList<string> Arguments, string WorkingDirectory);
public sealed partial class App
{
    readonly Queue<string> pendingFiles = new(), pendingUrls = new();
    Action<string>? openFile, openUrl;
    readonly HashSet<(string Value, bool Url)> launchDeliveries = new();
    Action<SecondInstanceEvent>? secondInstance;
    readonly Queue<SecondInstanceEvent> pendingInstances = new();
    Desktop.SingleInstance? instanceLock;
    /// <summary>Launch and subsequent document requests. Pending requests drain on the first subscription.</summary>
    public event Action<string> OpenFile
    {
        add
        {
            CheckThread();
            openFile += value;
            Drain(pendingFiles, () => openFile);
        }

        remove
        {
            CheckThread();
            openFile -= value;
        }
    }

    public event Action<string> OpenURL
    {
        add
        {
            CheckThread();
            openUrl += value;
            Drain(pendingUrls, () => openUrl);
        }

        remove
        {
            CheckThread();
            openUrl -= value;
        }
    }

    public event Action<SecondInstanceEvent> SecondInstance
    {
        add
        {
            CheckThread();
            secondInstance += value;
            while (pendingInstances.TryDequeue(out var request))
                secondInstance?.Invoke(request);
        }

        remove
        {
            CheckThread();
            secondInstance -= value;
        }
    }

    static void Drain(Queue<string> pending, Func<Action<string>?> handler)
    {
        while (handler() is { } callback && pending.TryDequeue(out var entry))
            callback(entry);
    }

    internal void DeliverOpen(string value, bool url)
    {
        CheckThread();
        var callback = url ? openUrl : openFile;
        if (callback is null)
            (url ? pendingUrls : pendingFiles).Enqueue(value);
        else
            callback(value);
    }

    internal void DeliverLaunchOpen(string value, bool url)
    {
        if (launchDeliveries.Add((value, url)))
            DeliverOpen(value, url);
    }

    internal void ParseOpenArguments(IEnumerable<string> arguments, string directory, bool launch = false)
    {
        void Deliver(string value, bool url)
        {
            if (launch)
                DeliverLaunchOpen(value, url);
            else
                DeliverOpen(value, url);
        }
        bool operands = false;
        foreach (var argument in arguments)
        {
            if (argument == "--")
            {
                operands = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(argument) || (!operands && argument.StartsWith('-')))
                continue;
            if (!Path.IsPathRooted(argument) && Uri.TryCreate(argument, UriKind.Absolute, out var uri))
            {
                if (uri.IsFile && (uri.Host.Length == 0 || uri.IsLoopback))
                    Deliver(uri.LocalPath, false);
                else if (!uri.IsFile)
                    Deliver(argument, true);
            }
            else
                Deliver(Path.GetFullPath(argument, directory), false);
        }
    }

    internal void DeliverSecondInstance(SecondInstanceEvent request)
    {
        CheckThread();
        if (secondInstance is { } callback)
            callback(request);
        else
            pendingInstances.Enqueue(request);
        ParseOpenArguments(request.Arguments, request.WorkingDirectory);
    }

    public bool RequestSingleInstanceLock(string? identity = null)
    {
        CheckThread();
        if (instanceLock is not null)
            return true;
        instanceLock = Desktop.SingleInstance.Acquire(this, identity ?? Options.Name);
        return instanceLock is not null;
    }

    public void ReleaseSingleInstanceLock()
    {
        CheckThread();
        instanceLock?.Dispose();
        instanceLock = null;
    }
}
