import { useEffect, useState } from "react";
import { api } from "../../lib/api.js";
import { useLiveMessages } from "../../live/LiveProvider.jsx";

const isNum = (v) => typeof v === "number" && Number.isFinite(v);

// This fight's hit events (id -> { t, damage }), seeded from the stored session and kept up to date from the stream. A null id follows nothing.
export function useLiveHits(id) {
  const [hits, setHits] = useState(() => new Map());
  const add = (events) =>
    setHits((cur) => {
      let next = cur;
      for (const e of events) {
        const p = e?.payload;
        if (e?.type !== "hit" || !e.id || next.has(e.id) || !p || !isNum(p.t) || !isNum(p.damage)) continue;
        if (next === cur) next = new Map(cur);
        next.set(e.id, { t: p.t, damage: p.damage });
      }
      return next;
    });
  useEffect(() => {
    let alive = true;
    setHits(new Map());
    if (!id) return undefined; // nothing to follow (the commentary passes null while it is off or the fight is over)
    api.session(id).then((d) => alive && add(d.events || [])).catch(() => {});
    return () => {
      alive = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id]);
  useLiveMessages((type, e) => {
    if (type === "event_created" && e?.sessionId === id) add([e]);
  });
  return hits;
}
