import { useEffect, useState } from "react";
import { Host } from "./kumiko/client.js";

type Info = Awaited<ReturnType<typeof Host.info>>;

// Outside the desktop host (for example `npm run dev` in a normal browser) there is no bridge.
const bridged = window.kumiko !== undefined;

export function App() {
  const [count, setCount] = useState<number | null>(null);
  const [info, setInfo] = useState<Info | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (bridged) Host.info({}).then(setInfo, (e: Error) => setError(e.message));
  }, []);

  // The native count arrives as a typed AsyncIterable. Breaking out of the loop
  // cancels the C# producer; a loop waiting for its next value stops after it.
  useEffect(() => {
    if (!bridged) return;
    let active = true;
    (async () => {
      try {
        for await (const value of Host.watch({})) {
          if (!active) break;
          setCount(value);
        }
      } catch (e) {
        if (active) setError((e as Error).message);
      }
    })();
    return () => {
      active = false;
    };
  }, []);

  const add = () => Host.add({ by: 1 }).catch((e: Error) => setError(e.message));

  if (!bridged) {
    return (
      <main className="page">
        <h1>React + Vite</h1>
        <p className="muted">
          This page is running in a plain browser. Start the desktop host with
          <code> --dev-server=http://localhost:5173 </code> to connect it to Kumiko.
        </p>
      </main>
    );
  }

  return (
    <main className="page">
      <header>
        <h1>React + Vite inside Kumiko</h1>
        <p className="muted">
          This is a normal Vite React app. The count below is owned by the native
          Kumiko model and streamed into React; the button calls a typed C# service.
        </p>
      </header>

      <section className="card counter">
        <span className="label">Shared count</span>
        <span className="count" data-testid="count">{count ?? "…"}</span>
        <button data-testid="add" onClick={add} disabled={count === null}>
          Add from React
        </button>
      </section>

      <section className="card">
        <dl>
          <dt>Served</dt>
          <dd data-testid="mode">{info?.mode ?? "…"}</dd>
          <dt>Platform</dt>
          <dd>{info?.platform ?? "…"}</dd>
          <dt>Runtime</dt>
          <dd>{info?.runtime ?? "…"}</dd>
        </dl>
      </section>

      {error && <p className="error" role="alert">{error}</p>}
    </main>
  );
}
