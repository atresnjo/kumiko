// The window's title bar is ordinary React. `--app-region: drag` lets the
// pointer move the native window; double-clicking it toggles maximize.
// Controls inside opt out with `--app-region: no-drag`.
export function TitleBar({ title }: { title: string }) {
  const bridge = window.kumiko;
  const platform = bridge?.platform;
  return (
    <header className={`titlebar ${platform ?? "browser"}`}>
      <span className="title">{title}</span>
      {platform === "windows" && (
        <div className="window-buttons">
          <button aria-label="Minimize" onClick={() => bridge!.window.minimize()}>–</button>
          <button aria-label="Maximize" onClick={() => bridge!.window.toggleMaximize()}>□</button>
          <button aria-label="Close" className="close" onClick={() => bridge!.window.close()}>×</button>
        </div>
      )}
    </header>
  );
}
