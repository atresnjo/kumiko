using System.Runtime.CompilerServices;
using Kumiko.UI;

namespace Kumiko.iOS;

/// <summary>The system font's native design, scaled by Dynamic Type.</summary>
public enum NativeFontDesign
{
    Default,
    Serif,
    Rounded,
    Monospaced
}

public static partial class NativeComponents
{
    /// <summary>Adds native text with a system font design, explicit styling and Dynamic Type scaling.</summary>
    public static void NativeLabel(this Context context, string text, TextStyle style, NativeFontDesign design,
        NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0)
    {
        if (!Enum.IsDefined(design))
            throw new ArgumentOutOfRangeException(nameof(design));
        NativeLabel(context, text, style, key, source, line);
        context.Tree[context.Count - 1].NativeContent = new NativeLabelContent(text, style, design);
    }
}
