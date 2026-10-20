// Seeds 10 TEST participants (ZZTEST-P01 .. ZZTEST-P10) into the DEV database, with consent, pre-play answers, simulated fights
// and post-play questionnaires, so the Thesis Lab's study section has something to show.
//
//   npm run dev:seed-study                 add them (skips ones that exist)
//   npm run dev:seed-study -- --reset      delete the ZZTEST participants and their fights first, then add them
//   npm run dev:seed-study -- --url http://localhost:4100   (the dev backend; default http://localhost:4000)
//
// REFUSES to run against anything but the local development database: the connection string must point at this machine
// (localhost / 127.0.0.1 / ::1) and the process must not look like production. Unlike the backend's own guard it ignores
// DEV_DB_ALLOWED_HOSTS and ALLOW_PROD_DB: there is no override. Every row it writes is named ZZTEST-...; --reset removes only those.

const { runFight, parseArgs } = require("./simulate-live");

function assertDevDatabaseOnly() {
  const env = process.env;
  if (env.RENDER || env.NODE_ENV === "production" || env.APP_ENV === "production") throw new Error("refusing to run: this looks like a production environment");
  // Load the environment exactly as the server does (a development process reads .env.development and ignores .env's DATABASE_URL).
  require("../envLoader").loadEnv(require("path").join(__dirname, ".."));
  const url = process.env.DATABASE_URL;
  if (!url) throw new Error("refusing to run: DATABASE_URL is not set (start the dev database with npm run dev:db)");
  let host;
  try {
    host = new URL(url).hostname.toLowerCase();
  } catch {
    throw new Error("refusing to run: DATABASE_URL is not a valid connection string");
  }
  if (!["localhost", "127.0.0.1", "::1", "[::1]"].includes(host)) throw new Error(`refusing to run: the database host is "${host}", not this machine. This script only ever writes to the local dev database.`);
  return { host };
}

// code, age, vr experience, weekly exercise, fights [level, outcome, seconds, daysAgo], rpe per fight, enjoyment items, effort 1-5, play again, SUS answers
const PLAN = [
  ["ZZTEST-P01", "18-24", "never", "1-3h", [["Easy", "victory", 40, 6], ["Medium", "victory", 62, 6]], [11, 13], [4, 4, 5, 3, 5], 3, "yes", [4, 2, 5, 1, 4, 2, 5, 2, 4, 1]],
  ["ZZTEST-P02", "25-34", "once-or-twice", "none", [["Easy", "victory", 46, 5], ["Easy", "defeat", 52, 5]], [14, 15], [3, 3, 4, 2, 4], 4, "maybe", [3, 3, 4, 2, 3, 2, 4, 3, 3, 2]],
  ["ZZTEST-P03", "25-34", "regularly", "3-5h", [["Medium", "victory", 60, 5], ["Hard", "defeat", 100, 5]], [12, 16], [5, 5, 4, 4, 5], 3, "yes", [5, 1, 5, 1, 4, 1, 5, 1, 5, 1]],
  ["ZZTEST-P04", "35-44", "never", "under-1h", [["Easy", "victory", 44, 4], ["Medium", "defeat", 70, 4]], [13, 17], [4, 3, 3, 3, 4], 5, "yes", [3, 2, 4, 3, 4, 2, 3, 2, 3, 3]],
  ["ZZTEST-P05", "18-24", "occasionally", "1-3h", [["Easy", "victory", 38, 4], ["Hard", "victory", 120, 3]], [10, 15], [5, 4, 5, 4, 5], 4, "yes", [4, 1, 5, 2, 5, 1, 4, 1, 5, 2]],
  ["ZZTEST-P06", "45-54", "never", "none", [["Easy", "defeat", 50, 3], ["Easy", "victory", 48, 3]], [15, 14], [3, 2, 3, 2, 3], 5, "maybe", [2, 4, 3, 3, 2, 3, 3, 4, 2, 4]],
  ["ZZTEST-P07", "25-34", "once-or-twice", "3-5h", [["Medium", "victory", 58, 2], ["Medium", "victory", 56, 2]], [12, 12], [4, 4, 4, 4, 4], 2, "yes", [4, 2, 4, 2, 4, 2, 4, 2, 4, 2]],
  ["ZZTEST-P08", "35-44", "occasionally", "over-5h", [["Easy", "victory", 36, 2], ["Hard", "defeat", 110, 2]], [9, 16], [5, 5, 5, 4, 5], 2, "yes", [5, 1, 5, 2, 5, 1, 5, 1, 4, 1]],
  ["ZZTEST-P09", "55-64", "never", "under-1h", [["Easy", "victory", 52, 1], ["Easy", "victory", 50, 1]], [13, 12], [3, 4, 3, 3, 4], 4, "maybe", [3, 3, 3, 3, 3, 3, 3, 3, 3, 3]],
  ["ZZTEST-P10", "18-24", "regularly", "1-3h", [["Medium", "defeat", 66, 1], ["Hard", "defeat", 105, 1]], [17, 18], [4, 5, 4, 5, 5], 3, "no", [4, 2, 4, 1, 5, 2, 4, 2, 4, 2]],
];

