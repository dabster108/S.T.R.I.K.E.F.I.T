// "What this tells us": one plain-language sentence per Thesis Lab chart, written from the numbers and careful about what a small
// sample can and cannot say. Pure functions (labText.test.js). The Lab's sample-size rule (backend/effort.js SMALL_SAMPLE) is 10.

const SMALL = 10;
const pct = (v) => `${Math.round(v)}%`;
/** "A", "A and B", "A, B and C". */
export const listText = (items) => (items.length <= 1 ? items.join("") : `${items.slice(0, -1).join(", ")} and ${items[items.length - 1]}`);

export function returnRateText(returnRate) {
  const w = (d) => returnRate?.windows?.find((x) => x.days === d);
  const one = w(1);
  const seven = w(7);
  if (!one || !one.n) return "No player has been around for a full day yet, so there is nothing to say about coming back.";
  let s = `${one.returned} of ${one.n} players came back within a day`;
  if (seven && seven.n) s += seven.returned === one.returned && seven.n === one.n ? ", and nobody else returned later in the week" : `, ${seven.returned} of ${seven.n} within a week`;
  s += ".";
  if (one.n < SMALL) s += ` With ${one.n} players this describes this group only, not players in general.`;
  return s;
}

export function correlationWord(r) {
  const a = Math.abs(r);
  if (a < 0.1) return "no clear";
  if (a < 0.3) return "a weak";
  if (a < 0.5) return "a moderate";
  return "a strong";
}

export function rpeText(rpe) {
  if (!rpe || rpe.status !== "ok" || typeof rpe.r !== "number") {
    return `Not enough rated fights to say anything yet (${rpe?.n ?? 0} of the ${rpe?.minPairs ?? 8} needed).`;
  }
  const word = correlationWord(rpe.r);
  const dir = rpe.r > 0 ? "fights with faster swings tended to feel harder" : "fights with faster swings tended to feel easier";
  let s = word === "no clear" ? `There is no clear link between how fast players swung and how hard the fight felt (r = ${rpe.r.toFixed(2)}).` : `There is ${word} link (r = ${rpe.r.toFixed(2)}): ${dir}.`;
  if (rpe.n < 30) s += ` ${rpe.n} rated fights is a small sample, and a link is not a cause.`;
  else s += " A link is not a cause.";
  return s;
}

export function levelsText(levels) {
  const decided = (levels || []).filter((l) => l.decided > 0 && typeof l.winRate === "number");
  if (!decided.length) return "No level has a won or lost fight yet.";
  const parts = [];
  if (decided.length >= 2) {
    const first = decided[0];
    const last = decided[decided.length - 1];
    const harder = last.winRate < first.winRate;
    parts.push(`Players won ${pct(first.winRate)} of decided fights on ${first.label} and ${pct(last.winRate)} on ${last.label}${harder ? ", so the later level is harder" : first.winRate === last.winRate ? "" : ", so the later level was not harder for this group"}`);
  } else {
    parts.push(`Players won ${pct(decided[0].winRate)} of decided fights on ${decided[0].label}`);
  }
  const swung = (levels || []).filter((l) => typeof l.avgSwingSpeed === "number");
  if (swung.length >= 2) {
    const top = swung.reduce((a, b) => (b.avgSwingSpeed > a.avgSwingSpeed ? b : a));
    parts.push(`swings were fastest on ${top.label} (${top.avgSwingSpeed.toFixed(1)} m/s on average)`);
  }
  let s = `${parts.join("; ")}.`;
  const thin = decided.filter((l) => l.decided < SMALL);
  if (thin.length) s += ` ${listText(thin.map((l) => l.label))} ${thin.length === 1 ? "has" : "have"} fewer than ${SMALL} decided fights, so treat ${thin.length === 1 ? "its" : "their"} rate as rough.`;
  return s;
}
