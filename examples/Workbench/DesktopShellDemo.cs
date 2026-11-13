using System.Text;
using Kumiko.Desktop;
using Kumiko.Rendering;

namespace Kumiko.Examples.Workbench;
/// <summary>Opt-in interactive shell demonstration. Snapshot mode never constructs native shell services.</summary>
static class DesktopShellDemo
{
    internal static void Run(InventoryWorkbench workbench)
    {
        using var app = new App(new("Kumiko desktop shell"));
        void Status(string text) => workbench.Program.Send(new WorkbenchMsg.ShellStatus(text));
        void Copy()
        {
            Clipboard.Write(new(new TransferItem(new TransferRepresentation(TransferFormats.Text, Encoding.UTF8.GetBytes("Inventory selection")), new TransferRepresentation(TransferFormats.HTML, Encoding.UTF8.GetBytes("<strong>Inventory selection</strong>")))));
            Status("Clipboard formats: " + string.Join(", ", Clipboard.Formats));
        }

        Notification? notifications = null;
        void Notify()
        {
            try
            {
                notifications ??= new(app);
                _ = Observe(notifications.Show(new("inventory", "Inventory ready", "The current selection is available.", Group: "workbench"), () =>
                {
                    app.Activate();
                    Status("Notification opened");
                }));
            }
            catch (Exception error)
            {
                Status(error.Message);
            }
        }

        async Task Observe(Task operation)
        {
            try
            {
                await operation.ConfigureAwait(false);
            }
            catch (Exception error)
            {
                try
                {
                    app.Dispatch(() => Status(error.Message));
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        using var menu = new Menu(app, new("Copy text and HTML", Click: _ => Copy()), new("Notify", Click: _ => Notify()), new(Separator: true), new("Quit", Role: MenuRole.Quit));
        app.SetMenu(new MenuItem("Desktop shell", Submenu: [new("Copy formats", Click: _ => Copy()), new("Notify", Click: _ => Notify())]));
        app.NewWindow(new(Title: "Inventory · desktop shell", Width: 1180, Height: 760), workbench.Program);
        Tray? tray = null;
        GlobalShortcut? shortcut = null;
        try
        {
            using var png = new MemoryStream();
            var pixels = new Pixels(new byte[18 * 18 * 4], 18, 18);
            for (int i = 0; i < pixels.Bytes.Length; i += 4)
            {
                pixels.Bytes[i] = 210;
                pixels.Bytes[i + 1] = 120;
                pixels.Bytes[i + 2] = 75;
                pixels.Bytes[i + 3] = 255;
            }

            Png.Write(png, pixels);
            tray = new(app, new("inventory", png.ToArray(), Menu: menu));
            shortcut = new(app);
            if (!shortcut.Register("Primary+Shift+K", Copy))
                Status("Primary+Shift+K is already registered by another application.");
            app.Run();
        }
        finally
        {
            notifications?.Dispose();
            shortcut?.Dispose();
            tray?.Dispose();
        }
    }
}
