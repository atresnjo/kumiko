using Kumiko;
using Kumiko.Examples.Workbench;

using var workbench = new InventoryWorkbench();
if (args.Contains("--dark", StringComparer.Ordinal))
    workbench.Program.Send(new WorkbenchMsg.Theme(WorkbenchTheme.Dark));
if (Dev.Option(args, "--kumiko-snapshot") is null && Dev.Option(args, "--kumiko-tree") is null)
    workbench.Program.Send(new WorkbenchMsg.Load());
if (args.Contains("--desktop-shell", StringComparer.Ordinal))
{
    workbench.Program.Send(new WorkbenchMsg.ShellStatus("Desktop shell · clipboard: text/plain + text/html · tray: inventory · shortcut: Primary+Shift+K"));
    if (Dev.Option(args, "--kumiko-snapshot") is null && Dev.Option(args, "--kumiko-tree") is null)
    {
        DesktopShellDemo.Run(workbench);
        return;
    }
}

Dev.Run("Inventory workbench", 1180, 760, workbench.Program, args);
