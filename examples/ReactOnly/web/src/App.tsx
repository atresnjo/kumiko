import { useState } from "react";
import { Native } from "./kumiko/client.js";
import { TitleBar } from "./TitleBar";
import { bridged, useNativeInfo } from "./useNative";

export function App() {
  const info = useNativeInfo();
  const [text, setText] = useState("Write something, then save it with a native dialog.");
  const [path, setPath] = useState<string | null>(null);
  const [status, setStatus] = useState("Ready");

  // Every native call can fail (cancelled permissions, unsupported platform…); show why.
  const run = (action: () => Promise<void>) => () => action().catch((e: Error) => setStatus(e.message));

  const open = run(async () => {
    const file = await Native.openText({});
    if (!file) return;
    setText(file.text);
    setPath(file.path);
    setStatus("Opened " + file.path);
  });
  const save = run(async () => {
    const saved = await Native.saveText({ name: fileName(path), text });
    if (!saved) return;
    setPath(saved);
    setStatus("Saved " + saved);
  });
  const copy = run(async () => {
    await Native.copyText({ text });
    setStatus(`Copied ${text.length} characters`);
  });
  const paste = run(async () => {
    setText(await Native.readText({}));
    setStatus("Pasted from the clipboard");
  });
  const notify = run(async () => {
    await Native.notify({ title: "React Only", body: `${text.length} characters in ${fileName(path)}` });
    setStatus("Notification sent");
  });
  const reveal = run(async () => {
    if (path) await Native.reveal({ path });
  });

  return (
    <div className="app">
      <TitleBar title={path ? fileName(path) : "React Only"} />
      {!bridged ? (
        <main className="content">
          <p className="muted">
            Running in a plain browser. Start the desktop host with
            <code> --dev-server=http://localhost:5173 </code> to reach native features.
          </p>
        </main>
      ) : (
        <main className="content">
          <div className="toolbar">
            <button onClick={open}>Open…</button>
            <button onClick={save}>Save…</button>
            <button onClick={reveal} disabled={!path}>Show in folder</button>
            <span className="spacer" />
            <button data-testid="copy" onClick={copy}>Copy</button>
            <button data-testid="paste" onClick={paste}>Paste</button>
            <button onClick={notify}>Notify</button>
          </div>
          <textarea data-testid="editor" value={text} onChange={(e) => setText(e.target.value)} spellCheck={false} />
          <footer>
            <span data-testid="status">{status}</span>
            <span className="muted">
              <span data-testid="platform">{info?.platform ?? "…"}</span> · {info?.dark ? "dark" : "light"}
            </span>
          </footer>
        </main>
      )}
    </div>
  );
}

function fileName(path: string | null) {
  return path?.split(/[\\/]/).pop() ?? "untitled.txt";
}
