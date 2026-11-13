using System.Security.Cryptography;
using System.Text;
using Foundation;
using Kumiko.Runtime;
using UIKit;

namespace Kumiko.iOS;

/// <summary>
/// Starts a UIKit application whose window hosts one Kumiko program. Info.plist must name
/// <c>KumikoSceneDelegate</c> as the application scene delegate. Apps that need their own delegates can host
/// <see cref="NativeViewController"/> directly instead.
/// </summary>
public static class NativeApplication
{
    /// <summary>The Objective-C name to use for UISceneDelegateClassName.</summary>
    public const string SceneDelegateName = "KumikoSceneDelegate";

    static Func<IProgram>? factory;
    static NativeHostOptions configured = new();

    /// <summary>Validates the bundle configuration, then runs UIKit. Does not return while the app runs.</summary>
    public static void Run(string[] args, Func<IProgram> create, NativeHostOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(create);
        if (!NSThread.IsMain)
            throw new InvalidOperationException("NativeApplication.Run must be called on the main thread.");
        if (factory is not null)
            throw new InvalidOperationException("NativeApplication.Run was already called.");
        Validate(NSBundle.MainBundle.InfoDictionary);
        factory = create;
        configured = options ?? new();
        UIApplication.Main(args, null, typeof(KumikoAppDelegate));
    }

    /// <summary>The state file for a scene session. Identifiers are hashed, so any value maps inside the directory.</summary>
    public static string StatePath(string sceneIdentifier, string? directory = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(sceneIdentifier);
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sceneIdentifier))).ToLowerInvariant();
        return Path.Combine(directory ?? StateDirectory(), name + ".json");
    }

    /// <summary>Removes the state of scene sessions the system has discarded.</summary>
    public static void DiscardState(IEnumerable<string> sceneIdentifiers, string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(sceneIdentifiers);
        foreach (var identifier in sceneIdentifiers)
            File.Delete(StatePath(identifier, directory));
    }

    internal static (Func<IProgram> Create, NativeHostOptions Options) Configuration() => factory is { } create
        ? (create, configured)
        : throw new InvalidOperationException($"{SceneDelegateName} needs NativeApplication.Run in the app's Main.");

    static string StateDirectory()
    {
        var support = NSFileManager.DefaultManager.GetUrls(NSSearchPathDirectory.ApplicationSupportDirectory, NSSearchPathDomain.User)[0];
        return Path.Combine(support.Path!, "Kumiko", "Scenes");
    }

    static void Validate(NSDictionary? info)
    {
        var manifest = info?["UIApplicationSceneManifest"] as NSDictionary;
        if (manifest?["UIApplicationSupportsMultipleScenes"] is NSNumber { BoolValue: true })
            throw new InvalidOperationException("Kumiko hosts one scene; set UIApplicationSupportsMultipleScenes to false in Info.plist.");
        var roles = (manifest?["UISceneConfigurations"] as NSDictionary)?["UIWindowSceneSessionRoleApplication"] as NSArray;
        for (nuint i = 0; roles is not null && i < roles.Count; i++)
            if (roles.GetItem<NSDictionary>(i)?["UISceneDelegateClassName"]?.ToString() == SceneDelegateName)
                return;
        throw new InvalidOperationException(
            $"Info.plist must name {SceneDelegateName} as UISceneDelegateClassName under UIApplicationSceneManifest/UISceneConfigurations/UIWindowSceneSessionRoleApplication.");
    }
}

/// <summary>Application delegate used by <see cref="NativeApplication"/>.</summary>
[Register("KumikoAppDelegate")]
public sealed class KumikoAppDelegate : UIApplicationDelegate
{
    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions) => true;

    public override void DidDiscardSceneSessions(UIApplication application, NSSet<UISceneSession> sceneSessions)
    {
        try
        {
            NativeApplication.DiscardState(sceneSessions.ToArray().Select(session => session.PersistentIdentifier));
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
        }
    }
}

/// <summary>Scene delegate used by <see cref="NativeApplication"/>: one window whose root hosts the program.</summary>
[Register("KumikoSceneDelegate")]
public sealed class KumikoSceneDelegate : UIWindowSceneDelegate
{
    public override UIWindow? Window
    {
        get; set;
    }

    public override void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        try
        {
            if (scene is not UIWindowScene windowScene)
                return;
            var (create, options) = NativeApplication.Configuration();
            if (options.RestoreState)
                options = options with
                {
                    StatePath = NativeApplication.StatePath(session.PersistentIdentifier)
                };
            Window = new UIWindow(windowScene)
            {
                RootViewController = new NativeViewController(create, options)
            };
            Window.MakeKeyAndVisible();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
        }
    }

    public override void DidDisconnect(UIScene scene)
    {
        try
        {
            var controller = Window?.RootViewController as NativeViewController;
            controller?.SaveState();
            if (Window is { } window)
                window.RootViewController = null;
            Window?.Dispose();
            Window = null;
            controller?.Dispose();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
        }
    }
}
