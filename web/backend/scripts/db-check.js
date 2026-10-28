// Checks DATABASE_URL connectivity with one simple query.
// Prints a short success/failure line and exits with 0/1.
// Uses db.js, which already applies 30 s connection timeout and retry/backoff for cold starts.

const { query, pool } = require("../db");
const { sanitize } = require("../dbRetry");

(async () => {
  const started = Date.now();
  await query("SELECT 1 AS ok");
  const ms = Date.now() - started;
  console.log(`[db:check] success (${ms} ms)`);
})()
  .catch((err) => {
    console.error(`[db:check] failed: ${sanitize(err.message)}`);
    process.exitCode = 1;
  })
  .finally(async () => {
    await pool.end().catch(() => {});
  });
