using Kumiko.Runtime;

namespace Kumiko.UI;

public sealed partial class Tester
{
    readonly Dictionary<string, Tester> windowTesters = new(StringComparer.Ordinal);
    readonly string? selectedWindowKey;
    bool testerDisposed;

    Tester(IProgram program, string key, TesterOptions options) : this(context => program.ViewWindow(context, key), options)
    {
        this.program = program;
        selectedWindowKey = key;
    }

    public IReadOnlyList<ProgramWindow> Windows => program?.WindowList() ?? [];

    /// <summary>Gets an offscreen window by typed identity. Each window retains its own focus, caret and scrolling.</summary>
    public Tester In<TWindow>(TWindow identity) where TWindow : notnull =>
        In((program ?? throw new InvalidOperationException("Construct Tester with a program.")).WindowKey(identity));

    /// <summary>Selects a canonical window key, or a unique title for CLI-friendly inspection.</summary>
    public Tester In(string key)
    {
        ObjectDisposedException.ThrowIf(testerDisposed, this);
        var owner = program ?? throw new InvalidOperationException("Construct Tester with a program.");
        var matches = Windows.Where(w => w.Key == key || w.Title == key).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException($"Expected one open window for {key}; found {matches.Length}.");
        var info = matches[0];
        if (selectedWindowKey == info.Key || selectedWindowKey is null && Windows[0].Key == info.Key)
            return this;
        if (!windowTesters.TryGetValue(info.Key, out var tester))
        {
            tester = new(owner, info.Key, new(info.Width, info.Height, Context.Scale, Context.Theme, Context.DevMode, Context.ApplicationIcons));
            tester.Context.ReduceMotion = Context.ReduceMotion;
            windowTesters.Add(info.Key, tester);
        }
        return tester;
    }

    void FrameWindows(float dt)
    {
        if (selectedWindowKey is not null || program is null || !program.UsesWindows)
            return;
        var windows = Windows;
        foreach (var key in windowTesters.Keys.ToArray())
        {
            if (!windows.Any(w => w.Key == key) || windows.FirstOrDefault()?.Key == key)
            {
                windowTesters[key].Dispose();
                windowTesters.Remove(key);
            }
            else
                windowTesters[key].Frame(dt);
        }
    }

    void DisposeWindows()
    {
        foreach (var tester in windowTesters.Values)
            tester.Dispose();
        windowTesters.Clear();
        program?.ForgetContext(Context);
    }
}
