import { useMemo } from "react";
import { useSearchParams } from "react-router-dom";
import { api } from "../lib/api.js";
import { formatDuration, formatNumber, formatPercent } from "../lib/format.js";
import { useApi } from "../hooks/useApi.js";
import { Card, CardHeader, PageHeader } from "../components/ui/Card.jsx";
import { SegmentedControl, Select } from "../components/ui/Controls.jsx";
import { EmptyState, Skeleton } from "../components/ui/Feedback.jsx";
import { CompareBars } from "../components/charts/Charts.jsx";

const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const speed = (v) => `${v.toFixed(1)} m/s`;
const pct = (r) => (isNum(r) ? r * 100 : null);
const MODES = [
  { id: "players", label: "Two players" },
  { id: "levels", label: "Level 1 vs Level 2" },
];

// Effort (how hard they swing) and engagement (how much they play) from one profile.
function metrics(p) {
  const done = p.sessions.filter((s) => s.status !== "live");
  const secs = done.reduce((a, s) => a + (s.durationSec || 0), 0);
  const hits = done.reduce((a, s) => a + (s.hitCount || 0), 0);
  return {
    sessions: p.totals.sessions,
    days: new Set(p.sessions.map((s) => String(s.startedAt).slice(0, 10))).size,
    secs,
    winRate: p.totals.winRate,
    avgScore: done.length ? done.reduce((a, s) => a + s.score, 0) / done.length : null,
    avgDamage: done.length ? done.reduce((a, s) => a + s.damageDealt, 0) / done.length : null,
    hitsPerMin: secs ? (hits / secs) * 60 : null,
    avgSpeed: p.totals.avgSwingSpeed,
    full: pct(p.totals.fullPowerRatio),
    samples: p.totals.swingSamples,
  };
}

function Pair({ a, b, effort, engagement }) {
  return (
    <div className="grid gap-6 xl:grid-cols-2">
      <Card className="overflow-hidden">
        <CardHeader title="Effort" subtitle="How hard they swing when they play" />
        <div className="px-5 py-4">
          <CompareBars a={a} b={b} rows={effort} />
        </div>
      </Card>
      <Card className="overflow-hidden">
        <CardHeader title="Engagement and results" subtitle="How much they play, and how it goes" />
        <div className="px-5 py-4">
          <CompareBars a={a} b={b} rows={engagement} />
        </div>
      </Card>
    </div>
  );
}

function PlayersCompare({ a, b, setParam, names }) {
  const pa = useApi(() => (a ? api.player(a) : Promise.resolve(null)), [a]);
  const pb = useApi(() => (b ? api.player(b) : Promise.resolve(null)), [b]);
  const opts = [{ id: "", label: "Choose a player" }, ...names.map((n) => ({ id: n, label: n }))];
  const ma = pa.data && metrics(pa.data);
  const mb = pb.data && metrics(pb.data);

  return (
    <>
      <div className="flex flex-wrap items-center gap-2 text-xs text-[var(--muted)]">
        <Select label="Player A" value={a} onChange={(v) => setParam("a", v)} options={opts} />
        <span>vs</span>
        <Select label="Player B" value={b} onChange={(v) => setParam("b", v)} options={opts} />
      </div>
      {!a || !b || a === b ? (
        <Card>
          <EmptyState icon="compare" title={a && a === b ? "Pick two different players" : "Pick two players"} description="Each row is scaled on its own, so different units never share an axis." />
        </Card>
      ) : pa.error || pb.error ? (
        <Card>
          <EmptyState icon="xCircle" title="Couldn't load a player" description={(pa.error || pb.error).message} />
        </Card>
      ) : !ma || !mb ? (
        <Skeleton className="h-72 w-full rounded-2xl" />
      ) : (
        <Pair
          a={{ label: a, color: "var(--level-1)" }}
          b={{ label: b, color: "var(--level-2)" }}
          effort={[
            { label: "Avg swing speed", a: ma.avgSpeed, b: mb.avgSpeed, fmt: speed, hint: `${formatNumber(ma.samples)} vs ${formatNumber(mb.samples)} measured hits` },
            { label: "Full-power swings", a: ma.full, b: mb.full, fmt: formatPercent },
            { label: "Hits per minute", a: ma.hitsPerMin, b: mb.hitsPerMin, fmt: (v) => v.toFixed(1) },
            { label: "Avg damage", a: ma.avgDamage, b: mb.avgDamage, fmt: formatNumber, hint: "per session" },
          ]}
          engagement={[
            { label: "Sessions", a: ma.sessions, b: mb.sessions, fmt: formatNumber },
            { label: "Days played", a: ma.days, b: mb.days, fmt: formatNumber },
            { label: "Time in fights", a: ma.secs, b: mb.secs, fmt: formatDuration },
            { label: "Win rate", a: ma.winRate, b: mb.winRate, fmt: formatPercent },
            { label: "Avg score", a: ma.avgScore, b: mb.avgScore, fmt: formatNumber },
          ]}
        />
      )}
    </>
  );
}

