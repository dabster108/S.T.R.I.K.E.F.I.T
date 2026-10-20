import { useMemo } from "react";
import { api } from "../lib/api.js";
import { useApi } from "./useApi.js";

const MAX_PLAYERS = 8; // a list shows many players; the chips are looked up for the first few distinct ones (each answer is cached by the server)

// Which fights in a list set a personal best: { [sessionId]: [metric, ...] }, from GET /api/players/:name/records for the players in `rows`.
// A player the server does not know (or a failed request) just has no chips. Returns undefined until the answers are in.
export function useRecordFlags(rows) {
  const names = useMemo(() => [...new Set((rows || []).map((r) => r.playerName))].slice(0, MAX_PLAYERS), [rows]);
  const key = names.join("\u0001");
  const res = useApi(async () => {
    const answers = await Promise.all(names.map((n) => api.playerRecords(n).catch(() => null)));
    return Object.assign({}, ...answers.filter(Boolean).map((a) => a.bySession));
  }, [key]);
  return res.data;
}
