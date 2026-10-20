// A small in-memory cache with a time limit, for the read-only endpoints that are computed from many rows (rank, Wrapped, milestones).
// Entries live ttlMs; two requests for the same missing key share one computation; a key prefix can be dropped when something changes
// (a fight finished, a session was deleted or a player renamed). One Render instance, so process memory is the whole cache.

function createTtlCache({ ttlMs, max = 500, now = Date.now }) {
  const entries = new Map(); // key -> { at, value } | { promise }
  return {
    async get(key, load) {
      const hit = entries.get(key);
      if (hit && hit.promise) return hit.promise;
      if (hit && now() - hit.at < ttlMs) return hit.value;
      const promise = Promise.resolve()
        .then(load)
        .then((value) => {
          entries.set(key, { at: now(), value });
          while (entries.size > max) entries.delete(entries.keys().next().value); // oldest first
          return value;
        })
        .catch((err) => {
          entries.delete(key);
          throw err;
        });
      entries.set(key, { promise });
      return promise;
    },
    /** Drops every entry whose key starts with `prefix` (no prefix: everything). */
    invalidate(prefix = "") {
      for (const k of [...entries.keys()]) if (k.startsWith(prefix)) entries.delete(k);
    },
    size: () => entries.size,
  };
}

module.exports = { createTtlCache };
