// Mapping between the API's JSON field names and the Postgres columns in schema.sql.
// Order matters: it is the key order of the JSON objects the API returns, which
// matches what the old db.json store produced.

const SESSION_FIELDS = [
  ["id", "id"],
  ["playerName", "player_name"],
  ["level", "level"],
  ["outcome", "outcome"],
  ["score", "score"],
  ["damageDealt", "damage_dealt"],
  ["hitCount", "hit_count"],
  ["tapCount", "tap_count"],
  ["durationSec", "duration_sec"],
  ["playerHpRemaining", "player_hp_remaining"],
  ["monsterHpRemaining", "monster_hp_remaining"],
  ["pointsEarned", "points_earned"],
  ["startedAt", "started_at"],
  ["endedAt", "ended_at"],
  ["source", "source"],
  ["notes", "notes"],
];

// Columns added for live sessions (schema.sql migration section). Kept out of
// SESSION_FIELDS so the one-off db.json migration and its verification are
// unchanged; the API appends them after the original keys.
const SESSION_LIVE_FIELDS = [
  ["status", "status"],
  ["xp", "xp"],
  ["fullSwings", "full_swings"],
  ["lastEventAt", "last_event_at"],
];

const EVENT_FIELDS = [
  ["id", "id"],
  ["type", "type"],
  ["sessionId", "session_ref"],
  ["playerName", "player_name"],
  ["message", "message"],
  ["payload", "payload"],
  ["at", "occurred_at"],
];

const TIMESTAMP_FIELDS = new Set(["startedAt", "endedAt", "at", "lastEventAt"]);

// timestamptz -> ISO-8601 UTC with microseconds, independent of the session time zone.
const isoSql = (col) => `to_char(${col} AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')`;

const selectList = (fields) =>
  [...fields.map(([key, col]) => `${TIMESTAMP_FIELDS.has(key) ? isoSql(col) : col} AS "${key}"`), "extra"].join(", ");

const SESSION_SELECT = selectList(SESSION_FIELDS);
const EVENT_SELECT = selectList(EVENT_FIELDS);
// What the API returns for a session: the original keys, then the live-session keys.
const SESSION_API_FIELDS = [...SESSION_FIELDS, ...SESSION_LIVE_FIELDS];
const SESSION_API_SELECT = selectList(SESSION_API_FIELDS);

// "…15.469000Z" -> "…15.469Z" (what JS toISOString produces); sub-millisecond digits are kept.
const trimIso = (s) => s.replace(/(\.\d{3}\d*?)0+Z$/, "$1Z");

// A selected row -> the API object: timestamps tidied, then any extra fields appended.
function fromRow(row) {
  const { extra, inserted, ...obj } = row;
  for (const key of TIMESTAMP_FIELDS) if (typeof obj[key] === "string") obj[key] = trimIso(obj[key]);
  return Object.assign(obj, extra);
}

module.exports = {
  SESSION_FIELDS,
  SESSION_LIVE_FIELDS,
  SESSION_API_FIELDS,
  EVENT_FIELDS,
  SESSION_SELECT,
  SESSION_API_SELECT,
  EVENT_SELECT,
  TIMESTAMP_FIELDS,
  fromRow,
};
