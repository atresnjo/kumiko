namespace Kumiko.UI;

public sealed partial class Context
{
    int contentRenderDepth;

    // A content renderer gets no handle to the component's container. Reject
    // interactive descendants before they can steal its hit target or focus.
    internal void RenderContent<TItem, TState>(Action<Context, TItem, TState> render, TItem item, TState state)
    {
        var savedFocus = focus;
        var savedFocusNext = focusNext;
        var savedFocusVisible = focusVisible;
        var savedKey = Input.Key;
        contentRenderDepth++;
        try
        {
            render(this, item, state);
        }
        finally
        {
            contentRenderDepth--;
            focus = savedFocus;
            focusNext = savedFocusNext;
            focusVisible = savedFocusVisible;
            Input.Key = savedKey;
        }
    }
}
