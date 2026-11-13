using System.Text.Json;
using System.Text.Json.Serialization;
using CoreGraphics;
using CRMLite;
using Kumiko.iOS;
using UIKit;

internal static class ResponsiveFlow
{
    internal static void Run(string output)
    {
        ResponsiveRtlFlow.Run(output);
        ResponsiveEnvironmentFlow.Run(output);
        var initial = Data.Initial();
        using var program = CrmApp.CreateProgram(initial, new FakeCrmBackend(seed: initial).Services());
        using var parent = new UIViewController();
        // Never shown or activated: this window only supplies UIKit controller geometry.
#pragma warning disable CA1422
        using var window = new UIWindow(new CGRect(0, 0, 390, 844)) { RootViewController = parent, Hidden = true, OverrideUserInterfaceStyle = UIUserInterfaceStyle.Light };
#pragma warning restore CA1422
        parent.LoadViewIfNeeded();
        window.AddSubview(parent.View!);
        using var controller = new UIViewController();
        parent.AddChildViewController(controller);
        parent.View!.AddSubview(controller.View!);
        controller.DidMoveToParentViewController(parent);
        using var view = new NativeView(program);
        controller.View!.AddSubview(view);
        var errors = new List<Exception>();
        view.UnhandledException += errors.Add;
        var checks = new List<string>();
        void Check(bool condition, string description)
        {
            ResponsiveEnvironmentFlow.Require(condition, description);
            checks.Add(description);
        }
        program.Send(new CrmMsg.OpenCompany("northwind"));
        int? visit = null;
        UITextView? editor = null;
        foreach (var (width, height) in new[] { (320, 640), (390, 844), (844, 390), (320, 1024), (507, 1024), (678, 1024), (1024, 1366), (844, 390) })
        {
            var regular = width >= 678 && height > 390;
            window.Frame = new CGRect(0, 0, width, height);
            parent.View.Frame = window.Bounds;
            controller.View.Frame = parent.View.Bounds;
            view.Frame = controller.View.Bounds;
            ResponsiveEnvironmentFlow.SetTraits(parent, controller,
                regular ? UIUserInterfaceSizeClass.Regular : UIUserInterfaceSizeClass.Compact,
                height < 600 ? UIUserInterfaceSizeClass.Compact : UIUserInterfaceSizeClass.Regular);
            controller.AdditionalSafeAreaInsets = new UIEdgeInsets(20, 16, 24, 12);
            parent.View.LayoutIfNeeded();
            controller.View.LayoutIfNeeded();
            Settle(view);
            var directory = All(view).OfType<UIScrollView>().SingleOrDefault(v => v.AccessibilityIdentifier == "directory-list");
            Check((directory is not null) == regular, $"CRM chooses native list/detail from traits at {width}x{height}");
            var stack = ResponsiveEnvironmentFlow.Find<UIView>(view, "crm-stack");
            var stackRect = stack.ConvertRectToView(stack.Bounds, view);
            var bar = All(view).OfType<UITabBar>().Single();
            var barRect = bar.ConvertRectToView(bar.Bounds, view);
            Check(stackRect.X >= 16 && stackRect.Right <= width - 12 + .1 && stackRect.Y >= 20 && stackRect.Bottom <= height + .1 &&
                barRect.Bottom >= height - .1 && barRect.Y <= height - 24,
                "navigation pane fits the host safe area; the floating tab bar covers the bottom inset");
            if (directory is not null)
            {
                var listRect = directory.ConvertRectToView(directory.Bounds, view);
                Check(listRect.Right <= stackRect.X && listRect.Width >= 200, "native directory and detail do not overlap");
            }
            var currentEditor = All(view).OfType<UITextView>().Single(v => v.AccessibilityIdentifier == "note");
            if (editor is null)
            {
                editor = currentEditor;
                visit = program.Model.Navigation.Top.Id;
                editor.Text = "Responsive draft";
                editor.Delegate!.Changed(editor);
                Settle(view);
            }
            Check(ReferenceEquals(editor, currentEditor) && program.Model.Navigation.Top.Id == visit && program.Model.Note == "Responsive draft",
                "compact/regular reflow retains the native editor, visit and draft");
            var scroll = ResponsiveEnvironmentFlow.Find<UIScrollView>(view, "details-scroll");
            Check(scroll.Frame.Width > 0 && scroll.Frame.Height > 0 && scroll.ContentSize.Width <= scroll.Bounds.Width + 1,
                "details retain a usable viewport without horizontal clipping");
            if (height == 390)
                Check(scroll.ContentSize.Height > scroll.Bounds.Height, "landscape details can scroll to lower actions");
            Capture(view, output, $"crm-{width}x{height}");
        }
        // Return to regular traits without changing bounds. Directory selection must
        // enter the same guarded navigation path as compact list selection.
        ResponsiveEnvironmentFlow.SetTraits(parent, controller, UIUserInterfaceSizeClass.Regular, UIUserInterfaceSizeClass.Compact);
        parent.View.LayoutIfNeeded();
        controller.View.LayoutIfNeeded();
        Settle(view);
        Press(view, "Kestrel Freight");
        Settle(view);
        Check(program.Model.CompanyId == "kestrel" && program.Model.Navigation.Entries.Single(entry => entry.Id == visit).Note == "Responsive draft",
            "regular directory pushes a visit without discarding the covered draft");
        var nextEditor = ResponsiveEnvironmentFlow.Find<UITextView>(view, "note");
        nextEditor.Text = "Guarded draft";
        nextEditor.Delegate!.Changed(nextEditor);
        Settle(view);
        Back(view);
        Settle(view);
        Check(program.Model.PendingNavigation is not null && program.Model.CompanyId == "kestrel", "native Back still guards unsaved notes in regular layout");
        program.Send(new CrmMsg.CancelNavigation());
        Settle(view);
        nextEditor.Text = "";
        nextEditor.Delegate!.Changed(nextEditor);
        Settle(view);
        Back(view);
        Settle(view);
        Check(program.Model.CompanyId == "northwind" && program.Model.Note == "Responsive draft", "Back restores the original draft after directory navigation");
        var add = ResponsiveEnvironmentFlow.Find<UIButton>(view, "add-note");
        var detailScroll = ResponsiveEnvironmentFlow.Find<UIScrollView>(view, "details-scroll");
        detailScroll.ScrollRectToVisible(add.ConvertRectToView(add.Bounds, detailScroll), false);
        Settle(view);
        var actionRect = add.ConvertRectToView(add.Bounds, detailScroll);
        Check(actionRect.Y >= detailScroll.Bounds.Y && actionRect.Bottom <= detailScroll.Bounds.Bottom + 1,
            "landscape scrolling reveals the save action inside its viewport");
        add.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
        Settle(view);
        Check(program.Model.Note == "" && Crm.Timeline(program.Model, "northwind").Count(a => a.Kind == ActivityKind.Note) == 2,
            "native action after responsive reflow saves the draft once");
        Press(view, "Kestrel Freight");
        Settle(view);
        Check(program.Model.CompanyId == "kestrel" && program.Model.PendingNavigation is null, "regular directory selects intended company after saving");
        Capture(view, output, "crm-selected");
        Capture(view, output, "crm-repeat");
        Check(File.ReadAllBytes(Path.Combine(output, "responsive-crm-selected.png")).SequenceEqual(
            File.ReadAllBytes(Path.Combine(output, "responsive-crm-repeat.png"))), "settled layer snapshots are repeatable");
        Check(window.Hidden && errors.Count == 0, "responsive UIKit checks stay hidden without callback exceptions");
        program.Save(Path.Combine(output, "responsive-crm-session.jsonl"));
        File.WriteAllText(Path.Combine(output, "responsive-report.json"), JsonSerializer.Serialize(
            new ResponsiveReport(true, checks), ResponsiveJson.Default.ResponsiveReport));
    }

