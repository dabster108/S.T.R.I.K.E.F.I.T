# STRIKEFIT backend

Express + Socket.IO API that the Unity game posts battle results to and the web
dashboard reads from. Data lives in **Neon serverless Postgres** (it used to be a
local `db.json` file).

## Setup

```bash
cd web/backend
npm install
# create web/backend/.env (git-ignored) containing one line:
#   DATABASE_URL=<your Neon connection string>
npm run db:schema    # creates tables + indexes; safe to run any number of times
npm start            # http://localhost:4000 (or $PORT)
```

`DATABASE_URL` is read only from the environment. Locally it comes from
`web/backend/.env` via dotenv; on Render it comes from the dashboard (no `.env`
file is needed or used there). Never commit the connection string.

## Scripts

| Command              | What it does                                                        |
| -------------------- | ------------------------------------------------------------------- |
| `npm start`          | Runs the API on `process.env.PORT` (default 4000)                   |
| `npm run dev`        | Same, restarting on file changes                                    |
| `npm run db:schema`  | Applies `schema.sql` (`CREATE ... IF NOT EXISTS`, idempotent)       |
| `npm run db:migrate` | Imports `db.json` into Postgres (see below)                         |

## Schema

See `schema.sql`. Summary:

- `sessions`: one row per finished battle. `id` is the Unity/UUID id; every
  field the API returns has its own column (`player_name`, `level`, `outcome`,
  `score`, `damage_dealt`, …, `started_at`, `ended_at`), plus `created_at`,
  a `seq` insertion counter (used to break sort ties), and `extra jsonb` for any
  unknown field.
- `events`: `session_id` is a foreign key to `sessions(id)` with
  `ON DELETE CASCADE`; `session_ref` keeps the `sessionId` exactly as sent.
  Unity posts `session_start` *before* the session row exists (and never
  creates one for an abandoned battle), so `session_id` is NULL until the
  session is posted, at which point the server links it.
- `app_meta`: the old `db.json` `meta` object.
- Live-session columns on `sessions` (added with `ADD COLUMN IF NOT EXISTS`,
  applied automatically on boot): `status` (`live`/`finished`, existing rows
  default to `finished`), `xp`, `full_swings`, `last_event_at`, `live_state jsonb`.
- Players are not a table; `/api/players` is computed from `sessions`.

## Migrating from db.json (one-off, already done once)

```bash
npm run db:schema
npm run db:migrate                 # reads ./db.json, writes ./db.json.bak first
npm run db:migrate -- db.json.bak  # or restore from the backup
```

The migration runs in one transaction, keeps original ids and timestamps,
uses `ON CONFLICT (id) DO NOTHING` (re-running it inserts nothing), and ends with
a count + field-by-field verification. It exits non-zero on any mismatch; do not
delete `db.json` in that case. `db.json` and `db.json.bak` are git-ignored.

Postgres stores timestamps to the microsecond, so a Unity timestamp such as
`…57.1700990Z` comes back as `…57.170099Z`. It is the same instant.

## Deploying on Render

- **Root Directory:** `web/backend`
- **Build command:** `npm install`
- **Start command:** `npm run start` (the server listens on `process.env.PORT`)
- **Environment variable:** `DATABASE_URL` = the Neon connection string
  (Render → service → Environment). Run `npm run db:schema` once against the
  database if the tables don't exist yet (they already do for the current Neon DB).

`GET /api/health` reports `database.ok` and returns 503 if Postgres is unreachable.

Also set `ALLOWED_ORIGINS` to the Vercel URL (comma separated; `https://*.vercel.app`
wildcards work). localhost is always allowed. Requests without an `Origin`
header (Unity, curl) are never affected by CORS.

## API

Original routes, same response shapes (session objects gained four keys at the
end: `status`, `xp`, `fullSwings`, `lastEventAt`):

- `GET /api/health` · `/api/stats` · `/api/players` (the last two count finished sessions only)
- `GET /api/sessions?player=&level=&outcome=&status=&limit=` (newest first) · `/api/sessions/:id` (adds a top-level `liveState`)
- `GET /api/events?limit=&type=&sessionId=&player=` (newest first, max 1000)
- `POST /api/sessions` (re-posting an id updates it) · `POST /api/events` (`ping` is broadcast only)
- `DELETE /api/sessions` (everything) · `DELETE /api/sessions/:id` (session + its events)

New:

- `GET /api/stream?player=NAME`: Server-Sent Events. Named events `session_started`,
  `session_updated`, `session_finished`, `event_created`, `session_deleted`, plus
  `hello` and a `heartbeat` every 15 s. Each `data` is `{ type, payload, at }`.
- `POST /api/events` also accepts a batch: `{ "events": [...] }` (max 200), answered with `{ ok, count, events }`.
- `GET /api/live`: sessions being played now, each with `liveState` (HP, max HP, XP, score, hits, round time left).
- `GET /api/players/:name`: profile (totals, wins/losses, best score, damage, swings,
  average swing speed, full-power ratio, tier counts, per-level breakdown, every session).
- `GET /api/overview?tz=`: dashboard totals, sessions per day (last 14 days), per level, 5 most recent.
- `GET /api/public-stats`: aggregates only, no names (landing page).
- `GET /api/analytics?player=&level=&from=&to=`: swing-speed histogram, tiers, effort by fifth of a session, damage per swing, level comparison.
- `GET /api/export/sessions.csv` · `/api/export/events.csv` (`player`, `level`, `from`, `to`, `type`).
- `PATCH /api/players/:name` with `{ "newName": "..." }`: renames a player. In one transaction it
  rewrites `sessions.player_name` and `events.player_name`, then answers with the new profile (same
  shape as `GET /api/players/:name`) plus `X-Renamed-Sessions` / `X-Renamed-Events` headers, and
  broadcasts `player_renamed` `{ oldName, newName, sessions, events }` on `/api/stream` to every client.
  `newName` is trimmed and must be 1–40 characters. Errors: `400` (empty, too long, control
  characters, unchanged), `404` (no such player), `409` if the name is already taken (players are
  never merged: that would combine two people's histories irreversibly) or if the player is in a
  live fight. Event payload snapshots and message text (e.g. `session_complete`) keep the name as
  recorded. Unity keeps posting under its own `Player Name`, so change it there too.

### Live sessions

A session is `live` from its first event (or a `POST /api/sessions` with
`"status": "live"`) until an outcome arrives (`victory`, `defeat`, `timeout`).
If no event arrives for 60 s it is finished automatically with outcome
`abandoned`; if events resume (the headset was taken off, which pauses the
app) it goes back to `live`. The sweep only queries Postgres while a session is
live, so an idle server doesn't keep Neon awake. Broadcasts are in-process (one
Render instance); Postgres LISTEN/NOTIFY is not used because Neon's pooled
endpoint doesn't support it.

`npm run simulate -- --url <api> [--player NAME] [--level Easy|Medium] [--seconds 30] [--keep]`
plays a fake fight the way Unity does. It deletes its session at the end unless `--keep` is passed.
