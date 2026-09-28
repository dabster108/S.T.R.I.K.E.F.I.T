// One-off import of the old db.json store into Postgres.
//   npm run db:migrate                      # reads web/backend/db.json
//   npm run db:migrate -- path/to/file.json  # e.g. restore from db.json.bak
//
// Idempotent (ON CONFLICT (id) DO NOTHING), all-or-nothing (one transaction),
// preserves ids, timestamps and array order, then verifies every record field
// by field. Exits non-zero on any mismatch — do not delete db.json in that case.
const fs = require("fs");
const path = require("path");
const { query, withTransaction, pool } = require("../db");
const { SESSION_FIELDS, EVENT_FIELDS, SESSION_SELECT, EVENT_SELECT, TIMESTAMP_FIELDS, fromRow } = require("../columns");

const source = path.resolve(process.argv[2] || path.join(__dirname, "..", "db.json"));

// Timestamps are compared as instants at microsecond precision (Postgres's limit).
// Digits past the 6th must be zero, otherwise precision would have been lost.
function sameInstant(a, b) {
  const micros = (s) => {
    const m = /^(.*T\d{2}:\d{2}:\d{2})(?:\.(\d+))?Z$/.exec(s);
    if (!m) return { key: String(new Date(s).getTime()), lossy: false };
    const frac = (m[2] || "").padEnd(6, "0");
    return { key: `${m[1]}.${frac.slice(0, 6)}`, lossy: /[1-9]/.test(frac.slice(6)) };
  };
  const x = micros(a);
  const y = micros(b);
  return !x.lossy && !y.lossy && x.key === y.key;
}

function diffRecord(original, stored) {
  const problems = [];
  for (const key of new Set([...Object.keys(original), ...Object.keys(stored)])) {
    const a = original[key];
    const b = stored[key];
    const same = TIMESTAMP_FIELDS.has(key) && a != null && b != null
      ? sameInstant(a, b)
      : JSON.stringify(a) === JSON.stringify(b);
    if (!same) problems.push(`${key}: ${JSON.stringify(a)} -> ${JSON.stringify(b)}`);
  }
  return problems;
}

