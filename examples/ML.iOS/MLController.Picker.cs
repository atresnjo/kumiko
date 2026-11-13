using CoreFoundation;
using Foundation;
using PhotosUI;
using UIKit;

internal sealed partial class MLController
{
    PHPickerViewController? picker;
    PhotoDelegate? pickerDelegate;

    void ChoosePhoto()
    {
        if (picker is not null)
            return;
        using var configuration = new PHPickerConfiguration
        {
            Filter = PHPickerFilter.ImagesFilter,
            SelectionLimit = 1
        };
        pickerDelegate = new PhotoDelegate(this);
        picker = new PHPickerViewController(configuration) { Delegate = pickerDelegate };
        PresentViewController(picker, true, null);
    }

    void Picked(PHPickerResult[] selections)
    {
        if (selections.Length == 0)
        {
            DismissViewController(true, DisposePicker);
            return;
        }
        SetBusy(true);
        status.Text = "Loading selected photo…";
        // Keep the picker and provider alive until loading finishes. The completion
        // runs on a background queue, so decode and downsample there.
        selections[0].ItemProvider.LoadDataRepresentation("public.image", (data, error) =>
        {
            UIImage? selected = null;
            Exception? failure = null;
            try
            {
                if (error is null && data is not null)
                    selected = PhotoLoader.Decode(data);
            }
            catch (Exception decodeError)
            {
                failure = decodeError;
            }
            DispatchQueue.MainQueue.DispatchAsync(() => Guard(() =>
            {
                try
                {
                    if (selected is null)
                        throw new InvalidOperationException(error?.LocalizedDescription ?? "The photo could not be loaded.", failure);
                    SetImage(selected);
                    preview.AccessibilityLabel = "Selected photo";
                }
                finally
                {
                    selected?.Dispose();
                    SetBusy(false);
                    DismissViewController(true, DisposePicker);
                }
            }));
        });
    }

    void DisposePicker()
    {
        if (picker is not null)
            picker.WeakDelegate = null;
        picker?.Dispose();
        picker = null;
        pickerDelegate?.Dispose();
        pickerDelegate = null;
    }

    sealed class PhotoDelegate(MLController owner) : PHPickerViewControllerDelegate
    {
        public override void DidFinishPicking(PHPickerViewController picker, PHPickerResult[] results) =>
            owner.Guard(() => owner.Picked(results));
    }
}
