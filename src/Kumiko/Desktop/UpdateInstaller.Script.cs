using System.Diagnostics;
using System.Text;

namespace Kumiko.Desktop;

/// <summary>The detached helper for portable updates. It is the system Windows PowerShell running a command
/// generated here (never a downloaded file), passed with -EncodedCommand so no script file can be swapped
/// and no execution policy applies. It uses cmdlets only, so it also works in Constrained Language Mode.</summary>
internal static class WindowsUpdateScript
{
    internal static ProcessStartInfo Helper(WindowsUpdateHost host, WindowsSwapPlan plan, bool relaunch)
    {
        var start = new ProcessStartInfo(host.PowerShell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(plan.Target)!
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(Script(host, plan, relaunch))));
        return start;
    }

    // Each step is one same-folder rename, so the installation is always either the old or the new tree.
    // Rename-Item -NewName is never wildcard-expanded, unlike Move-Item -Destination in Windows PowerShell 5.1.
    static string Script(WindowsUpdateHost host, WindowsSwapPlan plan, bool relaunch) => $$"""
        $ErrorActionPreference = 'Stop'
        $target = {{Literal(plan.Target)}}
        $fresh = {{Literal(plan.Fresh)}}
        $backup = {{Literal(plan.Backup)}}
        $log = {{Literal(plan.Log)}}
        $exe = {{Literal(plan.Executable)}}
        $cwd = {{Literal(host.WorkingDirectory)}}
        $arguments = {{Literal(CommandLine(host.Arguments))}}
        $relaunch = ${{(relaunch ? "true" : "false")}}
        function Start-Application {
            if (-not $relaunch) {
                return
            }
            $directory = $cwd
            if (-not (Test-Path -LiteralPath $directory)) {
                $directory = $target
            }
            try {
                if ($arguments.Length -gt 0) {
                    Start-Process -FilePath $exe -WorkingDirectory $directory -ArgumentList $arguments
                } else {
                    Start-Process -FilePath $exe -WorkingDirectory $directory
                }
            } catch {
                Add-Content -LiteralPath $log -Value "Relaunch failed: $_"
            }
        }
        $old = Get-Process -Id {{host.ProcessId}} -ErrorAction SilentlyContinue
        if ($old -and $old.Path -eq $exe) {
            Wait-Process -InputObject $old -Timeout 600 -ErrorAction SilentlyContinue
        }
        $moved = $false
        for ($attempt = 0; $attempt -lt 120 -and -not $moved; $attempt++) {
            try {
                Rename-Item -LiteralPath $target -NewName {{Literal(Path.GetFileName(plan.Backup))}}
                $moved = $true
            } catch {
                Start-Sleep -Milliseconds 250
            }
        }
        if (-not $moved) {
            Add-Content -LiteralPath $log -Value 'The installation is still in use; the update was not applied.'
            Start-Application
            exit 1
        }
        try {
            Rename-Item -LiteralPath $fresh -NewName {{Literal(Path.GetFileName(plan.Target))}}
        } catch {
            Add-Content -LiteralPath $log -Value "The update could not be moved into place: $_"
            Rename-Item -LiteralPath $backup -NewName {{Literal(Path.GetFileName(plan.Target))}}
            Start-Application
            exit 1
        }
        Start-Application
        Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction SilentlyContinue
        """;

    /// <summary>A single-quoted PowerShell literal. PowerShell also treats typographic single quotes as quotes.</summary>
    internal static string Literal(string value)
    {
        var text = new StringBuilder("'");
        foreach (var c in value)
        {
            if (c is '\'' or '‘' or '’' or '‚' or '‛')
                text.Append(c);
            text.Append(c);
        }
        return text.Append('\'').ToString();
    }

    /// <summary>Joins arguments with the CommandLineToArgvW / MSVC quoting rules.</summary>
    internal static string CommandLine(IEnumerable<string> arguments)
    {
        var line = new StringBuilder();
        foreach (var argument in arguments)
        {
            if (line.Length > 0)
                line.Append(' ');
            if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
            {
                line.Append(argument);
                continue;
            }
            line.Append('"');
            int slashes = 0;
            foreach (var c in argument)
            {
                if (c == '\\')
                {
                    slashes++;
                    continue;
                }
                line.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                line.Append(c);
                slashes = 0;
            }
            line.Append('\\', slashes * 2);
            line.Append('"');
        }
        return line.ToString();
    }
}
