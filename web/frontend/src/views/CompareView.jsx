import { useMemo } from "react";
import { useSearchParams } from "react-router-dom";
import { api } from "../lib/api.js";
import { formatDuration, formatNumber, formatPercent } from "../lib/format.js";
import { useApi } from "../hooks/useApi.js";
import { useLiveMessages } from "../live/LiveProvider.jsx";
import { Card, CardHeader, PageHeader } from "../components/ui/Card.jsx";
import { SegmentedControl, Select } from "../components/ui/Controls.jsx";
import { EmptyState } from "../components/ui/Feedback.jsx";
import { CompareSkeleton } from "../components/ui/Skeletons.jsx";
import Icon from "../components/ui/Icon.jsx";

const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const speed = (v) => `${v.toFixed(1)} m/s`;
const pct = (r) => (isNum(r) ? r * 100 : null);
const MODES = [
  { id: "players", label: "Two players" },
  { id: "levels", label: "Level 1 vs Level 2" },
];
const SIDE_A = { color: "var(--level-1)", soft: "bg-[var(--accent-soft)]", ring: "ring-[var(--accent-ring)]", text: "text-[var(--accent-strong)]" };
const SIDE_B = { color: "var(--level-2)", soft: "bg-amber-50", ring: "ring-amber-200", text: "text-amber-800" };

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

// One metric: A grows left from the centre line, B grows right. Each row has its own scale.
function DiffRow({ row, i }) {
  const a = isNum(row.a) ? row.a : null;
  const b = isNum(row.b) ? row.b : null;
  const top = Math.max(a ?? 0, b ?? 0) || 1;
  const lead = a !== null && b !== null && a !== b ? (a > b ? "a" : "b") : null;
  const low = lead === "a" ? b : a;
  const gap = lead && low > 0 ? Math.round((Math.abs(a - b) / low) * 100) : null;
  const bar = (v, side) => (
    <div className={`flex h-3 flex-1 ${side === "a" ? "justify-end" : "justify-start"}`}>
      {v !== null && (
        <div className="h-full" style={{ width: `${Math.max(3, (v / top) * 100)}%` }}>
          <div
            className={`${side === "a" ? "grow-x-end rounded-l-full" : "grow-x rounded-r-full"} h-full w-full`}
            style={{ background: side === "a" ? SIDE_A.color : SIDE_B.color, opacity: lead && lead !== side ? 0.45 : 1, "--i": i }}
          />
        </div>
      )}
    </div>
  );
  return (
    <li className="py-3">
      <p className="text-center text-xs font-bold text-[var(--ink-secondary)]">
        {row.label}
        {row.hint && <span className="ml-1.5 font-medium text-[var(--muted)]">· {row.hint}</span>}
      </p>
      <div className="mt-2 flex items-center gap-2">
        <span className={`w-20 shrink-0 text-right text-sm tabular-nums sm:w-24 ${lead === "a" ? "font-extrabold text-[var(--ink)]" : "font-semibold text-[var(--ink-secondary)]"}`}>
          {a !== null ? row.fmt(a) : "—"}
        </span>
        {bar(a, "a")}
        <span className="h-5 w-px shrink-0 bg-[var(--border-strong)]" aria-hidden />
        {bar(b, "b")}
        <span className={`w-20 shrink-0 text-sm tabular-nums sm:w-24 ${lead === "b" ? "font-extrabold text-[var(--ink)]" : "font-semibold text-[var(--ink-secondary)]"}`}>
          {b !== null ? row.fmt(b) : "—"}
        </span>
      </div>
      {gap !== null && gap > 0 && (
        <p className={`mt-1 text-xs font-semibold ${lead === "a" ? "text-left" : "text-right"} ${lead === "a" ? SIDE_A.text : SIDE_B.text}`}>
          {gap}% higher
        </p>
      )}
    </li>
  );
}

function DiffCard({ title, subtitle, rows, a, b, className = "" }) {
  return (
    <Card className={`overflow-hidden ${className}`}>
      <CardHeader title={title} subtitle={subtitle} />
      <div className="px-4 pb-2 pt-1 sm:px-6">
        <div className="flex items-center justify-between border-b border-[var(--border)] py-2 text-xs font-bold">
          <span className={`inline-flex items-center gap-1.5 ${SIDE_A.text}`}>
            <span className="h-2.5 w-2.5 rounded-sm" style={{ background: SIDE_A.color }} aria-hidden />
            {a}
          </span>
          <span className={`inline-flex items-center gap-1.5 ${SIDE_B.text}`}>
            {b}
            <span className="h-2.5 w-2.5 rounded-sm" style={{ background: SIDE_B.color }} aria-hidden />
          </span>
        </div>
        <ul className="divide-y divide-[var(--border)]">
          {rows.map((r, i) => (
            <DiffRow key={r.label} row={r} i={i} />
          ))}
        </ul>
        <table className="sr-only">
          <caption>{title}</caption>
          <thead>
            <tr>
              <th scope="col">Metric</th>
              <th scope="col">{a}</th>
              <th scope="col">{b}</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.label}>
                <th scope="row">{r.label}</th>
                <td>{isNum(r.a) ? r.fmt(r.a) : "—"}</td>
                <td>{isNum(r.b) ? r.fmt(r.b) : "—"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Card>
  );
}

