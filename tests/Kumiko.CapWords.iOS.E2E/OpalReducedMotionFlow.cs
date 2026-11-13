using CoreGraphics;
using Kumiko.iOS;
using Kumiko.Runtime;
using Kumiko.UI;
using UIKit;
using static CapWordsFlow;

internal sealed partial class OpalReducedApp : IKumiko<OpalModel, OpalMessage>
{
    public static OpalModel Init => OpalApp.Init;
    public static (OpalModel, Cmd<OpalMessage>) Update(OpalModel model, OpalMessage message) => OpalApp.Update(model, message);

    public static NavigationRoot<OpalMessage> Root(OpalModel model) => NavigationRoot<OpalMessage>.Create(
        model.Navigation, OpalApp.Host, _ => new OpalReducedScreen(model).Map<OpalMessage>(static message => message),
        static request => new OpalMessage.Navigate(request));
}

internal sealed class OpalReducedScreen(OpalModel model) : Screen<OpalModel, OpalMessage>(model)
{
    protected override void Render()
    {
        C.ReduceMotion = true;
        Ui.Child(new OpalView(Model), "timer", static message => message);
    }
}

internal static class OpalReducedMotionFlow
{
    internal static async Task Run(UIView parent, CGRect frame, string output)
    {
        using var program = OpalReducedApp.CreateProgram();
        using var view = new NativeView(program) { Frame = frame };
        parent.AddSubview(view);
        view.Render();
        await Task.Delay(100);
        var button = Find<UIButton>(view, "opal-value");
        button.Highlighted = true;
        Require(button.Layer.AnimationKeys?.Length is null or 0, "Reduced Motion omits native press animation");
        button.Highlighted = false;
        Tap(view, "opal-value");
        view.Render();
        var pose = Find<UIView>(view, "opal-value-pose").Subviews.Single();
        Require(pose.Layer.AnimationKeys?.Length is null or 0 && pose.Layer.Transform.M41 == -46,
            "Reduced Motion applies the final expanded pose immediately");
        var slider = Find<UIView>(view, "opal-slider");
        using var contact = new OpalContact();
        OpalContact.Send(slider, contact, UIGestureRecognizerState.Began, slider.Bounds.Width + 100);
        OpalContact.Send(slider, contact, UIGestureRecognizerState.Changed, slider.Bounds.Width + 100);
        view.Render();
        Require(program.Model.Index == 35 && slider.Subviews.Single().Layer.Transform.IsIdentity &&
            Find<UIView>(slider, "kumiko-rubber-stretch").Layer.Transform.IsIdentity,
            "Reduced Motion keeps native duration selection without stretch");
        Capture(view, output, "opal-reduced-motion");
        OpalContact.Send(slider, contact, UIGestureRecognizerState.Cancelled, 0);
        File.WriteAllText(Path.Combine(output, "opal-reduced-motion-report.json"),
            """{"passed":true,"contextPreference":true,"instantPresentation":true,"noPressAnimation":true,"selectionWithoutStretch":true} """);
        view.RemoveFromSuperview();
    }
}
