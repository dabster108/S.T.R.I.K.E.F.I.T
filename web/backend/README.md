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

### Daily goals (added 2026-10-10; additive, no existing route or field changed)

One goal per player per local day (`Asia/Kathmandu` unless `?tz=` names another zone), one of four metrics:
`kcal` (an ESTIMATE, MET method), `fullSwings`, `activeMinutes` (60 s buckets of the fight that contain a swing), `xp`.
Targets are validated against fixed safety caps (`goals.TARGET_RANGE`). Table `daily_goals` is created by `schema.sql`
(`CREATE TABLE IF NOT EXISTS`, run on every boot), unique on `(player_name, goal_date)`.

- `PUT /api/players/:name/goal` `{ "metric": "kcal", "target": 100 }`: sets or changes TODAY's goal (idempotent upsert).
  `400` invalid metric/target, `409` if today's goal is already met (a met goal is not reopened), `429` over 30 changes per
  10 min per IP. Broadcasts `goal_set` (and `goal_met` if the day's total already satisfies it).
- `GET /api/players/:name/goal/today`: `{ goal, progress { value, target, percent, met, metric }, completed, bonusXp, totals,
  suggestions { source: "data" | "fallback", note, presets }, ranges, bonusXpOnCompletion, estimateNote }`. One database
  round trip (a single CTE); presets come from a stale-while-revalidate cache pre-warmed at boot.
- `GET /api/players/:name/goals?days=30` (1 to the history cap): `{ days: [{ date, goal, progress, met, completedAt, bonusXp,
  sessions }], daysMet, goalStreak { current, longest, daysMet }, bonusXpTotal }`, oldest day first.
- `GET /api/export/goals.csv` (`player`, `from`, `to`).
- `GET /api/overview` gains `goalsToday { date, goalsSet, goalsMet }` (added fields only).
- Completion is evaluated when a session finishes (`POST /api/sessions` with an outcome). It is idempotent: a conditional
  `UPDATE ... WHERE completed_at IS NULL` awards the one-off `BONUS_XP` (75) exactly once per day. The bonus is exposed as NEW
  fields on `GET /api/players/:name/achievements`: `goalBonusXp`, `totalXpWithGoals`, `levelWithGoals`, `goalStreak`,
  `goalsMet`; the existing `level` / XP fields still mean session XP only and are unchanged.
- SSE: `goal_set`, `goal_met` on `/api/stream` (`{ playerName, date, metric, target, completedAt, bonusXp }`).
- Badges: Goal Getter (goal met on 3 days), Goal Week (7 days), plus a goal streak (consecutive days).
- No login exists, so anyone who knows a player's name can set that player's goal. Rate limiting and range caps are the
  only protection. The kcal figures are estimates, never measurements.

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

## Engagement routes (added with the web-engagement pass)

All additions; no existing route or field changed. Names are validated like the daily-goal routes (1-40 characters, no control characters). Days and weeks use the same time zone constant as the streak and goals (`Asia/Kathmandu`), weeks run Monday to Sunday.

| Route | What it returns |
| --- | --- |
| `GET /api/players/:name/challenges` | 3 daily + 1 weekly challenge with progress, target, target source (`data` / `fallback` / `fixed`), rest-day note |
| `GET /api/players/:name/challenges/history?days=30` | completions with their bonus XP (max 90 days) |
| `GET /api/players/:name/calendar?weeks=26` / `GET /api/calendar` | sessions and estimated kcal per local day (max 53 weeks) |
| `GET /api/players/:name/week[?week=last]` | the week's totals and "vs previous week" (only when that week had a session) |
| `GET /api/players/:name/best?level=Easy&before=<sessionId>` | highest-score earlier run on a level with its swing and monster HP samples |
| `GET /api/challenge/:sessionId`, `/check?name=` | the public challenge page's data |
| `GET /api/activity?limit=20` | milestones, names and event types only |
| `GET /api/live/reactions`, `POST /api/live/react` | cheers (validated, rate limited, never stored; SSE `reaction`) |

Table `challenge_completions` is created by `schema.sql` on boot. `REACTIONS_ENABLED=false` (Render environment) switches cheers off. To add a challenge, append one object to `CHALLENGES` in `challenges.js` (the comment above the array lists the fields); every target is clamped to `SAFETY_CAPS`.
