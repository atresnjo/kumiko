# Kumiko.iOS

Native UIKit host and controls for [Kumiko](https://www.nuget.org/packages/Kumiko)
applications. Kumiko views reconcile real UIKit controls (`UIButton`,
`UITextField`, `UITableView`, system sheets, navigation stacks and more); UIKit
owns their appearance, feedback and accessibility. The same model, messages,
update and screens can build for desktop and iOS.

Start a new app with the template:

```sh
dotnet new install Kumiko.Templates
dotnet new kumiko-ios -n MyApp
```

Or host an existing program yourself:

```csharp
static void Main(string[] args) => NativeApplication.Run(args, MyApp.Create,
    new NativeHostOptions { RestoreState = true });
```

Info.plist must name `KumikoSceneDelegate` as the application scene delegate.
`NativeViewController` hosts a program inside an existing UIKit hierarchy, and
`NativeLifecycle.Phases` delivers active/inactive/background transitions as
messages. Requires the .NET 10 iOS workload and Xcode on macOS.
