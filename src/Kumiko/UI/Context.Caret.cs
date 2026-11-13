namespace Kumiko.UI;

public sealed partial class Context
{
    /// <summary>Logical coordinates of the focused editor's caret for native IME placement.</summary>
    public Rect? CaretRectangle
    {
        get
        {
            CheckThread();
            if (!Edits.TryGetValue(focus, out var edit) || edit.Buffer is not { } buffer)
                return null;
            int index = Array.FindIndex(Tree, 0, Count, node => node.Id == focus);
            if (index < 0)
                return null;
            var node = Tree[index];
            if (node.TextCaret is not { } caret)
                return null;
            return new Rect(node.Rect.X + caret.X, node.Rect.Y + caret.Y, caret.Width, caret.Height).Intersect(node.ClipRect);
        }
    }
}
