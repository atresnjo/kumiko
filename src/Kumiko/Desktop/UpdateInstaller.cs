namespace Kumiko.Desktop;

public enum UpdateApplyMethod
{
    /// <summary>The process has MSIX package identity; App Installer installs the verified .msix.</summary>
    AppInstaller,
    /// <summary>A portable (zip) installation; a helper swaps the folder after the process exits.</summary>
    PortableSwap
}

/// <summary>A verified Windows update prepared by <see cref="UpdateFeed.InstallAsync"/>.
/// A portable update is applied whenever the application quits normally; <see cref="Restart"/> also relaunches it.
/// An MSIX update is installed only after <see cref="Restart"/> hands it to App Installer.</summary>
public sealed class UpdateInstallation
{
    readonly WindowsUpdateHost host;
    bool restarted;
    volatile bool relaunch;

    internal UpdateInstallation(WindowsUpdateHost host, UpdateApplyMethod method, string version, string path)
    {
        this.host = host;
        Method = method;
        Version = version;
        Path = path;
    }

    public UpdateApplyMethod Method
    {
        get;
    }

    public string Version
    {
        get;
    }

    /// <summary>The verified .msix copy, or the extracted folder that replaces the installation.</summary>
    public string Path
    {
        get;
    }

    internal bool Relaunch => relaunch;

    /// <summary>Quits through the normal cancelable quit path. Call on the UI thread.
    /// MSIX: opens App Installer first, which offers to launch the new version.
    /// Portable: the helper swaps the folder after this process exits, then starts the new executable with the original arguments.
    /// If the quit is cancelled, the portable update is applied (and relaunched) at the next normal quit.</summary>
    public void Restart()
    {
        if (Method == UpdateApplyMethod.AppInstaller && !restarted)
            host.Open(Path);
        restarted = true;
        relaunch = true;
        host.Quit();
    }
}