// The split header: side A, a VS medallion, side B.
function Split({ a, b }) {
  const side = (s, x, align) => (
    <div className={`flex min-w-0 flex-1 flex-col gap-3 p-5 sm:p-6 ${x.soft} ${align === "right" ? "items-end text-right" : "items-start"}`}>
      <div className={`flex items-center gap-3 ${align === "right" ? "flex-row-reverse" : ""}`}>
        <span className={`flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl bg-[var(--surface)] text-base font-extrabold ring-2 ${x.ring} ${x.text}`}>
          {s.initials}
        </span>
        <div className="min-w-0">
          <p className="text-xs font-bold uppercase tracking-wider text-[var(--muted)]">{s.kicker}</p>
          {s.control || <p className="truncate text-lg font-extrabold text-[var(--ink)]">{s.label}</p>}
        </div>
      </div>
      {s.facts && (
        <dl className={`flex flex-wrap gap-x-5 gap-y-1 ${align === "right" ? "justify-end" : ""}`}>
          {s.facts.map(([k, v]) => (
            <div key={k}>
              <dt className="text-xs font-semibold text-[var(--muted)]">{k}</dt>
              <dd className="text-base font-extrabold tabular-nums text-[var(--ink)]">{v}</dd>
            </div>
          ))}
        </dl>
      )}
    </div>
  );
  return (
    <Card className="relative flex flex-col overflow-hidden sm:flex-row">
      {side(a, SIDE_A, "left")}
      <span className="pop-in absolute left-1/2 top-1/2 z-[1] hidden h-12 w-12 -translate-x-1/2 -translate-y-1/2 items-center justify-center rounded-full bg-[var(--surface)] text-sm font-extrabold text-[var(--ink)] shadow-[var(--shadow-lift)] ring-1 ring-[var(--border)] sm:flex">
        VS
      </span>
      <span className="flex items-center justify-center border-y border-[var(--border)] bg-[var(--surface)] py-1.5 text-xs font-extrabold text-[var(--ink)] sm:hidden">VS</span>
      {side(b, SIDE_B, "right")}
    </Card>
  );
}

const facts = (m) => (m ? [["Sessions", formatNumber(m.sessions)], ["Win rate", isNum(m.winRate) ? formatPercent(m.winRate) : "—"], ["Avg swing", isNum(m.avgSpeed) && m.samples ? speed(m.avgSpeed) : "—"]] : null);

function PlayersCompare({ a, b, setParam, names }) {
  const pa = useApi(() => (a ? api.player(a) : Promise.resolve(null)), [a]);
  const pb = useApi(() => (b ? api.player(b) : Promise.resolve(null)), [b]);
  const opts = [{ id: "", label: "Choose a player" }, ...names.map((n) => ({ id: n, label: n }))];
  const ma = pa.data && metrics(pa.data);
  const mb = pb.data && metrics(pb.data);
  const ready = a && b && a !== b;

  return (
    <>
      <Split
        a={{ initials: a ? a.slice(0, 2).toUpperCase() : "A", kicker: "Player A", control: <Select label="Player A" value={a} onChange={(v) => setParam("a", v)} options={opts} />, facts: ready ? facts(ma) : null }}
        b={{ initials: b ? b.slice(0, 2).toUpperCase() : "B", kicker: "Player B", control: <Select label="Player B" value={b} onChange={(v) => setParam("b", v)} options={opts} />, facts: ready ? facts(mb) : null }}
      />
      {!ready ? (
        <Card>
          <EmptyState icon="compare" title={a && a === b ? "Pick two different players" : "Pick two players"} description="Each row is scaled on its own, so different units never share an axis." />
        </Card>
      ) : pa.error || pb.error ? (
        <Card>
          <EmptyState icon="players" title="One of these profiles isn't available" description="It loads as soon as the server answers, or pick another player." />
        </Card>
      ) : !ma || !mb ? (
        <div className="grid gap-6 xl:grid-cols-12" aria-busy="true">
          <div className="skeleton h-80 w-full rounded-2xl xl:col-span-7" />
          <div className="skeleton h-80 w-full rounded-2xl xl:col-span-5" />
        </div>
      ) : (
        <div key={`${a}|${b}`} className="grid gap-6 xl:grid-cols-12">
          <DiffCard
            className="xl:col-span-7"
            title="Effort"
            subtitle="How hard they swing when they play"
            a={a}
            b={b}
            rows={[
              { label: "Avg swing speed", a: ma.samples ? ma.avgSpeed : null, b: mb.samples ? mb.avgSpeed : null, fmt: speed, hint: `${formatNumber(ma.samples)} vs ${formatNumber(mb.samples)} measured hits` },
              { label: "Full-power swings", a: ma.full, b: mb.full, fmt: formatPercent },
              { label: "Hits per minute", a: ma.hitsPerMin, b: mb.hitsPerMin, fmt: (v) => v.toFixed(1) },
              { label: "Avg damage", a: ma.avgDamage, b: mb.avgDamage, fmt: formatNumber, hint: "per session" },
            ]}
          />
          <DiffCard
            className="xl:col-span-5"
            title="Engagement and results"
            subtitle="How much they play, and how it goes"
            a={a}
            b={b}
            rows={[
              { label: "Sessions", a: ma.sessions, b: mb.sessions, fmt: formatNumber },
              { label: "Days played", a: ma.days, b: mb.days, fmt: formatNumber },
              { label: "Time in fights", a: ma.secs, b: mb.secs, fmt: formatDuration },
              { label: "Win rate", a: ma.winRate, b: mb.winRate, fmt: formatPercent },
              { label: "Avg score", a: ma.avgScore, b: mb.avgScore, fmt: formatNumber },
            ]}
          />
        </div>
      )}
    </>
  );
}

