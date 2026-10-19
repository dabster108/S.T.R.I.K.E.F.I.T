import { lazy, Suspense, useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { api } from "../lib/api.js";
import { clampSpeed, levelName } from "../lib/game.js";
import { formatDateTime, formatDuration, formatNumber } from "../lib/format.js";
import { useApi, useThrottledCallback } from "../hooks/useApi.js";
import { useOnScreen, useReducedMotion } from "../hooks/motion.js";
import { useLiveMessages } from "../live/LiveProvider.jsx";
import { Card, CardHeader, PageHeader, SectionRule } from "../components/ui/Card.jsx";
import PbBanner from "../components/records/PbBanner.jsx";
import { ConfirmDialog, EmptyState } from "../components/ui/Feedback.jsx";
import { SessionDetailSkeleton } from "../components/ui/Skeletons.jsx";
import { LevelBadge, OutcomeBadge } from "../components/ui/Badge.jsx";
import { SegmentedControl } from "../components/ui/Controls.jsx";
import Button, { buttonClass } from "../components/ui/Button.jsx";
import Icon from "../components/ui/Icon.jsx";
import EventList from "../components/sessions/EventList.jsx";
import Scrubber, { SCRUB_LEGEND, SCRUB_LEGEND_OPTIONAL } from "../components/sessions/Scrubber.jsx";
import SwingTimeline from "../components/sessions/SwingTimeline.jsx";
import { HpBar, LiveChip } from "../components/live/LiveFight.jsx";
import { BarChart, Legend, LineChart } from "../components/charts/Charts.jsx";
import { CalorieEstimate, EstimateNote, ExertionChart, SwingTierChart } from "../components/fitness/Fitness.jsx";
import { NewPbChip, PersonalBestsHeld } from "../components/fitness/Achievements.jsx";
import RatingForm, { RatingSummary } from "../components/feedback/RatingForm.jsx";
import RecapPanel from "../components/recap/RecapCard.jsx";
import ShareChallenge from "../components/engage/ShareChallenge.jsx";
import { ghostSummary, valueAt } from "../lib/engage.js";

// Chart components load after the page, in their own chunks; the fixed-height skeleton keeps the layout from jumping.
const FadePanel = lazy(() => import("../components/effort/FadePanel.jsx"));
const Fingerprint = lazy(() => import("../components/effort/Fingerprint.jsx"));
const FightReplay = lazy(() => import("../components/effort/FightReplay.jsx"));
const FadeSkeleton = lazy(() => import("../components/effort/FadePanel.jsx").then((m) => ({ default: m.FadeSkeleton })));

const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const P = (e) => (e.payload && typeof e.payload === "object" && !Array.isArray(e.payload) ? e.payload : {});
const clock = (s) => `${String(Math.floor(Math.max(0, s) / 60)).padStart(2, "0")}:${String(Math.floor(Math.max(0, s) % 60)).padStart(2, "0")}`;
const SPEEDS = [
  { id: 1, label: "1×" },
  { id: 4, label: "4×" },
  { id: 10, label: "10×" },
];

// Seconds since the fight started: Unity's own clock (payload.t) when present, else arrival time.
function timed(events, startedAt) {
  const t0 = new Date(startedAt).getTime();
  return events
    .map((e) => ({ ...e, t: isNum(P(e).t) ? P(e).t : Math.max(0, (new Date(e.at).getTime() - t0) / 1000) }))
    .sort((a, b) => a.t - b.t);
}

// "vs your best earlier run on this level": the readout under the scrubber and the summary line.
function GhostLine({ best, session, events, t, show, onToggle }) {
  if (!best) return null;
  if (best.loading && !best.data) return <p className="mt-2 text-xs font-medium text-[var(--muted)]">Looking for your best earlier run on this level…</p>;
  const b = best.data;
  if (!b || best.error) return <p className="mt-2 text-xs font-medium text-[var(--muted)]">The comparison with an earlier run isn't available right now.</p>;
  if (!b.best) return <p className="mt-2 text-xs font-medium text-[var(--muted)]">No earlier run on {levelName(session.level)} to compare with yet.</p>;
  if (b.reason === "no_events") {
    return <p className="mt-2 text-xs font-medium text-[var(--muted)]">Your best earlier run on {levelName(session.level)} (score {formatNumber(b.best.score)}) has no swing or health samples, so there is no curve to compare.</p>;
  }
  const hits = events.filter((e) => e.type === "hit" && isNum(P(e).swingSpeed)).map((e) => ({ t: e.t, speed: clampSpeed(P(e).swingSpeed) }));
  const hpNow = events.filter((e) => isNum(P(e).monsterHp) && P(e).monsterMaxHp > 0).map((e) => ({ t: e.t, f: P(e).monsterHp / P(e).monsterMaxHp }));
  const summary = ghostSummary({ durationSec: session.durationSec, outcome: session.outcome, hits }, b);
  const pct = (f) => (isNum(f) ? `${Math.round(f * 100)}%` : "—");
  const speed = (v) => (isNum(v) ? `${v.toFixed(1)} m/s` : "—");
  return (
    <div className="mt-3 rounded-xl border border-[var(--border)] bg-[var(--surface-muted)] px-4 py-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-xs font-bold text-[var(--ink)]">
          vs your best earlier run on {levelName(session.level)}{" "}
          <Link to={`/dashboard/session/${encodeURIComponent(b.best.id)}`} className="press rounded font-semibold text-[var(--accent)] hover:underline">
            (score {formatNumber(b.best.score)}, {formatDateTime(b.best.startedAt)})
          </Link>
        </p>
        <label className="inline-flex cursor-pointer items-center gap-2 text-xs font-semibold text-[var(--ink-secondary)]">
          <input type="checkbox" checked={show} onChange={onToggle} className="h-4 w-4 accent-[var(--accent)]" /> Show on the chart
        </label>
      </div>
      {summary && <p className="mt-1 text-sm font-bold text-[var(--ink-secondary)]">{summary}.</p>}
      <dl className="mt-2 grid grid-cols-2 gap-x-4 gap-y-1 text-xs sm:grid-cols-4" aria-live="off">
        <div><dt className="font-semibold text-[var(--muted)]">At {clock(t)}, monster (this run)</dt><dd className="font-extrabold tabular-nums text-[var(--ink)]">{pct(valueAt(hpNow, t, "f"))}</dd></div>
        <div><dt className="font-semibold text-[var(--muted)]">Monster (best run)</dt><dd className="font-extrabold tabular-nums text-[var(--ink)]">{pct(valueAt(b.samples.hp, t, "f"))}</dd></div>
        <div><dt className="font-semibold text-[var(--muted)]">Last swing (this run)</dt><dd className="font-extrabold tabular-nums text-[var(--ink)]">{speed(valueAt(hits, t, "speed"))}</dd></div>
        <div><dt className="font-semibold text-[var(--muted)]">Last swing (best run)</dt><dd className="font-extrabold tabular-nums text-[var(--ink)]">{speed(valueAt(b.samples.hits, t, "speed"))}</dd></div>
      </dl>
    </div>
  );
}

function Replay({ events, duration, live, best = null, session = null }) {
  const [showGhost, setShowGhost] = useState(true);
  const ghost = showGhost && best?.data?.best && !best.data.reason ? { hits: best.data.samples.hits, hp: best.data.samples.hp, duration: best.data.best.durationSec } : null;
  const reduced = useReducedMotion();
  const [t, setT] = useState(duration);
  const [playing, setPlaying] = useState(false);
  const [speed, setSpeed] = useState(4);
  const follow = useRef(true);
  const list = useRef(null);

  useEffect(() => {
    if (follow.current) setT(duration); // a live fight keeps the cursor on "now"
  }, [duration]);

  useEffect(() => {
    if (!playing) return undefined;
    let raf;
    let last = performance.now();
    const tick = (now) => {
      const dt = ((now - last) / 1000) * speed;
      last = now;
      setT((cur) => {
        if (cur + dt >= duration) {
          setPlaying(false);
          follow.current = true;
          return duration;
        }
        return cur + dt;
      });
      raf = requestAnimationFrame(tick);
    };
    raf = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(raf);
  }, [playing, speed, duration]);

  const upTo = events.filter((e) => e.t <= t + 1e-6);
  const state = {};
  for (const e of upTo) for (const k of ["playerHp", "playerMaxHp", "monsterHp", "monsterMaxHp", "score", "xp", "hits"]) if (isNum(P(e)[k])) state[k] = P(e)[k];
  const shown = upTo.filter((e) => e.type !== "hp").reverse();

  useEffect(() => {
    if (list.current && !reduced) list.current.scrollTo({ top: 0 });
  }, [shown.length, reduced]);

  const atEnd = t >= duration - 0.05;
  const seek = (v) => {
    setT(v);
    follow.current = v >= duration - 0.05;
  };
  const keyEvents = events.filter((e) => ["hit", "monster_attack", "player_hurt", "clash", "monster_guard", "opening_hit", "session_complete"].includes(e.type));
  const legend = [...SCRUB_LEGEND, ...Object.entries(SCRUB_LEGEND_OPTIONAL).filter(([type]) => events.some((e) => e.type === type)).map(([, m]) => m)];
  const jump = (dir) => {
    setPlaying(false);
    const target = dir > 0 ? keyEvents.find((e) => e.t > t + 0.01) : [...keyEvents].reverse().find((e) => e.t < t - 0.01);
    seek(target ? target.t : dir > 0 ? duration : 0);
  };

  return (
    <div className="px-5 py-5">
      <div className="flex flex-wrap items-center gap-2">
        <Button
          variant="primary"
          size="md"
          icon={playing ? "pause" : "play"}
          onClick={() => {
            if (playing) return setPlaying(false);
            if (atEnd) setT(0);
            follow.current = false;
            setPlaying(true);
          }}
        >
          {playing ? "Pause" : atEnd ? "Replay" : "Play"}
        </Button>
        <Button variant="ghost" icon="chevronLeft" onClick={() => jump(-1)} aria-label="Previous event">
          <span className="hidden sm:inline">Prev event</span>
        </Button>
        <Button variant="ghost" onClick={() => jump(1)} aria-label="Next event">
          <span className="hidden sm:inline">Next event</span>
          <Icon name="chevronRight" className="h-3.5 w-3.5" />
        </Button>
        <span className="ml-auto text-sm font-bold tabular-nums text-[var(--ink)]">
          {clock(t)} <span className="font-semibold text-[var(--muted)]">/ {clock(duration)}</span>
          {live && <span className="ml-1 text-xs font-bold text-[var(--good-ink)]">· live</span>}
        </span>
        <SegmentedControl label="Replay speed" options={SPEEDS} value={speed} onChange={setSpeed} />
      </div>

      <div className="mt-2">
        <Scrubber t={Math.min(t, duration)} duration={duration} events={events} onSeek={seek} onScrubStart={() => setPlaying(false)} />
        <ul className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs font-semibold text-[var(--ink-secondary)]" aria-label="Marker legend">
          {legend.map((m) => (
            <li key={m.label} className="inline-flex items-center gap-1.5">
              <span className={`h-3 w-[3px] rounded-full ${m.cls}`} aria-hidden />
              {m.label}
            </li>
          ))}
          <li className="ml-auto hidden font-medium text-[var(--muted)] sm:block">Arrow keys move 1 s, Shift for 5 s</li>
        </ul>
        {session && <GhostLine best={best} session={session} events={events} t={Math.min(t, duration)} show={showGhost} onToggle={() => setShowGhost((v) => !v)} />}
      </div>

      <div className="mt-5">
        <p className="mb-1 text-xs font-bold text-[var(--ink)]">Swing speed and monster health</p>
        <SwingTimeline
          events={events}
          duration={duration}
          t={Math.min(t, duration)}
          ghost={ghost}
          onSeek={(v) => {
            setPlaying(false);
            seek(v);
          }}
        />
      </div>

      <div className="mt-5 grid gap-6 lg:grid-cols-5">
        <div className="space-y-4 lg:col-span-2">
          <HpBar label="Player" tone="player" value={state.playerHp} max={state.playerMaxHp} />
          <HpBar label="Monster" tone="monster" value={state.monsterHp} max={state.monsterMaxHp} />
          <dl className="grid grid-cols-3 gap-2 border-t border-[var(--border)] pt-3 text-center">
            {[
              ["Score", state.score],
              ["XP", state.xp],
              ["Hits", state.hits],
            ].map(([k, v]) => (
              <div key={k}>
                <dt className="text-xs font-semibold text-[var(--muted)]">{k}</dt>
                <dd className="text-base font-extrabold tabular-nums text-[var(--ink)]">{isNum(v) ? formatNumber(v) : "—"}</dd>
              </div>
            ))}
          </dl>
        </div>
        <div ref={list} className="scrollbar-thin max-h-80 overflow-y-auto rounded-xl border border-[var(--border)] lg:col-span-3">
          {shown.length ? <EventList events={shown} dense /> : <p className="px-4 py-6 text-xs font-medium text-[var(--muted)]">Press play or drag along the timeline.</p>}
        </div>
      </div>
    </div>
  );
}

// Once the page title has scrolled under the top bar: who, which level, how it ended and the score stay in view, with jumps
// to the three main sections. It sits in a zero-height sticky slot, so showing it moves nothing on the page.
function BattleBar({ s, show, telemetry }) {
  if (!show) return null;
  const jumps = [
    s.status !== "live" && ["#battle-fade", "Effort fade"],
    ["#battle-replay", "Replay"],
    ["#battle-fingerprint", "Fingerprint"],
  ].filter(Boolean);
  return (
    <div className="sticky top-[4.25rem] z-10 h-0">
      <div className="arena-rise flex flex-wrap items-center gap-x-4 gap-y-2 rounded-xl border border-[var(--border)] bg-[var(--surface)] px-4 py-2.5 shadow-[var(--shadow-lift)]">
        <span className="min-w-0 truncate text-sm font-extrabold text-[var(--ink)]">
          {s.playerName} <span className="font-semibold text-[var(--muted)]">· {levelName(s.level)}</span>
        </span>
        {s.status === "live" ? <LiveChip /> : <OutcomeBadge outcome={s.outcome} />}
        <span className="text-sm font-extrabold tabular-nums text-[var(--ink)]">
          {formatNumber(s.score)} <span className="text-xs font-semibold text-[var(--muted)]">pts</span>
        </span>
        <span className="hidden text-sm font-semibold tabular-nums text-[var(--ink-secondary)] sm:inline">{formatDuration(s.durationSec)}</span>
        <nav aria-label="Jump to" className="ml-auto hidden items-center gap-1 md:flex">
          {jumps.map(([href, label]) => (
            <a key={href} href={href} className="press rounded-md px-2 py-1 text-xs font-semibold text-[var(--ink-secondary)] hover:bg-[var(--surface-muted)] hover:text-[var(--accent)]">
              {label}
            </a>
          ))}
        </nav>
        {!telemetry && <span className="sr-only">No fight telemetry for this session</span>}
      </div>
    </div>
  );
}

export default function SessionDetailView({ onDeleted }) {
  const { id } = useParams();
  const navigate = useNavigate();
  const { data, error, reload } = useApi(() => api.session(id), [id]);
  const refresh = useThrottledCallback(reload, 1500);
  useLiveMessages((type, payload) => {
    if ((payload?.session?.id || payload?.sessionId) === id) refresh();
    else if (type === "player_renamed" && payload.oldName === data?.session?.playerName) reload();
  });
  const [confirm, setConfirm] = useState(false);
  const [headRef, headOnScreen] = useOnScreen("-64px 0px 0px 0px");
  const headSeen = useRef(false); // the observer reports "not on screen" before its first callback: only show the bar after the header was seen
  if (headOnScreen) headSeen.current = true;
  const [busy, setBusy] = useState(false);
  const [recap, setRecap] = useState(false);

  const s = data?.session;
  const events = useMemo(() => (data ? timed(data.events, data.session.startedAt) : []), [data]);
  const hp = events.filter((e) => isNum(P(e).playerHp) || isNum(P(e).monsterHp));
  const duration = Math.max(s?.durationSec || 0, events.length ? events[events.length - 1].t : 0);
  const attacks = useMemo(() => {
    const m = new Map();
    for (const e of events) {
      const p = P(e);
      if (e.type === "monster_attack" && p.attack) m.set(p.attack, { ...(m.get(p.attack) || { used: 0, landed: 0 }), used: (m.get(p.attack)?.used || 0) + 1 });
      if (e.type === "player_hurt" && p.attack && m.has(p.attack)) m.get(p.attack).landed++;
    }
    return [...m.entries()].sort((a, b) => b[1].used - a[1].used);
  }, [events]);
  const telemetry = events.some((e) => !["session_start", "session_complete"].includes(e.type));
  // The replay ghost: this player's best run on this level that started before this one.
  const best = useApi(() => (s && telemetry ? api.playerBest(s.playerName, s.level, s.id) : Promise.resolve(null)), [s?.id, s?.playerName, telemetry]);

  const remove = async () => {
    setBusy(true);
    try {
      await api.deleteSession(id);
      onDeleted?.(s);
      navigate("/dashboard/sessions");
    } catch {
      setBusy(false);
      setConfirm(false);
    }
  };

  if (error?.status === 404) {
    return (
      <div className="space-y-6">
        <PageHeader eyebrow="Session" title="Session not found" />
        <Card>
          <EmptyState
            icon="search"
            title="This session doesn't exist"
            description="It may have been deleted."
            action={
              <Link to="/dashboard/sessions" className={buttonClass("secondary")}>
                All sessions
              </Link>
            }
          />
        </Card>
      </div>
    );
  }
  if (!data && !error) return <SessionDetailSkeleton />;

  return (
    <div className="space-y-6">
      <div ref={headRef}>
      <PageHeader
        eyebrow={
          <Link to="/dashboard/sessions" className="press inline-flex items-center gap-1 rounded hover:underline">
            <Icon name="chevronLeft" className="h-3 w-3" strokeWidth={2.4} /> Sessions
          </Link>
        }
        title={s ? `${s.playerName} · ${levelName(s.level)}` : "Session"}
        description={s ? `${formatDateTime(s.startedAt)} · ${s.source}${s.notes ? ` · ${s.notes}` : ""}` : undefined}
        actions={
          s && (
            <>
              <NewPbChip records={data?.personalBestsSet} />
              {s.status !== "live" && <ShareChallenge sessionId={s.id} playerName={s.playerName} level={levelName(s.level)} />}
              {s.status === "live" ? <LiveChip /> : <OutcomeBadge outcome={s.outcome} />}
              <Button variant="danger" icon="trash" onClick={() => setConfirm(true)}>
                Delete
              </Button>
            </>
          )
        }
      />
      </div>
      {s && <BattleBar s={s} show={headSeen.current && !headOnScreen} telemetry={telemetry} />}

      {error && !data ? (
        <Card>
          <EmptyState icon="sessions" title="This session will appear here" description="It loads as soon as the server answers." />
        </Card>
      ) : (
        <>
          {/* what this fight beat on its level (never for a first fight, an abandoned one or one still being played) */}
          <PbBanner records={data?.newRecords} fireKey={s.id} />

          <Card className="overflow-hidden">
            <CardHeader title="Summary" />
            <dl className="stagger grid grid-cols-2 gap-x-6 gap-y-4 px-5 py-5 sm:grid-cols-3 lg:grid-cols-5">
              {[
                ["Level", <LevelBadge key="l" level={s.level} />],
                ["Score", formatNumber(s.score)],
                ["XP", s.xp ? formatNumber(s.xp) : "—"],
                ["Hits", formatNumber(s.hitCount)],
                ["Full-power hits", s.fullSwings ? formatNumber(s.fullSwings) : "—"],
                ["Damage dealt", formatNumber(s.damageDealt)],
                ["Duration", formatDuration(s.durationSec)],
                ["Player HP left", formatNumber(s.playerHpRemaining)],
                ["Monster HP left", formatNumber(s.monsterHpRemaining)],
                ["Player", <Link key="p" to={`/dashboard/player/${encodeURIComponent(s.playerName)}`} className="press rounded text-[var(--accent)] hover:underline">{s.playerName}</Link>],
              ].map(([k, v]) => (
                <div key={k}>
                  <dt className="text-xs font-semibold text-[var(--muted)]">{k}</dt>
                  <dd className="mt-0.5 text-base font-extrabold tabular-nums text-[var(--ink)]">{v}</dd>
                </div>
              ))}
            </dl>
          </Card>

          <PersonalBestsHeld records={data.personalBestsSet} />

          {/* 01 how the effort held up, 02 the fight itself, 03 the player behind it, then the detail */}
          {s.status !== "live" && (
            <section id="battle-fade" aria-labelledby="battle-fade-title" className="scroll-mt-32 space-y-4">
              <SectionRule index="01" id="battle-fade-title" title="Effort" />
              <Card className="overflow-hidden">
                <CardHeader title="Effort fade" subtitle="Did your swings slow down? Average swing speed in the last third of the fight against the first third" />
                <Suspense fallback={<FadeSkeleton />}>
                  <FadePanel sessionId={s.id} />
                </Suspense>
              </Card>
            </section>
          )}

          <section id="battle-replay" aria-labelledby="battle-replay-title" className="scroll-mt-32 space-y-4">
            <SectionRule index="02" id="battle-replay-title" title="The fight" />
            {!telemetry ? (
              <Card>
                <EmptyState icon="clock" title="No fight telemetry for this session" description="It was recorded before the live-telemetry build of the game, so only the final result above exists." />
              </Card>
            ) : (
            <Card className="overflow-hidden">
              <CardHeader title="Fight replay" subtitle="The whole fight on one line: swings, monster attacks, clashes and damage taken. Press play or drag the slider" />
              <Suspense fallback={<div style={{ minHeight: 520 }} aria-hidden><div className="skeleton m-5 h-[188px] rounded-xl" /></div>}>
                <FightReplay events={events} durationSec={duration} />
              </Suspense>
            </Card>
            )}
          </section>

          <section id="battle-fingerprint" aria-labelledby="battle-fingerprint-title" className="scroll-mt-32 space-y-4">
            <SectionRule index="03" id="battle-fingerprint-title" title={`The player · ${s.playerName}`} />
            <Card className="overflow-hidden">
              <CardHeader title="Effort fingerprint" subtitle={`${s.playerName}'s last 10 fights with swing data, so this one is seen in context`} />
              <Suspense fallback={<div style={{ minHeight: 560 }} aria-hidden><div className="skeleton m-5 h-[26rem] rounded-xl" /></div>}>
                <Fingerprint name={s.playerName} />
              </Suspense>
            </Card>
          </section>

          {telemetry && (
            <section aria-labelledby="battle-detail-title" className="space-y-4">
              <SectionRule index="04" id="battle-detail-title" title="Moment by moment" />
              <Card className="overflow-hidden">
                <CardHeader title="Timeline replay" subtitle="Step through the fight: health at the chosen moment, and everything that had happened by then" />
                <Replay events={events} duration={duration} live={s.status === "live"} best={best} session={s} />
              </Card>

              <div className="grid gap-6 xl:grid-cols-2">
                <Card className="overflow-hidden">
                  <CardHeader title="Health over the fight" subtitle="Player and monster HP, sampled every second and at each hit" />
                  <div className="px-3 pb-4 pt-4 sm:px-5">
                    {hp.length > 1 ? (
                      <>
                        <LineChart
                          caption="Player and monster health over time"
                          step
                          markers={false}
                          series={[
                            { key: "p", label: "Player", color: "var(--hp-player)", points: hp.filter((e) => isNum(P(e).playerHp)).map((e) => ({ x: e.t, y: P(e).playerHp })) },
                            { key: "m", label: "Monster", color: "var(--hp-monster)", points: hp.filter((e) => isNum(P(e).monsterHp)).map((e) => ({ x: e.t, y: P(e).monsterHp })) },
                          ]}
                          xFormat={clock}
                          yFormat={(v) => formatNumber(v)}
                        />
                        <Legend items={[{ label: "Player", color: "var(--hp-player)" }, { label: "Monster", color: "var(--hp-monster)" }]} shape="line" />
                      </>
                    ) : (
                      <EmptyState icon="heart" title="Not enough HP samples yet" />
                    )}
                  </div>
                </Card>

                <Card className="overflow-hidden">
                  <CardHeader title="Monster attacks used" subtitle="Which attacks it chose, and how many of them hurt the player" />
                  <div className="px-3 pb-4 pt-4 sm:px-5">
                    {attacks.length ? (
                      <>
                        <BarChart
                          caption="Monster attacks used and landed"
                          categories={attacks.map(([n]) => n)}
                          series={[
                            { key: "used", label: "Used", color: "var(--hp-monster)", values: attacks.map(([, a]) => a.used) },
                            { key: "landed", label: "Hurt the player", color: "var(--border-strong)", values: attacks.map(([, a]) => a.landed) },
                          ]}
                          yFormat={(v) => formatNumber(v)}
                        />
                        <Legend items={[{ label: "Used", color: "var(--hp-monster)" }, { label: "Hurt the player", color: "var(--border-strong)" }]} />
                      </>
                    ) : (
                      <EmptyState icon="flame" title="No monster attacks recorded" />
                    )}
                  </div>
                </Card>
              </div>
            </section>
          )}

          {data.fitness && (
            <section aria-labelledby="battle-fitness-title" className="space-y-4">
              <SectionRule index={telemetry ? "05" : "04"} id="battle-fitness-title" title="Swing strength and energy" />
            <div className="grid gap-6 xl:grid-cols-2">
              <Card className="overflow-hidden">
                <CardHeader title="How hard the swings were" subtitle="Share of this fight's swings in each strength band" />
                <div className="px-3 pb-4 pt-4 sm:px-5">
                  <SwingTierChart breakdown={data.fitness.swingIntensity} />
                </div>
              </Card>
              <Card className="overflow-hidden">
                <CardHeader title="Effort over the course of the fight" subtitle="Average swing strength in each fifth of the fight, start to finish (x axis is % of the fight)" />
                <div className="px-3 pb-4 pt-4 sm:px-5">
                  <ExertionChart trend={data.fitness.exertionTrend} />
                </div>
              </Card>
              {isNum(data.fitness.estimatedCalories) && (
                <Card className="overflow-hidden xl:col-span-2">
                  <CardHeader title="Estimated energy use" subtitle="An estimate from fight length and swing strength, not a measurement" />
                  <div className="px-5 py-4">
                    <CalorieEstimate kcal={data.fitness.estimatedCalories} walkingMinutes={data.fitness.estimatedWalkingMinutes} joggingMinutes={data.fitness.estimatedJoggingMinutes} />
                  </div>
                  <EstimateNote>
                    {data.fitness.estimate.note} Assumes {data.fitness.estimate.assumedBodyMassKg} kg body mass; intensity factor {data.fitness.estimate.metUsed} MET (assumed range {data.fitness.estimate.metRange[0]} to {data.fitness.estimate.metRange[1]}).
                  </EstimateNote>
                </Card>
              )}
            </div>
            </section>
          )}

          {s.status !== "live" && (
            <section aria-labelledby="battle-share-title" className="space-y-4">
              <SectionRule id="battle-share-title" title="Rate and share" />
            <div className="grid gap-6 xl:grid-cols-2">
              <Card className="overflow-hidden">
                <CardHeader
                  title={data.feedback ? "Player rating" : "Rate this session"}
                  subtitle={data.feedback ? `Saved ${formatDateTime(data.feedback.createdAt)}` : "Enjoyment (1 to 5) and perceived exertion (Borg 6 to 20), stored with the session for research"}
                />
                <div className="px-5 py-5">{data.feedback ? <RatingSummary feedback={data.feedback} /> : <RatingForm sessionId={s.id} onSaved={reload} />}</div>
              </Card>
              <Card className="overflow-hidden">
                <CardHeader
                  title="Recap card"
                  subtitle="A shareable image of this session"
                  actions={
                    <Button icon={recap ? "chevronUp" : "chevronDown"} onClick={() => setRecap((r) => !r)} aria-expanded={recap}>
                      {recap ? "Hide" : "Show"}
                    </Button>
                  }
                />
                <div className="px-5 py-5">{recap ? <RecapPanel data={data} /> : <p className="text-sm font-medium text-[var(--muted)]">Create a 1080 × 1350 card with the player, result, estimated energy use, peak swing speed, XP and any badges earned.</p>}</div>
              </Card>
            </div>
            </section>
          )}
        </>
      )}

      <ConfirmDialog
        open={confirm}
        title="Delete this session?"
        description="The session and all its events are removed from the database. This can't be undone."
        confirmLabel="Delete session"
        onConfirm={remove}
        onCancel={() => setConfirm(false)}
        busy={busy}
      />
    </div>
  );
}
