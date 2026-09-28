// Applies schema.sql to DATABASE_URL. Idempotent: every statement is IF NOT EXISTS.
//   npm run db:schema
const fs = require("fs");
const path = require("path");
const { query, pool } = require("../db");

(async () => {
  const sql = fs.readFileSync(path.join(__dirname, "..", "schema.sql"), "utf8");
  await query(sql);
  const { rows } = await query(
    "SELECT table_name FROM information_schema.tables WHERE table_schema = current_schema() ORDER BY table_name"
  );
  console.log(`[schema] applied — tables: ${rows.map((r) => r.table_name).join(", ")}`);
})()
  .catch((err) => {
    console.error("[schema] failed:", err.message);
    process.exitCode = 1;
  })
  .finally(() => pool.end());