function LevelsCompare() {
  const { data, error } = useApi(() => api.analytics(), []);
  if (error && !data)
    return (
      <Card>
        <EmptyState icon="layers" title="Level data will appear here" description="It loads as soon as the server answers." />
      </Card>
    );
  if (!data) return <CompareSkeleton />;
  const by = Object.fromEntries(data.levelComparison.map((l) => [l.level, l]));
  const [l1, l2] = [by.Easy, by.Medium];
  const lf = (l) => (l?.sessions ? [["Finished", formatNumber(l.sessions)], ["Win rate", isNum(l.winRate) ? formatPercent(l.winRate) : "—"], ["Avg swing", isNum(l.avgSwingSpeed) ? speed(l.avgSwingSpeed) : "—"]] : null);
  return (
    <>
      <Split a={{ initials: "L1", kicker: "Level 1", label: "Easy", facts: lf(l1) }} b={{ initials: "L2", kicker: "Level 2", label: "Medium", facts: lf(l2) }} />
      {!l1?.sessions || !l2?.sessions ? (
        <Card>
          <EmptyState
            icon="compare"
            title="Needs finished fights on both levels"
            description={l1?.sessions ? "Level 1 has data; Level 2 doesn't yet." : l2?.sessions ? "Level 2 has data; Level 1 doesn't yet." : "No finished fights yet."}
          />
        </Card>
      ) : (
        <div className="grid gap-6 xl:grid-cols-12">
          <DiffCard
            className="xl:col-span-7"
            title="Effort"
            subtitle="How hard people swing on each level"
            a="Level 1"
            b="Level 2"
            rows={[
              { label: "Avg swing speed", a: l1.avgSwingSpeed, b: l2.avgSwingSpeed, fmt: speed, hint: `${formatNumber(l1.swingSamples)} vs ${formatNumber(l2.swingSamples)} measured hits` },
              { label: "Full-power swings", a: pct(l1.fullPowerRatio), b: pct(l2.fullPowerRatio), fmt: formatPercent },
              { label: "Hits per minute", a: l1.hitsPerMinute, b: l2.hitsPerMinute, fmt: (v) => v.toFixed(1) },
              { label: "Damage per hit", a: l1.damagePerHit, b: l2.damagePerHit, fmt: (v) => v.toFixed(1) },
            ]}
          />
          <DiffCard
            className="xl:col-span-5"
            title="Engagement and results"
            subtitle="How long fights last, and how they end"
            a="Level 1"
            b="Level 2"
            rows={[
              { label: "Finished sessions", a: l1.sessions, b: l2.sessions, fmt: formatNumber },
              { label: "Avg length", a: l1.avgSessionSec, b: l2.avgSessionSec, fmt: formatDuration },
              { label: "Win rate", a: l1.winRate, b: l2.winRate, fmt: formatPercent },
              { label: "Avg score", a: l1.avgScore, b: l2.avgScore, fmt: formatNumber },
            ]}
          />
        </div>
      )}
    </>
  );
}

export default function CompareView() {
  const [params, setParams] = useSearchParams();
  const mode = params.get("mode") === "levels" ? "levels" : "players";
  const players = useApi(() => api.players(), []);
  const names = useMemo(() => (players.data || []).map((p) => p.playerName).sort((x, y) => x.localeCompare(y)), [players.data]);
  // A renamed player keeps their slot in the comparison.
  useLiveMessages((type, payload) => {
    if (type !== "player_renamed") return;
    players.reload();
    const next = new URLSearchParams(params);
    let moved = false;
    for (const k of ["a", "b"]) {
      if (next.get(k) === payload.oldName) {
        next.set(k, payload.newName);
        moved = true;
      }
    }
    if (moved) setParams(next, { replace: true });
  });
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
      <p className="flex items-start gap-2 text-xs font-medium text-[var(--muted)]">
        <Icon name="info" className="mt-px h-3.5 w-3.5 shrink-0" />
        Swing speed and full-power share exist only for fights recorded by the live-telemetry build. Small samples can move these numbers a lot.
      </p>
    </div>
  );
}
