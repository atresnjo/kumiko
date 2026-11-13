using CoreGraphics;
using Kumiko.iOS;
using UIKit;

internal static partial class FocusFlow
{
    static CGRect BoundsIn(UIView child, UIView root) => child.ConvertRectToView(child.Bounds, root);

    static void CheckSessionLayout(NativeView view, string output, string state)
    {
        var start = BoundsIn(Find<UIButton>(view, "start"), view);
        var pause = BoundsIn(Find<UIButton>(view, "pause"), view);
        var strip = BoundsIn(Find<UIView>(view, "day-strip"), view);
        var dial = BoundsIn(Find<UIView>(view, "timer-dial"), view);
        var navigation = BoundsIn(Find<UIView>(view, "bottom-bar"), view);
        Require(start.Width >= 44 && start.Height >= 44 && pause.Width >= 44 && pause.Height >= 44,
            "Session actions retain touch targets at " + state);
        Require(Math.Abs(start.Right + 10 - pause.Left) < 1 && Math.Abs(start.Top - pause.Top) < 1,
            "Flexible primary action shares its row without overlap at " + state);
        Require(Math.Abs(start.Left - strip.Left) < 1 && Math.Abs(pause.Right - strip.Right) < 1 &&
            Math.Abs(strip.GetMidX() - view.Bounds.GetMidX()) < 1 && strip.Width <= 420,
            "Session fills its centered bounded column at " + state);
        Require(start.Left >= 22 && pause.Right <= view.Bounds.Width - 22 &&
            start.Bottom <= navigation.Top && dial.Width == dial.Height && dial.Left >= strip.Left && dial.Right <= strip.Right,
            "Session stays inside safe content bounds at " + state);
        WriteFrames(output, state, ("start", start), ("pause", pause), ("timeline", strip), ("dial", dial));
    }

    static void CheckDurationReflow(NativeView view, string output)
    {
        var buttons = new[] { "15 min", "25 min", "45 min", "60 min" }
            .Select(label => Find<UIButton>(view, "duration-choice", label)).ToArray();
        var original = view.Frame;
        foreach (var (width, height, state, columns) in new[] { (320, 640, "duration-narrow", 1), (820, 1020, "duration-tablet", 2) })
        {
            view.Frame = new CGRect(0, 0, width, height);
            view.Render();
            var frames = buttons.Select(button => BoundsIn(button, view)).ToArray();
            var sheet = BoundsIn(Find<UIView>(view, "sheet"), view);
            for (var i = 0; i < buttons.Length; i++)
            {
                Require(ReferenceEquals(buttons[i], Find<UIButton>(view, "duration-choice", buttons[i].AccessibilityLabel)),
                    "Duration button survives reflow at " + state);
                Require(frames[i].Width >= 44 && frames[i].Height >= 44 && sheet.Contains(frames[i]),
                    "Duration target fits sheet at " + state);
                for (var j = 0; j < i; j++)
                    Require(!frames[i].IntersectsWith(frames[j]), "Duration targets do not overlap at " + state);
            }
            Require(frames.Count(frame => Math.Abs(frame.Top - frames[0].Top) < 1) == columns,
                "Duration choices wrap into expected columns at " + state);
            WriteFrames(output, state, buttons.Select((button, i) => (button.AccessibilityLabel!, frames[i])).ToArray());
            Capture(view, output, state);
        }
        view.Frame = original;
        view.Render();
    }

    static void WriteFrames(string output, string state, params (string Name, CGRect Frame)[] frames)
    {
        var entries = frames.Select(entry => FormattableString.Invariant(
            $"{{\"name\":\"{entry.Name}\",\"x\":{entry.Frame.X},\"y\":{entry.Frame.Y},\"width\":{entry.Frame.Width},\"height\":{entry.Frame.Height}}}"));
        File.WriteAllText(Path.Combine(output, $"focus-{state}-frames.json"), "[" + string.Join(",", entries) + "]");
    }
}
