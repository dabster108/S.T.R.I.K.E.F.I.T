// Which fights the Live Arena shows when more than one player is on screen: "Both" (everyone) or one player. Pure, so it is tested.
// The choice is stored by player NAME, not fight id: fight ids change every round, the player stays.

export const VIEW_KEY = "strikefit.arena.view";
export const BOTH = "both";

/** One entry per player on stage (their newest fight wins), in the order the fights arrive. entries: [{ id, playerName, level, live }] */
export function playersOnStage(entries) {
  const byName = new Map();
  for (const e of entries) {
    const have = byName.get(e.playerName);
    // a live fight beats a finished one (a player who starts again while their result is still showing)
    if (!have || (e.live && !have.live)) byName.set(e.playerName, e);
  }
  return [...byName.values()];
}

/**
 * The fight ids to render for `choice`. "both" (or nothing chosen) shows everyone. A player's name shows that player's fights; if
 * that player is not on stage right now it falls back to everyone, so the page is never empty while a fight is on, and the choice
 * (which the caller keeps) applies again as soon as they are back.
 */
export function pickStage(entries, choice) {
  if (!choice || choice === BOTH) return entries.map((e) => e.id);
  const mine = entries.filter((e) => e.playerName === choice);
  return (mine.length ? mine : entries).map((e) => e.id);
}

/** The choice that is actually in effect: the stored one if that player is on stage, else "both". */
export const effectiveChoice = (entries, choice) => (choice && choice !== BOTH && entries.some((e) => e.playerName === choice) ? choice : BOTH);

export function readChoice(storage, search = "") {
  try {
    const fromUrl = new URLSearchParams(search).get("player");
    if (fromUrl) return fromUrl;
    return storage.getItem(VIEW_KEY) || BOTH;
  } catch {
    return BOTH;
  }
}

export function writeChoice(storage, choice) {
  try {
    storage.setItem(VIEW_KEY, choice);
  } catch {
    /* private mode: the choice just is not remembered */
  }
}

/** The link that opens the Live Arena on the same view. */
export function arenaLink(origin, choice) {
  const url = new URL("/dashboard/live", origin);
  if (choice && choice !== BOTH) url.searchParams.set("player", choice);
  return url.toString();
}
