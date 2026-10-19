import { lazy, Suspense, useEffect, useMemo, useRef, useState } from "react";
import { api } from "../../lib/api.js";
import { formatNumber, formatRelative } from "../../lib/format.js";
import { shortName } from "../../lib/engage.js";
import { useApi, useThrottledCallback } from "../../hooks/useApi.js";
import { useLiveMessages } from "../../live/LiveProvider.jsx";
import { Card, CardHeader } from "../ui/Card.jsx";
import { EmptyState, Skeleton } from "../ui/Feedback.jsx";
// react-select is a third of the dashboard's code: the overview shows this picker, but the card does not wait for it.
const Select = lazy(() => import("../ui/Select.jsx").then((m) => ({ default: m.Select })));
import Button from "../ui/Button.jsx";
import Icon from "../ui/Icon.jsx";
import ProgressRing from "../ui/ProgressRing.jsx";
import Confetti from "../ui/Confetti.jsx";

// Daily (easy / medium / stretch) and weekly challenges for one player. Progress is computed by the API on every read;
// a completion is stored once and earns its bonus XP once. No countdowns, no loss framing: "Resets tomorrow".

const METRIC_ICON = { fullSwings: "bolt", kcal: "flame", sessions: "swords", activeMinutes: "timer", bestCombo: "activity", clearLevel: "trophy", clearDamage: "shield" };
const TIER_LABEL = { easy: "Easy", medium: "Medium", stretch: "Stretch" };
const SOURCE_LABEL = { data: "target from player data", fallback: "fallback target", fixed: "fixed target" };
const fmtValue = (c, v) => (c.metric === "kcal" ? (Math.round(v * 10) / 10).toString() : formatNumber(v));
const unit = (c) => ({ kcal: "kcal (est.)", fullSwings: "full swings", sessions: "fights", activeMinutes: "active min", bestCombo: "combo" })[c.metric] || "";

export function useChallenges(name) {
  const state = useApi(() => (name ? api.playerChallenges(name) : Promise.resolve(null)), [name]);
  const refresh = useThrottledCallback(state.reload, 2500);
  useLiveMessages((type, payload) => {
    const who = payload?.playerName || payload?.session?.playerName;
    if (who !== name) return;
    if (type === "challenge_done" || type === "session_finished") refresh();
  });
  const data = state.data && state.data.playerName === name ? state.data : null;
  return { ...state, data };
}