    static void Back(UIView view)
    {
        var stack = All(view).SelectMany(child => Responders(child)).OfType<UINavigationController>().First();
        var button = (UIButton)stack.TopViewController!.NavigationItem.LeftBarButtonItem!.CustomView!;
        button.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
    }

    /// <summary>Sends the primary action of the one directory row with this accessible name.</summary>
    static void Press(UIView view, string name)
    {
        var directory = ResponsiveEnvironmentFlow.Find<UIScrollView>(view, "directory-list");
        All(directory).OfType<UIButton>().Single(button => button.AccessibilityLabel == name)
            .SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
    }

    static IEnumerable<UIResponder> Responders(UIResponder responder)
    {
        for (UIResponder? current = responder; current is not null; current = current.NextResponder)
            yield return current;
    }

    static void Settle(NativeView view)
    {
        for (var i = 0; i < 4; i++)
        {
            view.Render();
            view.LayoutIfNeeded();
        }
    }

    internal static IEnumerable<UIView> All(UIView view) => view.Subviews.SelectMany(child => new[] { child }.Concat(All(child)));

    internal static void Capture(UIView view, string output, string name)
    {
        view.LayoutIfNeeded();
        using var format = new UIGraphicsImageRendererFormat { Scale = 2, Opaque = true };
        using var renderer = new UIGraphicsImageRenderer(view.Bounds.Size, format);
        using var image = renderer.CreateImage(context => view.Layer.RenderInContext(context.CGContext));
        using var png = image.AsPNG() ?? throw new InvalidOperationException("PNG encoding failed");
        File.WriteAllBytes(Path.Combine(output, $"responsive-{name}.png"), png.ToArray());
        File.WriteAllLines(Path.Combine(output, $"responsive-{name}.txt"), All(view).Select(v => $"{v.GetType().Name} {v.AccessibilityIdentifier} {v.Frame}"));
        var geometry = All(view).Where(v => !string.IsNullOrEmpty(v.AccessibilityIdentifier)).Select(v =>
        {
            var box = v.ConvertRectToView(v.Bounds, view);
            var value = v switch
            {
                UILabel label => label.Text,
                UIButton button => button.CurrentTitle,
                UITextView edit => edit.Text,
                _ => null
            };
            return new ResponsiveBox(v.GetType().Name, v.AccessibilityIdentifier!, value, (double)box.X, (double)box.Y, (double)box.Width, (double)box.Height);
        }).ToArray();
        File.WriteAllText(Path.Combine(output, $"responsive-{name}.json"), JsonSerializer.Serialize(geometry, ResponsiveJson.Default.ResponsiveBoxArray));
    }
}
internal sealed record ResponsiveReport(bool passed, List<string> Checks);
internal sealed record ResponsiveBox(string Kind, string Key, string? Value, double X, double Y, double Width, double Height);
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ResponsiveReport))]
[JsonSerializable(typeof(ResponsiveBox[]))]
internal partial class ResponsiveJson : JsonSerializerContext;
