using Kumiko.Desktop;

namespace Kumiko.UI;

public sealed partial class Context
{
    internal (TransferData Data, Rect Bounds)? PendingNativeDrag
    {
        get; private set;
    }

    bool nativeDragIssued;
    internal void DragTransfer(int index, TransferData data, string? label)
    {
        if (Input.Pressed)
            nativeDragIssued = false;
        DragSource(index, new("kumiko/transfer", ""), label);
        ref var node = ref Tree[index];
        if (dragStarted && !dragEnded && active == node.Id && !nativeDragIssued)
        {
            nativeDragIssued = true;
            PendingNativeDrag = (data, rects.GetValueOrDefault(node.Id));
        }
    }

    internal (TransferData Data, Rect Bounds)? TakeNativeDrag()
    {
        var request = PendingNativeDrag;
        PendingNativeDrag = null;
        if (request is not null)
        {
            dragPayload = null;
            dragStarted = false;
            Input.Down = false;
        }

        return request;
    }
}

public readonly partial struct Div
{
    /// <summary>Offers serialized items to native drag receivers. Call Control too. Tester retains the offer without starting a system drag.</summary>
    public Div DragSource(TransferData data, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        context.DragTransfer(index, data, label);
        return this;
    }
}
