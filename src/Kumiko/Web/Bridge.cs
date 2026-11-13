namespace Kumiko.Web;

internal static class Bridge
{
    internal const string Source = """
// The page runtime, injected at document start into every page's main frame.
// It owns the wire protocol (see README): calls go out as JSON strings,
// replies and events come back batched through __kumiko.receive(document, [...]).
(() => {
  if (window !== window.top || window.kumiko) return;
  const identity = () => Array.from(crypto.getRandomValues(new Uint8Array(16)), b => b.toString(16).padStart(2, "0")).join("");
  const root = identity();
  let generation = identity();
  const post = (m, document = generation) => {
    m.root = root;
    m.document = document;
    m.nonce = __KUMIKO_NONCE__;
    const data = JSON.stringify(m);
    if (window.chrome?.webview) window.chrome.webview.postMessage(data);
    else window.webkit.messageHandlers.kumiko.postMessage(data);
  };
  const pending = new Map();
  const listeners = new Map();
  const streams = new Map();
  let nextId = 1;

  const call = (method, ...args) =>
    new Promise((resolve, reject) => {
      const id = nextId++;
      pending.set(id, { resolve, reject });
      post({ t: "call", id, m: method, a: args });
    });

  const stream = async function* (method, argument) {
    const document = generation;
    const id = nextId++;
    post({t:"stream-open", id, m:method, a:argument}, document);
    try {
      while (true) {
        if (document !== generation) throw new Error("Document navigation");
        const item = await new Promise((resolve, reject) => {
          streams.set(id, {resolve, reject});
          post({t:"stream-next", id}, document);
        });
        if (item.done) return;
        yield item.v;
      }
    } finally {
      if (document === generation) streams.delete(id);
      post({t:"stream-cancel", id}, document);
    }
  };

  const on = (name, fn) => {
    let set = listeners.get(name);
    if (!set) listeners.set(name, (set = new Set()));
    set.add(fn);
    return () => set.delete(fn);
  };
  const once = (name, fn) => {
    const off = on(name, (p) => {
      off();
      fn(p);
    });
    return off;
  };

  const receive = (document, messages) => {
    if (document !== generation) return;
    for (const m of messages) {
      if (m.t === "stream-item") {
        const item = streams.get(m.id);
        if (!item) continue;
        streams.delete(m.id);
        if (m.error) item.reject(new Error(m.error));
        else item.resolve(m);
      } else if (m.t === "reply") {
        const p = pending.get(m.id);
        if (!p) continue;
        pending.delete(m.id);
        if (m.ok) p.resolve(m.v);
        else p.reject(new Error(m.e));
      } else if (m.t === "event") {
        const set = listeners.get(m.n);
        if (!set) continue;
        for (const fn of [...set]) {
          try {
            fn(m.p);
          } catch (e) {
            console.error(e);
          }
        }
      }
    }
  };

  // Prints to the app's stderr: for tests run with --kumiko-eval.
  const log = (...args) =>
    post({ t: "log", v: args.map((a) => (typeof a === "string" ? a : JSON.stringify(a))).join(" ") });

  const win = Object.freeze({
    close: () => post({ t: "win", op: "close" }),
    minimize: () => post({ t: "win", op: "minimize" }),
    toggleMaximize: () => post({ t: "win", op: "zoom" }),
    setTitle: (title) => post({ t: "win", op: "title", v: String(title) }),
  });

  const renew = (target, recovery) => {
    if (target !== root) return;
    const stale = [...pending.values(), ...streams.values()];
    pending.clear();
    streams.clear();
    generation = identity();
    nextId = 1;
    post({ t: "document-start", recovery });
    post({ t: "dom-ready" });
    for (const p of stale) p.reject(new Error("Document navigation"));
  };
  Object.defineProperty(window, "__kumiko", { value: Object.freeze({ receive, renew }) });
  Object.defineProperty(window, "kumiko", {
    value: Object.freeze({ call, stream, on, once, log, window: win, platform: "__KUMIKO_PLATFORM__", version: "0.2.0" }),
  });

  post({ t: "document-start" });
  const ready = () => post({ t: "dom-ready" });
  window.addEventListener("pageshow", e => {
    if (!e.persisted) return;
    renew(root);
  });
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", ready, { once: true });
  else ready();

  // Frameless windows: elements styled `--app-region: drag` move the window.
  document.addEventListener(
    "mousedown",
    (e) => {
      if (e.button !== 0) return;
      for (let el = e.target; el && el.nodeType === 1; el = el.parentElement) {
        const region = getComputedStyle(el).getPropertyValue("--app-region").trim();
        if (region === "no-drag") return;
        if (region === "drag") {
          post({ t: e.detail === 2 ? "dblclick" : "drag" });
          return;
        }
      }
    },
    true,
  );
})();

""";
}
