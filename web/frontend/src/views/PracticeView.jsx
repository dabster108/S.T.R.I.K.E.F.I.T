import { useCallback, useEffect, useId, useRef, useState } from "react";
import { Link } from "react-router-dom";
import LandingNav from "../components/landing/LandingNav.jsx";
import { Footer } from "../components/landing/FinalCta.jsx";
import { buttonClass } from "../components/ui/Button.jsx";
import { SegmentedControl } from "../components/ui/Controls.jsx";
import Icon from "../components/ui/Icon.jsx";
import MonsterPortrait from "../components/ui/MonsterPortrait.jsx";
import { downloadBlob } from "../components/wrapped/exportPng.js";
import { useReducedMotion } from "../hooks/motion.js";
import { LEVELS, SWING_SPEED_CAP, TIER_LABEL, roundToInt, swingBands } from "../lib/game.js";
import { MONSTER_IMAGES } from "../lib/monsters.js";
import {
  GUARD,
  PRACTICE_LEVELS,
  applySwing,
  createDragTracker,
  createGravityFilter,
  createMotionEstimator,
  demoSpeed,
  guardAt,
  liveCombo,
  loadPrefs,
  newSession,
  savePrefs,
  summary,
} from "../lib/practice.js";
import { guardianForKey } from "../lib/story.js";

// /practice: swing at a guardian without a headset. Phone motion (DeviceMotion), a mouse / touch drag, or a demo swing; the
// damage, tiers, XP, combo and guard are the game's own numbers (lib/practice.js). Nothing is sent anywhere: the page makes no
// network request of its own, and only a best speed and the calibration are kept, in this browser's localStorage when allowed.

const TIER_COLOR = { none: "var(--border-strong)", tap: "var(--tier-tap)", light: "var(--tier-light)", medium: "var(--tier-medium)", full: "var(--tier-full)" };
const fmt = (v, d = 1) => (Number.isFinite(v) ? v.toFixed(d) : "-");
const LEVEL_OPTS = PRACTICE_LEVELS.map((k) => ({ id: k, label: `${LEVELS[k].name} · ${LEVELS[k].weapons.replace("Dual ", "").toLowerCase()}` }));
const MODE_OPTS = [
  { id: "drag", label: "Mouse / touch" },
  { id: "motion", label: "Phone motion" },
];

