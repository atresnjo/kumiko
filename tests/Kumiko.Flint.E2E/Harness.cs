using Flint;
using Kumiko.Runtime;
using Kumiko.UI;

/// <summary>Offscreen Tester input with real host semantics: Meta/Control on pointer events and click counts.</summary>
sealed class Harness(Program<FlintModel, Msg> program, Tester tester, string output)
{
    public List<string> Checks { get; } = [];
    public Program<FlintModel, Msg> Program => program;
    public Tester Tester => tester;
    public FlintModel Model => program.Model;
    public RepoTab Tab => program.Model.Current ?? throw new InvalidOperationException("No active repository.");
    /// <summary>The host's primary modifier: Command on macOS, Control elsewhere. Never the synthetic Primary bit.</summary>
    public static Modifiers Primary => OperatingSystem.IsMacOS() ? Modifiers.Meta : Modifiers.Control;
    public void Check(bool value, string name)
    {
        if (!value)
        {
            Fail();
            throw new InvalidOperationException(name);
        }
        Checks.Add(name);
    }
    void Fail()
    {
        File.WriteAllText(Path.Combine(output, "failure.txt"), tester.Dump());
        tester.Screenshot(Path.Combine(output, "failure.png"));
        program.Save(Path.Combine(output, "failure.jsonl"));
        Console.Error.WriteLine($"busy={program.Model.Busy}; palette={program.Model.Palette}; error={program.Model.Error}; pending={program.Model.Pending?.Title}");
    }
    public void Idle()
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        do
        {
            tester.Settle();
            if (program.PendingCommands == 0 && !program.Model.Busy)
            {
                tester.Settle();
                return;
            }
            Thread.Sleep(10);
        } while (DateTime.UtcNow < until);
        Fail();
        throw new TimeoutException("Flint did not finish its commands.");
    }
    public void WaitFor(Func<bool> condition, string what, int seconds = 10)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition() && DateTime.UtcNow < until)
        {
            tester.Settle();
            Thread.Sleep(10);
        }
        Check(condition(), what);
    }
    Tester.Locator Find(Role role, string name)
    {
        try
        {
            return tester.Get(role, name);
        }
        catch
        {
            Fail();
            Console.Error.WriteLine($"Missing {role}: {name}");
            throw;
        }
    }
    public void Click(string label, Role role = Role.Button)
    {
        Find(role, label).Click();
        Idle();
    }
    /// <summary>A pointer click carrying modifiers on both press and release, as native mouse events do.</summary>
    public void ClickWith(string label, Modifiers modifiers, int count = 1, Role role = Role.Button)
    {
        var rect = Find(role, label).Rect;
        var x = rect.X + Math.Min(24, rect.Width / 2);
        var y = rect.Y + rect.Height / 2;
        for (var i = 1; i <= count; i++)
        {
            tester.Context.Input.ClickCount = i;
            tester.Context.Input.Modifiers = modifiers;
            tester.Press(x, y);
            tester.Context.Input.Modifiers = modifiers;
            tester.Release(x, y);
        }
        tester.Context.Input.ClickCount = 1;
        Idle();
    }
    public void DoubleClick(string label, Role role = Role.Button) => ClickWith(label, Modifiers.None, 2, role);
    public void Menu(string target, string item, Role role = Role.Button)
    {
        var rect = Find(role, target).Rect;
        tester.RightClick(rect.X + Math.Min(24, rect.Width / 2), rect.Y + rect.Height / 2);
        tester.Settle();
        Click(item, Role.MenuItem);
    }
    public void Type(string label, string value)
    {
        Find(Role.TextField, label).Click();
        tester.Key(Key.A, Modifiers.Primary);
        tester.TypeText(value);
        tester.Settle();
    }
    public void Press(Key key, Modifiers modifiers = Modifiers.None)
    {
        tester.Key(key, modifiers);
        Idle();
    }
    public bool Has(Role role, string name) => tester.Context.Accessibility.Any(n => n.Role == role && n.Name == name && !n.Offscreen);
    public string? NameStarting(Role role, string prefix) => tester.Context.Accessibility.FirstOrDefault(n => n.Role == role && n.Name.StartsWith(prefix, StringComparison.Ordinal))?.Name;
    public void Shot(string name)
    {
        tester.Screenshot(Path.Combine(output, name + ".png"));
        File.WriteAllText(Path.Combine(output, name + ".txt"), tester.Dump());
        tester.WriteWarnings(Path.Combine(output, name + ".warnings.json"));
    }
    public void OpenRepository(string path)
    {
        Click("Open Repository");
        Type("Open repository", path);
        Press(Key.Enter);
    }
    public string CommitLabel => Tab.Amend ? "Amend" : Tab.Snapshot.Staged.Length switch { 0 => "Commit", 1 => "Commit 1 file", var n => $"Commit {n} files" };
}