function LevelsCompare() {
  const { data, error } = useApi(() => api.analytics(), []);
  if (error && !data)
    return (
      <Card>
        <EmptyState icon="xCircle" title="Couldn't load level data" description={error.message} />
      </Card>
    );
  if (!data) return <Skeleton className="h-72 w-full rounded-2xl" />;
  const by = Object.fromEntries(data.levelComparison.map((l) => [l.level, l]));
  const [l1, l2] = [by.Easy, by.Medium];
  if (!l1?.sessions || !l2?.sessions)
    return (
      <Card>
        <EmptyState icon="compare" title="Needs finished fights on both levels" description={l1?.sessions ? "Level 1 has data; Level 2 doesn't yet." : l2?.sessions ? "Level 2 has data; Level 1 doesn't yet." : "No finished fights yet."} />
      </Card>
    );
  return (
    <Pair
      a={{ label: "Level 1", color: "var(--level-1)" }}
      b={{ label: "Level 2", color: "var(--level-2)" }}
      effort={[
        { label: "Avg swing speed", a: l1.avgSwingSpeed, b: l2.avgSwingSpeed, fmt: speed, hint: `${formatNumber(l1.swingSamples)} vs ${formatNumber(l2.swingSamples)} measured hits` },
        { label: "Full-power swings", a: pct(l1.fullPowerRatio), b: pct(l2.fullPowerRatio), fmt: formatPercent },
        { label: "Hits per minute", a: l1.hitsPerMinute, b: l2.hitsPerMinute, fmt: (v) => v.toFixed(1) },
        { label: "Damage per hit", a: l1.damagePerHit, b: l2.damagePerHit, fmt: (v) => v.toFixed(1) },
      ]}
      engagement={[
        { label: "Finished sessions", a: l1.sessions, b: l2.sessions, fmt: formatNumber },
        { label: "Avg length", a: l1.avgSessionSec, b: l2.avgSessionSec, fmt: formatDuration },
        { label: "Win rate", a: l1.winRate, b: l2.winRate, fmt: formatPercent },
        { label: "Avg score", a: l1.avgScore, b: l2.avgScore, fmt: formatNumber },
      ]}
    />
  );
}

export default function CompareView() {
  const [params, setParams] = useSearchParams();
  const mode = params.get("mode") === "levels" ? "levels" : "players";
  const players = useApi(() => api.players(), []);
  const names = useMemo(() => (players.data || []).map((p) => p.playerName).sort((x, y) => x.localeCompare(y)), [players.data]);
  const setParam = (k, v) => {
    const next = new URLSearchParams(params);
    if (v && v !== "players") next.set(k, v);
    else next.delete(k);
    setParams(next, { replace: true });
  };

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Research"
        title="Compare"
        description="Two players, or the two playable levels, side by side on effort (how hard people swing) and engagement (how much they play)."
        actions={<SegmentedControl label="Compare" options={MODES} value={mode} onChange={(v) => setParam("mode", v)} />}
      />
      {mode === "levels" ? <LevelsCompare /> : <PlayersCompare a={params.get("a") || ""} b={params.get("b") || ""} setParam={setParam} names={names} />}
      <p className="text-xs text-[var(--muted)]">
        Swing speed and full-power share exist only for fights recorded by the live-telemetry build. Small samples can move these numbers a lot.
      </p>
    </div>
  );
}