// A floating damage number over the guardian. Rises and fades (just fades under reduced motion / Calm mode).
function Pop({ pop, reduced, onDone }) {
  const ref = useRef(null);
  useEffect(() => {
    const el = ref.current;
    if (!el || !el.animate) {
      const t = setTimeout(onDone, 900);
      return () => clearTimeout(t);
    }
    const frames = reduced
      ? [{ opacity: 0 }, { opacity: 1, offset: 0.15 }, { opacity: 1, offset: 0.7 }, { opacity: 0 }]
      : [
          { opacity: 0, transform: "translate(-50%, 8px) scale(0.85)" },
          { opacity: 1, transform: "translate(-50%, -6px) scale(1.08)", offset: 0.18 },
          { opacity: 1, transform: "translate(-50%, -34px) scale(1)", offset: 0.7 },
          { opacity: 0, transform: "translate(-50%, -52px) scale(0.96)" },
        ];
    const a = el.animate(frames, { duration: 1000, easing: "cubic-bezier(.2,.7,.2,1)", fill: "forwards" });
    a.onfinish = onDone;
    return () => a.cancel();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  return (
    <span
      ref={ref}
      className="pointer-events-none absolute whitespace-nowrap rounded-lg bg-white/90 px-2 py-0.5 text-xl font-extrabold tabular-nums shadow-[0_4px_14px_rgba(0,0,0,0.25)] ring-1 ring-black/5 sm:text-2xl"
      style={{ left: `${pop.x}%`, top: `${pop.y}%`, color: pop.tier === "none" ? "var(--muted)" : TIER_COLOR[pop.tier], transform: "translate(-50%, 0)", opacity: 0 }}
      aria-hidden
    >
      {pop.text}
    </span>
  );
}

// 0-8 m/s ruler with this level's bands; the marker sits at the last swing's estimate.
function SpeedRuler({ level, speed }) {
  const bands = swingBands(level, SWING_SPEED_CAP);
  return (
    <div>
      <div className="relative">
        <div className="flex h-3 gap-[2px] overflow-hidden rounded-full" aria-hidden>
          {bands.map((b) => (
            <span key={b.id} style={{ width: `${((b.to - b.from) / SWING_SPEED_CAP) * 100}%`, background: TIER_COLOR[b.id] }} />
          ))}
        </div>
        {Number.isFinite(speed) && (
          <span
            className="absolute -top-1 h-5 w-1.5 -translate-x-1/2 rounded-full bg-[var(--ink)] shadow ring-2 ring-[var(--surface)] transition-[left] duration-300"
            style={{ left: `${(Math.min(speed, SWING_SPEED_CAP) / SWING_SPEED_CAP) * 100}%` }}
            aria-hidden
          />
        )}
      </div>
      <ul className="mt-2 flex flex-wrap gap-x-3 gap-y-1 text-[0.7rem] font-semibold text-[var(--muted)]">
        {bands.map((b) => (
          <li key={b.id} className="flex items-center gap-1">
            <span className="h-2 w-2 rounded-full" style={{ background: TIER_COLOR[b.id] }} aria-hidden />
            {b.label} {b.id === "none" ? `< ${b.from + b.to}` : `${fmt(b.from)}+`}
          </li>
        ))}
        <li>m/s</li>
      </ul>
    </div>
  );
}

function Stat({ label, value, hint }) {
  return (
    <div className="rounded-xl border border-[var(--border)] bg-[var(--surface)] px-3 py-2.5">
      <dt className="text-[0.7rem] font-bold uppercase tracking-wide text-[var(--muted)]">{label}</dt>
      <dd className="mt-0.5 text-lg font-extrabold tabular-nums text-[var(--ink)]">
        {value}
        {hint && <span className="ml-1 text-xs font-semibold text-[var(--muted)]">{hint}</span>}
      </dd>
    </div>
  );
}

// The shareable result card, rendered off screen at its exact size (1200 x 630) and turned into a PNG on demand.
function ResultCard({ cardRef, session, guardian }) {
  const s = summary(session);
  const l = LEVELS[session.level];
  return (
    <div aria-hidden style={{ position: "fixed", left: -20000, top: 0, pointerEvents: "none" }}>
      <div ref={cardRef} style={{ width: 1200, height: 630, fontFamily: "Inter, system-ui, sans-serif" }} className="relative flex overflow-hidden bg-[#0b1020] text-white">
        <div className="flex flex-1 flex-col justify-between p-14">
          <div>
            <p className="text-lg font-extrabold tracking-[0.2em] text-[#8fb3ff]">STRIKEFIT · PRACTICE</p>
            <p className="mt-6 text-5xl font-extrabold leading-tight">
              {s.defeated ? `${guardian?.name || l.monster} is down.` : `${roundToInt(s.damage)} damage to ${guardian?.name || l.monster}`}
            </p>
            <p className="mt-3 text-xl font-semibold text-white/70">
              {l.name} · {s.hits} hits in {s.swings} swings · best combo {s.bestCombo}
            </p>
          </div>
          <div className="grid grid-cols-3 gap-6">
            {[
              ["Best swing (est.)", `${fmt(s.bestSpeed)} m/s`],
              ["Full power", String(s.fullPower)],
              ["XP", String(s.xp)],
            ].map(([k, v]) => (
              <div key={k}>
                <p className="text-sm font-bold uppercase tracking-wider text-white/55">{k}</p>
                <p className="mt-1 text-4xl font-extrabold tabular-nums">{v}</p>
              </div>
            ))}
          </div>
          <p className="text-sm font-semibold text-white/55">Speeds are estimates from a phone or a mouse, not a VR controller. Practice only: not study data.</p>
        </div>
        <div className="relative w-[380px] shrink-0 bg-gradient-to-b from-[#1b2550] to-[#0b1020]">
          <img src={MONSTER_IMAGES[session.level].webp[480]} alt="" className="absolute bottom-0 left-1/2 h-[560px] -translate-x-1/2 object-contain" />
        </div>
      </div>
    </div>
  );
}

export default function PracticeView() {
  const reduced = useReducedMotion();
  const [prefs, setPrefs] = useState(() => loadPrefs());
  const [level, setLevel] = useState(() => (PRACTICE_LEVELS.includes(prefs.level) ? prefs.level : "Easy"));
  const [session, setSession] = useState(() => newSession(level));
  const sessionRef = useRef(session);
  const [last, setLast] = useState(null);
  const [pops, setPops] = useState([]);
  // a touch screen is most likely a phone: start on phone motion (it still needs the safety tick); otherwise the mouse
  const [mode, setMode] = useState(() => (typeof matchMedia === "function" && matchMedia("(pointer: coarse)").matches ? "motion" : "drag"));
  const [clock, setClock] = useState(0);
  const t0 = useRef(typeof performance !== "undefined" ? performance.now() : 0);
  const now = () => (performance.now() - t0.current) / 1000;
  const n = useRef(0);
  const guardian = guardianForKey(level);
  const sens = prefs.sensitivity ?? 1;
  const scale = prefs.motionScale ?? 1;

  useEffect(() => {
    document.title = "Practice your swing · STRIKEFIT";
  }, []);

  // the guard and the combo run on the session clock
  useEffect(() => {
    const id = setInterval(() => setClock(now()), 100);
    return () => clearInterval(id);
  }, []);
  const guard = guardAt(clock);

  const updatePrefs = (patch) =>
    setPrefs((p) => {
      const next = { ...p, ...patch };
      savePrefs(next);
      return next;
    });

  const startOver = useCallback(
    (lv = level) => {
      const s = newSession(lv);
      sessionRef.current = s;
      setSession(s);
      setLast(null);
      setPops([]);
      t0.current = performance.now();
      setClock(0);
    },
    [level]
  );

  const pickLevel = (lv) => {
    setLevel(lv);
    updatePrefs({ level: lv });
    startOver(lv);
  };

  // one swing from any input
  const swing = useCallback(
    (speed, source) => {
      const t = now();
      const { session: s, hit } = applySwing(sessionRef.current, speed, { now: t, guarding: guardAt(t).guarding });
      if (!hit) return;
      sessionRef.current = s;
      setSession(s);
      setLast({ ...hit, source, at: t });
      const id = ++n.current;
      const text = !hit.counts ? "No hit" : hit.guarded ? `Blocked ${roundToInt(hit.damage)}` : String(roundToInt(hit.damage));
      setPops((p) => [...p.slice(-5), { id, text, tier: hit.tier, x: 38 + ((id * 37) % 26), y: 30 + ((id * 23) % 22) }]);
      if (hit.counts && hit.speed > (prefs.best?.[s.level] ?? 0)) updatePrefs({ best: { ...(prefs.best || {}), [s.level]: +hit.speed.toFixed(2) } });
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [prefs]
  );

  const demo = useCallback(() => swing(demoSpeed(n.current, sessionRef.current.level), "demo"), [swing]);

  // S: a demo swing (not while typing, not with a modifier)
  useEffect(() => {
    const onKey = (e) => {
      if (e.key !== "s" && e.key !== "S") return;
      if (e.ctrlKey || e.metaKey || e.altKey || e.repeat) return;
      const tag = (e.target?.tagName || "").toLowerCase();
      if (tag === "input" || tag === "textarea" || tag === "select" || e.target?.isContentEditable) return;
      e.preventDefault();
      demo();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [demo]);

  const s = summary(session);
  const combo = liveCombo(session, clock);
  const hpPct = Math.round((session.hpLeft / session.hp) * 100);
  const cardRef = useRef(null);
  const [saving, setSaving] = useState(false);
  const saveCard = async () => {
    if (!cardRef.current) return;
    setSaving(true);
    try {
      if (document.fonts?.ready) await document.fonts.ready;
      const { toBlob } = await import("html-to-image");
      const opts = { width: 1200, height: 630, pixelRatio: 1, cacheBust: true };
      await toBlob(cardRef.current, opts); // first pass loads the image and fonts
      const blob = await toBlob(cardRef.current, opts);
      if (blob) downloadBlob(blob, `strikefit-practice-${session.level.toLowerCase()}.png`);
    } finally {
      setSaving(false);
    }
  };

  const liveText = last
    ? last.counts
      ? `${TIER_LABEL[last.tier]} hit at an estimated ${fmt(last.speed)} metres per second: ${roundToInt(last.damage)} damage${last.guarded ? " (guarded, 70 percent less)" : ""}, ${last.xp} XP${last.combo > 1 ? `, combo ${last.combo}` : ""}.${session.defeated ? ` ${guardian?.name || "The monster"} is down.` : ""}`
      : `No hit: an estimated ${fmt(last.speed)} metres per second is under the ${LEVELS[session.level].swing.min} minimum.`
    : "";

  return (
    <div className="min-h-screen bg-[var(--bg)]">
      <LandingNav live={false} />
      <main id="main" className="mx-auto max-w-6xl px-4 pb-16 pt-10 sm:px-6 lg:pt-14">
        <header className="max-w-3xl">
          <p className="t-kicker">Practice</p>
          <h1 className="t-h2 mt-4 text-[var(--ink)]">Practice your swing, no headset needed</h1>
          <p className="measure mt-4 text-lg font-medium leading-relaxed text-[var(--ink-secondary)]">
            Swing your phone, flick your mouse, or press a demo swing. The damage, tiers, XP, combo and guard are the game&apos;s own numbers. Speeds here are{" "}
            <strong className="text-[var(--ink)]">estimates</strong>: a phone or a mouse is not a VR controller.
          </p>
          <p className="mt-4 inline-flex items-start gap-2 rounded-xl bg-[var(--warn-soft,var(--surface-muted))] px-3.5 py-2.5 text-sm font-semibold text-[var(--ink)] ring-1 ring-[var(--border)]">
            <Icon name="info" className="mt-0.5 h-4 w-4 shrink-0 text-[var(--accent)]" />
            <span>
              This is not study data. Nothing here is sent anywhere; your best speed and settings stay in this browser only (if it allows local storage).
            </span>
          </p>
          <div className="mt-5 flex flex-wrap gap-2">
            <Link to="/#how" className={buttonClass("secondary", "md")}>
              <Icon name="headset" className="h-4 w-4" /> How the headset game works
            </Link>
            <Link to="/dashboard" className={buttonClass("secondary", "md")}>
              <Icon name="analytics" className="h-4 w-4" /> Real fights on the dashboard
            </Link>
          </div>
        </header>

        <div className="mt-10 grid gap-6 lg:grid-cols-12">
          {/* the arena */}
          <section aria-labelledby="arena-title" className="rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-5 shadow-[var(--shadow-lift)] sm:p-6 lg:col-span-7">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <h2 id="arena-title" className="text-lg font-extrabold tracking-tight text-[var(--ink)]">
                {guardian?.name || LEVELS[level].monster}
                <span className="ml-2 text-sm font-semibold text-[var(--muted)]">{guardian?.title}</span>
              </h2>
              <SegmentedControl label="Level" options={LEVEL_OPTS} value={level} onChange={pickLevel} />
            </div>

            <div className="relative mt-4 grid place-items-center overflow-hidden rounded-xl bg-gradient-to-b from-[var(--accent-soft)] to-[var(--surface-muted)]" style={{ minHeight: 260 }}>
              <MonsterPortrait level={level} size="md" glow={!reduced} className={`!w-44 transition-[filter] duration-200 [mask-image:radial-gradient(ellipse_at_center,#000_58%,transparent_90%)] sm:!w-56 ${guard.guarding ? "[filter:drop-shadow(0_0_18px_rgba(120,170,255,0.9))]" : ""}`} />
              {pops.map((p) => (
                <Pop key={p.id} pop={p} reduced={reduced} onDone={() => setPops((all) => all.filter((x) => x.id !== p.id))} />
              ))}
              <div className="absolute left-3 top-3 flex flex-col gap-1.5">
                <span
                  className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-bold ring-1 ${
                    guard.guarding
                      ? "bg-[#dbe8ff] text-[#173a8a] ring-[#9bbcff]"
                      : guard.warning
                        ? "bg-[var(--surface)] text-[var(--ink)] ring-[var(--border-strong)]"
                        : "bg-[var(--surface)]/90 text-[var(--ink-secondary)] ring-[var(--border)]"
                  }`}
                >
                  <Icon name={guard.guarding ? "shield" : "swords"} className="h-3.5 w-3.5" />
                  {guard.guarding ? "Guard up: 70% less damage" : guard.warning ? "Guard rising…" : "Open: hit now"}
                </span>
                {combo > 1 && <span className="inline-flex w-max items-center rounded-full bg-[var(--surface)] px-2.5 py-1 text-xs font-extrabold text-[var(--accent)] ring-1 ring-[var(--border)]">Combo ×{combo}</span>}
              </div>
              {session.defeated && (
                <div className="absolute inset-x-3 bottom-3 rounded-xl bg-[var(--surface)]/95 px-4 py-3 text-center shadow ring-1 ring-[var(--border)]">
                  <p className="text-sm font-extrabold text-[var(--ink)]">{guardian?.name || "The monster"} is down. </p>
                  <button type="button" onClick={() => startOver()} className={buttonClass("primary", "sm", "mt-2")}>
                    Fight again
                  </button>
                </div>
              )}
            </div>

            <div className="mt-4">
              <div className="flex items-baseline justify-between text-xs font-bold text-[var(--muted)]">
                <span>Health</span>
                <span className="tabular-nums">
                  {roundToInt(session.hpLeft)} / {session.hp}
                </span>
              </div>
              <div className="mt-1 h-2.5 overflow-hidden rounded-full bg-[var(--surface-muted)]" role="progressbar" aria-label={`${guardian?.name || "Monster"} health`} aria-valuemin={0} aria-valuemax={session.hp} aria-valuenow={roundToInt(session.hpLeft)}>
                <div className="h-full rounded-full bg-[var(--bad,#e5484d)] transition-[width] duration-300" style={{ width: `${hpPct}%` }} />
              </div>
            </div>

            {/* the last swing */}
            <div className="mt-5 grid gap-4 sm:grid-cols-[auto_1fr] sm:items-center">
              <div className="min-w-[10rem]">
                <p className="text-[0.7rem] font-bold uppercase tracking-wide text-[var(--muted)]">
                  Last swing <span className="ml-1 rounded bg-[var(--surface-muted)] px-1.5 py-0.5 text-[0.65rem] text-[var(--ink-secondary)] ring-1 ring-[var(--border)]">ESTIMATE</span>
                </p>
                <p className="mt-1 text-4xl font-extrabold tabular-nums tracking-tight text-[var(--ink)]">
                  {last ? fmt(last.speed) : "-"}
                  <span className="ml-1 text-base font-bold text-[var(--muted)]">m/s</span>
                </p>
                <p className="mt-1 text-sm font-bold" style={{ color: last ? (last.tier === "none" ? "var(--muted)" : TIER_COLOR[last.tier]) : "var(--muted)" }}>
                  {last ? (last.counts ? `${TIER_LABEL[last.tier]}${last.guarded ? " · guarded" : ""}` : "No hit") : "Waiting for a swing"}
                </p>
              </div>
              <div>
                <SpeedRuler level={level} speed={last?.speed} />
                <dl className="mt-3 grid grid-cols-3 gap-2">
                  <Stat label="Damage" value={last ? roundToInt(last.damage) : "-"} hint={last?.guarded ? `of ${roundToInt(last.fullDamage)}` : null} />
                  <Stat label="XP" value={last ? `+${last.xp}` : "-"} />
                  <Stat label="Combo" value={combo || "-"} />
                </dl>
              </div>
            </div>
            <p className="sr-only" aria-live="polite" aria-atomic="true">
              {liveText}
            </p>
          </section>

          {/* the input */}
          <section aria-labelledby="input-title" className="rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-5 shadow-[var(--shadow-card)] sm:p-6 lg:col-span-5">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <h2 id="input-title" className="text-lg font-extrabold tracking-tight text-[var(--ink)]">
                Swing
              </h2>
              <SegmentedControl label="Input" options={MODE_OPTS} value={mode} onChange={setMode} />
            </div>
            {mode === "drag" ? (
              <DragPad onSwing={(v) => swing(v, "drag")} sensitivity={sens} onSensitivity={(v) => updatePrefs({ sensitivity: v })} />
            ) : (
              <MotionInput onSwing={(v) => swing(v, "motion")} scale={scale} onScale={(v) => updatePrefs({ motionScale: v })} onFallback={() => setMode("drag")} />
            )}
            <div className="mt-5 flex flex-wrap items-center gap-3 border-t border-[var(--border)] pt-4">
              <button type="button" onClick={demo} className={buttonClass("primary", "md")} aria-keyshortcuts="S">
                <Icon name="bolt" className="h-4 w-4" /> Demo swing
              </button>
              <span className="text-xs font-semibold text-[var(--muted)]">
                or press <kbd className="rounded border border-[var(--border)] bg-[var(--surface-muted)] px-1.5 py-0.5 font-mono text-[0.7rem]">S</kbd>: one swing from each band in turn
              </span>
            </div>
          </section>
        </div>

        {/* the session */}
        <section aria-labelledby="session-title" className="mt-6 rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-5 shadow-[var(--shadow-card)] sm:p-6">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <h2 id="session-title" className="text-lg font-extrabold tracking-tight text-[var(--ink)]">
              This session
            </h2>
            <div className="flex flex-wrap gap-2">
              <button type="button" onClick={saveCard} disabled={!s.swings || saving} className={buttonClass("secondary", "md")}>
                <Icon name={saving ? "loader" : "download"} className={`h-4 w-4 ${saving ? "animate-spin" : ""}`} /> Save result card (PNG)
              </button>
              <button type="button" onClick={() => startOver()} disabled={!s.swings} className={buttonClass("ghost", "md")}>
                <Icon name="refresh" className="h-4 w-4" /> Start over
              </button>
            </div>
          </div>
          <dl className="mt-4 grid grid-cols-2 gap-2 sm:grid-cols-4 lg:grid-cols-8">
            <Stat label="Swings" value={s.swings} />
            <Stat label="Hits" value={s.hits} hint={s.swings ? `${Math.round(s.accuracy * 100)}%` : null} />
            <Stat label="Best (est.)" value={s.swings ? fmt(s.bestSpeed) : "-"} hint="m/s" />
            <Stat label="Average (est.)" value={s.swings ? fmt(s.avgSpeed) : "-"} hint="m/s" />
            <Stat label="Damage" value={roundToInt(s.damage)} />
            <Stat label="XP" value={s.xp} />
            <Stat label="Best combo" value={s.bestCombo} />
            <Stat label="Full power" value={s.fullPower} hint={s.guardedHits ? `${s.guardedHits} guarded` : null} />
          </dl>
          <p className="mt-3 text-xs font-medium text-[var(--muted)]">
            Best ever on {LEVELS[level].name} in this browser: {prefs.best?.[level] ? `${fmt(prefs.best[level])} m/s (estimate)` : "none yet"}. Level 3&apos;s weapons use values this page does not model, so it is not here.
          </p>
        </section>
        <ResultCard cardRef={cardRef} session={session} guardian={guardian} />
      </main>
      <Footer />
    </div>
  );
}

// ───────────────────────── mouse / touch ─────────────────────────
function DragPad({ onSwing, sensitivity, onSensitivity }) {
  const tracker = useRef(null);
  const [active, setActive] = useState(false);
  const [lastPx, setLastPx] = useState(null);
  const sid = useId();
  if (!tracker.current) tracker.current = createDragTracker({ sensitivity });
  useEffect(() => tracker.current.setSensitivity(sensitivity), [sensitivity]);
  const pt = (e) => ({ t: e.timeStamp, x: e.clientX, y: e.clientY });
  return (
    <div className="mt-4">
      <div
        role="img"
        aria-label="Drag area: press, then flick across it fast, like a swing. Keyboard: use the Demo swing button or the S key."
        className={`relative grid h-48 touch-none select-none place-items-center rounded-xl border-2 border-dashed text-center transition-colors sm:h-56 ${
          active ? "border-[var(--accent)] bg-[var(--accent-soft)]" : "border-[var(--border-strong)] bg-[var(--surface-muted)]"
        }`}
        onPointerDown={(e) => {
          e.currentTarget.setPointerCapture?.(e.pointerId);
          tracker.current.start(pt(e));
          setActive(true);
        }}
        onPointerMove={(e) => active && tracker.current.move(pt(e))}
        onPointerUp={(e) => {
          if (!active) return;
          setActive(false);
          const r = tracker.current.end(pt(e));
          if (r) {
            setLastPx(r.pxPerSec);
            onSwing(r.speed);
          }
        }}
        onPointerCancel={() => setActive(false)}
        data-drag-pad
      >
        <span className="pointer-events-none flex flex-col items-center gap-2 px-4 text-sm font-semibold text-[var(--ink-secondary)]">
          <Icon name="pointer" className="h-6 w-6 text-[var(--accent)]" />
          Press and flick across this box
          <span className="text-xs font-medium text-[var(--muted)]">{lastPx ? `last drag ${Math.round(lastPx)} px/s` : "faster flick, harder hit"}</span>
        </span>
      </div>
      <label htmlFor={sid} className="mt-4 flex items-center justify-between text-xs font-bold text-[var(--muted)]">
        <span>Drag calibration</span>
        <span className="tabular-nums text-[var(--ink)]">{sensitivity.toFixed(1)}×</span>
      </label>
      <input id={sid} type="range" min="0.5" max="2" step="0.1" value={sensitivity} onChange={(e) => onSensitivity(+e.target.value)} className="mt-1 w-full accent-[var(--accent)]" aria-describedby={`${sid}-h`} />
      <p id={`${sid}-h`} className="mt-1 text-xs font-medium text-[var(--muted)]">
        1× reads 900 px of screen per second as 1 m/s. Raise it on a small screen, lower it on a large one.
      </p>
    </div>
  );
}

// ───────────────────────── phone motion ─────────────────────────
function MotionInput({ onSwing, scale, onScale, onFallback }) {
  const [agreed, setAgreed] = useState(false);
  const [state, setState] = useState("off"); // off | asking | waiting | on | denied | unsupported | nosensor
  const est = useRef(null);
  const grav = useRef(null);
  const seen = useRef(false);
  const handler = useRef(null);
  const sid = useId();
  if (!est.current) est.current = createMotionEstimator({ scale });
  useEffect(() => est.current.setScale(scale), [scale]);
  useEffect(() => () => handler.current && window.removeEventListener("devicemotion", handler.current), []);

  const supported = typeof window !== "undefined" && "DeviceMotionEvent" in window;

  const enable = async () => {
    if (!supported) return setState("unsupported");
    const DME = window.DeviceMotionEvent;
    if (typeof DME.requestPermission === "function") {
      // iOS: only after a tap, which this is
      setState("asking");
      try {
        if ((await DME.requestPermission()) !== "granted") return setState("denied");
      } catch {
        return setState("denied");
      }
    }
    seen.current = false;
    grav.current = createGravityFilter(0.1);
    est.current.reset();
    const onMotion = (e) => {
      let a = e.acceleration;
      let sample = null;
      if (a && a.x != null) sample = { t: e.timeStamp, x: a.x, y: a.y, z: a.z };
      else if (e.accelerationIncludingGravity && e.accelerationIncludingGravity.x != null) sample = { t: e.timeStamp, ...grav.current(e.accelerationIncludingGravity) };
      if (!sample) return;
      if (!seen.current) {
        seen.current = true;
        setState("on");
      }
      const r = est.current.push(sample);
      if (r) onSwing(r.speed);
    };
    handler.current = onMotion;
    window.addEventListener("devicemotion", onMotion);
    setState("waiting");
    setTimeout(() => {
      if (!seen.current) setState("nosensor");
    }, 2000);
  };

  const stop = () => {
    if (handler.current) window.removeEventListener("devicemotion", handler.current);
    handler.current = null;
    setState("off");
  };

  const on = state === "on" || state === "waiting";
  return (
    <div className="mt-4">
      <div id={`${sid}-safety`} className="rounded-xl border border-amber-300 bg-amber-50 px-4 py-3 text-sm font-medium leading-relaxed text-amber-950">
        <p className="flex items-center gap-2 font-extrabold">
          <Icon name="shield" className="h-4 w-4" /> Swing safely
        </p>
        <ul className="mt-1.5 list-disc space-y-0.5 pl-5">
          <li>Hold the phone firmly; use a wrist strap or a grippy case if you have one.</li>
          <li>Clear the space around you: no people, pets, screens or anything fragile within reach.</li>
          <li>Start gently, and stop if anything hurts.</li>
        </ul>
      </div>
      <label className="mt-3 flex cursor-pointer items-start gap-2.5 text-sm font-semibold text-[var(--ink)]">
        <input type="checkbox" checked={agreed} onChange={(e) => setAgreed(e.target.checked)} className="mt-0.5 h-4 w-4 accent-[var(--accent)]" aria-describedby={`${sid}-safety`} disabled={on} />
        I have cleared the space and have a firm grip on my phone.
      </label>
      <div className="mt-3 flex flex-wrap items-center gap-2">
        {!on ? (
          <button type="button" onClick={enable} disabled={!agreed || state === "asking"} className={buttonClass("primary", "md")}>
            <Icon name="phone" className="h-4 w-4" /> Enable motion
          </button>
        ) : (
          <button type="button" onClick={stop} className={buttonClass("secondary", "md")}>
            Stop motion
          </button>
        )}
        <span className="text-xs font-semibold text-[var(--muted)]" role="status">
          {
            {
              off: agreed ? "Ready when you are." : "Tick the box to enable motion.",
              asking: "Waiting for your browser's permission…",
              waiting: "Listening for the motion sensor…",
              on: "Motion on: swing the phone like a sword.",
              denied: "Motion permission was refused. You can still use the mouse / touch drag.",
              unsupported: "This browser has no motion sensor access.",
              nosensor: "No motion data arrived (a desktop, or the sensor is blocked).",
            }[state]
          }
        </span>
      </div>
      {(state === "unsupported" || state === "nosensor" || state === "denied") && (
        <button type="button" onClick={onFallback} className={buttonClass("secondary", "sm", "mt-3")}>
          <Icon name="pointer" className="h-3.5 w-3.5" /> Use the mouse / touch drag instead
        </button>
      )}
      <label htmlFor={`${sid}-scale`} className="mt-4 flex items-center justify-between text-xs font-bold text-[var(--muted)]">
        <span>Motion scale</span>
        <span className="tabular-nums text-[var(--ink)]">{scale.toFixed(1)}×</span>
      </label>
      <input id={`${sid}-scale`} type="range" min="0.5" max="2" step="0.1" value={scale} onChange={(e) => onScale(+e.target.value)} className="mt-1 w-full accent-[var(--accent)]" />
      <p className="mt-1 text-xs font-medium text-[var(--muted)]">
        The speed is the phone&apos;s acceleration added up over one swing (reset to zero before each swing, capped at {SWING_SPEED_CAP} m/s). A phone in the hand moves slower than a sword tip;
        raise the scale if your hardest swings read low.
      </p>
      <p className="mt-2 text-xs font-medium text-[var(--muted)]">Guard: {GUARD.duration} s every {GUARD.cooldown} s, as the monsters in the game.</p>
    </div>
  );
}