async function main() {
  if (!fs.existsSync(source)) throw new Error(`${source} not found`);

  // Safety copy before touching anything.
  if (!source.endsWith(".bak")) {
    fs.copyFileSync(source, `${source}.bak`);
    console.log(`[migrate] backup written: ${path.basename(source)}.bak`);
  }

  const data = JSON.parse(fs.readFileSync(source, "utf8"));
  const sessions = Array.isArray(data.sessions) ? data.sessions : [];
  const events = Array.isArray(data.events) ? data.events : [];
  const otherKeys = Object.keys(data).filter((k) => !["sessions", "events"].includes(k));
  console.log(`[migrate] ${path.basename(source)}: ${sessions.length} sessions, ${events.length} events, other keys: ${otherKeys.join(", ") || "none"}`);

  const tables = await query("SELECT to_regclass('sessions') AS s, to_regclass('events') AS e, to_regclass('app_meta') AS m");
  if (!tables.rows[0].s || !tables.rows[0].e || !tables.rows[0].m) throw new Error("tables missing — run npm run db:schema first");

  const sessionKeys = new Set(SESSION_FIELDS.map(([k]) => k));
  const eventKeys = new Set(EVENT_FIELDS.map(([k]) => k));
  const extraOf = (record, known) =>
    Object.fromEntries(Object.entries(record).filter(([k]) => !known.has(k)));

  const result = await withTransaction(async (client) => {
    let sessionsInserted = 0;
    let eventsInserted = 0;
    const sessionIds = new Set();

    // Sessions first so the events' foreign keys can resolve. Array order -> seq order.
    for (const s of sessions) {
      sessionIds.add(s.id);
      const cols = SESSION_FIELDS.map(([, col]) => col);
      const values = SESSION_FIELDS.map(([key]) => s[key]);
      const r = await client.query(
        `INSERT INTO sessions (${cols.join(", ")}, extra, created_at)
         VALUES (${cols.map((_, i) => `$${i + 1}`).join(", ")}, $${cols.length + 1}, $${cols.length + 2})
         ON CONFLICT (id) DO NOTHING`,
        [...values, JSON.stringify(extraOf(s, sessionKeys)), s.endedAt || s.startedAt || new Date().toISOString()]
      );
      sessionsInserted += r.rowCount;
    }

    // Events whose session isn't in the file keep their sessionId in session_ref,
    // with a NULL foreign key (Unity's session_start for battles never finished).
    const orphans = [];
    for (const e of events) {
      const ref = e.sessionId ?? null;
      if (ref && !sessionIds.has(ref)) orphans.push(e);
      const r = await client.query(
        `INSERT INTO events (id, session_id, session_ref, type, player_name, message, payload, occurred_at, extra)
         VALUES ($1, (SELECT id FROM sessions WHERE id = $2), $2, $3, $4, $5, $6::json, $7, $8)
         ON CONFLICT (id) DO NOTHING`,
        [e.id, ref, e.type, e.playerName, e.message,
          e.payload == null ? null : JSON.stringify(e.payload), e.at, JSON.stringify(extraOf(e, eventKeys))]
      );
      eventsInserted += r.rowCount;
    }

    for (const key of otherKeys) {
      await client.query(
        "INSERT INTO app_meta (key, value) VALUES ($1, $2) ON CONFLICT (key) DO NOTHING",
        [`db_json.${key}`, JSON.stringify(data[key])]
      );
    }
    return { sessionsInserted, eventsInserted, orphans };
  });

  console.log(`[migrate] inserted ${result.sessionsInserted} sessions, ${result.eventsInserted} events (existing ids skipped: ${sessions.length - result.sessionsInserted} sessions, ${events.length - result.eventsInserted} events)`);
  if (result.orphans.length) {
    console.log(`[migrate] ${result.orphans.length} events reference a session id not in the file — stored with session_id NULL, sessionId kept in session_ref:`);
    for (const e of result.orphans) console.log(`           ${e.id}  ${e.type}  sessionId=${e.sessionId}`);
  }

  // ---- Verification: every record read back through the same mapping the API uses.
  const storedSessions = await query(`SELECT ${SESSION_SELECT} FROM sessions WHERE id = ANY($1)`, [sessions.map((s) => s.id)]);
  const storedEvents = await query(`SELECT ${EVENT_SELECT} FROM events WHERE id = ANY($1)`, [events.map((e) => e.id)]);
  const totals = await query("SELECT (SELECT count(*) FROM sessions)::int AS s, (SELECT count(*) FROM events)::int AS e");
  const linked = await query("SELECT count(*)::int AS n FROM events WHERE id = ANY($1) AND session_id IS NOT NULL", [events.map((e) => e.id)]);

  const byId = (rows) => new Map(rows.map((r) => [r.id, fromRow(r)]));
  const sMap = byId(storedSessions.rows);
  const eMap = byId(storedEvents.rows);
  const fieldProblems = [];
  for (const s of sessions) {
    const p = sMap.has(s.id) ? diffRecord(s, sMap.get(s.id)) : ["missing"];
    if (p.length) fieldProblems.push(`session ${s.id}: ${p.join("; ")}`);
  }
  for (const e of events) {
    const p = eMap.has(e.id) ? diffRecord(e, eMap.get(e.id)) : ["missing"];
    if (p.length) fieldProblems.push(`event ${e.id}: ${p.join("; ")}`);
  }
  // jsonb reorders object keys, so app_meta is compared with keys sorted.
  const canonical = (v) =>
    JSON.stringify(v, (_k, x) => (x && typeof x === "object" && !Array.isArray(x)
      ? Object.fromEntries(Object.entries(x).sort(([a], [b]) => a.localeCompare(b))) : x));
  for (const key of otherKeys) {
    const m = await query("SELECT value FROM app_meta WHERE key = $1", [`db_json.${key}`]);
    if (!m.rows.length || canonical(m.rows[0].value) !== canonical(data[key])) fieldProblems.push(`app_meta db_json.${key} differs`);
  }

  const row = (label, file, found, total) =>
    `  ${label.padEnd(9)}${String(file).padStart(8)}${String(found).padStart(14)}${String(total).padStart(12)}   ${file === found ? "MATCH" : "MISMATCH"}`;
  console.log("\n[verify]    db.json   ids in PG   PG total");
  console.log(row("sessions", sessions.length, sMap.size, totals.rows[0].s));
  console.log(row("events", events.length, eMap.size, totals.rows[0].e));
  console.log(`  events linked to a session by FK: ${linked.rows[0].n}, unlinked (session_id NULL): ${eMap.size - linked.rows[0].n}`);
  console.log(`  field-by-field comparison: ${fieldProblems.length ? `${fieldProblems.length} PROBLEM(S)` : `all ${sessions.length + events.length} records identical`}`);
  for (const p of fieldProblems) console.log(`    ${p}`);

  if (sMap.size !== sessions.length || eMap.size !== events.length || fieldProblems.length) {
    console.error("\n[verify] FAILED — do NOT delete db.json.");
    process.exitCode = 1;
  } else {
    console.log("\n[verify] OK — every db.json record is in Postgres unchanged.");
  }
}

main()
  .catch((err) => {
    console.error("[migrate] failed, nothing committed:", err.message);
    process.exitCode = 1;
  })
  .finally(() => pool.end());
