using CoreGraphics;
using Kumiko.UI;
using UIKit;

namespace Kumiko.iOS;

internal sealed partial class UIKitControls
{
    readonly UIButton measure = new(UIButtonType.System);

    public Size MeasureButton(string label, ButtonOptions options)
    {
        Configure(measure, label, options);
        var size = measure.SizeThatFits(new CGSize(options.Width ?? float.MaxValue, float.MaxValue));
        return new(Math.Max(44, (float)size.Width), Math.Max(44, (float)size.Height));
    }

    static void Configure(UIButton button, string label, ButtonOptions options)
    {
        // System configuration owns appearance, highlight, focus, and disabled rendering.
        using var configuration = options.Variant switch
        {
            ButtonVariant.Primary or ButtonVariant.Danger => UIButtonConfiguration.FilledButtonConfiguration,
            ButtonVariant.Secondary => UIButtonConfiguration.TintedButtonConfiguration,
            ButtonVariant.Default or ButtonVariant.Ghost => UIButtonConfiguration.PlainButtonConfiguration,
            _ => throw new NotSupportedException($"UIKit button variant {options.Variant} is not implemented.")
        };
        configuration.Title = label;
        if (options.Variant == ButtonVariant.Danger)
            configuration.BaseBackgroundColor = UIColor.SystemRed;
        button.Configuration = configuration;
        button.Enabled = !options.Disabled;
        button.AccessibilityLabel = label;
    }

}
