using CoreGraphics;
using Foundation;
using Kumiko.UI;
using UIKit;

namespace Kumiko.iOS;

internal sealed partial class UIKitControls
{
    Entry? CreateStandard(NativeControlNode node)
    {
        var id = node.Id;
        switch (node.Content)
        {
            case NativeSwitchContent:
                var toggle = new UISwitch();
                return Intrinsic(toggle, id, () => toggle.On);
            case NativeSliderContent:
                var slider = new UISlider();
                return new(slider, Owner: new ValueOwner(this, slider, id, () => slider.Value));
            case NativeStepperContent:
                var stepper = new UIStepper();
                return Intrinsic(stepper, id, () => stepper.Value);
            case NativeChoiceContent { Wheel: false }:
                var segments = new UISegmentedControl();
                return new(segments, Owner: new ValueOwner(this, segments, id, () =>
                {
                    if (current[id].Content is not NativeChoiceContent content ||
                        segments.SelectedSegment < 0 || segments.SelectedSegment >= content.Items.Length)
                        return null;
                    return content.Items[(int)segments.SelectedSegment].Id;
                }));
            case NativeChoiceContent:
                var picker = new UIPickerView(new CGRect(0, 0, node.Bounds.Width, node.Bounds.Height));
                var pickerOwner = new PickerOwner(this, picker, id);
                picker.Model = pickerOwner;
                return new(picker, Owner: pickerOwner);
            case NativeDateContent:
                var date = new UIDatePicker(new CGRect(0, 0, node.Bounds.Width, node.Bounds.Height))
                {
                    PreferredDatePickerStyle = UIDatePickerStyle.Wheels
                };
                return new(date, Owner: new ValueOwner(this, date, id,
                    () => DateTimeOffset.UnixEpoch.AddSeconds(date.Date.SecondsSince1970)));
            case NativeSearchContent:
                var search = new UISearchBar { SearchBarStyle = UISearchBarStyle.Minimal };
                var searchOwner = new SearchOwner(this, search, id);
                search.Delegate = searchOwner;
                return new(search, Owner: searchOwner);
            case NativeEditorContent:
                var editor = new UITextView
                {
                    Font = UIFont.GetPreferredFontForTextStyle(UIFontTextStyle.Body),
                    AdjustsFontForContentSizeCategory = true,
                    BackgroundColor = UIColor.SecondarySystemBackground,
                    TextColor = UIColor.Label
                };
                var editorOwner = new EditorOwner(this, editor, id);
                editor.Delegate = editorOwner;
                return new(editor, Owner: editorOwner);
            case NativeProgressContent:
                return new(new UIProgressView(UIProgressViewStyle.Default));
            case NativeActivityContent:
                return new(new UIActivityIndicatorView(UIActivityIndicatorViewStyle.Medium) { HidesWhenStopped = true });
            case NativePageContent:
                var pages = new UIPageControl
                {
                    PageIndicatorTintColor = UIColor.SystemGray3,
                    CurrentPageIndicatorTintColor = UIColor.SystemBlue
                };
                return new(pages, Owner: new ValueOwner(this, pages, id, () => (int)pages.CurrentPage));
            case NativeImageContent:
                var image = new UIImageView { ContentMode = UIViewContentMode.ScaleAspectFit, ClipsToBounds = true };
                return new(image, Owner: new ImageOwner(image));
            case NativeSeparatorContent:
                return new(new UIView { BackgroundColor = UIColor.Separator, IsAccessibilityElement = false });
            case NativeCommandsContent commands:
                return CreateCommands(id, commands.Kind);
            default:
                return null;
        }
    }

