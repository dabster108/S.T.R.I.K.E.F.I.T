import { useMemo } from "react";
import { Link } from "react-router-dom";
import { api } from "../../../lib/api.js";
import { useApi } from "../../../hooks/useApi.js";
import { useLiveMessages } from "../../../live/LiveProvider.jsx";
import { LEVEL_KEYS, resultLabel } from "../../../lib/arena.js";
import { monsterFor } from "../../../lib/arenaApp.js";
import { formatDuration, formatNumber, formatRelative } from "../../../lib/format.js";
import { Skeleton } from "../../ui/Feedback.jsx";
import MonsterPortrait from "../../ui/MonsterPortrait.jsx";
import { OutcomeBadge } from "../../ui/Badge.jsx";
import Icon from "../../ui/Icon.jsx";

// The arena with nobody in it: not an empty page, but the three monsters waiting and the last fight that finished.

function MonsterTile({ levelKey }) {
  const m = monsterFor(levelKey);
  return (
    <li className="min-w-0 overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--surface)] shadow-[var(--shadow-card)]">
      <div className="flex items-end justify-center bg-gradient-to-b from-[var(--accent-soft)] to-[var(--surface)] px-4 pt-4">
        <MonsterPortrait level={levelKey} size="md" glow={false} className="!w-full max-w-[12.5rem]" />
      </div>
      <div className="p-4">
        <p className="text-xs font-bold text-[var(--muted)]">
          {m.levelName} · {m.difficulty}
        </p>
        <h3 className="mt-1 text-lg font-extrabold leading-tight tracking-tight text-[var(--ink)]">{m.name}</h3>
        <p className="mt-1 text-sm font-semibold text-[var(--ink-secondary)]">{m.weapon}</p>
        {m.tips[0] && <p className="mt-2 text-xs font-medium leading-relaxed text-[var(--muted)]">{m.tips[0]}</p>}
      </div>
    </li>
  );
}

function LastFight() {
  const { data, error, reload } = useApi(() => api.sessions({ status: "finished", limit: 12 }), []);
  useLiveMessages((type) => type === "session_finished" && reload()); // a fight that just ended becomes "the last fight"
  const last = useMemo(() => (data || []).find((s) => s.status === "finished" && s.outcome !== "in_progress"), [data]);
  if (error && !data) return <p className="text-sm font-medium text-[var(--muted)]">The last fight will show here once the server answers.</p>;
  if (!data) return <Skeleton className="h-20 w-full rounded-xl" />;
  if (!last) return <p className="text-sm font-medium text-[var(--muted)]">No fight has finished yet.</p>;
  const m = monsterFor(last.level);
  const r = resultLabel(last.outcome);
  return (
    <Link to={`/dashboard/session/${encodeURIComponent(last.id)}`} className="press group flex flex-wrap items-center gap-x-6 gap-y-3 rounded-xl border border-[var(--border)] bg-[var(--surface)] p-4 hover:border-[var(--accent-ring)]">
      <div className="min-w-0 flex-1">
        <p className="text-xs font-bold uppercase tracking-wider text-[var(--muted)]">Last fight · {formatRelative(last.endedAt)}</p>
        <p className="mt-1 truncate text-lg font-extrabold tracking-tight text-[var(--ink)]">
          {last.playerName} <span className="text-[var(--muted)]">vs</span> {m ? m.name : last.level}
        </p>
      </div>
      <OutcomeBadge outcome={last.outcome} />
      <dl className="flex gap-6 text-sm">
        <div>
          <dt className="text-xs font-semibold text-[var(--muted)]">Score</dt>
          <dd className="font-extrabold tabular-nums text-[var(--ink)]">{formatNumber(last.score)}</dd>
        </div>
        <div>
          <dt className="text-xs font-semibold text-[var(--muted)]">Length</dt>
          <dd className="font-extrabold tabular-nums text-[var(--ink)]">{formatDuration(last.durationSec)}</dd>
        </div>
      </dl>
      <Icon name="chevronRight" className="h-4 w-4 text-[var(--muted)] group-hover:text-[var(--accent)]" />
      <span className="sr-only">{r.title}</span>
    </Link>
  );
}

export default function QuietArena() {
  return (
    <div className="space-y-6">
      <section className="rounded-2xl border border-dashed border-[var(--border-strong)] bg-[var(--surface)] px-6 py-8 text-center sm:py-10" aria-label="The arena is quiet">
        <span className="mx-auto flex h-12 w-12 items-center justify-center rounded-2xl bg-[var(--accent-soft)] text-[var(--accent)] ring-1 ring-[var(--accent-ring)]" aria-hidden>
          <Icon name="headset" className="h-6 w-6" />
        </span>
        <h1 className="mt-4 text-3xl font-extrabold tracking-tight text-[var(--ink)] sm:text-4xl">Arena is quiet</h1>
        <p className="mx-auto mt-2 max-w-xl text-base font-medium text-[var(--muted)]">
          Nobody is fighting right now. When a player starts a fight in the headset, it appears here at once: who they face, both health bars and every swing.
        </p>
      </section>

      <section aria-label="The three monsters">
        <h2 className="mb-3 text-xs font-extrabold uppercase tracking-wider text-[var(--ink-secondary)]">Waiting in the arena</h2>
        <ul className="grid gap-4 sm:grid-cols-3">
          {LEVEL_KEYS.map((k) => (
            <MonsterTile key={k} levelKey={k} />
          ))}
        </ul>
      </section>

      <section aria-label="The last finished fight">
        <LastFight />
      </section>
    </div>
  );
}
