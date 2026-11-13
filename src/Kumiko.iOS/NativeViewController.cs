using CoreFoundation;
using Foundation;
using Kumiko.Runtime;
using UIKit;

namespace Kumiko.iOS;

/// <summary>
/// Hosts one Kumiko program in a view controller. The controller creates the program when its view loads, owns
/// the program and its <see cref="NativeView"/>, and disposes both. Use it as a window root or inside an existing
/// UIKit hierarchy. Construct, present and dispose it on the main thread.
/// </summary>
public sealed class NativeViewController : UIViewController
{
    readonly Func<IProgram> create;
    readonly NativeHostOptions options;
    NSObject? background;

    public NativeViewController(Func<IProgram> create, NativeHostOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(create);
        if (!NSThread.IsMain)
            throw new InvalidOperationException("UIKit controllers must be created on the main thread.");
        this.create = create;
        this.options = options ?? new();
    }

    /// <summary>Startup, render and state errors. Exceptions never cross a UIKit callback.</summary>
    public event Action<Exception>? UnhandledException;

    /// <summary>The hosted program, once the view has loaded and until disposal.</summary>
    public IProgram? Program
    {
        get; private set;
    }

    /// <summary>The native content view, once the view has loaded and until disposal.</summary>
    public NativeView? Content
    {
        get; private set;
    }

    public NativeHostOptions Options => options;

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        var view = View!;
        view.BackgroundColor = options.Background ?? UIColor.SystemBackground;
        if (options.InterfaceStyle != UIUserInterfaceStyle.Unspecified)
            OverrideUserInterfaceStyle = options.InterfaceStyle;
        IProgram? program = null;
        try
        {
            program = create() ?? throw new InvalidOperationException("The program factory returned null.");
            if (options.StatePath is { } path)
                Restore(program, path);
            var content = new NativeView(program)
            {
                BackgroundColor = view.BackgroundColor
            };
            content.UnhandledException += Report;
            view.AddSubview(content);
            Program = program;
            Content = content;
        }
        catch (Exception error)
        {
            program?.Dispose();
            Report(error);
            return;
        }
        if (options.StatePath is not null)
            background = NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.DidEnterBackgroundNotification, _ => OnMain(SaveState));
    }

    public override void ViewDidLayoutSubviews()
    {
        base.ViewDidLayoutSubviews();
        if (Content is { } content)
            content.Frame = options.EdgeToEdge ? View!.Bounds : View!.SafeAreaLayoutGuide.LayoutFrame;
    }

    /// <summary>Writes the current model to <see cref="NativeHostOptions.StatePath"/>, if restoration is enabled.</summary>
    public void SaveState()
    {
        if (Program is not { } program || options.StatePath is not { } path)
            return;
        try
        {
            program.SaveModel(path);
        }
        catch (Exception error)
        {
            Report(error);
        }
    }

    void Restore(IProgram program, string path)
    {
        if (!File.Exists(path))
            return;
        // A missing, malformed or schema-incompatible file starts from Init; remove it so it is not retried.
        if (!program.RestoreModel(path))
            File.Delete(path);
    }

    void OnMain(Action action)
    {
        if (NSThread.IsMain)
            action();
        else
            DispatchQueue.MainQueue.DispatchAsync(action);
    }

    void Report(Exception error)
    {
        try
        {
            if (UnhandledException is { } handler)
                handler(error);
            else
                Console.Error.WriteLine(error);
        }
        catch (Exception reportingError)
        {
            Console.Error.WriteLine(reportingError);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (background is not null)
            {
                NSNotificationCenter.DefaultCenter.RemoveObserver(background);
                background.Dispose();
                background = null;
            }
            Content?.RemoveFromSuperview();
            Content?.Dispose();
            Content = null;
            Program?.Dispose();
            Program = null;
        }
        base.Dispose(disposing);
    }
}
