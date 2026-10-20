// Demo mode: the whole site on scripted data, with no backend and no internet. On with ?demo=1 or the footer button, off with the
// ribbon's Exit button. The flag lives in sessionStorage (this tab only), so a normal visit in another tab is never affected.

const KEY = "strikefit.demo";

const read = () => {
  try {
    return sessionStorage.getItem(KEY) === "1";
  } catch {
    return false;
  }
};
const write = (on) => {
  try {
    on ? sessionStorage.setItem(KEY, "1") : sessionStorage.removeItem(KEY);
  } catch {
    /* blocked storage: the ?demo=1 link still works for this load */
  }
};

let on = false;

/** Call once, before the app renders: reads ?demo=1 (and removes it from the address bar) and the stored flag. */
export function initDemoFlag() {
  const url = new URL(window.location.href);
  const q = url.searchParams.get("demo");
  if (q === "1") {
    write(true);
    on = true;
    url.searchParams.delete("demo");
    window.history.replaceState(null, "", url.pathname + url.search + url.hash);
  } else if (q === "0") {
    write(false);
    on = false;
    url.searchParams.delete("demo");
    window.history.replaceState(null, "", url.pathname + url.search + url.hash);
  } else on = read();
  if (on) document.documentElement.dataset.demo = "1";
  return on;
}

export const isDemo = () => on;

/** Switches demo mode on or off and reloads the current page, so every module starts from the right data source. */
export function setDemo(next) {
  write(next);
  window.location.reload();
}
