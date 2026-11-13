using Kumiko.iOS;
using KumikoiOSTemplate;

// UIKit owns the event loop. Each scene restores its model after the system ends the process in the background.
NativeApplication.Run(args, KumikoiOSTemplateApp.Create, new NativeHostOptions { RestoreState = true });
