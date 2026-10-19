// Fuzzy matching for the command palette. score() is lower-is-better, null = no match:
//   0        the text starts with the query
//   1..2     the query is a substring (earlier is better), or starts a word ("arena" in "Live Arena")
//   2..3     (3+ characters) every query character appears in order and close together; tighter scores better
// A query of several words must match every word ("lab csv" finds "Export Thesis Lab CSV").
const norm = (s) => String(s ?? "").toLowerCase();

function scoreWord(text, q) {
  if (!q) return 0;
  if (text.startsWith(q)) return 0;
  const words = text.split(/[\s/·\-_]+/);
  let at = 0;
  for (const w of words) {
    if (w.startsWith(q)) return 0.5 + at / 1000; // a word start: "arena" -> "Live Arena"
    at += w.length + 1;
  }
  const i = text.indexOf(q);
  if (i >= 0) return 1 + Math.min(i, 99) / 100;
  // subsequence: "ldb" -> "Leaderboard". Only for 3+ characters, and only when the letters sit close together: "hard" must not
  // match "Show keyboard shortcuts" just because h, a, r, d appear somewhere in it.
  if (q.length < 3) return null;
  let pos = 0;
  let gaps = 0;
  let prev = -1;
  for (const ch of q) {
    pos = text.indexOf(ch, pos);
    if (pos < 0) return null;
    if (prev >= 0 && pos !== prev + 1) gaps += pos - prev - 1;
    prev = pos;
    pos++;
  }
  if (gaps > q.length * 1.5) return null;
  return 2 + gaps / 100;
}

export function score(text, query) {
  const t = norm(text);
  const words = norm(query).split(/\s+/).filter(Boolean);
  if (!words.length) return 0;
  let total = 0;
  for (const w of words) {
    const s = scoreWord(t, w);
    if (s === null) return null;
    total += s;
  }
  return total / words.length;
}

// items: any objects; textOf(item) gives the searchable string. Stable: equal scores keep their order.
export function fuzzyFilter(items, query, textOf = (x) => `${x.label} ${x.keywords || ""}`) {
  if (!norm(query).trim()) return items;
  return items
    .map((it, i) => ({ it, i, s: score(textOf(it), query) }))
    .filter((x) => x.s !== null)
    .sort((a, b) => a.s - b.s || a.i - b.i)
    .map((x) => x.it);
}
