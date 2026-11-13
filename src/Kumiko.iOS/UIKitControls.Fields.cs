using CoreGraphics;
using Kumiko.UI;
using UIKit;

namespace Kumiko.iOS;

// Core controls (text fields, checkboxes, styled boxes and scroll areas) presented as UIKit views.
internal sealed partial class UIKitControls
{
    readonly Dictionary<ulong, string> texts = new();
    ulong submitted;

    public string? TakeText(ulong id) => texts.Remove(id, out var text) ? text : null;

    public ulong TakeSubmit()
    {
        var field = submitted;
        submitted = 0;
        return field;
    }

    Entry? CreateHosted(NativeControlNode node)
    {
        switch (node.Content)
        {
            case NativeTextFieldContent:
                var field = new UITextField
                {
                    BorderStyle = UITextBorderStyle.RoundedRect,
                    ReturnKeyType = UIReturnKeyType.Done,
                    ClearButtonMode = UITextFieldViewMode.WhileEditing
                };
                return new(field, Owner: new FieldOwner(this, field, node.Id));
            case NativeCheckboxContent:
                var box = new UIButton(UIButtonType.System);
                var id = node.Id;
                var action = UIAction.Create(_ => guard(() =>
                {
                    if (current.TryGetValue(id, out var latest) && latest.Enabled && !latest.Inert)
                        invoke(id);
                }));
                box.AddAction(action, UIControlEvent.PrimaryActionTriggered);
                return new(box, action, Target: box);
            case NativeBoxContent:
                return new(new UIView { ClipsToBounds = true });
            case NativeScrollContent:
                return new(new UIScrollView
                {
                    AlwaysBounceVertical = true,
                    KeyboardDismissMode = UIScrollViewKeyboardDismissMode.Interactive
                });
            default:
                return null;
        }
    }

    static void UpdateHosted(Entry entry, NativeControlNode node)
    {
        switch (node.Content)
        {
            case NativeTextFieldContent text:
                ((FieldOwner)entry.Owner!).Update(text, node.Enabled && !node.Inert);
                break;
            case NativeCheckboxContent check:
                Configure((UIButton)entry.View, check);
                ((UIButton)entry.View).Enabled = node.Enabled && !node.Inert;
                break;
            case NativeBoxContent box:
                entry.View.BackgroundColor = box.Background is { } fill ? NativePaint.Color(fill) : null;
                entry.View.Layer.CornerRadius = box.Radius;
                entry.View.ClipsToBounds = box.Clip || box.Radius > 0;
                entry.View.Layer.BorderWidth = box.Border is null ? 0 : box.BorderWidth;
                entry.View.Layer.BorderColor = box.Border is { } line ? NativePaint.Color(line).CGColor : null;
                // A named image box is one element, so VoiceOver reads its name rather than its children.
                entry.View.IsAccessibilityElement = box.Image is not null;
                entry.View.AccessibilityLabel = box.Image;
                entry.View.AccessibilityTraits = box.Image is null ? UIAccessibilityTrait.None : UIAccessibilityTrait.Image;
                break;
            case NativeScrollContent scroll:
                ((UIScrollView)entry.View).ContentSize = new CGSize(node.Bounds.Width, Math.Max(node.Bounds.Height, scroll.ContentHeight));
                break;
        }
    }

    static void Configure(UIButton button, NativeCheckboxContent check)
    {
        using var configuration = UIButtonConfiguration.PlainButtonConfiguration;
        configuration.Title = check.Label;
        configuration.Image = UIImage.GetSystemImage(check.Checked ? "checkmark.circle.fill" : "circle")?
            .ApplyTintColor(check.Checked ? UIColor.SystemBlue : UIColor.TertiaryLabel, UIImageRenderingMode.AlwaysOriginal);
        configuration.ImagePadding = 10;
        configuration.BaseForegroundColor = UIColor.Label;
        configuration.ContentInsets = new NSDirectionalEdgeInsets(0, 0, 0, 0);
        button.Configuration = configuration;
        button.HorizontalAlignment = UIControlContentHorizontalAlignment.Leading;
        button.AccessibilityLabel = check.Label;
        button.AccessibilityValue = check.Checked ? "checked" : "unchecked";
    }

    /// <summary>Owns a field's editing callbacks. Typed text waits in <see cref="texts"/> for the next frame.</summary>
    sealed class FieldOwner : IDisposable
    {
        readonly UIKitControls controls;
        readonly UITextField field;
        readonly ulong id;
        readonly UIAction changed;

        public FieldOwner(UIKitControls controls, UITextField field, ulong id)
        {
            this.controls = controls;
            this.field = field;
            this.id = id;
            changed = UIAction.Create(_ => controls.Guard(() =>
            {
                controls.texts[id] = field.Text ?? "";
                controls.RequestFrame();
            }));
            field.AddAction(changed, UIControlEvent.EditingChanged);
            field.ShouldReturn = _ =>
            {
                controls.Guard(() =>
                {
                    controls.submitted = id;
                    controls.RequestFrame();
                });
                return false;
            };
        }

        public void Update(NativeTextFieldContent content, bool enabled)
        {
            // While an edit waits for the next frame, the model has not caught up; keep the user's text.
            if (!controls.texts.ContainsKey(id) && field.Text != content.Value && field.MarkedTextRange is null)
                field.Text = content.Value;
            field.Placeholder = content.Placeholder;
            field.SecureTextEntry = content.Password;
            field.Enabled = enabled;
            field.AccessibilityLabel = content.Name;
        }

        public void Dispose()
        {
            field.RemoveAction(changed, UIControlEvent.EditingChanged);
            field.ShouldReturn = null;
            changed.Dispose();
            controls.texts.Remove(id);
        }
    }
}
