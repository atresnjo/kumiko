# Kumiko Launcher

A native launcher example using Kumiko's owned panel, application catalog, icons,
startup controls, tray, global shortcuts and single-instance forwarding.

The default shortcut is Alt+Shift+Space; override with `--shortcut=Control+Alt+K`.
Start-at-login changes only when explicitly selected in Settings. Shortcut
changes last for this process; the CLI override selects the next process's chord.

Safe offscreen commands (use the pinned SDK from AGENTS.md):

```sh
dotnet run --project examples/Launcher -c Release -- \
  --kumiko-snapshot=artifacts/launcher/example.png \
  --kumiko-tree=artifacts/launcher/example.txt --kumiko-dev
dotnet run --project examples/Launcher -c Release -- --dark \
  --kumiko-snapshot=artifacts/launcher/example-dark.png
dotnet run --project tests/Kumiko.Launcher.E2E -c Release -- artifacts/launcher
```

The example is a typed `IKumiko` application. `LauncherApp` owns immutable search
and preference state; `LauncherScreen` renders it; `LauncherController` marshals
service work and retires late results. `NativeLauncherDesktop` uses public APIs;
`FixtureLauncherDesktop` is selected before any desktop service exists in export,
agent, replay and `--hidden` mode. `package.json` gives the macOS bundle the same
startup identity and `LSUIElement` accessory behavior. The icon's source is
`packaging/AppIcon.svg` (`sips -s format png ... --out ...` recreates its PNG).

Interactive execution intentionally installs a tray/shortcut and can launch apps.
It was not run during development; follow the dedicated-runtime checklist in the
launcher guide when those effects are permitted.
