using System.Collections.Immutable;
using CoreGraphics;
using Kumiko.iOS;
using Kumiko.Runtime;
using Kumiko.UI;
using UIKit;

internal static partial class CatalogFlow
{
    static void Images(string output)
    {
        using var format = new UIGraphicsImageRendererFormat { Scale = 1, Opaque = true };
        using var renderer = new UIGraphicsImageRenderer(new CGSize(64, 64), format);
        using var image = renderer.CreateImage(context =>
        {
            UIColor.SystemBlue.SetFill();
            context.FillRect(new CGRect(0, 0, 64, 64));
        });
        using var png = image.AsPNG()!;
        using var program = CatalogValidationApp.CreateProgram(new CatalogValidationModel(0, png.ToArray().ToImmutableArray()));
        using var view = new NativeView(program) { Frame = new CGRect(0, 0, 320, 200) };
        view.Render();
        var native = Find<UIImageView>(view, "image");
        Require(native.Image is { } decoded && decoded.Size.Width == 64 && decoded.Size.Height == 64,
            "Immutable encoded image bytes decode through UIKit");
        Require(native.IsAccessibilityElement && native.AccessibilityLabel == "Blue image" &&
            !Find<UIImageView>(view, "decoration").IsAccessibilityElement, "Named and decorative images have distinct accessibility");
        Capture(view, output, "images");
        view.Render();
        Require(ReferenceEquals(native, Find<UIImageView>(view, "image")), "Unchanged image keeps its native view");
    }

    static void InvalidInputs()
    {
        for (var scenario = 1; scenario <= 11; scenario++)
        {
            using var program = CatalogValidationApp.CreateProgram(new CatalogValidationModel(scenario, default));
            using var view = new NativeView(program) { Frame = new CGRect(0, 0, 320, 400) };
            var rejected = false;
            try
            {
                view.Render();
            }
            catch (ArgumentException)
            {
                rejected = true;
            }
            Require(rejected, "Invalid native input fails explicitly: " + scenario);
        }
        using var replacement = CatalogValidationApp.CreateProgram(new CatalogValidationModel(12, default));
        using var host = new NativeView(replacement) { Frame = new CGRect(0, 0, 320, 400) };
        host.Render();
        var search = Find<UISearchBar>(host, "replacement");
        search.Text = "week";
        search.Delegate!.TextChanged(search, "week");
        replacement.Send(new CatalogValidationMessage.Change());
        host.Render();
        Require(replacement.Model.Edits == 1 && Find<UISegmentedControl>(host, "replacement").SelectedSegment == 0,
            "Replacing a control kind discards a queued value with the same payload type");
    }
}

internal sealed record CatalogValidationModel(int Scenario, ImmutableArray<byte> Image, int Edits = 0)
{
    public NavigationHistory<string> Navigation { get; init; } = NavigationHistory<string>.Start("validation");
}
internal abstract partial record CatalogValidationMessage
{
    public sealed record Change : CatalogValidationMessage;
    public sealed record Navigate(HistoryRequest Request) : CatalogValidationMessage;
}

internal sealed partial class CatalogValidationApp : IKumiko<CatalogValidationModel, CatalogValidationMessage>
{
    public static CatalogValidationModel Init => new(1, default);
    public static (CatalogValidationModel, Cmd<CatalogValidationMessage>) Update(CatalogValidationModel model,
        CatalogValidationMessage message) => (message is CatalogValidationMessage.Navigate navigate
        ? model with
        {
            Navigation = model.Navigation.Move(navigate.Request, "validation")
        }
        : model with
        {
            Scenario = model.Scenario == 12 ? 13 : model.Scenario,
            Edits = model.Edits + 1
        }, Cmd<CatalogValidationMessage>.None);

    public static NavigationRoot<CatalogValidationMessage> Root(CatalogValidationModel model) => NavigationRoot<CatalogValidationMessage>.Create(
        model.Navigation, "validation", _ => new CatalogValidationScreen(new(model.Scenario, model.Image)).Map<CatalogValidationMessage>(static message => message),
        static request => new CatalogValidationMessage.Navigate(request));
}

internal sealed record CatalogValidationProps(int Scenario, ImmutableArray<byte> Image);

/// <summary>Each scenario declares one invalid or replaced native control.</summary>
internal sealed class CatalogValidationScreen(CatalogValidationProps props) : Screen<CatalogValidationProps, CatalogValidationMessage>(props)
{
    protected override void Render()
    {
        var ui = Ui;
        var model = Model;
        using var page = ui.Context.Div(key: "page").SizeFull().Gap(Space.S3);
        switch (model.Scenario)
        {
            case 0:
                ui.Context.NativeImage(model.Image, 96, 96, "Blue image", key: "image");
                ui.Context.NativeSymbol("star", accessibleName: null, key: "decoration");
                break;
            case 1:
                ui.NativeSlider("Range", float.NaN, _ => new CatalogValidationMessage.Change(), key: "invalid");
                break;
            case 2:
                ui.NativeStepper("Count", 2, _ => new CatalogValidationMessage.Change(), step: 0, key: "invalid");
                break;
            case 3:
                ui.NativeSegmentedControl("Choice", [new("same", "One"), new("same", "Two")], "same",
                    _ => new CatalogValidationMessage.Change(), key: "invalid");
                break;
            case 4:
                ui.NativePicker("Choice", [new("a", "One")], "absent", _ => new CatalogValidationMessage.Change(), key: "invalid");
                break;
            case 5:
                ui.NativeDatePicker("Date", CatalogApp.Init.Date, _ => new CatalogValidationMessage.Change(),
                    minimum: CatalogApp.Init.Date.AddDays(1), key: "invalid");
                break;
            case 6:
                ui.NativePageControl("Pages", 2, 2, _ => new CatalogValidationMessage.Change(), key: "invalid");
                break;
            case 7:
                ui.Context.NativeProgress("Progress", 1.5f, key: "invalid");
                break;
            case 8:
                ui.NativeMenu("Commands", [new("same", "One"), new("same", "Two")],
                    _ => new CatalogValidationMessage.Change(), key: "invalid");
                break;
            case 9:
                ui.Context.NativeImage([1, 2, 3], 96, 96, key: "invalid");
                break;
            case 10:
                ui.Context.NativeSymbol("kumiko.nonexistent.symbol", key: "invalid");
                break;
            case 11:
                ui.NativeTextEditor("Notes", "", _ => new CatalogValidationMessage.Change(), height: -1, key: "invalid");
                break;
            case 12:
                ui.NativeSearchField("Period", "day", _ => new CatalogValidationMessage.Change(), key: "replacement");
                break;
            case 13:
                ui.NativeSegmentedControl("Period", [new("day", "Day"), new("week", "Week")], "day",
                    _ => new CatalogValidationMessage.Change(), key: "replacement");
                break;
        }
    }
}
