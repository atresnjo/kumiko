using Kumiko;
using Kumiko.UI;

// Incremental layout skips work that cannot change the result: text that measures the same
// keeps cached geometry, and fixed-size absolute boxes relayout alone. Every step must match a
// fresh layout of the same state, and skipped layouts must still paint the new text.
internal static class IncrementalLayoutChecks
{
    sealed class State
    {
        public int Counter, Label, Badge;
        public bool Wide;
    }

    internal static void Run(string output, Action<bool, string> check)
    {
        var state = new State();
        using var live = new Tester(c => View(c, state), new(Width: 420, Height: 300));
        var steps = new (string Name, Action Change)[]
        {
            ("same-width counter", () => state.Counter++),
            ("same-width counter again", () => state.Counter += 7),
            ("wider label in a fixed box", () => state.Label++),
            ("shorter label in a fixed box", () => state.Label += 3),
            ("baseline row and auto panel", () => state.Badge++),
            ("several changes", () => { state.Counter++; state.Label++; state.Badge += 2; }),
            ("resize", () => state.Wide = !state.Wide),
            ("text after resize", () => { state.Label++; state.Badge++; }),
        };
        var matches = 0;
        var painted = 0;
        foreach (var (name, change) in steps)
        {
            change();
            if (state.Wide != (live.Context.Size.Width > 420))
                live.Resize(state.Wide ? 560 : 420, 300);
            else
                live.Frame();
            using var fresh = new Tester(c => View(c, state), new(Width: state.Wide ? 560 : 420, Height: 300));
            var difference = Difference(live, fresh);
            matches += difference is null ? 1 : 0;
            if (difference is not null)
                check(false, $"incremental layout after '{name}' matches a fresh layout: {difference}");
            painted += live.HasText(Counter(state)) && live.HasText(Label(state)) ? 1 : 0;
        }
        check(matches == steps.Length, $"incremental layouts match fresh layouts across {steps.Length} text, boundary and resize steps");
        check(painted == steps.Length, "counters whose size is unchanged still present their new text");
        var path = Path.Combine(output, "incremental-layout");
        live.Screenshot(path + ".png");
        File.WriteAllText(path + ".txt", live.Dump());
    }

    static string Counter(State s) => $"Frame {s.Counter % 10000:0000}";
    static string Label(State s) => s.Label % 2 == 0 ? $"Label {s.Label}" : $"A wider label {s.Label}";

    static void View(Context c, State s)
    {
        using var page = c.Div(key: "page").SizeFull().Padding(8).Gap(6);
        c.Text(Counter(s), new(Size: 13, Mono: true), key: "counter");
        using (c.Div(key: "row").Row().Items(Align.Baseline).Gap(8))
        {
            c.Text("Status", new(Size: 18), key: "status");
            c.Text(new string('!', 1 + s.Badge % 4), new(Size: 12), key: "badge");
        }
        for (var i = 0; i < 3; i++)
            using (c.Div(key: i).Absolute().Offset(10 + i * 130, 120).Width(120).Height(40).Padding(4)
                .Background(Color.Hex(0xe8eef6)))
                c.Text(i == 1 ? Label(s) : $"Box {i}", new(Size: 12, Wrap: true), key: "box-label");
        using (c.Div(key: "panel").Absolute().Offset(10, 200).Padding(6).Background(Color.Hex(0xf4e8d8)))
            c.Text($"Auto panel {new string('-', s.Badge % 5)}", key: "panel-label");
        c.Text("Following content", key: "following");
    }

    static string? Difference(Tester live, Tester fresh)
    {
        var a = live.Context.Nodes;
        var b = fresh.Context.Nodes;
        if (a.Length != b.Length)
            return $"{a.Length} nodes instead of {b.Length}";
        for (var i = 0; i < a.Length; i++)
            if (a[i].Rect != b[i].Rect)
                return $"node {i} ({a[i].Key}) at {a[i].Rect} instead of {b[i].Rect}";
        return null;
    }
}
