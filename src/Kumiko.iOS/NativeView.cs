using CoreGraphics;
using Foundation;
using Kumiko.Runtime;
using Kumiko.UI;
using UIKit;

namespace Kumiko.iOS;

/// <summary>Experimental UIKit host for a Kumiko view containing supported native UIKit components.</summary>
/// <remarks>The caller owns the program. Construct, render, and dispose this view on the main thread.</remarks>
public sealed class NativeView : UIView
{
    readonly IProgram program;
    readonly Context context;
    readonly UIKitControls renderer;
    readonly IDisposable connection;
    readonly IDisposable hotReload;
    int pending;
    volatile bool stopped;
    bool rendering;
    CGSize renderedSize;
    UITraitCollection? renderedTraits;
    bool renderedReduceMotion;
    UIEdgeInsets renderedSafeArea;

    /// <summary>Receives callback errors on the UI thread. Exceptions never cross the UIKit callback.</summary>
    public event Action<Exception>? UnhandledException;

    public NativeView(IProgram program) : base(InitialFrame())
    {
        CheckMainThread();
        ArgumentNullException.ThrowIfNull(program);
        this.program = program;
        renderer = new(this, Invoke, Guard, RequestFrame);
        context = new()
        {
            ControlRenderer = renderer
        };
        try
        {
            connection = program.ConnectHost(RequestFrame);
        }
        catch
        {
            context.Dispose();
            renderer.Dispose();
            base.Dispose(true);
            throw;
        }
        program.ModelChanged += ModelChanged;
        BackgroundColor = UIColor.SystemBackground;
        hotReload = HotReload.RegisterHost(RequestFrame);
    }

    static void CheckMainThread()
    {
        if (!NSThread.IsMain)
            throw new InvalidOperationException("UIKit views must run on the main thread.");
    }

    static CGRect InitialFrame()
    {
        CheckMainThread();
        return CGRect.Empty;
    }

    /// <summary>Builds the Kumiko view and reconciles native controls within this view's bounds.</summary>
    public void Render()
    {
        CheckMainThread();
        ObjectDisposedException.ThrowIf(stopped, this);
        if (rendering || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        rendering = true;
        try
        {
            renderer.NavigationGeneration = context.ProgramViewVersion;
            context.ReduceMotion = UIAccessibility.IsReduceMotionEnabled;
            context.Size = new((float)Bounds.Width, (float)Bounds.Height);
            context.HorizontalSizeClassOverride = MapSizeClass(TraitCollection.HorizontalSizeClass);
            context.VerticalSizeClassOverride = MapSizeClass(TraitCollection.VerticalSizeClass);
            var safe = SafeAreaInsets;
            context.SafeAreaInsets = new((float)safe.Top, (float)safe.Right, (float)safe.Bottom, (float)safe.Left);
            context.Frame(program.View);
            renderedSafeArea = safe;
            renderedSize = Bounds.Size;
            renderedTraits = TraitCollection;
            renderedReduceMotion = context.ReduceMotion;
            // Semantic invocations are consumed one per frame; coalescing must not drop a burst.
            if (context.NextFrame == TimeSpan.Zero)
                RequestFrame();
        }
        finally
        {
            rendering = false;
        }
    }

    void RequestFrame()
    {
        if (stopped || Interlocked.Exchange(ref pending, 1) != 0)
            return;
        // DispatchQueue remains valid even if the UIView is disposed before the queued work runs.
        CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            Interlocked.Exchange(ref pending, 0);
            if (!stopped)
                Guard(Render);
        });
    }

    void ModelChanged()
    {
        if (!rendering)
            RequestFrame();
    }

    void Invoke(ulong id) => Guard(() =>
    {
        if (!stopped && context.PerformNativeAction(id).Succeeded)
            RequestFrame();
    });

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        // Child controls invalidate UIKit layout while reconciling. Only changes to
        // the host environment require another application frame; model/input wakes
        // and explicit Render calls continue to rebuild unconditionally.
        if (!stopped && (renderedSafeArea != SafeAreaInsets || renderedSize != Bounds.Size || renderedTraits?.IsEqual(TraitCollection) != true ||
            renderedReduceMotion != UIAccessibility.IsReduceMotionEnabled))
            Guard(Render);
    }

    static SizeClass? MapSizeClass(UIUserInterfaceSizeClass value) => value switch
    {
        UIUserInterfaceSizeClass.Compact => SizeClass.Compact,
        UIUserInterfaceSizeClass.Regular => SizeClass.Regular,
        _ => null
    };

    public override void SafeAreaInsetsDidChange()
    {
        base.SafeAreaInsetsDidChange();
        if (!stopped)
            SetNeedsLayout();
    }

    // This callback also covers the supported iOS 15/16 hosts; newer UIKit still
    // delivers it for legacy trait observation. Do not create a polling source.
#pragma warning disable CA1422
    public override void TraitCollectionDidChange(UITraitCollection? previousTraitCollection)
    {
        base.TraitCollectionDidChange(previousTraitCollection);
        if (!stopped)
            SetNeedsLayout();
    }
#pragma warning restore CA1422

    void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error)
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
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !stopped)
        {
            CheckMainThread();
            stopped = true;
            hotReload.Dispose();
            program.ModelChanged -= ModelChanged;
            connection.Dispose();
            renderer.Dispose();
            context.Dispose();
            renderedTraits = null;
        }
        base.Dispose(disposing);
    }
}
