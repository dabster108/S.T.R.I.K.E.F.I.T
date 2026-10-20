// The /study page's flow, as pure functions (study.test.js): which step a participant is on, and whether a form is complete.

/** "consent" | "pre" | "play" | "post" | "done", from the server's status for a code (null = no code yet). */
export function stepFor(status) {
  if (!status) return "consent";
  if (status.postDone) return "done";
  if (!status.preDone) return "pre";
  return status.fights?.finished > 0 ? "post" : "play";
}

/** Labels of the required answers still missing from the pre-play form. */
export function preMissing(a) {
  return [["ageBand", "age band"], ["vrExperience", "VR experience"], ["weeklyExercise", "weekly exercise"]].filter(([k]) => !a[k]).map(([, l]) => l);
}

/** Labels of the required answers still missing from the post-play form. */
export function postMissing(a, { enjoymentItems = 5, susItems = 10 } = {}) {
  const out = [];
  if (!a.rpe) out.push("how hard it felt (Borg scale)");
  const blanks = (arr, n) => Array.from({ length: n }, (_, i) => arr?.[i]).filter((v) => !v).length;
  const e = blanks(a.enjoyment, enjoymentItems);
  if (e) out.push(`${e} enjoyment statement${e === 1 ? "" : "s"}`);
  if (!a.effortVsUsual) out.push("effort compared with a usual workout");
  if (!a.playAgain) out.push("whether you would play again");
  const s = blanks(a.sus, susItems);
  if (s) out.push(`${s} usability statement${s === 1 ? "" : "s"}`);
  return out;
}

/** The body for POST .../post (numbers, not strings). */
export const postBody = (a) => ({ enjoyment: a.enjoyment.map(Number), effortVsUsual: Number(a.effortVsUsual), playAgain: a.playAgain, sus: a.sus.map(Number) });
