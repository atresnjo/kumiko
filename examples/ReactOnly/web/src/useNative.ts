import { useEffect, useState } from "react";
import { Native, onAppearance } from "./kumiko/client.js";

export type Info = Awaited<ReturnType<typeof Native.info>>;

// Outside the desktop host (for example `npm run dev` in a normal browser) there is no bridge.
export const bridged = window.kumiko !== undefined;

/** Native platform info, kept in sync with the system's light/dark appearance. */
export function useNativeInfo() {
  const [info, setInfo] = useState<Info | null>(null);
  useEffect(() => {
    if (!bridged) return;
    Native.info({}).then(setInfo);
    return onAppearance((dark) => setInfo((current) => (current ? { ...current, dark } : current)));
  }, []);
  useEffect(() => {
    const dark = info?.dark ?? matchMedia("(prefers-color-scheme: dark)").matches;
    document.documentElement.dataset.theme = dark ? "dark" : "light";
  }, [info?.dark]);
  return info;
}
