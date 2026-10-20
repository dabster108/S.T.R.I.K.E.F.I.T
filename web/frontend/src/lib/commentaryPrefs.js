// The three things a viewer can choose about the commentary, remembered in this browser only: hide it, have it spoken aloud, and use
// plain lines only (no AI commentator). Hidden is off by default (the caption shows), speech is off by default (nothing is spoken until
// someone turns it on), plain is off by default (the AI commentator is used whenever the server has it).
// Storage can be missing or throw (private windows, blocked site data); the choices then just last until the tab closes.

import { COMMENTARY } from "../config/commentary.js";

const parse = (v) => v === "1";

/** A tiny store with subscribe/get, so it works with useSyncExternalStore. `storage` is injectable for the tests. */
export function createPrefs(storage, keys = COMMENTARY.storageKeys) {
  const read = (key) => {
    try {
      return parse(storage?.getItem(key));
    } catch {
      return false;
    }
  };
  let value = { hidden: read(keys.hidden), speech: read(keys.speech), plain: read(keys.plain) };
  const listeners = new Set();
  const write = (name, next) => {
    value = { ...value, [name]: Boolean(next) };
    try {
      storage?.setItem(keys[name], value[name] ? "1" : "0");
    } catch {
      /* not remembered */
    }
    listeners.forEach((l) => l());
  };
  return {
    get: () => value,
    setHidden: (v) => write("hidden", v),
    setSpeech: (v) => write("speech", v),
    setPlain: (v) => write("plain", v),
    subscribe(l) {
      listeners.add(l);
      return () => listeners.delete(l);
    },
  };
}

let local = null;
try {
  local = typeof localStorage === "undefined" ? null : localStorage;
} catch {
  local = null;
}
export const commentaryPrefs = createPrefs(local);

/** Is spoken commentary possible in this browser (SpeechSynthesis)? */
export const speechSupported = () => typeof window !== "undefined" && "speechSynthesis" in window && typeof SpeechSynthesisUtterance !== "undefined";