function ChallengeRow({ c, popped }) {
  const done = c.completed;
  return (
    <li className="flex items-center gap-4 px-5 py-4">
      <ProgressRing percent={done ? 100 : c.percent} done={done} label={done ? `${c.title}: completed` : `${c.title}: ${c.percent}%`}>
        {done ? (
          <span key={popped ? "pop" : "still"} className={`flex h-9 w-9 items-center justify-center rounded-full bg-[var(--good)] text-white ${popped ? "pb-pop" : ""}`}>
            <Icon name="check" className="h-5 w-5" strokeWidth={2.6} />
          </span>
        ) : (
          <span className="text-sm font-extrabold tabular-nums text-[var(--ink)]">{c.percent}%</span>
        )}
      </ProgressRing>
      <div className="min-w-0 flex-1">
        <p className="flex flex-wrap items-center gap-x-2 gap-y-1">
          <span className="t-kicker">{c.period === "weekly" ? "This week" : TIER_LABEL[c.tier] || "Today"}</span>
          <span className="inline-flex items-center gap-1 text-[0.95rem] font-bold text-[var(--ink)]">
            <Icon name={METRIC_ICON[c.metric] || "target"} className="h-4 w-4 text-[var(--accent-strong)]" />
            {c.title}
          </span>
        </p>
        <p className="mt-0.5 text-sm font-medium text-[var(--ink-secondary)]">{c.description}</p>
        <p className="mt-1 flex flex-wrap gap-x-3 gap-y-0.5 text-xs font-semibold text-[var(--muted)]">
          {c.metric === "clearLevel" || c.metric === "clearDamage" ? (
            <span>{c.met || done ? "Done" : "Not yet"}</span>
          ) : (
            <span className="tabular-nums">
              {fmtValue(c, c.value)} / {fmtValue(c, c.target)} {unit(c)}
            </span>
          )}
          <span>{done ? `+${formatNumber(c.bonusXp)} XP earned` : `+${formatNumber(c.bonusXp)} XP`}</span>
          <span>{c.resets}</span>
          <span title={c.median != null ? `Median ${c.median}` : undefined}>{SOURCE_LABEL[c.targetSource] || ""}</span>
        </p>
        {c.tracking && <p className="mt-1 text-xs font-medium text-[var(--muted)]">Combo is not recorded in these fights (older game build), so this can't move yet.</p>}
        {c.unmeasured && !done && <p className="mt-1 text-xs font-medium text-[var(--muted)]">Health lost wasn't recorded for these wins, so they can't count.</p>}
      </div>
    </li>
  );
}

function RestNote({ rest }) {
  if (!rest?.show) return null;
  return (
    <p className="flex items-start gap-2.5 border-b border-[var(--border)] bg-[var(--accent-soft)] px-5 py-3.5 text-sm font-semibold text-[var(--accent-strong)]" role="note">
      <Icon name="coffee" className="mt-0.5 h-4 w-4 shrink-0" />
      <span>{rest.text}</span>
    </p>
  );
}

function History({ name }) {
  const { data, error } = useApi(() => api.playerChallengeHistory(name, 30), [name]);
  const mine = data && data.playerName === name ? data : null;
  if (!mine) return <p className="px-5 py-4 text-xs font-medium text-[var(--muted)]">{error ? "History will appear when the server answers." : "Loading history…"}</p>;
  if (!mine.completions.length) {
    return <p className="px-5 py-4 text-sm font-medium text-[var(--muted)]">No challenges completed in the last 30 days yet. Completed ones are listed here with the XP they earned.</p>;
  }
  return (
    <div>
      <p className="px-5 pt-3 text-xs font-semibold text-[var(--muted)]">
        {mine.completedInRange} completed in the last 30 days · {formatNumber(mine.bonusXpInRange)} bonus XP · {mine.totalCompleted} in total
      </p>
      <ul className="divide-y divide-[var(--border)]">
        {mine.completions.map((c) => (
          <li key={c.key} className="flex items-center justify-between gap-3 px-5 py-2.5 text-sm">
            <span className="min-w-0">
              <span className="block truncate font-bold text-[var(--ink)]">{c.title}</span>
              <span className="block truncate text-xs font-medium text-[var(--muted)]">{c.description}</span>
            </span>
            <span className="shrink-0 text-right text-xs font-semibold text-[var(--muted)]">
              <span className="block font-bold text-[var(--good-ink)]">+{formatNumber(c.bonusXp)} XP</span>
              {formatRelative(c.completedAt)}
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}

// Confetti once per completion seen in this view: on a live completion (challenge_done / a refresh that completes more),
// or when the API recorded it on this very read (completedNow). Confetti itself does nothing under reduced motion / calm mode.
function useCompletionBurst(data) {
  const seen = useRef(null);
  const [burst, setBurst] = useState(0);
  const [popped, setPopped] = useState(new Set());
  useEffect(() => {
    if (!data) return;
    const cards = [...data.daily, ...data.weekly];
    const done = new Set(cards.filter((c) => c.completed).map((c) => c.key));
    const fresh = cards.filter((c) => c.completed && (c.completedNow || (seen.current && !seen.current.has(c.key)))).map((c) => c.key);
    seen.current = done;
    if (fresh.length) {
      setPopped(new Set(fresh));
      setBurst(Date.now());
    }
  }, [data]);
  return { burst, popped };
}

// `state`: an already loaded useChallenges(name) (the player page shares one with the Next step card).
export function ChallengesCard({ name, title = "Challenges", picker = null, showHistory = true, state = null }) {
  const own = useChallenges(state ? null : name);
  const { data, error } = state || own;
  const [history, setHistory] = useState(false);
  const { burst, popped } = useCompletionBurst(data);
  const cards = useMemo(() => (data ? [...data.daily, ...data.weekly] : []), [data]);

  return (
    <Card className="relative overflow-hidden" data-tour="challenges">
      <Confetti fire={burst} />
      <CardHeader
        title={title}
        subtitle={data ? `3 for today, 1 for the week · ${name ? shortName(name, 28) : ""}` : "3 for today, 1 for the week"}
        actions={picker}
      />
      {!name ? (
        <EmptyState icon="players" title="Pick a player" description="Challenges are per player: progress comes from that player's finished fights." />
      ) : !data ? (
        error ? (
          <EmptyState icon="target" title="Challenges will appear here" description="They load as soon as the server answers." />
        ) : (
          <div className="space-y-3 p-5" aria-hidden>
            <Skeleton className="h-16 w-full" />
            <Skeleton className="h-16 w-full" />
            <Skeleton className="h-16 w-full" />
          </div>
        )
      ) : (
        <>
          <RestNote rest={data.rest} />
          <ul className="divide-y divide-[var(--border)]">
            {cards.map((c) => (
              <ChallengeRow key={c.key} c={c} popped={popped.has(c.key)} />
            ))}
          </ul>
          <div className="space-y-1 border-t border-[var(--border)] px-5 py-3 text-xs font-medium text-[var(--muted)]">
            <p className="flex items-center gap-1.5 font-semibold text-[var(--ink-secondary)]">
              <Icon name="heart" className="h-3.5 w-3.5" /> {data.safety}
            </p>
            <p>
              {data.targets.daily.source === "data" ? `Targets scale from the typical day of ${data.targets.daily.playersUsed} regular players` : "Targets are fixed starting values until enough players have 3+ active days"}, capped for safety. {data.estimateNote} Days follow {data.timeZone}.
            </p>
          </div>
          {showHistory && (
            <div className="border-t border-[var(--border)]">
              <div className="flex items-center justify-between px-5 py-2.5">
                <p className="t-kicker">History</p>
                <Button icon={history ? "chevronUp" : "chevronDown"} onClick={() => setHistory((h) => !h)} aria-expanded={history}>
                  {history ? "Hide" : "Show last 30 days"}
                </Button>
              </div>
              {history && <History name={name} />}
            </div>
          )}
        </>
      )}
    </Card>
  );
}

// Overview: the same card with a player picker (defaults to whoever played most recently; the choice is remembered).
const PICK_KEY = "strikefit.challenges.player";
export function OverviewChallenges() {
  const players = useApi(() => api.players(), []);
  const [picked, setPicked] = useState(() => {
    try {
      return localStorage.getItem(PICK_KEY) || "";
    } catch {
      return "";
    }
  });
  const list = players.data || [];
  const recent = [...list].sort((a, b) => new Date(b.lastPlayedAt || 0) - new Date(a.lastPlayedAt || 0))[0]?.playerName || "";
  const name = list.some((p) => p.playerName === picked) ? picked : recent;
  const choose = (v) => {
    setPicked(v);
    try {
      localStorage.setItem(PICK_KEY, v);
    } catch {
      /* not remembered */
    }
  };
  const options = list.map((p) => ({ id: p.playerName, label: shortName(p.playerName, 28) })).sort((a, b) => a.label.localeCompare(b.label));
  return (
    <ChallengesCard
      name={name}
      title="Today's challenges"
      showHistory={false}
      picker={
        options.length ? (
          <Suspense fallback={<div className="h-9 w-40 rounded-lg border border-[var(--border)] bg-[var(--surface-muted)]" aria-hidden="true" />}>
            <Select label="Player" value={name} onChange={choose} options={options} />
          </Suspense>
        ) : null
      }
    />
  );
}
