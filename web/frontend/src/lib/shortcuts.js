// Keyboard "go to" sequences (press G, then a letter) and the palette's recent items. Pure, so they are tested without a browser.

export const GO_KEYS = {
  o: { to: "/dashboard", label: "Overview" },
  l: { to: "/dashboard/live", label: "Live Arena" },
  p: { to: "/dashboard/players", label: "Players" },
  s: { to: "/dashboard/sessions", label: "Sessions" },
  a: { to: "/dashboard/analytics", label: "Analytics" },
  t: { to: "/dashboard/lab", label: "Thesis Lab" },
  e: { to: "/dashboard/export", label: "Export" },
};

export const goTarget = (key) => GO_KEYS[String(key || "").toLowerCase()]?.to || null;
export const goHint = (to) => {
  const k = Object.keys(GO_KEYS).find((x) => GO_KEYS[x].to === to);
  return k ? ["G", k.toUpperCase()] : null;
};

// How long after G the letter still counts.
export const GO_WINDOW_MS = 1200;

export const RECENT_KEY = "strikefit.recent";
export const RECENT_MAX = 5;

// Most recent first, no duplicates, newest RECENT_MAX kept. `item` needs { id, label, to }; only those fields (plus kind/icon/hint) are stored.
export function addRecent(list, item) {
  const keep = { id: item.id, label: item.label, to: item.to, kind: item.kind, icon: item.icon, hint: item.hint };
  return [keep, ...list.filter((x) => x.id !== item.id)].slice(0, RECENT_MAX);
}

export function readRecent(storage) {
  try {
    const v = JSON.parse(storage.getItem(RECENT_KEY) || "[]");
    return Array.isArray(v) ? v.filter((x) => x && typeof x.id === "string" && typeof x.to === "string" && typeof x.label === "string").slice(0, RECENT_MAX) : [];
  } catch {
    return [];
  }
}

export function writeRecent(storage, list) {
  try {
    storage.setItem(RECENT_KEY, JSON.stringify(list));
  } catch {
    /* private mode: recents just don't persist */
  }
}
