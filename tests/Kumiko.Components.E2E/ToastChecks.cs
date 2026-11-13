using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Kumiko.Components;
using Kumiko.Runtime;
using Kumiko.UI;

/// <summary>
/// Kit.Toaster on its own: the collapsed deck, spreading on hover, clicks, the close button, swiping, and an expiry
/// timer that waits while the user looks at the stack.
/// </summary>
static class ToastChecks
{
    public static void Run(string output, Action<bool, string> check)
    {
        using var program = new Program<ToastModel, ToastMsg>(new([], []), Update, static model => NavigationRoot<ToastMsg>.Create(NavigationHistory<string>.Start("toasts"), "app",
            _ => new ToastView(model).Map<ToastMsg>(static m => m), static _ => throw new InvalidOperationException()), ToastJson.Default.ToastModel, ToastJson.Default.ToastMsg);
        using var tester = new Tester(program, new(900, 640, DevMode: true));
        tester.Context.ReduceMotion = true;
        void Send(ToastMsg msg)
        {
            program.Send(msg);
            tester.Settle(3);
        }
        Rect Card(string id) => tester.Context.Accessibility.Single(n => n.Role == Role.Alert && n.Name == "Toast " + id && !n.Hidden).Rect;
        bool Shows(string id) => tester.Context.Accessibility.Any(n => n.Role == Role.Alert && n.Name == "Toast " + id && !n.Hidden);

        foreach (var id in new[] { "a", "b", "c", "d" })
            Send(new ToastMsg.Add(id, null));
        var front = Card("d");
        check(front.Right <= 900 - 19 && front.Bottom <= 640 - 19 && front.Right > 900 - 40, "the newest toast rests at the bottom right");
        check(Shows("b") && Shows("c") && !Shows("a"), "three toasts show and older ones wait hidden behind them");
        check(Card("c").Y < front.Y && front.Y - Card("c").Y < 30 && Card("c").Height == front.Height,
            "collapsed toasts tuck behind the front one at its height");
        check(tester.Context.Warnings.Count == 0, "the collapsed stack renders without warnings: " +
            string.Join("; ", tester.Context.Warnings.Select(w => $"{w.Code} {w.Path} {w.Message}")));
        tester.Screenshot(Path.Combine(output, "toasts-collapsed.png"));

        tester.MoveTo(front.X + 40, front.Y + 20);
        tester.Settle(3);
        front = Card("d");
        check(Card("c").Bottom <= front.Y - 9 && Card("b").Bottom <= Card("c").Y - 9 && !Shows("a"), "hovering spreads the visible toasts into a list");
        check(tester.Context.Accessibility.Any(n => n.Role == Role.Button && n.Name == "Dismiss Toast d"), "spread toasts offer a close button");
        tester.Screenshot(Path.Combine(output, "toasts-expanded.png"));

        tester.Get(Role.Alert, "Toast d").Click();
        tester.Settle(3);
        check(program.Model.Log is [.., "d Clicked"] && Shows("d"), "clicking a clickable toast reports it and keeps it");
        tester.Get(Role.Button, "Dismiss Toast d").Click();
        tester.Settle(3);
        check(program.Model.Log is [.., "d Dismissed"] && !Shows("d"), "the close button dismisses a toast");

        var c = Card("c");
        var (x, y) = (c.X + c.Width / 2, c.Y + c.Height / 2);
        tester.Press(x, y);
        for (var i = 1; i <= 6; i++)
            tester.MoveTo(x + i * 25, y);
        tester.Release(x + 150, y);
        tester.Settle(3);
        check(program.Model.Log is [.., "c Dismissed"] && !Shows("c") && !program.Model.Log.Contains("c Clicked"),
            "swiping a toast sideways dismisses it without clicking it");

        Send(new ToastMsg.Add("e", 2));
        tester.MoveTo(20, 20);
        tester.Settle();
        tester.Advance(1);
        tester.Settle();
        check(Shows("e"), "a timed toast stays until its time is up");
        var e = Card("e");
        tester.MoveTo(e.X + 40, e.Y + 20);
        tester.Settle();
        tester.Advance(5);
        tester.Settle();
        check(Shows("e"), "the timer waits while the pointer is over the stack");
        tester.MoveTo(20, 20);
        tester.Settle();
        tester.Advance(1.5f);
        tester.Settle(3);
        check(program.Model.Log is [.., "e Expired"] && !Shows("e"), "the toast expires once the remaining time passes unobserved");
    }

    static (ToastModel, Cmd<ToastMsg>) Update(ToastModel m, ToastMsg msg) => (msg switch
    {
        ToastMsg.Add add => m with { Toasts = m.Toasts.Add(new(add.Id, add.Seconds)) },
        ToastMsg.Happened h => m with
        {
            Log = m.Log.Add($"{h.Id} {h.Kind}"),
            Toasts = h.Kind is ToastEventKind.Dismissed or ToastEventKind.Expired ? m.Toasts.RemoveAll(t => t.Id == h.Id) : m.Toasts
        },
        _ => m
    }, Cmd<ToastMsg>.None);

    sealed class ToastView(ToastModel model) : Kumiko.Runtime.Screen<ToastModel, ToastMsg>(model)
    {
        protected override void Render()
        {
            var c = C;
            var m = Model;
            c.Text("Toasts", key: "title");
            var toasts = m.Toasts.Select(t => new Toast(t.Id, "Toast " + t.Id, new(Description: "Something happened in the background.",
                Clickable: true, Progress: .4f, Duration: t.Seconds is { } s ? TimeSpan.FromSeconds(s) : null))).ToArray();
            foreach (var e in c.Kit.Toaster(toasts))
                Ui.Dispatch(new ToastMsg.Happened(e.Id, e.Kind));
        }
    }
}

sealed record ToastEntry(string Id, int? Seconds);

sealed record ToastModel(ImmutableArray<ToastEntry> Toasts, ImmutableArray<string> Log);

abstract partial record ToastMsg
{
    public sealed record Add(string Id, int? Seconds) : ToastMsg;
    public sealed record Happened(string Id, ToastEventKind Kind) : ToastMsg;
}

[JsonSerializable(typeof(ToastModel))]
[JsonSerializable(typeof(ToastMsg))]
partial class ToastJson : JsonSerializerContext;
