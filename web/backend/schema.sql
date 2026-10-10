-- STRIKEFIT backend schema. Safe to apply repeatedly (npm run db:schema).
--
-- Column names are snake_case versions of the JSON fields the API returns
-- (playerName -> player_name, ...). server.js maps them back, so response
-- shapes are unchanged. Numbers are double precision because the API accepts
-- any finite JS number (durationSec and playerHpRemaining are fractional).

CREATE TABLE IF NOT EXISTS sessions (
  id                    text PRIMARY KEY,           -- Unity 32-hex id or a UUID
  seq                   bigint GENERATED ALWAYS AS IDENTITY,  -- insertion order, breaks sort ties like the old array did
  player_name           text NOT NULL,
  level                 text NOT NULL,
  outcome               text NOT NULL,
  score                 double precision NOT NULL DEFAULT 0,
  damage_dealt          double precision NOT NULL DEFAULT 0,
  hit_count             double precision NOT NULL DEFAULT 0,
  tap_count             double precision NOT NULL DEFAULT 0,
  duration_sec          double precision NOT NULL DEFAULT 0,
  player_hp_remaining   double precision NOT NULL DEFAULT 0,
  monster_hp_remaining  double precision NOT NULL DEFAULT 0,
  points_earned         double precision NOT NULL DEFAULT 0,
  started_at            timestamptz NOT NULL,
  ended_at              timestamptz NOT NULL,
  source                text NOT NULL DEFAULT 'unity',
  notes                 text NOT NULL DEFAULT '',
  extra                 jsonb NOT NULL DEFAULT '{}'::jsonb,   -- any field without its own column
  created_at            timestamptz NOT NULL DEFAULT now()
);

-- Unity posts session_start *before* the session row exists (the session is only
-- posted when the battle ends), and abandoned battles never get one. So:
--   session_ref  the sessionId exactly as sent; the API's sessionId and its filters use this.
--   session_id   the foreign key; set once the session exists, NULL until then.
-- Deleting a session cascades to its linked events.
CREATE TABLE IF NOT EXISTS events (
  id           text PRIMARY KEY,
  seq          bigint GENERATED ALWAYS AS IDENTITY,
  session_id   text REFERENCES sessions(id) ON DELETE CASCADE,
  session_ref  text,
  type         text NOT NULL,
  player_name  text NOT NULL,
  message      text NOT NULL,
  payload      json,                                     -- json (not jsonb) keeps the payload verbatim, key order included
  occurred_at  timestamptz NOT NULL DEFAULT now(),       -- the API's "at"
  extra        jsonb NOT NULL DEFAULT '{}'::jsonb
);

-- db.json's top-level "meta" object, kept so nothing from the file is lost.
CREATE TABLE IF NOT EXISTS app_meta (
  key    text PRIMARY KEY,
  value  jsonb NOT NULL
);

CREATE INDEX IF NOT EXISTS sessions_player_name_idx ON sessions (player_name);
CREATE INDEX IF NOT EXISTS sessions_level_idx       ON sessions (level);
CREATE INDEX IF NOT EXISTS sessions_outcome_idx     ON sessions (outcome);
CREATE INDEX IF NOT EXISTS sessions_created_at_idx  ON sessions (created_at);
CREATE INDEX IF NOT EXISTS sessions_ended_at_idx    ON sessions (ended_at DESC, seq);
CREATE INDEX IF NOT EXISTS events_session_id_idx    ON events (session_id);
CREATE INDEX IF NOT EXISTS events_session_ref_idx   ON events (session_ref);
CREATE INDEX IF NOT EXISTS events_type_idx          ON events (type);
CREATE INDEX IF NOT EXISTS events_seq_idx           ON events (seq);

-- ---------------------------------------------------------------------------
-- Live sessions (added for the real-time dashboard). Additive and idempotent:
-- existing rows become status 'finished', which is what they are. server.js
-- runs this section on boot, so a Render deploy migrates itself.
-- ---------------------------------------------------------------------------
ALTER TABLE sessions ADD COLUMN IF NOT EXISTS status        text NOT NULL DEFAULT 'finished';  -- 'live' | 'finished'
ALTER TABLE sessions ADD COLUMN IF NOT EXISTS xp            double precision NOT NULL DEFAULT 0;
ALTER TABLE sessions ADD COLUMN IF NOT EXISTS full_swings   double precision NOT NULL DEFAULT 0;
ALTER TABLE sessions ADD COLUMN IF NOT EXISTS last_event_at timestamptz;                       -- last activity; drives the 60 s auto-finish
ALTER TABLE sessions ADD COLUMN IF NOT EXISTS live_state    jsonb;                              -- latest HP / XP / score snapshot while live

CREATE INDEX IF NOT EXISTS sessions_status_idx     ON sessions (status);
CREATE INDEX IF NOT EXISTS sessions_started_at_idx ON sessions (started_at);
CREATE INDEX IF NOT EXISTS events_occurred_at_idx  ON events (occurred_at);

-- ---------------------------------------------------------------------------
-- Post-session survey (enjoyment 1-5, Borg RPE 6-20). Additive and idempotent.
-- One row per session; deleting a session deletes its rating.
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS session_feedback (
  session_id  text PRIMARY KEY REFERENCES sessions(id) ON DELETE CASCADE,
  enjoyment   smallint CHECK (enjoyment BETWEEN 1 AND 5),
  rpe         smallint CHECK (rpe BETWEEN 6 AND 20),
  comment     text CHECK (comment IS NULL OR char_length(comment) <= 500),
  created_at  timestamptz NOT NULL DEFAULT now(),
  CHECK (enjoyment IS NOT NULL OR rpe IS NOT NULL)
);

-- ---------------------------------------------------------------------------
-- Daily goals (one per player per local day). Additive and idempotent.
-- goal_date is the player's LOCAL day (Asia/Kathmandu by default, the same constant the streak
-- and achievements use). completed_at is set once and never cleared, so the bonus XP can only be
-- awarded once per day. Target ranges are enforced by goals.js (TARGET_RANGE); the CHECKs here
-- only keep the table sane.
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS daily_goals (
  id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  player_name   text NOT NULL,
  goal_date     date NOT NULL,
  metric        text NOT NULL CHECK (metric IN ('kcal', 'fullSwings', 'activeMinutes', 'xp')),
  target        double precision NOT NULL CHECK (target > 0),
  created_at    timestamptz NOT NULL DEFAULT now(),
  updated_at    timestamptz NOT NULL DEFAULT now(),
  completed_at  timestamptz,
  bonus_xp      integer NOT NULL DEFAULT 0,
  UNIQUE (player_name, goal_date)
);
CREATE INDEX IF NOT EXISTS daily_goals_player_idx ON daily_goals (player_name, goal_date DESC);
CREATE INDEX IF NOT EXISTS daily_goals_date_idx   ON daily_goals (goal_date);
