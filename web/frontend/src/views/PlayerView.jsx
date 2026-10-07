import { useCallback, useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { api } from "../lib/api.js";
import { levelName } from "../lib/game.js";
import { formatDuration, formatNumber, formatPercent, formatRelative, formatShortDate } from "../lib/format.js";
import { useApi, useThrottledCallback } from "../hooks/useApi.js";
import { useFeed, useLiveForPlayer, useLiveMessages } from "../live/LiveProvider.jsx";
import { Card, CardHeader, PageHeader, SectionRule } from "../components/ui/Card.jsx";
import { SegmentedControl, Select } from "../components/ui/Controls.jsx";
import { EmptyState } from "../components/ui/Feedback.jsx";
import { PlayerSkeleton } from "../components/ui/Skeletons.jsx";
import { LevelBadge, OutcomeBadge } from "../components/ui/Badge.jsx";
import { buttonClass } from "../components/ui/Button.jsx";
import Icon from "../components/ui/Icon.jsx";
import { FigureStrip } from "../components/ui/StatTile.jsx";
import PlayerName from "../components/players/PlayerName.jsx";
import { SessionList } from "../components/sessions/SessionCards.jsx";
import EventList, { collapseRepeats } from "../components/sessions/EventList.jsx";
import { LiveArenaCard, LiveChip } from "../components/live/LiveFight.jsx";
import { BarChart, CompareBars, LEVEL_COLORS, Legend, LineChart, TierBar } from "../components/charts/Charts.jsx";
import { CalorieEstimate, EstimateNote, ExertionChart, LevelIntensityChart, PersonalBests, SwingTierChart, VsAverage } from "../components/fitness/Fitness.jsx";
import PlayerEngagement from "../components/fitness/Engagement.jsx";
import { AchievementsSection } from "../components/fitness/Achievements.jsx";

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

function LivePanel({ name, latest, estimate, progress }) {
  const live = useLiveForPlayer(name);
  const mine = useCallback((e) => e.playerName === name, [name]);
  const feed = useFeed(mine);

  if (live) {
    return (
      <div id="live-now" className="grid scroll-mt-20 gap-6 xl:grid-cols-12">
        <div className="xl:col-span-5">
          <LiveArenaCard id={live.session.id} />
        </div>
        <Card className="overflow-hidden xl:col-span-7">
          <CardHeader title="This fight, as it happens" subtitle="Newest first" />
          {feed.length ? (
            <div className="scrollbar-thin max-h-80 overflow-y-auto">
              <EventList events={collapseRepeats(feed).slice(0, 40)} dense />
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
        <CalorieEstimate kcal={estimate?.estimatedCalories} walkingMinutes={estimate?.estimatedWalkingMinutes} joggingMinutes={estimate?.estimatedJoggingMinutes} />
      </div>
      {progress && progress.latestSessionId === latest.id && <VsAverage progress={progress} />}
    </Card>
  );
}

export default function PlayerView() {
  const { name: raw } = useParams();
  const name = decodeURIComponent(raw || "");
  const navigate = useNavigate();
  const { data: fetched, error: fetchError, loading, reload, set: setProfile } = useApi(() => api.player(name), [name]);
  const engagement = useApi(() => api.playerEngagement(name), [name]);
  // useApi keeps the previous response while the next one loads; after switching players that
  // is someone else's profile (or 404), so show the skeleton instead of mixing two people.
  const p = fetched && fetched.playerName === name ? fetched : undefined;
  const error = loading && !p ? null : fetchError;
  const liveNow = useLiveForPlayer(name);
  const players = useApi(() => api.players(), []);
  const refresh = useThrottledCallback(reload, 3000);
  useLiveMessages((type, payload) => {
    if (type === "player_renamed") {
      players.reload();
      // This player was renamed (here or in another tab): move to the new URL in place.
      if (payload.oldName === name) {
        navigate(`/dashboard/player/${encodeURIComponent(payload.newName)}`, { replace: true, state: { renamedFrom: payload.oldName } });
      }
      return;
    }
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
            description="Players are named on the headset before each fight (the name picker in front of the Start button). Names must match exactly, including spaces and capitals."
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
    <div>
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div className="flex min-w-0 flex-1 items-center gap-4">
          <Avatar name={name} />
          <div className="min-w-0 flex-1">
            <p className="t-eyebrow">Player</p>
            <div className="mt-1 max-w-xl">
              <PlayerName name={name} size="title" onRenamed={(profile) => setProfile(profile)}>
                <h1 className="t-h1 truncate text-[var(--ink)]">{name}</h1>
              </PlayerName>
            </div>
            {liveNow && (
              <a href="#live-now" className="mt-2 inline-block">
                <LiveChip label={`Playing now · ${levelName(liveNow.session.level)}`} />
              </a>
            )}
            {p && (
              <p className="mt-1.5 text-sm font-medium text-[var(--muted)]">
                First played {formatRelative(p.firstPlayedAt)} · last played {formatRelative(p.lastPlayedAt)}
              </p>
            )}
          </div>
        </div>
        <Link to={`/dashboard/compare?a=${encodeURIComponent(name)}`} className={buttonClass("secondary", "sm")}>
          <Icon name="compare" className="h-3.5 w-3.5" />
          Compare with…
        </Link>
      </header>

      {p && (rankIndex >= 0 || streak.best >= 2 || latestIsBest || best) && (
        <div className="stagger mt-4 flex flex-wrap gap-2">
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
        <Card className="mt-6">
          <EmptyState icon="players" title="This profile will appear here" description="It loads as soon as the server answers." />
        </Card>
      ) : (
        <>
          <div className="mt-6">
            <LivePanel
              name={name}
              latest={p.sessions.find((s) => s.status !== "live")}
              estimate={p.series.find((s) => s.id === p.sessions.find((x) => x.status !== "live")?.id)}
              progress={p.progress}
            />
          </div>

          <SectionRule index="01" title="Progress" className="mt-10" />
          <div className="mt-4">
            <AchievementsSection name={name} />
          </div>

          <SectionRule index="02" title="Results" className="mt-12" />
          <FigureStrip
            className="mt-4"
            items={[
              { label: "Best score", value: formatNumber(t.bestScore), sub: `${formatNumber(t.totalXp)} XP earned in total`, lead: true },
              { label: "Sessions", value: formatNumber(t.sessions), sub: `${formatDuration(t.totalDurationSec)} in fights` },
              { label: "Wins / losses", value: `${t.wins} / ${t.losses}`, sub: t.winRate === null ? "No decided fights yet" : `${formatPercent(t.winRate)} win rate` },
              { label: "Total damage", value: formatNumber(t.totalDamage), sub: "HP taken off monsters" },
              { label: "Levels played", value: formatNumber(levelsAll.length), sub: levelsAll.map(levelName).join(" · ") || "—" },
            ]}
          />

          <SectionRule index="03" title="Effort over time" className="mt-12" />
          <div className="mt-4 grid gap-6 xl:grid-cols-12">
            <Card className="overflow-hidden xl:col-span-8">
              <CardHeader title="Damage over time" subtitle="Total damage in each fight, in the order played" />
              <div className="px-3 pb-4 pt-4 sm:px-5">
                {series.length ? (
                  <>
                    <LineChart caption="Damage per session" height={260} series={damageSeries} xFormat={(x) => (Number.isInteger(x) ? `#${x}` : "")} yFormat={(v) => formatNumber(v)} />
                    <Legend items={damageSeries} shape="line" />
                  </>
                ) : (
                  <EmptyState icon="flame" title="No finished sessions yet" />
                )}
              </div>
            </Card>

            {/* effort: the full-power share leads, speeds as a readout, tiers underneath */}
            <Card className="flex flex-col overflow-hidden xl:col-span-4">
              <CardHeader title="Swing effort" subtitle="Counted hits sorted into the game's swing tiers" />
              {t.swingSamples ? (
                <div className="flex flex-1 flex-col p-5">
                  <p className="t-kicker">Full-power swings</p>
                  <p className="t-figure-md mt-2 text-[var(--ink)]">{share(t.fullPowerRatio)}</p>
                  <p className="mt-1.5 text-xs font-semibold text-[var(--muted)]">of {formatNumber(t.swingSamples)} measured hits</p>
                  <dl className="readout mt-5 grid grid-cols-3 divide-x divide-[var(--border)] border-y border-[var(--border)] py-3 text-center">
                    {[
                      ["Avg", speed(t.avgSwingSpeed)],
                      ["Peak", speed(t.peakSwingSpeed)],
                      ["Counted", formatNumber(t.totalSwings)],
                    ].map(([k, v]) => (
                      <div key={k}>
                        <dt className="text-[0.6875rem] font-semibold text-[var(--muted)]">{k}</dt>
                        <dd className="text-sm font-extrabold text-[var(--ink)]">{v}</dd>
                      </div>
                    ))}
                  </dl>
                  <div className="mt-5">
                    <TierBar counts={p.tiers} columns={2} />
                  </div>
                </div>
              ) : (
                <EmptyState
                  icon="gauge"
                  title="No swing-speed data yet"
                  description={`${formatNumber(t.totalSwings)} swings counted. Speeds are recorded from the live-telemetry build onward; older sessions only kept totals.`}
                />
              )}
            </Card>
          </div>

          <div className="mt-6 grid gap-6 xl:grid-cols-12">
            <Card className="overflow-hidden xl:col-span-5">
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

            <Card className="overflow-hidden xl:col-span-7">
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

          <SectionRule index="04" title="Fitness outcomes" className="mt-12" />
          <div className="mt-4 space-y-6">
            <PersonalBests bests={p.personalBests} />
            <div className="grid gap-6 xl:grid-cols-12">
              <Card className="overflow-hidden xl:col-span-5">
                <CardHeader title="How hard the swings were" subtitle="Share of all this player's swings in each strength band" />
                <div className="px-3 pb-4 pt-4 sm:px-5">
                  <SwingTierChart breakdown={p.swingIntensity} caption="Swings by strength, all sessions" />
                </div>
              </Card>
              <Card className="overflow-hidden xl:col-span-7">
                <CardHeader title="Did harder levels mean harder swings?" subtitle="Share of swings by strength on each level this player has fought" />
                <div className="px-3 pb-4 pt-4 sm:px-5">
                  <LevelIntensityChart levels={p.intensityByLevel} />
                </div>
              </Card>
            </div>
            {p.exertionTrend && (
              <Card className="overflow-hidden">
                <CardHeader title="Typical effort over a fight" subtitle={`Average across ${p.exertionTrend.sessionsUsed} sessions with enough swings, each scaled to its own length`} />
                <div className="px-3 pb-4 pt-4 sm:px-5">
                  <ExertionChart trend={p.exertionTrend} caption="Typical effort over the course of a fight" />
                </div>
              </Card>
            )}
            <Card className="overflow-hidden">
              <CardHeader title="Estimated energy use, all sessions" subtitle="An estimate, not a measurement" />
              <div className="px-5 py-4">
                {p.fitnessEstimate.estimatedCalories !== null ? (
                  <CalorieEstimate kcal={p.fitnessEstimate.estimatedCalories} walkingMinutes={p.fitnessEstimate.estimatedWalkingMinutes} joggingMinutes={p.fitnessEstimate.estimatedJoggingMinutes} />
                ) : (
                  <p className="text-sm font-medium text-[var(--muted)]">No session with recorded swings yet, so there is nothing to estimate from.</p>
                )}
              </div>
              <EstimateNote>
                Built from fight length and swing strength with an assumed body mass and an assumed intensity range. Sessions with no recorded swings are left out ({p.fitnessEstimate.sessionsEstimated} of {p.totals.sessions} counted).
              </EstimateNote>
            </Card>
          </div>

          <SectionRule
            index="05"
            title="Engagement"
            className="mt-12"
            aside={
              <Link to={`/dashboard/engagement?player=${encodeURIComponent(name)}`} className="press rounded text-xs font-semibold text-[var(--accent)] hover:underline">
                Open Engagement page
              </Link>
            }
          />
          <div className="mt-4">{engagement.data?.playerName === name ? <PlayerEngagement data={engagement.data} /> : null}</div>

          <SectionRule index="06" title="History" className="mt-12" />
          <Card className="mt-4 overflow-hidden">
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
