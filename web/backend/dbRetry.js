// Bounded retries for a database that scales to zero (Layerbase, Neon): the first connection after an idle period can fail or time out
// while the database wakes up (a few seconds). Only failures that look like "could not connect / still starting" are retried, a few
// times with a growing pause; anything else (a bad password, a SQL error) is thrown at once. Pure helpers, so they can be tested.

const TRANSIENT_CODES = new Set([
  "ECONNREFUSED", "ECONNRESET", "ETIMEDOUT", "EPIPE", "EAI_AGAIN", // the network or the process behind it is not ready
  "57P01", "57P02", "57P03", // admin shutdown, crash shutdown, cannot connect now (starting up)
  "08000", "08001", "08003", "08004", "08006", // connection exceptions
]);
const TRANSIENT_TEXT = /timeout exceeded when trying to connect|connection terminated|server closed the connection|starting up|resuming|read ECONNRESET/i;

/** Does this error look like a database that is not reachable yet (rather than a mistake in the request)? */
function isTransient(err) {
  if (!err) return false;
  return TRANSIENT_CODES.has(err.code) || TRANSIENT_TEXT.test(String(err.message || ""));
}

/** An error message with any connection string in it removed, safe to log. */
function sanitize(message) {
  return String(message || "").replace(/postgres(?:ql)?:\/\/\S+/gi, "<connection string>").slice(0, 200);
}

/**
 * Runs fn(), retrying transient failures. attempts is the total number of tries; the pause before try n+1 is baseMs * 2^(n-1).
 * `sleep` and `onRetry({ attempt, delayMs, error })` can be replaced.
 */
async function withRetry(fn, { attempts = 3, baseMs = 750, sleep = (ms) => new Promise((r) => setTimeout(r, ms)), onRetry = () => {} } = {}) {
  for (let attempt = 1; ; attempt++) {
    try {
      return await fn();
    } catch (err) {
      if (attempt >= attempts || !isTransient(err)) throw err;
      const delayMs = baseMs * 2 ** (attempt - 1);
      onRetry({ attempt, delayMs, error: err });
      await sleep(delayMs);
    }
  }
}

module.exports = { isTransient, sanitize, withRetry };
