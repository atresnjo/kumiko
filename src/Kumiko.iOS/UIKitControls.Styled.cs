using Foundation;
using Kumiko.UI;
using UIKit;

namespace Kumiko.iOS;

// App-styled inputs: native editing, IME and selection; typography and colors from the app's palette.
internal sealed partial class UIKitControls
{
    Entry? CreateStyled(NativeControlNode node)
    {
        if (node.Content is not NativeFieldContent)
            return null;
        var field = new UITextField
        {
            BorderStyle = UITextBorderStyle.None,
            BackgroundColor = UIColor.Clear,
            ReturnKeyType = UIReturnKeyType.Done,
            ClearButtonMode = UITextFieldViewMode.WhileEditing
        };
        return new(field, Owner: new StyledFieldOwner(this, field, node.Id));
    }

    static void UpdateStyled(Entry entry, NativeControlNode node)
    {
        if (node.Content is NativeFieldContent field)
            ((StyledFieldOwner)entry.Owner!).Update(field, node.Enabled && !node.Inert);
    }

    static UIFont InputFont(NativeInputStyle style) => NativePaint.Font(style.Text.Size, NativePaint.Weight(style.Text.Weight));

    /// <summary>Applies app typography to a UITextView editor and keeps its placeholder in sync.</summary>
    static void StyleEditor(UITextView editor, NativeEditorContent content)
    {
        if (content.Style is not { } style)
            return;
        using var font = InputFont(style);
        using var text = NativePaint.Color(style.Text.Color ?? style.Placeholder);
        using var tint = NativePaint.Color(style.Tint);
        editor.Font = font;
        editor.TextColor = text;
        editor.TintColor = tint;
        editor.BackgroundColor = UIColor.Clear;
        editor.TextContainerInset = new UIEdgeInsets(8, 0, 8, 0);
        editor.TextContainer.LineFragmentPadding = 0;
        editor.KeyboardAppearance = style.Dark ? UIKeyboardAppearance.Dark : UIKeyboardAppearance.Light;
        var placeholder = editor.Subviews.OfType<EditorPlaceholder>().FirstOrDefault();
        if (placeholder is null)
        {
            placeholder = new EditorPlaceholder();
            editor.AddSubview(placeholder);
        }
        using var faint = NativePaint.Color(style.Placeholder);
        placeholder.Font = font;
        placeholder.TextColor = faint;
        placeholder.Text = content.Placeholder;
        placeholder.Hidden = content.Placeholder.Length == 0 || (editor.Text?.Length ?? 0) > 0;
        placeholder.SizeToFit();
        placeholder.Frame = new(editor.TextContainerInset.Left, editor.TextContainerInset.Top,
            placeholder.Frame.Width, placeholder.Frame.Height);
    }

    sealed class EditorPlaceholder : UILabel
    {
        internal EditorPlaceholder()
        {
            UserInteractionEnabled = false;
            IsAccessibilityElement = false;
        }
    }

    /// <summary>Owns a styled field's editing callbacks; typed text reaches the model through the value queue.</summary>
    sealed class StyledFieldOwner : IDisposable
    {
        readonly UIKitControls renderer;
        readonly UITextField field;
        readonly ulong id;
        readonly UIAction changed;
        NativeInputStyle? style;
        string placeholder = "";

        internal StyledFieldOwner(UIKitControls renderer, UITextField field, ulong id)
        {
            this.renderer = renderer;
            this.field = field;
            this.id = id;
            changed = UIAction.Create(_ => renderer.Guard(() =>
            {
                if (field.MarkedTextRange is null)
                    renderer.QueueValue(id, field.Text ?? "");
            }));
            field.AddAction(changed, UIControlEvent.EditingChanged);
            field.ShouldReturn = _ =>
            {
                renderer.Guard(() => field.ResignFirstResponder());
                return false;
            };
        }

        internal void Update(NativeFieldContent content, bool enabled)
        {
            // An edit waiting for the next frame is newer than the model; never overwrite it or marked text.
            if (!renderer.values.ContainsKey(id) && field.Text != content.Value && field.MarkedTextRange is null)
                field.Text = content.Value;
            if (style != content.Style || placeholder != content.Placeholder)
            {
                style = content.Style;
                placeholder = content.Placeholder;
                using var font = InputFont(content.Style);
                using var text = NativePaint.Color(content.Style.Text.Color ?? content.Style.Placeholder);
                using var tint = NativePaint.Color(content.Style.Tint);
                using var faint = NativePaint.Color(content.Style.Placeholder);
                field.Font = font;
                field.TextColor = text;
                field.TintColor = tint;
                field.KeyboardAppearance = content.Style.Dark ? UIKeyboardAppearance.Dark : UIKeyboardAppearance.Light;
                var attributes = new UIStringAttributes { ForegroundColor = faint, Font = font };
                field.AttributedPlaceholder = new NSAttributedString(content.Placeholder, attributes);
            }
            field.Enabled = enabled;
            field.AccessibilityLabel = content.Label;
        }

        public void Dispose()
        {
            field.RemoveAction(changed, UIControlEvent.EditingChanged);
            field.ShouldReturn = null;
            changed.Dispose();
        }
    }
}