(async () => {
  const guard = assertDevDatabaseOnly();
  const args = parseArgs(process.argv.slice(2));
  const base = String(args.url || "http://localhost:4000").replace(/\/+$/, "");
  const { query, pool } = require("../db");
  const core = require("../studyCore");
  try {
    const health = await fetch(`${base}/api/health`).then((r) => r.json()).catch(() => null);
    if (!health || !health.ok) throw new Error(`no healthy backend at ${base}. Start it with npm run dev:safe first.`);
    console.log(`[seed-study] database host ${guard.host} (local), backend ${base}`);

    if (args.reset) {
      const codes = PLAN.map((p) => p[0]);
      const del = await query(`DELETE FROM sessions WHERE player_name = ANY($1) RETURNING id`, [codes]);
      const p = await query(`DELETE FROM study_participants WHERE code LIKE 'ZZTEST-%' RETURNING code`);
      console.log(`[seed-study] reset: removed ${p.rowCount} participants and ${del.rowCount} fights`);
    }

    let added = 0;
    for (const [code, age, vr, weekly, fights, rpes, enj, effortVsUsual, playAgain, sus] of PLAN) {
      const exists = await query(`SELECT 1 FROM study_participants WHERE code = $1`, [code]);
      if (exists.rowCount) {
        console.log(`[seed-study] ${code} exists, skipped`);
        continue;
      }
      await query(`INSERT INTO study_participants (code, age_band, vr_experience, weekly_exercise, pre_at) VALUES ($1, $2, $3, $4, now())`, [code, age, vr, weekly]);
      await query(`INSERT INTO study_consents (participant_code, version) VALUES ($1, $2)`, [code, core.CONSENT_VERSION]);
      for (let i = 0; i < fights.length; i++) {
        const [level, outcome, seconds, daysAgo] = fights[i];
        const r = await runFight({ url: base, player: code, level, outcome, seconds, daysAgo, fast: true, keep: true, quiet: true, seed: (PLAN.findIndex((p) => p[0] === code) + 1) * 131 + i });
        await query(`INSERT INTO session_feedback (session_id, rpe) VALUES ($1, $2) ON CONFLICT DO NOTHING`, [r.id, rpes[i]]);
      }
      await query(
        `INSERT INTO study_responses (participant_code, enjoyment, effort_vs_usual, play_again, sus, sus_score) VALUES ($1, $2, $3, $4, $5, $6)`,
        [code, enj, effortVsUsual, playAgain, sus, core.susScore(sus)]
      );
      added++;
      console.log(`[seed-study] ${code}: ${fights.length} fights, SUS ${core.susScore(sus)}`);
    }
    console.log(`[seed-study] done: ${added} participants added`);
  } finally {
    await pool.end().catch(() => {});
  }
})().catch((e) => {
  console.error("[seed-study] failed:", e.message);
  process.exit(1);
});
