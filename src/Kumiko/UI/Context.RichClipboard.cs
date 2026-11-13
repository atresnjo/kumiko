using Kumiko.Desktop;
using Kumiko.RichText;

namespace Kumiko.UI;

public sealed partial class Context
{
    TransferData? richClipboard;
    Func<TransferData>? readRichClipboard;
    Action<TransferData>? writeRichClipboard;
    /// <summary>Optional native rich reader. Tester retains a private transfer offer instead.</summary>
    public Func<TransferData>? ReadRichClipboard
    {
        get => ReadUI(readRichClipboard);
        set
        {
            CheckThread();
            readRichClipboard = value;
        }
    }
    public Action<TransferData>? WriteRichClipboard
    {
        get => ReadUI(writeRichClipboard);
        set
        {
            CheckThread();
            writeRichClipboard = value;
        }
    }
    internal void CopyRich(RichValue value)
    {
        Clipboard = value.PlainText;
        richClipboard = RichExchange.Transfer(value);
        if (WriteRichClipboard is { } write)
            write(richClipboard);
        else
            WriteClipboard?.Invoke(value.PlainText);
    }
    internal RichValue PasteRich(bool plain)
    {
        if (plain)
            return RichValue.Text(ReadClipboard?.Invoke() ?? Clipboard);
        if (ReadRichClipboard is { } read)
            return RichExchange.FromTransfer(read());
        return richClipboard is { } data ? RichExchange.FromTransfer(data) : RichValue.Text(ReadClipboard?.Invoke() ?? Clipboard);
    }
}
