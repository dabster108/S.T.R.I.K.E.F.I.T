// The full-time report, plain version (no AI): a pure function of the finished fight's totals. The AI commentator rewords this text,
// and it is what is shown when the AI is off, slow or refused, so a fight always ends with a summary.
// The text uses plain digits (5639, not 5,639) so the server can check the AI's numbers against it.

const count = (v) => (Number.isFinite(Number(v)) && Number(v) > 0 ? Math.round(Number(v)) : 0);

/** "1m 42s", "42s"; "" when the length is unknown. */
export function spokenDuration(sec) {
  const s = count(sec);
  if (!s) return "";
  return s < 60 ? `${s}s` : `${Math.floor(s / 60)}m ${String(s % 60).padStart(2, "0")}s`;
}

/** What the report is built from: the finished session plus the names and the best combo the commentary saw. */
export function summaryFacts(session, { playerName, monsterName, peakCombo } = {}) {
  const outcome = session?.outcome === "victory" || session?.outcome === "defeat" ? session.outcome : "abandoned";
  return {
    outcome,
    player: playerName || session?.playerName || "The player",
    monster: monsterName || "The monster",
    level: ["Easy", "Medium", "Hard"].includes(session?.level) ? session.level : null,
    durationSec: count(session?.durationSec),
    score: count(session?.score),
    damageDealt: count(session?.damageDealt),
    hitCount: count(session?.hitCount),
    fullSwings: count(session?.fullSwings),
    peakCombo: count(peakCombo),
  };
}

/** The plain report as one paragraph. */
export function plainSummary(f) {
  const time = spokenDuration(f.durationSec);
  const opener =
    f.outcome === "victory"
      ? `Full time: ${f.player} beat ${f.monster}${time ? ` in ${time}` : ""}.`
      : f.outcome === "defeat"
        ? `Full time: ${f.monster} wins this round${time ? ` after ${time}` : ""}.`
        : `Fight abandoned${time ? ` after ${time}` : ""}.`;
  const bits = [];
  if (f.hitCount) bits.push(`${f.hitCount} hits landed${f.fullSwings ? ` (${f.fullSwings} at full power)` : ""}`);
  if (f.peakCombo >= 2) bits.push(`best combo ${f.peakCombo}`);
  if (f.score) bits.push(`score ${f.score}`);
  if (!bits.length) return opener;
  const rest = bits.join(", ");
  return `${opener} ${rest[0].toUpperCase()}${rest.slice(1)}.`;
}
