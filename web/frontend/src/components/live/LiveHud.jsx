import { memo, useEffect, useRef, useState } from "react";
import { api } from "../../lib/api.js";
import { TIER_LABEL } from "../../lib/game.js";
import { useLiveIds, useLiveMessages, useLiveSession } from "../../live/LiveProvider.jsx";
import Icon from "../ui/Icon.jsx";

// Combo counter, swing-speed gauge and player-vs-monster health race for one live fight.
// Driven by the same stream as the rest of the live view: liveState (combo, HP) and the `hit`
// events (swingSpeed, tier, combo). Only transform and opacity animate. A session whose game build
// sends no combo or no swing speed simply shows less, never a made-up number.

const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const GAUGE_MAX = 8; // m/s: the speed at which the game's damage curve tops out (README: 1.2 to 8 m/s)
const GAUGE_IDLE_MS = 2500; // the gauge falls back to zero when no swing has landed for this long
const PB_SHOW_MS = 2800;

const TONES = {
  card: { text: "text-[var(--ink)]", sub: "text-[var(--muted)]", track: "bg-gray-100", wrap: "border-t border-[var(--border)] pt-4 mt-4" },
  overlay: { text: "text-white", sub: "text-[var(--blue-100)]", track: "bg-white/20", wrap: "rounded-2xl bg-[var(--blue-900)]/80 p-3 backdrop-blur-sm sm:p-4" },
};

const Combo = memo(function Combo({ value, tone }) {
  const prev = useRef(value);
  const [bump, setBump] = useState(0);
  useEffect(() => {
    if (value > prev.current) setBump((b) => b + 1);
    prev.current = value;
  }, [value]);
  return (
    <div className="min-w-0 text-center" aria-label={`Combo ${value}`}>
      <p className={`text-[0.65rem] font-extrabold uppercase tracking-wider ${tone.sub}`}>Combo</p>
      <p key={bump} className={`text-4xl font-extrabold tabular-nums leading-none sm:text-5xl ${tone.text} ${bump ? "tick-up inline-block" : ""}`}>
        {value}
        <span className="text-xl sm:text-2xl">×</span>
      </p>
    </div>
  );
});

const Gauge = memo(function Gauge({ speed, tier, tone }) {
  const pct = isNum(speed) ? Math.max(0, Math.min(1, speed / GAUGE_MAX)) : 0;
  return (
    <div className="min-w-0 flex-1">
      <div className="flex items-baseline justify-between gap-2">
        <p className={`text-[0.65rem] font-extrabold uppercase tracking-wider ${tone.sub}`}>Swing speed</p>
        <p className={`text-sm font-extrabold tabular-nums ${tone.text}`}>
          {isNum(speed) && speed > 0 ? speed.toFixed(1) : "0.0"} <span className={`text-xs font-semibold ${tone.sub}`}>m/s{speed > 0 && tier && TIER_LABEL[tier] ? ` · ${TIER_LABEL[tier]}` : ""}</span>
        </p>
      </div>
      <div className={`relative mt-1.5 h-3 w-full overflow-hidden rounded-full ${tone.track}`} role="meter" aria-label="Swing speed" aria-valuemin={0} aria-valuemax={GAUGE_MAX} aria-valuenow={isNum(speed) ? Number(speed.toFixed(1)) : 0}>
        <div className="absolute inset-0 origin-left rounded-full bg-[var(--accent)] transition-transform duration-200 ease-out" style={{ transform: `scaleX(${pct})` }} />
      </div>
    </div>
  );
});

const Race = memo(function Race({ player, monster, tone }) {
  const lead = player === null || monster === null ? null : player > monster ? "player" : monster > player ? "monster" : "even";
  const Row = ({ label, pct, color }) => (
    <div className="flex items-center gap-2">
      <span className={`w-14 shrink-0 text-xs font-bold ${tone.sub}`}>{label}</span>
      <div className={`relative h-3 flex-1 overflow-hidden rounded-full ${tone.track}`} role="meter" aria-label={`${label} health`} aria-valuemin={0} aria-valuemax={100} aria-valuenow={pct === null ? 0 : Math.round(pct * 100)}>
        <div className="absolute inset-0 origin-left rounded-full transition-transform duration-300 ease-out" style={{ transform: `scaleX(${pct ?? 0})`, background: color }} />
      </div>
      <span className={`w-10 shrink-0 text-right text-xs font-extrabold tabular-nums ${tone.text}`}>{pct === null ? "—" : `${Math.round(pct * 100)}%`}</span>
    </div>
  );
  return (
    <div className="min-w-0 space-y-1.5">
      <Row label="Player" pct={player} color="var(--hp-player)" />
      <Row label="Monster" pct={monster} color="var(--hp-monster)" />
      <p className={`text-[0.7rem] font-semibold ${tone.sub}`} aria-live="polite">
        {lead === "player" ? "Player ahead" : lead === "monster" ? "Monster ahead" : lead === "even" ? "Dead even" : "Waiting for health data"}
      </p>
    </div>
  );
});

