using Kumiko.UI;
using UIKit;

namespace Kumiko.iOS;

internal sealed partial class UIKitControls
{
    sealed class PickerOwner(UIKitControls renderer, UIPickerView picker, ulong id) : UIPickerViewModel
    {
        NativeChoiceContent? content;
        bool stopped;

        internal void Update(NativeChoiceContent next)
        {
            var reload = content is null || !content.Items.SequenceEqual(next.Items);
            content = next;
            if (reload)
                picker.ReloadAllComponents();
            picker.Select(next.Items.IndexOf(next.Items.First(item => item.Id == next.SelectedId)), 0, false);
            picker.AccessibilityLabel = next.Label;
        }

        public override nint GetComponentCount(UIPickerView pickerView) => 1;
        public override nint GetRowsInComponent(UIPickerView pickerView, nint component) => content?.Items.Length ?? 0;
        public override string GetTitle(UIPickerView pickerView, nint row, nint component) =>
            content is { } snapshot && row >= 0 && row < snapshot.Items.Length ? snapshot.Items[(int)row].Label : "";
        public override void Selected(UIPickerView pickerView, nint row, nint component) => renderer.Guard(() =>
        {
            if (!stopped && content is { } snapshot && row >= 0 && row < snapshot.Items.Length)
                renderer.QueueValue(id, snapshot.Items[(int)row].Id);
        });

        protected override void Dispose(bool disposing)
        {
            if (disposing && !stopped)
            {
                stopped = true;
                picker.Model = null;
                content = null;
            }
            base.Dispose(disposing);
        }
    }

    sealed class SearchOwner(UIKitControls renderer, UISearchBar search, ulong id) : UISearchBarDelegate
    {
        bool stopped;
        bool updating;

        internal void Update(NativeSearchContent content, bool enabled)
        {
            updating = true;
            try
            {
                if (!renderer.values.ContainsKey(id) && search.Text != content.Value && search.SearchTextField.MarkedTextRange is null)
                    search.Text = content.Value;
                search.Placeholder = content.Placeholder;
                search.SearchTextField.Enabled = enabled;
                search.AccessibilityLabel = content.Label;
                search.SearchTextField.AccessibilityLabel = content.Label;
            }
            finally
            {
                updating = false;
            }
        }

        public override void TextChanged(UISearchBar searchBar, string searchText) => renderer.Guard(() =>
        {
            if (!stopped && !updating && search.SearchTextField.MarkedTextRange is null)
                renderer.QueueValue(id, searchText);
        });

        public override void SearchButtonClicked(UISearchBar searchBar) => renderer.Guard(() => search.ResignFirstResponder());

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                stopped = true;
                search.WeakDelegate = null;
            }
            base.Dispose(disposing);
        }
    }

    sealed class EditorOwner(UIKitControls renderer, UITextView editor, ulong id) : UITextViewDelegate
    {
        bool stopped;
        bool updating;

        internal void Update(NativeEditorContent content, bool enabled)
        {
            updating = true;
            try
            {
                if (!renderer.values.ContainsKey(id) && editor.Text != content.Value && editor.MarkedTextRange is null)
                {
                    var selection = editor.SelectedRange;
                    editor.Text = content.Value;
                    var length = (long)content.Value.Length;
                    var location = Math.Clamp((long)selection.Location, 0, length);
                    editor.SelectedRange = new((nint)location, (nint)Math.Clamp((long)selection.Length, 0, length - location));
                }
                editor.Editable = enabled;
                editor.Selectable = enabled;
                editor.AccessibilityLabel = content.Label;
            }
            finally
            {
                updating = false;
            }
        }

        public override void Changed(UITextView textView) => renderer.Guard(() =>
        {
            if (!stopped && !updating && editor.MarkedTextRange is null)
                renderer.QueueValue(id, editor.Text ?? "");
        });

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                stopped = true;
                editor.WeakDelegate = null;
            }
            base.Dispose(disposing);
        }
    }
}
