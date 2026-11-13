using Kumiko.Rendering;

namespace Kumiko.UI;

public sealed partial class Context
{
    readonly TextSystem textSystem;
    public TextSystem TextSystem => ReadUI(textSystem);
    /// <summary>Logical window size used by layout.</summary>
    Size sizeValue = new(800, 600);
    public Size Size
    {
        get => ReadUI(sizeValue);
        internal set => sizeValue = value;
    }
    /// <summary>Device pixel scale used when rendering and measuring glyphs.</summary>
    float scaleValue = 1;
    public float Scale
    {
        get => ReadUI(scaleValue);
        internal set => scaleValue = value;
    }
    /// <summary>Theme colors, spacing, radius and type scales for this frame.</summary>
    public Theme Theme
    {
        get => ReadUI(theme);
        set
        {
            CheckThread();
            ArgumentNullException.ThrowIfNull(value);
            theme = value;
            TextSystem.Family = value.Font;
            TextSystem.MonoFamily = value.MonoFont;
        }
    }
    /// <summary>Input consumed by the next frame. Tester supplies isolated input.</summary>
    Input input = new();
    public Input Input
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            return input;
        }
        private set => input = value;
    }
    /// <summary>The latest completed tree, in parent-before-child order. Read on the owning thread.</summary>
    public ReadOnlySpan<Node> Nodes
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            return Tree.AsSpan(0, Count);
        }
    }
    /// <summary>Paint instances from the latest completed frame.</summary>
    public IReadOnlyList<Instance> Instances => ReadUI<IReadOnlyList<Instance>>(instances);
    /// <summary>Bytes the current and previous paint lists keep reserved between frames.</summary>
    internal long RetainedInstanceBytes => (long)(instances.Capacity + previousInstances.Capacity) * System.Runtime.CompilerServices.Unsafe.SizeOf<Instance>();
    /// <summary>Build, layout and paint statistics from the latest frame.</summary>
    FrameStats statsValue;
    public FrameStats Stats
    {
        get => ReadUI(statsValue);
        private set => statsValue = value;
    }
    /// <summary>
    /// Set when an input-driven state change requires rebuilding already-created content in the same frame.
    /// </summary>
    bool changedValue;
    public bool Changed
    {
        get => ReadUI(changedValue);
        internal set => changedValue = value;
    }
    /// <summary>True while node animation needs another frame.</summary>
    bool animatingValue;
    public bool Animating
    {
        get => ReadUI(animatingValue);
        private set => animatingValue = value;
    }
    /// <summary>Makes animated values reach their targets immediately.</summary>
    bool reduceMotionValue;
    public bool ReduceMotion
    {
        get => ReadUI(reduceMotionValue);
        set
        {
            CheckThread();
            reduceMotionValue = value;
        }
    }
    /// <summary>Whether the focused control is a text editor.</summary>
    /// <summary>True while IME marked text or a composition commit owns keyboard input.</summary>
    public bool IsComposing => ReadUI(NavigationCompositionActive);

    public bool IsEditing => ReadUI(Edits.ContainsKey(focus));

    /// <summary>Whether the focused text editor owns this buffer.</summary>
    public bool IsEditingBuffer(TextBuffer buffer) => Edits.TryGetValue(focus, out var edit) && ReferenceEquals(edit.Buffer, buffer);
    /// <summary>Requests pointer-move delivery for custom interactions.</summary>
    bool wantsMovesValue;
    public bool WantsMoves
    {
        get => ReadUI(wantsMovesValue);
        set
        {
            CheckThread();
            wantsMovesValue = value;
        }
    }
    /// <summary>Pointer cursor requested by this frame.</summary>
    Cursor cursorValue;
    public Cursor Cursor
    {
        get => ReadUI(cursorValue);
        set
        {
            CheckThread();
            cursorValue = value;
        }
    }
    /// <summary>
    /// Height of the draggable custom title bar; interactive descendants receive input normally.
    /// </summary>
    float titleBarHeightValue;
    public float TitleBarHeight
    {
        get => ReadUI(titleBarHeightValue);
        set
        {
            CheckThread();
            titleBarHeightValue = value;
        }
    }

    internal bool IsTitleBarBackground(Point point) => point.Y < TitleBarHeight && !hits.Any(h => h.Interactive && h.Rect.Contains(point));
    /// <summary>Accumulated frame time in seconds.</summary>
    double timeValue;
    public double Time
    {
        get => ReadUI(timeValue);
        internal set => timeValue = value;
    }
    /// <summary>Monotonically increasing frame identifier.</summary>
    public long FrameId => ReadUI(frame);
    /// <summary>Seconds used by the current animation step.</summary>
    float deltaTimeValue = 1 / 60f;
    public float DeltaTime
    {
        get => ReadUI(deltaTimeValue);
        private set => deltaTimeValue = value;
    }
    /// <summary>Earliest requested future invalidation, or null for an idle native event loop.</summary>
    TimeSpan? nextFrameValue;
    public TimeSpan? NextFrame
    {
        get => ReadUI(nextFrameValue);
        private set => nextFrameValue = value;
    }
    /// <summary>Isolated clipboard value unless explicit native clipboard callbacks are configured.</summary>
    string clipboardValue = "";
    public string Clipboard
    {
        get => ReadUI(clipboardValue);
        set
        {
            CheckThread();
            clipboardValue = value;
            richClipboard = null;
        }
    }
    /// <summary>Optional native clipboard reader; Tester never installs one.</summary>
    Func<string>? readClipboardValue;
    public Func<string>? ReadClipboard
    {
        get => ReadUI(readClipboardValue);
        set
        {
            CheckThread();
            readClipboardValue = value;
        }
    }
    /// <summary>Optional native clipboard writer; Tester never installs one.</summary>
    Action<string>? writeClipboardValue;
    public Action<string>? WriteClipboard
    {
        get => ReadUI(writeClipboardValue);
        set
        {
            CheckThread();
            writeClipboardValue = value;
        }
    }
    /// <summary>Identity of the currently focused control, or zero.</summary>
    public ulong Focus => ReadUI(focus);

    static T ReadUI<T>(T value)
    {
        Runtime.KumikoExecution.CheckUI();
        return value;
    }

}