const fraction = (v, max) => (isNum(v) && isNum(max) && max > 0 ? Math.max(0, Math.min(1, v / max)) : null);

export const LiveHud = memo(function LiveHud({ id, variant = "card" }) {
  const entry = useLiveSession(id);
  const tone = TONES[variant];
  const name = entry?.session.playerName;
  const live = entry?.session.status === "live";

  const [hit, setHit] = useState(null); // { speed, tier }
  const [pb, setPb] = useState(0); // key of the visible "new PB" label, 0 = hidden
  const best = useRef({ pb: null, session: 0 }); // player's stored record and this fight's best so far
  const timers = useRef([]);
  useEffect(() => () => timers.current.forEach(clearTimeout), []);

  // The record to beat: best swing speed over the player's earlier sessions. No baseline, no flash.
  useEffect(() => {
    if (!name) return undefined;
    let off = false;
    best.current = { pb: null, session: 0 };
    api
      .player(name)
      .then((p) => {
        if (!off) best.current.pb = p.personalBests?.highestSwingVelocity?.value ?? null;
      })
      .catch(() => {});
    return () => {
      off = true;
    };
  }, [name, id]);

  const idle = useRef(null);
  useLiveMessages((type, e) => {
    if (!live || type !== "event_created" || e.sessionId !== id || e.type !== "hit") return;
    const p = e.payload || {};
    if (!isNum(p.swingSpeed)) return;
    setHit({ speed: p.swingSpeed, tier: typeof p.tier === "string" ? p.tier : null });
    clearTimeout(idle.current);
    idle.current = setTimeout(() => setHit(null), GAUGE_IDLE_MS);
    const b = best.current;
    if (isNum(b.pb) && p.swingSpeed > Math.max(b.pb, b.session)) {
      const key = Date.now();
      setPb(key);
      timers.current.push(setTimeout(() => setPb((cur) => (cur === key ? 0 : cur)), PB_SHOW_MS));
    }
    b.session = Math.max(b.session, p.swingSpeed);
  });
  useEffect(() => () => clearTimeout(idle.current), []);

  if (!entry) return null;
  const st = entry.liveState;
  const combo = isNum(st?.combo) ? Math.max(0, Math.round(st.combo)) : null;
  const hasSpeed = best.current.session > 0 || hit !== null;

  return (
    <div className={`relative ${tone.wrap}`} aria-label="Live combo, swing speed and health">
      <div className="flex flex-wrap items-center gap-x-6 gap-y-3">
        {combo !== null && <Combo value={combo} tone={tone} />}
        {(hasSpeed || combo !== null) && <Gauge speed={hit?.speed ?? 0} tier={hit?.tier} tone={tone} />}
        <div className="min-w-[12rem] flex-[2]">
          <Race player={fraction(st?.playerHp, st?.playerMaxHp)} monster={fraction(st?.monsterHp, st?.monsterMaxHp)} tone={tone} />
        </div>
      </div>
      {combo === null && !hasSpeed && variant === "card" && live && <p className={`mt-3 text-xs font-medium ${tone.sub}`}>Combo and swing speed show up once the game reports them for this fight.</p>}
      {pb !== 0 && (
        <span key={pb} className="pb-pop pointer-events-none absolute -top-3 right-3 inline-flex items-center gap-1.5 rounded-full bg-amber-400 px-3 py-1 text-xs font-extrabold text-amber-950 shadow-[var(--shadow-lift)]" role="status">
          <Icon name="sparkles" className="h-3.5 w-3.5" /> New PB: fastest swing
        </span>
      )}
    </div>
  );
});

// Over the broadcast video: the first fight in the arena.
export function LiveOverlay() {
  const ids = useLiveIds();
  if (!ids.length) return null;
  return (
    <div className="pointer-events-none absolute inset-x-3 bottom-3 z-10 sm:inset-x-5 sm:bottom-5">
      <LiveHud id={ids[0]} variant="overlay" />
    </div>
  );
}
