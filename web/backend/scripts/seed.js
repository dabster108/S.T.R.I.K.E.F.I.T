// Idempotent database seed for all current backend tables.
// Safe to run multiple times: every insert uses ON CONFLICT handling.
//
// NOTE: this backend has no users/auth table. "Players" are names in sessions.
// So this script seeds realistic player profiles in app_meta and linked rows
// across every table, with valid foreign keys.

const { pool, withTransaction } = require("../db");

function isoDaysAgo(days, hour = 12) {
  const d = new Date(Date.now() - days * 24 * 60 * 60 * 1000);
  d.setUTCHours(hour, 0, 0, 0);
  return d.toISOString();
}

function dateDaysAgo(days) {
  return isoDaysAgo(days).slice(0, 10);
}

const PLAYERS = [
  { name: "Aarav Sharma", email: "aarav.sharma@example.com" },
  { name: "Maya Patel", email: "maya.patel@example.com" },
  { name: "Rohan Gurung", email: "rohan.gurung@example.com" },
];

const SESSIONS = [
  {
    id: "seed-session-aarav-001",
    playerName: PLAYERS[0].name,
    level: "Easy",
    outcome: "victory",
    score: 1240,
    damageDealt: 410.5,
    hitCount: 38,
    tapCount: 44,
    durationSec: 53.2,
    playerHpRemaining: 62,
    monsterHpRemaining: 0,
    pointsEarned: 1240,
    startedAt: isoDaysAgo(4, 9),
    endedAt: isoDaysAgo(4, 9),
    xp: 82,
    fullSwings: 16,
  },
  {
    id: "seed-session-maya-001",
    playerName: PLAYERS[1].name,
    level: "Medium",
    outcome: "victory",
    score: 1680,
    damageDealt: 520.2,
    hitCount: 45,
    tapCount: 51,
    durationSec: 67.9,
    playerHpRemaining: 49,
    monsterHpRemaining: 0,
    pointsEarned: 1680,
    startedAt: isoDaysAgo(3, 10),
    endedAt: isoDaysAgo(3, 10),
    xp: 104,
    fullSwings: 20,
  },
  {
    id: "seed-session-rohan-001",
    playerName: PLAYERS[2].name,
    level: "Hard",
    outcome: "defeat",
    score: 980,
    damageDealt: 330.8,
    hitCount: 29,
    tapCount: 36,
    durationSec: 71.1,
    playerHpRemaining: 0,
    monsterHpRemaining: 19,
    pointsEarned: 980,
    startedAt: isoDaysAgo(2, 11),
    endedAt: isoDaysAgo(2, 11),
    xp: 58,
    fullSwings: 11,
  },
];

const EVENTS = [
  {
    id: "seed-event-aarav-hit-001",
    sessionId: SESSIONS[0].id,
    type: "hit",
    playerName: SESSIONS[0].playerName,
    message: "Aarav lands a solid strike",
    payload: { swingSpeed: 7.1, damage: 15.3, level: "Easy" },
    at: isoDaysAgo(4, 9),
  },
  {
    id: "seed-event-maya-hit-001",
    sessionId: SESSIONS[1].id,
    type: "hit",
    playerName: SESSIONS[1].playerName,
    message: "Maya pushes combo",
    payload: { swingSpeed: 8.4, damage: 19.1, level: "Medium" },
    at: isoDaysAgo(3, 10),
  },
  {
    id: "seed-event-rohan-hit-001",
    sessionId: SESSIONS[2].id,
    type: "hit",
    playerName: SESSIONS[2].playerName,
    message: "Rohan attempts recovery",
    payload: { swingSpeed: 6.8, damage: 12.7, level: "Hard" },
    at: isoDaysAgo(2, 11),
  },
  {
    id: "seed-event-rohan-end-001",
    sessionId: SESSIONS[2].id,
    type: "session_end",
    playerName: SESSIONS[2].playerName,
    message: "Session ended in defeat",
    payload: { outcome: "defeat" },
    at: isoDaysAgo(2, 11),
  },
];

const DAILY_GOALS = [
  { playerName: PLAYERS[0].name, goalDate: dateDaysAgo(4), metric: "xp", target: 80, bonusXp: 10, completedAt: isoDaysAgo(4, 20) },
  { playerName: PLAYERS[1].name, goalDate: dateDaysAgo(3), metric: "fullSwings", target: 18, bonusXp: 10, completedAt: isoDaysAgo(3, 20) },
  { playerName: PLAYERS[2].name, goalDate: dateDaysAgo(2), metric: "activeMinutes", target: 12, bonusXp: 10, completedAt: null },
];