    static void UpdateStandard(Entry entry, NativeControlNode node)
    {
        var enabled = node.Enabled && !node.Inert;
        var native = entry.Owner is IntrinsicOwner intrinsic ? intrinsic.Control : entry.View;
        if (native is UIControl control)
            control.Enabled = enabled;
        switch (node.Content)
        {
            case NativeSwitchContent toggle:
                ((UISwitch)native).On = toggle.Value;
                if (toggle.Tint is { } tint)
                {
                    using var color = NativePaint.Color(tint);
                    ((UISwitch)native).OnTintColor = color;
                }
                else
                    ((UISwitch)native).OnTintColor = null;
                native.AccessibilityLabel = toggle.Label;
                break;
            case NativeSliderContent slider:
                var range = (UISlider)entry.View;
                range.MinValue = slider.Minimum;
                range.MaxValue = slider.Maximum;
                range.Value = slider.Value;
                range.AccessibilityLabel = slider.Label;
                break;
            case NativeStepperContent stepper:
                var step = (UIStepper)native;
                // Reset the old range first so a model can move to a disjoint range.
                step.MinimumValue = Math.Min(step.MinimumValue, stepper.Minimum);
                step.MaximumValue = stepper.Maximum;
                step.MinimumValue = stepper.Minimum;
                step.StepValue = stepper.Step;
                step.Value = stepper.Value;
                step.AccessibilityLabel = stepper.Label;
                break;
            case NativeChoiceContent { Wheel: false } choices:
                var segments = (UISegmentedControl)entry.View;
                if (segments.NumberOfSegments != choices.Items.Length || choices.Items.Where((item, index) =>
                    segments.TitleAt(index) != item.Label).Any())
                {
                    segments.RemoveAllSegments();
                    for (var index = 0; index < choices.Items.Length; index++)
                        segments.InsertSegment(choices.Items[index].Label, index, false);
                }
                segments.SelectedSegment = choices.Items.IndexOf(choices.Items.First(item => item.Id == choices.SelectedId));
                segments.AccessibilityLabel = choices.Label;
                break;
            case NativeChoiceContent choices:
                ((PickerOwner)entry.Owner!).Update(choices);
                break;
            case NativeDateContent date:
                var picker = (UIDatePicker)entry.View;
                picker.Mode = date.Mode switch
                {
                    NativeDatePickerMode.Time => UIDatePickerMode.Time,
                    NativeDatePickerMode.DateAndTime => UIDatePickerMode.DateAndTime,
                    _ => UIDatePickerMode.Date
                };
                picker.MinimumDate = null;
                picker.MaximumDate = null;
                using (var minimum = Date(date.Minimum))
                    picker.MinimumDate = minimum;
                using (var maximum = Date(date.Maximum))
                    picker.MaximumDate = maximum;
                using (var value = Date(date.Value)!)
                    picker.SetDate(value, false);
                picker.AccessibilityLabel = date.Label;
                break;
            case NativeSearchContent search:
                ((SearchOwner)entry.Owner!).Update(search, enabled);
                break;
            case NativeEditorContent editor:
                ((EditorOwner)entry.Owner!).Update(editor, enabled);
                StyleEditor((UITextView)entry.View, editor);
                break;
            case NativeProgressContent progress:
                ((UIProgressView)entry.View).Progress = progress.Value;
                entry.View.AccessibilityLabel = progress.Label;
                break;
            case NativeActivityContent activity:
                var indicator = (UIActivityIndicatorView)entry.View;
                if (activity.Running && !indicator.IsAnimating)
                    indicator.StartAnimating();
                else if (!activity.Running)
                    indicator.StopAnimating();
                indicator.AccessibilityLabel = activity.Label;
                break;
            case NativePageContent page:
                ((UIPageControl)entry.View).Pages = page.Count;
                ((UIPageControl)entry.View).CurrentPage = page.Page;
                entry.View.AccessibilityLabel = page.Label;
                break;
            case NativeImageContent image:
                ((ImageOwner)entry.Owner!).Update(image);
                break;
            case NativeCommandsContent commands:
                ((CommandsOwner)entry.Owner!).Update(commands, enabled);
                break;
        }
    }

    static NSDate? Date(DateTimeOffset? value) => value is { } date
        ? NSDate.FromTimeIntervalSince1970((date - DateTimeOffset.UnixEpoch).TotalSeconds) : null;

    sealed class ImageOwner(UIImageView view) : IDisposable
    {
        NativeImageContent? previous;
        UIImage? image;

        internal void Update(NativeImageContent content)
        {
            if (previous != content)
            {
                using var bytes = content.Symbol is null ? NSData.FromArray(content.Data.ToArray()) : null;
                var next = content.Symbol is { } symbol ? UIImage.GetSystemImage(symbol) : UIImage.LoadFromData(bytes!);
                if (next is null)
                    throw new ArgumentException("The image could not be decoded or the SF Symbol does not exist.");
                view.Image = next;
                image?.Dispose();
                image = next;
                previous = content;
            }
            if (content.Tint is { } tint)
            {
                using var color = NativePaint.Color(tint);
                view.TintColor = color;
            }
            else
                view.TintColor = null!; // nil restores UIKit's inherited tint.
            view.IsAccessibilityElement = content.Label is not null;
            view.AccessibilityLabel = content.Label;
        }

        public void Dispose()
        {
            view.Image = null;
            image?.Dispose();
        }
    }
}
