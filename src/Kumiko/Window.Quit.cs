namespace Kumiko;

public sealed partial class Window
{
    internal Action<Action<bool>>? QuitCloseRequested
    {
        get; set;
    }

    internal bool ApprovesQuitClose()
    {
        if (disposed)
            return true;
        if (requestingClose)
            return false;
        requestingClose = true;
        try
        {
            var closing = new CancelableEvent { IsApplicationQuit = true };
            if (Closing is { } handlers)
                foreach (Action<CancelableEvent> handler in handlers.GetInvocationList())
                {
                    if (disposed)
                        break;
                    handler(closing);
                }
            return !closing.DefaultPrevented;
        }
        finally
        {
            requestingClose = false;
        }
    }
}