const CHALLENGE_COMPLETIONS = [
  {
    playerName: PLAYERS[0].name,
    challengeKey: `full_swings_daily@${dateDaysAgo(4)}`,
    challengeId: "full_swings_daily",
    period: "daily",
    periodStart: dateDaysAgo(4),
    title: "Power Starter",
    description: "Reach your full swing target in one day",
    target: 15,
    bonusXp: 15,
    completedAt: isoDaysAgo(4, 20),
  },
  {
    playerName: PLAYERS[1].name,
    challengeKey: `wins_weekly@${dateDaysAgo(7)}`,
    challengeId: "wins_weekly",
    period: "weekly",
    periodStart: dateDaysAgo(7),
    title: "Weekly Winner",
    description: "Get two wins this week",
    target: 2,
    bonusXp: 30,
    completedAt: isoDaysAgo(3, 20),
  },
  {
    playerName: PLAYERS[2].name,
    challengeKey: `sessions_daily@${dateDaysAgo(2)}`,
    challengeId: "sessions_daily",
    period: "daily",
    periodStart: dateDaysAgo(2),
    title: "Stay In The Fight",
    description: "Complete one session today",
    target: 1,
    bonusXp: 10,
    completedAt: isoDaysAgo(2, 20),
  },
];

const STUDY_PARTICIPANTS = [
  { code: "ZZTEST-P91", ageBand: "18-24", vrExperience: "never", weeklyExercise: "1-3h" },
  { code: "ZZTEST-P92", ageBand: "25-34", vrExperience: "once-or-twice", weeklyExercise: "3-5h" },
  { code: "ZZTEST-P93", ageBand: "35-44", vrExperience: "regularly", weeklyExercise: "under-1h" },
];

const STUDY_RESPONSES = [
  { code: "ZZTEST-P91", enjoyment: [4, 4, 5, 4, 5], effortVsUsual: 3, playAgain: "yes", sus: [4, 2, 5, 2, 4, 2, 5, 2, 4, 2], susScore: 80.0 },
  { code: "ZZTEST-P92", enjoyment: [3, 4, 4, 3, 4], effortVsUsual: 4, playAgain: "maybe", sus: [3, 3, 4, 3, 3, 3, 4, 3, 3, 3], susScore: 62.5 },
  { code: "ZZTEST-P93", enjoyment: [5, 5, 4, 5, 5], effortVsUsual: 2, playAgain: "yes", sus: [5, 1, 5, 1, 5, 1, 5, 1, 5, 1], susScore: 95.0 },
];

