// How each rank LOOKS. The ladder itself (names, thresholds, the points rule) comes from the API (GET /api/players/:name/rank), which
// reads backend/config/ranks.js, so a threshold changed there is changed everywhere. Only the look is kept here, keyed by rank key.
// Blue and white only: the ranks climb from a pale outline to solid navy.

export const RANK_LOOK = Object.freeze({
  rookie: { icon: "shield", chip: "bg-[var(--surface-muted)] text-[var(--ink-secondary)] ring-[var(--border-strong)]", solid: "bg-[var(--surface-muted)] text-[var(--ink-secondary)]" },
  brawler: { icon: "hand", chip: "bg-[var(--accent-soft)] text-[var(--accent-strong)] ring-[var(--accent-ring)]", solid: "bg-[var(--accent-soft)] text-[var(--accent-strong)]" },
  warrior: { icon: "swords", chip: "bg-[var(--accent)] text-white ring-[var(--accent-strong)]", solid: "bg-[var(--accent)] text-white" },
  guardian: { icon: "crown", chip: "bg-[var(--blue-900)] text-white ring-[var(--blue-800)]", solid: "bg-[var(--blue-900)] text-white" },
});
export const FALLBACK_LOOK = RANK_LOOK.rookie;

/** How long the rank answers are kept before the page asks again (the live stream refreshes them sooner when a fight ends). */
export const RANK_REFRESH_MS = 4000;
