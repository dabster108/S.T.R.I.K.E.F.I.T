// Overview highlights computed from the sessions the dashboard already holds (nothing invented, nothing from another source):
//   best run          the session with the highest score (a score of 0 never counts)
//   longest streak    the most victories in a row for one player, over their decided fights (victory / defeat); an abandoned or
//                     unfinished fight neither extends nor breaks it
//   most played level the level with the most recorded fights (Easy / Medium / Hard)
const num = (v) => (Number.isFinite(Number(v)) ? Number(v) : 0);
const when = (s) => new Date(s.startedAt || s.endedAt || 0).getTime() || 0;

export function bestRun(sessions) {
  let best = null;
  for (const s of sessions) if (num(s.score) > 0 && (!best || num(s.score) > num(best.score))) best = s;
  return best;
}

export function longestStreak(sessions) {
  const byPlayer = new Map();
  for (const s of sessions) {
    if (s.outcome !== "victory" && s.outcome !== "defeat") continue;
    const name = s.playerName || "Player";
    if (!byPlayer.has(name)) byPlayer.set(name, []);
    byPlayer.get(name).push(s);
  }
  let best = { length: 0, player: null };
  for (const [player, list] of byPlayer) {
    list.sort((a, b) => when(a) - when(b));
    let run = 0;
    for (const s of list) {
      run = s.outcome === "victory" ? run + 1 : 0;
      if (run > best.length) best = { length: run, player };
    }
  }
  return best.length > 0 ? best : null;
}

export function mostPlayedLevel(sessions) {
  const counts = new Map();
  for (const s of sessions) if (["Easy", "Medium", "Hard"].includes(s.level)) counts.set(s.level, (counts.get(s.level) || 0) + 1);
  let best = null;
  for (const [level, count] of counts) if (!best || count > best.count) best = { level, count };
  return best;
}

export const highlights = (sessions) => ({ best: bestRun(sessions), streak: longestStreak(sessions), level: mostPlayedLevel(sessions) });
