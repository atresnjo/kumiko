namespace Kumiko.UI;

public enum Key
{
    None,
    Enter,
    Space,
    Tab,
    Escape,
    Backspace,
    Delete,
    Left,
    Right,
    Up,
    Down,
    Home,
    End,
    A,
    C,
    X,
    V,
    Z,
    Y,
    P,
    F,
    I,
    Q,
    D1,
    D2,
    D3,
    D4,
    D5,
    D6,
    D7,
    D8,
    D9,
    PageUp,
    PageDown,
    F2,
    F10,
    B,
    G,
    K,
    L,
    O,
    R,
    S,
    T,
    W,
    N,
    D0,
    Backslash,
    BracketLeft,
    BracketRight,
    D,
    U,
    E,
    H,
    J,
    M,
    Slash,
    Comma,
    Period,
    Minus,
    Equal,
    Semicolon,
    Quote,
    Backquote,
    F1,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,
    F9,
    F11,
    F12
}

[Flags]
public enum Modifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
    Meta = 8,
    Primary = 16
}

public enum PointerButton
{
    Primary,
    Secondary,
    Middle
}

public sealed class Input
{
    NavigationDirection navigationValue;
    public NavigationDirection Navigation
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            return navigationValue;
        }
        set
        {
            Runtime.KumikoExecution.CheckUI();
            navigationValue = value;
        }
    }
    Point pointerValue;
    public Point Pointer
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            return pointerValue;
        }
        set
        {
            Runtime.KumikoExecution.CheckUI();
            pointerValue = value;
        }
    }
    bool downValue;
    public bool Down
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            return downValue;
        }
        set
        {
            Runtime.KumikoExecution.CheckUI();
            downValue = value;
        }
    }
    bool pressedValue;
    public bool Pressed
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            return pressedValue;
        }
        set
        {
            Runtime.KumikoExecution.CheckUI();
            pressedValue = value;
        }
    }
    bool releasedValue;
    public bool Released
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            return releasedValue;
        }
        set
        {
            Runtime.KumikoExecution.CheckUI();
            releasedValue = value;
        }
    }
    PointerButton buttonValue;
    public PointerButton Button
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            return buttonValue;
        }
        set
        {
            Runtime.KumikoExecution.CheckUI();
            buttonValue = value;
        }
    }
    int clickCountValue = 1;
    public int ClickCount
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            return clickCountValue;
        }
        set
        {
            Runtime.KumikoExecution.CheckUI();
            clickCountValue = value;
        }
    }
    float scrollYValue;
    public float ScrollY
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            return scrollYValue;
        }
        set
        {
            Runtime.KumikoExecution.CheckUI();
            scrollYValue = value;
        }
    }
    bool scrollPreciseValue = true;
    /// <summary>
    /// True for pixel deltas (trackpads, which already carry momentum); false for wheel notches, which glide.
    /// </summary>
    internal bool ScrollPrecise
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            return scrollPreciseValue;
        }
        set
        {
            Runtime.KumikoExecution.CheckUI();
            scrollPreciseValue = value;
        }
    }
    float scrollXValue;
    public float ScrollX
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            return scrollXValue;
        }
        set
        {
            Runtime.KumikoExecution.CheckUI();
            scrollXValue = value;
        }
    }
    Key keyValue;
    public Key Key
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            return keyValue;
        }
        set
        {
            Runtime.KumikoExecution.CheckUI();
            keyValue = value;
        }
    }
    Modifiers modifiersValue;
    public Modifiers Modifiers
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            return modifiersValue;
        }
        set
        {
            Runtime.KumikoExecution.CheckUI();
            modifiersValue = value;
        }
    }
    Modifiers heldModifiersValue;
    /// <summary>
    /// Modifier keys held right now. Unlike <see cref="Modifiers"/> it persists across frames and changes when a
    /// modifier alone is pressed or released, so hold-to-switch interactions can commit on release.
    /// </summary>
    public Modifiers HeldModifiers
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            return heldModifiersValue;
        }
        set
        {
            Runtime.KumikoExecution.CheckUI();
            heldModifiersValue = value;
        }
    }
    char characterValue;
    /// <summary>
    /// The character the pressed key produced with the active keyboard layout, Shift and Option included,
    /// when no text input owns the keystroke; otherwise '\0'. Match it with <see cref="Context.Shortcut(char, Modifiers)"/>.
    /// </summary>
    public char Character
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            return characterValue;
        }
        set
        {
            Runtime.KumikoExecution.CheckUI();
            characterValue = value;
        }
    }
    string textValue = "";
    public string Text
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            return textValue;
        }
        set
        {
            Runtime.KumikoExecution.CheckUI();
            textValue = value;
        }
    }
    string? compositionValue;
    public string? Composition
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            return compositionValue;
        }
        set
        {
            Runtime.KumikoExecution.CheckUI();
            compositionValue = value;
        }
    }
    bool commitCompositionValue;
    public bool CommitComposition
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            return commitCompositionValue;
        }
        set
        {
            Runtime.KumikoExecution.CheckUI();
            commitCompositionValue = value;
        }
    }
    string[]? dragFilesValue;
    /// <summary>Paths of files the system is dragging over the window, or null. Platforms clear it when the drag leaves.</summary>
    public string[]? DragFiles
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            return dragFilesValue;
        }
        set
        {
            Runtime.KumikoExecution.CheckUI();
            dragFilesValue = value;
        }
    }
    string[]? droppedFilesValue;
    /// <summary>Absolute paths of files dropped on the window this frame, delivered at <see cref="Pointer"/>.</summary>
    public string[]? DroppedFiles
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            return droppedFilesValue;
        }
        set
        {
            Runtime.KumikoExecution.CheckUI();
            droppedFilesValue = value;
        }
    }
    internal void Clear()
    {
        Navigation = NavigationDirection.None;
        DroppedFiles = null;
        Pressed = Released = false;
        ScrollY = ScrollX = 0;
        ScrollPrecise = true;
        Key = Key.None;
        Character = '\0';
        Text = "";
        Composition = null;
        CommitComposition = false;
        Modifiers = Modifiers.None;
    }

    public bool Primary => (Modifiers & (Modifiers.Primary | (OperatingSystem.IsMacOS() ? Modifiers.Meta : Modifiers.Control))) != 0;
}

public readonly record struct Interaction(ulong Id, Rect? Rect, bool Hovered, bool Pressed, bool Clicked, bool Focused, bool DoubleClicked = false);
public readonly record struct FrameStats(
    double BuildMilliseconds,
    double LayoutMilliseconds,
    double PaintMilliseconds,
    int Passes,
    int Nodes,
    int Instances
);
public enum Cursor
{
    Arrow,
    Hand,
    Text,
    ResizeHorizontal,
    ResizeVertical
}
