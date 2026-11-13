using CoreGraphics;
using Foundation;
using Kumiko.iOS;
using Kumiko.Runtime;
using UIKit;

internal static partial class CatalogFlow
{
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        using var program = CatalogApp.CreateProgram();
        program.Record(Path.Combine(output, "catalog-session.jsonl"));
        using var view = new NativeView(program) { Frame = new CGRect(0, 0, 393, 759) };
        Exception? error = null;
        view.UnhandledException += exception => error = exception;
        view.Render();
        Require(program.Model.Edits == 0, "Reconciliation generates no editing messages");
        var toggle = Find<UISwitch>(view, "switch");
        var slider = Find<UISlider>(view, "slider");
        var stepper = Find<UIStepper>(view, "stepper");
        var segments = Find<UISegmentedControl>(view, "segments");
        var picker = Find<UIPickerView>(view, "picker");
        var date = Find<UIDatePicker>(view, "date");
        var search = Find<UISearchBar>(view, "search");
        var editor = Find<UITextView>(view, "editor");
        var pages = Find<UIPageControl>(view, "pages");
        Require(toggle.On && Math.Abs(slider.Value - .4f) < .001 && stepper.Value == 2,
            "Initial model reaches native controls");
        Capture(view, output, "phone");
        foreach (var intrinsic in new UIControl[] { toggle, stepper })
        {
            var slot = intrinsic.Superview!;
            Require(slot.Bounds.Height >= 44 && intrinsic.Frame.Right <= slot.Bounds.Width &&
                Math.Abs(intrinsic.Frame.GetMidY() - slot.Bounds.GetMidY()) < .5,
                "Intrinsic native size fits and centers within its layout slot");
        }
        toggle.On = false;
        Change(toggle);
        slider.Value = .75f;
        Change(slider);
        stepper.Value = 5;
        Change(stepper);
        segments.SelectedSegment = 1;
        Change(segments);
        picker.Select(2, 0, false);
        picker.Model!.Selected(picker, 2, 0);
        using (var selected = NSDate.FromTimeIntervalSince1970((CatalogApp.Init.Date.AddDays(2) - DateTimeOffset.UnixEpoch).TotalSeconds))
            date.SetDate(selected, false);
        Change(date);
        search.Text = "calendar";
        search.Delegate!.TextChanged(search, "calendar");
        editor.Text = "A note\nwith two lines and 🌱";
        editor.Delegate!.Changed(editor);
        pages.CurrentPage = 2;
        Change(pages);
        PumpUntil(() => program.Model.Edits == 9);
        Require(!program.Model.Notifications && program.Model.Volume == .75f && program.Model.Quantity == 5 &&
            program.Model.Segment == "week" && program.Model.Choice == "other" && program.Model.Page == 2 &&
            program.Model.Search == "calendar" && program.Model.Notes.Contains("🌱", StringComparison.Ordinal) &&
            program.Model.Date.Date == CatalogApp.Init.Date.AddDays(2).Date, "Native edits reach the Kumiko model exactly once");
        view.Render();
        Require(Find<UIProgressView>(view, "progress").Progress == .75f, "Slider drives model-bound progress");
        editor.SelectedRange = new NSRange(2, 3);
        Command(view, "toolbar", "busy");
        PumpUntil(() => program.Model.Busy);
        view.Render();
        Require(Find<UIActivityIndicatorView>(view, "activity").IsAnimating, "Activity follows model state");
        Require(editor.SelectedRange.Equals(new NSRange(2, 3)), "Unrelated updates preserve editor selection");
        Command(view, "toolbar", "busy");
        PumpUntil(() => !program.Model.Busy);
        view.Render();
        Require(!Find<UIActivityIndicatorView>(view, "activity").IsAnimating, "Stopped activity releases native animation");
        Command(view, "menu", "reverse");
        PumpUntil(() => program.Model.Reversed);
        view.Render();
        Require(ReferenceEquals(picker, Find<UIPickerView>(view, "picker")) &&
            ReferenceEquals(segments, Find<UISegmentedControl>(view, "segments")), "Reordering preserves keyed choice controls");
        segments.SelectedSegment = 0;
        Change(segments);
        picker.Select(2, 0, false);
        picker.Model!.Selected(picker, 2, 0);
        PumpUntil(() => program.Model.Segment == "month" && program.Model.Choice == "work");
        var count = program.Model.Edits;
        Command(view, "menu", "unavailable");
        Pump();
        Require(program.Model.Edits == count, "Disabled menu action is rejected");
        var menu = Find<UIButton>(view, "menu").Menu!;
        Require(((UIAction)menu.Children[0]).Attributes.HasFlag(UIMenuElementAttributes.Destructive), "Menu preserves destructive intent");
        DisabledAndModal(view, program);
        RemovedControls(view, program);
        // The replacement controls reflect the model, rather than UIKit's stale old editing state.
        Require(Find<UISlider>(view, "slider").Value == program.Model.Volume, "Restored controls use current model values");
        program.Send(new CatalogMessage.Command("reset"));
        view.Render();
        program.Send(new CatalogMessage.SearchChanged("replay"));
        program.Send(new CatalogMessage.NotesChanged("Saved\nfor replay"));
        view.Render();
        Layouts(view, output);
        view.Frame = new CGRect(0, 0, 393, 759);
        view.Render();
        Scroll(view, "scroll", 780);
        Capture(view, output, "live");
        using var replayProgram = CatalogApp.CreateProgram();
        replayProgram.Replay(Path.Combine(output, "catalog-session.jsonl"));
        using var replay = new NativeView(replayProgram) { Frame = view.Frame };
        replay.Render();
        Scroll(replay, "scroll", 780);
        Require(replayProgram.Model == program.Model, "Catalog session reproduces the model");
        Capture(replay, output, "replay");
        Require(File.ReadAllBytes(Path.Combine(output, "catalog-live.png")).AsSpan().SequenceEqual(
            File.ReadAllBytes(Path.Combine(output, "catalog-replay.png"))), "Catalog replay reproduces the visible image");
        Images(output);
        InvalidInputs();
        Require(error is null, "Native component callbacks remain guarded");
        view.Dispose();
        Pump();
        Require(error is null, "Queued component work ignores disposal");
        File.WriteAllText(Path.Combine(output, "catalog-report.json"),
            """
            {"passed":true,"offscreen":true,"nativeInputs":true,"exactlyOnce":true,
             "stableChoiceIds":true,"retainedIdentity":true,"disabled":true,"modalIsolation":true,
             "removalAndDisposal":true,"searchAndMultiline":true,"progressAndActivity":true,
             "commands":true,"images":true,"invalidInputs":true,"phone":true,"narrow":true,
             "tablet":true,"dark":true,"scrollRetention":true,"replay":true}
            """);
    }

    static void DisabledAndModal(NativeView view, Program<CatalogModel, CatalogMessage> program)
    {
        Command(view, "toolbar", "enable");
        PumpUntil(() => !program.Model.Enabled);
        view.Render();
        var count = program.Model.Edits;
        var slider = Find<UISlider>(view, "slider");
        Require(!slider.Enabled && !Find<UISwitch>(view, "switch").Enabled &&
            !Find<UITextView>(view, "editor").Editable, "Model disables native editing");
        slider.Value = .1f;
        Change(slider);
        Find<UISearchBar>(view, "search").Delegate!.TextChanged(Find<UISearchBar>(view, "search"), "ignored");
        Pump();
        Require(program.Model.Edits == count, "Disabled native events are rejected");
        Command(view, "toolbar", "enable");
        PumpUntil(() => program.Model.Enabled);
        Command(view, "navigation", "details");
        PumpUntil(() => program.Model.Details);
        view.Render();
        count = program.Model.Edits;
        slider.Value = .2f;
        Change(slider);
        Command(view, "menu", "reset");
        Command(view, "toolbar", "busy");
        Pump();
        Require(program.Model.Edits == count && slider.AccessibilityElementsHidden,
            "Modal blocks native values and command actions");
        Find<UIButton>(view, "close").SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
        PumpUntil(() => !program.Model.Details);
        view.Render();
    }

    static void RemovedControls(NativeView view, Program<CatalogModel, CatalogMessage> program)
    {
        var oldSlider = Find<UISlider>(view, "slider");
        var search = Find<UISearchBar>(view, "search");
        var staleSearch = search.Delegate!;
        var picker = Find<UIPickerView>(view, "picker");
        var stalePicker = picker.Model!;
        // Queue a value, then remove its control before the host consumes it.
        oldSlider.Value = .9f;
        Change(oldSlider);
        program.Send(new CatalogMessage.Command("visible"));
        view.Render();
        var count = program.Model.Edits;
        staleSearch.TextChanged(search, "detached");
        stalePicker.Selected(picker, 0, 0);
        Pump();
        Require(program.Model.Edits == count && !program.Model.Visible, "Removed controls discard pending edits and reject stale delegates");
        Require(oldSlider.Handle == IntPtr.Zero, "Removed native controls are disposed");
        program.Send(new CatalogMessage.Command("visible"));
        view.Render();
        Require(!ReferenceEquals(oldSlider, Find<UISlider>(view, "slider")), "Reopening creates fresh native controls");
    }

    static void Layouts(NativeView view, string output)
    {
        var slider = Find<UISlider>(view, "slider");
        foreach (var (name, width, height) in new[] { ("narrow", 320, 640), ("tablet", 820, 1100) })
        {
            view.Frame = new CGRect(0, 0, width, height);
            view.Render();
            Scroll(view, "scroll", 0);
            Capture(view, output, name);
            Require(ReferenceEquals(slider, Find<UISlider>(view, "slider")), "Resize retains native control identity");
            var scroll = Find<UIScrollView>(view, "scroll");
            Require(scroll.ContentSize.Height > scroll.Bounds.Height && scroll.ContentSize.Width <= scroll.Bounds.Width,
                "Catalog scrolls vertically inside host bounds");
            foreach (var key in new[] { "switch", "slider", "stepper", "segments", "picker", "date", "search", "editor" })
            {
                var native = All(view).Single(child => child.AccessibilityIdentifier == key);
                Require(native.Frame.Width > 0 && native.Frame.Right <= native.Superview!.Bounds.Width + .5,
                    "Native control fits its content width: " + key);
            }
            Scroll(view, "scroll", 780);
            var offset = scroll.ContentOffset.Y;
            view.Render();
            Require(Math.Abs(scroll.ContentOffset.Y - offset) < 1, "Unrelated render preserves native scroll");
            Capture(view, output, name + "-content");
        }
        if (!OperatingSystem.IsIOSVersionAtLeast(17))
            throw new PlatformNotSupportedException("The offscreen appearance check requires trait overrides.");
        view.TraitOverrides.UserInterfaceStyle = UIUserInterfaceStyle.Dark;
        view.UpdateTraitsIfNeeded();
        view.Render();
        Capture(view, output, "dark");
        view.TraitOverrides.UserInterfaceStyle = UIUserInterfaceStyle.Light;
        view.UpdateTraitsIfNeeded();
        view.Render();
    }

    static void Command(NativeView view, string key, string command)
    {
        UIAction action;
        if (key == "menu")
        {
            var index = CatalogApp.Menu.Select((item, position) => (item, position)).Single(pair => pair.item.Id == command).position;
            action = (UIAction)Find<UIButton>(view, key).Menu!.Children[index];
        }
        else
        {
            var items = key == "navigation" ? Find<UINavigationBar>(view, key).TopItem!.RightBarButtonItems! :
                Find<UIToolbar>(view, key).Items!;
            action = items.Single(item => item.AccessibilityIdentifier == command).PrimaryAction!;
        }
        // UIControl dispatches the real UIAction handler without presenting a menu or synthesizing input.
        using var sender = new UIButton();
        sender.SendAction(action);
    }

    static void Scroll(NativeView view, string key, float offset)
    {
        view.LayoutIfNeeded();
        var scroll = Find<UIScrollView>(view, key);
        scroll.SetContentOffset(new CGPoint(0, Math.Min(offset, Math.Max(0, scroll.ContentSize.Height - scroll.Bounds.Height))), false);
        view.LayoutIfNeeded();
    }

    static void Change(UIControl control) => control.SendActionForControlEvents(UIControlEvent.ValueChanged);
    static IEnumerable<UIView> All(UIView root) => root.Subviews.SelectMany(child => new[] { child }.Concat(All(child)));
    static T Find<T>(UIView view, string key) where T : UIView => All(view).OfType<T>().Single(child => child.AccessibilityIdentifier == key);
    static void Capture(UIView view, string output, string state)
    {
#pragma warning disable CA1422 // Private window stays hidden; the executable has no scene.
        using var window = new UIWindow(new CGRect(0, 0, view.Bounds.Width, view.Bounds.Height));
#pragma warning restore CA1422
        using var controller = new UIViewController();
        window.Hidden = true;
        window.RootViewController = controller;
        controller.LoadViewIfNeeded();
        if (controller.View!.Superview is null)
            window.AddSubview(controller.View);
        controller.View.AddSubview(view);
        view.LayoutIfNeeded();
        using var format = new UIGraphicsImageRendererFormat { Scale = 2, Opaque = true };
        using var renderer = new UIGraphicsImageRenderer(view.Bounds.Size, format);
        using var image = renderer.CreateImage(context => view.Layer.RenderInContext(context.CGContext));
        using var png = image.AsPNG() ?? throw new InvalidOperationException("PNG encoding failed.");
        File.WriteAllBytes(Path.Combine(output, $"catalog-{state}.png"), png.ToArray());
        var geometry = All(view).Select(child => FormattableString.Invariant(
            $"{child.GetType().Name} key={child.AccessibilityIdentifier} frame={child.Frame} bounds={child.Bounds} name={child.AccessibilityLabel}"));
        File.WriteAllLines(Path.Combine(output, $"catalog-{state}-geometry.txt"), geometry);
        foreach (var bar in All(view).OfType<UINavigationBar>())
            Require(All(bar).OfType<UILabel>().Any(label => label.Text == "UIKit components" && label.Bounds.Width > 0),
                "Navigation title has native layout");
        Require(window.Hidden, "Catalog capture never shows its private window");
        view.RemoveFromSuperview();
    }
    static void Pump() => NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.05));
    static void PumpUntil(Func<bool> done)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && timer.Elapsed < TimeSpan.FromSeconds(5))
            Pump();
        Require(done(), "Native event loop delivered catalog update");
    }
    static void Require(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException(description);
        Console.WriteLine("PASS " + description);
    }
}