async function seed() {
  await withTransaction(async (client) => {
    for (const s of SESSIONS) {
      await client.query(
        `INSERT INTO sessions (
          id, player_name, level, outcome, score, damage_dealt, hit_count, tap_count,
          duration_sec, player_hp_remaining, monster_hp_remaining, points_earned,
          started_at, ended_at, source, notes, status, xp, full_swings, last_event_at, live_state
        )
        VALUES ($1, $2, $3, $4, $5, $6, $7, $8,
                $9, $10, $11, $12, $13, $14, 'seed', 'idempotent seed row', 'finished', $15, $16, $17, '{}'::jsonb)
        ON CONFLICT (id) DO UPDATE SET
          player_name = EXCLUDED.player_name,
          level = EXCLUDED.level,
          outcome = EXCLUDED.outcome,
          score = EXCLUDED.score,
          damage_dealt = EXCLUDED.damage_dealt,
          hit_count = EXCLUDED.hit_count,
          tap_count = EXCLUDED.tap_count,
          duration_sec = EXCLUDED.duration_sec,
          player_hp_remaining = EXCLUDED.player_hp_remaining,
          monster_hp_remaining = EXCLUDED.monster_hp_remaining,
          points_earned = EXCLUDED.points_earned,
          started_at = EXCLUDED.started_at,
          ended_at = EXCLUDED.ended_at,
          source = EXCLUDED.source,
          notes = EXCLUDED.notes,
          status = EXCLUDED.status,
          xp = EXCLUDED.xp,
          full_swings = EXCLUDED.full_swings,
          last_event_at = EXCLUDED.last_event_at`,
        [
          s.id, s.playerName, s.level, s.outcome, s.score, s.damageDealt, s.hitCount, s.tapCount,
          s.durationSec, s.playerHpRemaining, s.monsterHpRemaining, s.pointsEarned, s.startedAt, s.endedAt, s.xp, s.fullSwings, s.endedAt,
        ]
      );
    }

    for (const e of EVENTS) {
      await client.query(
        `INSERT INTO events (id, session_id, session_ref, type, player_name, message, payload, occurred_at)
         VALUES ($1, $2, $2, $3, $4, $5, $6::json, $7)
         ON CONFLICT (id) DO UPDATE SET
           session_id = EXCLUDED.session_id,
           session_ref = EXCLUDED.session_ref,
           type = EXCLUDED.type,
           player_name = EXCLUDED.player_name,
           message = EXCLUDED.message,
           payload = EXCLUDED.payload,
           occurred_at = EXCLUDED.occurred_at`,
        [e.id, e.sessionId, e.type, e.playerName, e.message, JSON.stringify(e.payload), e.at]
      );
    }

    await client.query(
      `INSERT INTO app_meta (key, value)
       VALUES ('seed:profiles', $1::jsonb)
       ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value`,
      [JSON.stringify({ generatedBy: "scripts/seed.js", players: PLAYERS })]
    );

    for (const s of SESSIONS) {
      await client.query(
        `INSERT INTO session_feedback (session_id, enjoyment, rpe, comment)
         VALUES ($1, $2, $3, $4)
         ON CONFLICT (session_id) DO UPDATE SET
           enjoyment = EXCLUDED.enjoyment,
           rpe = EXCLUDED.rpe,
           comment = EXCLUDED.comment`,
        [s.id, 4, 13, "Seed feedback row"]
      );
    }

    for (const g of DAILY_GOALS) {
      await client.query(
        `INSERT INTO daily_goals (player_name, goal_date, metric, target, completed_at, bonus_xp)
         VALUES ($1, $2, $3, $4, $5, $6)
         ON CONFLICT (player_name, goal_date) DO UPDATE SET
           metric = EXCLUDED.metric,
           target = EXCLUDED.target,
           completed_at = EXCLUDED.completed_at,
           bonus_xp = EXCLUDED.bonus_xp,
           updated_at = now()`,
        [g.playerName, g.goalDate, g.metric, g.target, g.completedAt, g.bonusXp]
      );
    }

    for (const c of CHALLENGE_COMPLETIONS) {
      await client.query(
        `INSERT INTO challenge_completions (
          player_name, challenge_key, challenge_id, period, period_start, title, description, target, completed_at, bonus_xp
         )
         VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
         ON CONFLICT (player_name, challenge_key) DO UPDATE SET
           challenge_id = EXCLUDED.challenge_id,
           period = EXCLUDED.period,
           period_start = EXCLUDED.period_start,
           title = EXCLUDED.title,
           description = EXCLUDED.description,
           target = EXCLUDED.target,
           completed_at = EXCLUDED.completed_at,
           bonus_xp = EXCLUDED.bonus_xp`,
        [c.playerName, c.challengeKey, c.challengeId, c.period, c.periodStart, c.title, c.description, c.target, c.completedAt, c.bonusXp]
      );
    }

    for (const p of STUDY_PARTICIPANTS) {
      await client.query(
        `INSERT INTO study_participants (code, age_band, vr_experience, weekly_exercise, pre_at)
         VALUES ($1, $2, $3, $4, now())
         ON CONFLICT (code) DO UPDATE SET
           age_band = EXCLUDED.age_band,
           vr_experience = EXCLUDED.vr_experience,
           weekly_exercise = EXCLUDED.weekly_exercise`,
        [p.code, p.ageBand, p.vrExperience, p.weeklyExercise]
      );
      await client.query(
        `INSERT INTO study_consents (participant_code, version, consented_at)
         VALUES ($1, $2, now())
         ON CONFLICT (participant_code) DO UPDATE SET version = EXCLUDED.version`,
        [p.code, "seed-v1"]
      );
    }

    for (const r of STUDY_RESPONSES) {
      await client.query(
        `INSERT INTO study_responses (participant_code, enjoyment, effort_vs_usual, play_again, sus, sus_score)
         VALUES ($1, $2::smallint[], $3, $4, $5::smallint[], $6)
         ON CONFLICT (participant_code) DO UPDATE SET
           enjoyment = EXCLUDED.enjoyment,
           effort_vs_usual = EXCLUDED.effort_vs_usual,
           play_again = EXCLUDED.play_again,
           sus = EXCLUDED.sus,
           sus_score = EXCLUDED.sus_score`,
        [r.code, r.enjoyment, r.effortVsUsual, r.playAgain, r.sus, r.susScore]
      );
    }
  });

  const { rows } = await pool.query(
    `SELECT table_name
     FROM information_schema.tables
     WHERE table_schema = current_schema()
     ORDER BY table_name`
  );
  console.log(`[seed] complete for ${rows.length} table(s): ${rows.map((r) => r.table_name).join(", ")}`);
}

seed()
  .catch((err) => {
    console.error("[seed] failed:", err.message);
    process.exitCode = 1;
  })
  .finally(async () => {
    await pool.end().catch(() => {});
  });
