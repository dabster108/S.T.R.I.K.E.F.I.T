import { useMemo } from "react";
import { RANK_REFRESH_MS } from "../config/ranks.js";
import { api } from "../lib/api.js";
import { useLiveMessages } from "../live/LiveProvider.jsx";
import { useApi, useThrottledCallback } from "./useApi.js";

// One player's rank (GET /api/players/:name/rank), fetched once and refreshed when that player's fight ends. The answer for another
// name is never shown while the next one loads. A player with no fights has no rank: { data: undefined, notFound: true }.
export function useRank(name) {
  const rank = useApi(() => (name ? api.playerRank(name) : Promise.resolve(null)), [name]);
  const refresh = useThrottledCallback(rank.reload, RANK_REFRESH_MS);
  useLiveMessages((type, payload) => {
    if (!name || payload?.session?.playerName !== name) return;
    if (type === "session_finished" || type === "session_deleted") refresh();
  });
  const data = rank.data && rank.data.playerName === name ? rank.data : undefined;
  return { data, loading: rank.loading && !data, error: rank.error, notFound: rank.error?.status === 404 && !data };
}

// Ranks for several players in one request (the leaderboard rows). -> { [name]: { rank: { key, name }, effortPoints, progress } }
export function useRanks(names) {
  const key = useMemo(() => [...new Set(names)].filter(Boolean).slice(0, 50).join("\u0001"), [names]);
  const list = key ? key.split("\u0001") : [];
  const res = useApi(() => (list.length ? api.ranks(list) : Promise.resolve({ ranks: {} })), [key]);
  return res.data?.ranks || {};
}
