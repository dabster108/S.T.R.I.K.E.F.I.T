import { useCallback, useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { api } from "../lib/api.js";
import { levelName } from "../lib/game.js";
import { formatDuration, formatNumber, formatPercent, formatRelative, formatShortDate } from "../lib/format.js";
import { useApi, useThrottledCallback } from "../hooks/useApi.js";
import { useFeed, useLiveForPlayer, useLiveMessages } from "../live/LiveProvider.jsx";
import { Card, CardHeader, PageHeader } from "../components/ui/Card.jsx";
import { SegmentedControl, Select } from "../components/ui/Controls.jsx";
import { EmptyState } from "../components/ui/Feedback.jsx";
import { PlayerSkeleton } from "../components/ui/Skeletons.jsx";
import { LevelBadge, OutcomeBadge } from "../components/ui/Badge.jsx";
import { buttonClass } from "../components/ui/Button.jsx";
import Icon from "../components/ui/Icon.jsx";
import StatTile from "../components/ui/StatTile.jsx";
import { SessionList } from "../components/sessions/SessionCards.jsx";
import EventList from "../components/sessions/EventList.jsx";
import { LiveArenaCard } from "../components/live/LiveFight.jsx";
import { BarChart, CompareBars, LEVEL_COLORS, Legend, LineChart, TierBar } from "../components/charts/Charts.jsx";

const LEVELS = [
  { id: "all", label: "All levels" },
  { id: "Easy", label: "Level 1" },
  { id: "Medium", label: "Level 2" },
];
const OUTCOMES = [
  { id: "all", label: "All" },
  { id: "victory", label: "Victories" },
  { id: "defeat", label: "Defeats" },
];
const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const speed = (v) => (isNum(v) ? `${v.toFixed(1)} m/s` : "—");
const share = (r) => (isNum(r) ? formatPercent(r * 100) : "—");
const pct = (v) => `${Math.round(v * 10) / 10}%`;

function Avatar({ name }) {
  return (
    <span className="pop-in flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl bg-gradient-to-br from-[var(--accent)] to-[var(--blue-800)] text-xl font-extrabold text-white shadow-[var(--shadow-glow)] sm:h-16 sm:w-16">
      {name.slice(0, 2).toUpperCase()}
    </span>
  );
}

// Win streaks from finished fights, oldest to newest.
function streaks(sessions) {
  const chrono = sessions.filter((s) => s.status !== "live" && s.outcome).sort((a, b) => new Date(a.startedAt) - new Date(b.startedAt));
  let best = 0;
  let run = 0;
  for (const s of chrono) {
    run = s.outcome === "victory" ? run + 1 : 0;
    best = Math.max(best, run);
  }
  return { current: run, best };
}

function Highlight({ icon, children, tone = "accent" }) {
  const cls =
    tone === "gold"
      ? "bg-amber-50 text-amber-800 ring-amber-200"
      : tone === "good"
        ? "bg-[var(--good-soft)] text-[var(--good-ink)] ring-emerald-200"
        : "bg-[var(--accent-soft)] text-[var(--accent-strong)] ring-[var(--accent-ring)]";
  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-bold ring-1 ring-inset ${cls}`}>
      <Icon name={icon} className="h-3.5 w-3.5" />
      {children}
    </span>
  );
}

function LivePanel({ name, latest }) {
  const live = useLiveForPlayer(name);
  const mine = useCallback((e) => e.playerName === name, [name]);
  const feed = useFeed(mine);

  if (live) {
    return (
      <div className="grid gap-6 xl:grid-cols-12">
        <div className="xl:col-span-5">
          <LiveArenaCard id={live.session.id} />
        </div>
        <Card className="overflow-hidden xl:col-span-7">
          <CardHeader title="This fight, as it happens" subtitle="Newest first" />
          {feed.length ? (
            <div className="scrollbar-thin max-h-80 overflow-y-auto">
              <EventList events={feed.slice(0, 40)} dense />
            </div>
          ) : (
            <EmptyState icon="live" title="Waiting for the first event" />
          )}
        </Card>
      </div>
    );
  }
  if (!latest) return null;
  return (
    <Card className="overflow-hidden">
      <CardHeader
        title="Latest session"
        subtitle={`${formatRelative(latest.endedAt)} · not playing right now`}
        actions={
          <Link to={`/dashboard/session/${encodeURIComponent(latest.id)}`} className="press rounded text-xs font-semibold text-[var(--accent)] hover:underline">
            Open session
          </Link>
        }
      />
      <div className="flex flex-wrap items-center gap-x-8 gap-y-3 px-5 py-4">
        <span className="flex items-center gap-2">
          <LevelBadge level={latest.level} />
          <OutcomeBadge outcome={latest.outcome} />
        </span>
        {[
          ["Score", formatNumber(latest.score)],
          ["XP", latest.xp ? formatNumber(latest.xp) : "—"],
          ["Hits", formatNumber(latest.hitCount)],
          ["Damage", formatNumber(latest.damageDealt)],
          ["Duration", formatDuration(latest.durationSec)],
        ].map(([k, v]) => (
          <div key={k}>
            <p className="text-xs font-semibold text-[var(--muted)]">{k}</p>
            <p className="text-lg font-extrabold tabular-nums text-[var(--ink)]">{v}</p>
          </div>
        ))}
      </div>
    </Card>
  );
}

export default function PlayerView() {
  const { name: raw } = useParams();
  const name = decodeURIComponent(raw || "");
  const navigate = useNavigate();
  const { data: p, error, reload } = useApi(() => api.player(name), [name]);
  const players = useApi(() => api.players(), []);
  const refresh = useThrottledCallback(reload, 3000);
  useLiveMessages((type, payload) => {
    const who = payload?.session?.playerName;
    if (who === name && ["session_started", "session_finished", "session_deleted"].includes(type)) refresh();
  });
  const [level, setLevel] = useState("all");
  const [outcome, setOutcome] = useState("all");

  const history = useMemo(
    () => (p?.sessions || []).filter((s) => s.status !== "live" && (level === "all" || s.level === level) && (outcome === "all" || s.outcome === outcome)),
    [p, level, outcome]
  );
  const streak = useMemo(() => streaks(p?.sessions || []), [p]);

  if (error?.status === 404) {
    return (
      <div className="space-y-6">
        <PageHeader eyebrow="Player" title={name} leading={<Avatar name={name} />} />
        <LivePanel name={name} latest={null} />
        <Card>
          <EmptyState
            icon="players"
            title="No sessions for this player yet"
            description="Names must match what's set on the headset exactly, including spaces and capitals."
            action={
              <Link to="/dashboard/players" className={buttonClass("secondary")}>
                All players
              </Link>
            }
          />
        </Card>
      </div>
    );
  }
  if (!p && !error) return <PlayerSkeleton />;

  const t = p?.totals;
  const series = (p?.series || []).map((s, i) => ({ ...s, n: i + 1 })).filter((s) => s.status !== "live");
  const played = [...new Set(series.map((s) => s.level))].filter((l) => LEVEL_COLORS[l]);
  const damageSeries = played.map((l) => ({
    key: l,
    label: levelName(l),
    color: LEVEL_COLORS[l],
    points: series.filter((s) => s.level === l).map((s) => ({ x: s.n, y: s.damageDealt })),
  }));
  const withXp = series.filter((s) => s.xp > 0);
  const levelsAll = [...new Set((p?.sessions || []).map((s) => s.level))].filter((l) => LEVEL_COLORS[l]);
  const byLevel = Object.fromEntries((p?.byLevel || []).map((l) => [l.level, l]));
  const [l1, l2] = [byLevel.Easy, byLevel.Medium];
  const sessionLabel = (n) => {
    const s = series.find((x) => x.n === n);
    return s ? `Session ${n} · ${formatShortDate(s.startedAt)}` : `Session ${n}`;
  };

  // Rank among all players by best score (the order /api/players returns).
  const roster = players.data || [];
  const rankIndex = roster.findIndex((x) => x.playerName === name);
  const finished = (p?.sessions || []).filter((s) => s.status !== "live");
  const best = finished.reduce((m, s) => (!m || (Number(s.score) || 0) > (Number(m.score) || 0) ? s : m), null);
  const latestIsBest = finished.length > 1 && best && finished[0]?.id === best.id && Number(best.score) > 0;

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Player"
        title={name}
        leading={<Avatar name={name} />}
        description={p ? `First played ${formatRelative(p.firstPlayedAt)} · last played ${formatRelative(p.lastPlayedAt)}` : undefined}
        actions={
          <Link to={`/dashboard/compare?a=${encodeURIComponent(name)}`} className={buttonClass("secondary", "sm")}>
            <Icon name="compare" className="h-3.5 w-3.5" />
            Compare with…
          </Link>
        }
      />

      {p && (rankIndex >= 0 || streak.best >= 2 || latestIsBest || best) && (
        <div className="stagger -mt-2 flex flex-wrap gap-2">
          {rankIndex >= 0 && roster.length > 1 && (
            <Highlight icon={rankIndex === 0 ? "crown" : "medal"} tone={rankIndex < 3 ? "gold" : "accent"}>
              Rank #{rankIndex + 1} of {roster.length} by best score
            </Highlight>
          )}
          {best && Number(best.score) > 0 && (
            <Highlight icon="trophy">
              Best {formatNumber(best.score)} on {levelName(best.level)} · {formatShortDate(best.startedAt)}
            </Highlight>
          )}
          {latestIsBest && (
            <Highlight icon="sparkles" tone="good">
              Latest fight is a personal best
            </Highlight>
          )}
          {streak.current >= 2 && (
            <Highlight icon="flame" tone="good">
              {streak.current} wins in a row
            </Highlight>
          )}
          {streak.best >= 2 && streak.best > streak.current && <Highlight icon="flame">Best streak: {streak.best} wins</Highlight>}
        </div>
      )}

      {error && !p ? (
        <Card>
          <EmptyState icon="players" title="This profile will appear here" description="It loads as soon as the server answers." />
        </Card>
      ) : (
        <>
          <LivePanel name={name} latest={p.sessions.find((s) => s.status !== "live")} />

          <div className="stagger grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <StatTile label="Sessions" count={t.sessions} icon="sessions" tone="blue" hint={`${formatDuration(t.totalDurationSec)} in fights`} />
            <StatTile label="Wins / losses" value={`${t.wins} / ${t.losses}`} icon="check" tone="green" hint={t.winRate === null ? "No decided fights yet" : `${formatPercent(t.winRate)} win rate`} />
            <StatTile label="Best score" count={t.bestScore} icon="trophy" tone="indigo" hint={`${formatNumber(t.totalXp)} XP earned`} />
            <StatTile label="Total damage" count={t.totalDamage} icon="flame" tone="rose" hint="HP taken off monsters" />
            <StatTile label="Swings counted" count={t.totalSwings} icon="bolt" tone="amber" hint={t.swingSamples ? `avg ${speed(t.avgSwingSpeed)} · peak ${speed(t.peakSwingSpeed)}` : "fast enough to register"} />
            <StatTile
              label="Full-power swings"
              count={isNum(t.fullPowerRatio) ? t.fullPowerRatio * 100 : undefined}
              value={share(t.fullPowerRatio)}
              format={pct}
              icon="gauge"
              tone="blue"
              hint={t.swingSamples ? `of ${formatNumber(t.swingSamples)} measured hits` : "No speed data yet"}
            />
            <StatTile label="Win rate" count={isNum(t.winRate) ? t.winRate : undefined} value="—" format={pct} icon="target" tone="green" hint="of fights won or lost" />
            <StatTile label="Levels played" count={levelsAll.length} icon="layers" tone="slate" hint={levelsAll.map(levelName).join(" · ") || "—"} />
          </div>

          <div className="grid gap-6 xl:grid-cols-2">
            <Card className="overflow-hidden">
              <CardHeader title="Damage over time" subtitle="Total damage in each fight, in the order played" />
              <div className="px-3 pb-4 pt-4 sm:px-5">
                {series.length ? (
                  <>
                    <LineChart caption="Damage per session" series={damageSeries} xFormat={(x) => (Number.isInteger(x) ? `#${x}` : "")} yFormat={(v) => formatNumber(v)} />
                    <Legend items={damageSeries} shape="line" />
                  </>
                ) : (
                  <EmptyState icon="flame" title="No finished sessions yet" />
                )}
              </div>
            </Card>

            <Card className="overflow-hidden">
              <CardHeader title="Swing speed distribution" subtitle="Counted hits sorted into the game's swing tiers" />
              <div className="px-5 py-5">
                {t.swingSamples ? (
                  <TierBar counts={p.tiers} />
                ) : (
                  <EmptyState icon="gauge" title="No swing-speed data yet" description="Speeds are recorded from the live-telemetry build of the game onward; older sessions only kept totals." />
                )}
              </div>
            </Card>

            <Card className="overflow-hidden">
              <CardHeader title="XP per session" subtitle="Experience earned in each fight" />
              <div className="px-3 pb-4 pt-4 sm:px-5">
                {withXp.length ? (
                  <>
                    <BarChart
                      caption="XP per session"
                      categories={withXp.map((s) => `#${s.n}`)}
                      series={[{ key: "xp", label: "XP", colors: withXp.map((s) => LEVEL_COLORS[s.level] || "var(--accent)"), values: withXp.map((s) => s.xp) }]}
                      yFormat={(v) => formatNumber(v)}
                      tipTitle={(c) => sessionLabel(Number(c.slice(1)))}
                    />
                    <Legend items={played.map((l) => ({ label: levelName(l), color: LEVEL_COLORS[l] }))} />
                  </>
                ) : (
                  <EmptyState icon="sparkles" title="No XP recorded yet" description="XP is sent with each session from the live-telemetry build onward." />
                )}
              </div>
            </Card>

            <Card className="overflow-hidden">
              <CardHeader title="Level 1 vs Level 2" subtitle="Same player, both playable levels. Each row has its own scale." />
              <div className="px-5 py-4">
                {l1 && l2 ? (
                  <CompareBars
                    a={{ label: "Level 1", color: "var(--level-1)" }}
                    b={{ label: "Level 2", color: "var(--level-2)" }}
                    rows={[
                      { label: "Sessions", a: l1.sessions, b: l2.sessions, fmt: formatNumber },
                      { label: "Win rate", a: l1.winRate, b: l2.winRate, fmt: formatPercent },
                      { label: "Avg score", a: l1.avgScore, b: l2.avgScore, fmt: formatNumber },
                      { label: "Avg damage", a: l1.avgDamage, b: l2.avgDamage, fmt: formatNumber, hint: "per session" },
                      { label: "Avg swing speed", a: l1.avgSwingSpeed, b: l2.avgSwingSpeed, fmt: speed },
                      { label: "Full power", a: isNum(l1.fullPowerRatio) ? l1.fullPowerRatio * 100 : null, b: isNum(l2.fullPowerRatio) ? l2.fullPowerRatio * 100 : null, fmt: formatPercent },
                    ]}
                  />
                ) : (
                  <EmptyState
                    icon="compare"
                    title={l1 || l2 ? `Only ${levelName((l1 || l2).level)} played so far` : "No levels played yet"}
                    description="The comparison appears once this player has finished a fight on both levels."
                  />
                )}
              </div>
            </Card>
          </div>

          <Card className="overflow-hidden">
            <CardHeader
              title="Session history"
              subtitle={`${history.length} session${history.length === 1 ? "" : "s"}`}
              actions={
                <>
                  <SegmentedControl label="Outcome" options={OUTCOMES} value={outcome} onChange={setOutcome} />
                  <Select label="Level" value={level} onChange={setLevel} options={LEVELS} />
                </>
              }
            />
            {history.length ? (
              <div className="p-4 sm:p-5">
                <SessionList rows={history} onSelect={(s) => navigate(`/dashboard/session/${encodeURIComponent(s.id)}`)} />
              </div>
            ) : (
              <EmptyState icon="search" title="No sessions match these filters" />
            )}
          </Card>
        </>
      )}
    </div>
  );
}
