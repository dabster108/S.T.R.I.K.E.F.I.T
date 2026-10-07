import { useEffect, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { api } from "../lib/api.js";
import { levelName } from "../lib/game.js";
import { formatDateTime, formatDuration, formatNumber } from "../lib/format.js";
import { useApi } from "../hooks/useApi.js";
import { Brand } from "../components/landing/LandingNav.jsx";
import { Card } from "../components/ui/Card.jsx";
import { EmptyState, Skeleton } from "../components/ui/Feedback.jsx";
import { LevelBadge, OutcomeBadge } from "../components/ui/Badge.jsx";
import { SearchInput } from "../components/ui/Controls.jsx";
import Button, { buttonClass } from "../components/ui/Button.jsx";
import Icon from "../components/ui/Icon.jsx";
import RatingForm from "../components/feedback/RatingForm.jsx";

// strikefit.vercel.app/feedback: a public, phone-first page where a player rates a finished session.
// Pick your name, pick the session, answer two questions. Skippable.

function PickPlayer({ onPick }) {
  const players = useApi(() => api.players(), []);
  const [q, setQ] = useState("");
  const list = useMemo(() => {
    const term = q.trim().toLowerCase();
    return (players.data || []).filter((p) => !term || p.playerName.toLowerCase().includes(term)).slice(0, 12);
  }, [players.data, q]);

  return (
    <div>
      <h2 className="text-lg font-extrabold tracking-tight text-[var(--ink)]">Who played?</h2>
      <div className="mt-3">
        <SearchInput value={q} onChange={setQ} placeholder="Search your name" label="Search players" />
      </div>
      <div className="mt-4">
        {players.loading && !players.data ? (
          <div className="space-y-2" aria-busy>
            {[0, 1, 2].map((i) => (
              <Skeleton key={i} className="h-12 w-full" />
            ))}
          </div>
        ) : players.error && !players.data ? (
          <p role="alert" className="rounded-xl border border-red-200 bg-[var(--bad-soft)] px-3 py-2.5 text-sm font-semibold text-[var(--bad)]">
            Couldn't load players. The server may be waking up, so give it a moment and{" "}
            <button type="button" className="underline" onClick={players.reload}>
              try again
            </button>
            .
          </p>
        ) : list.length ? (
          <ul className="grid gap-2">
            {list.map((p) => (
              <li key={p.playerName}>
                <button
                  type="button"
                  onClick={() => onPick(p.playerName)}
                  className="press flex min-h-12 w-full items-center justify-between rounded-xl border border-[var(--border)] bg-[var(--surface)] px-4 text-left text-sm font-bold text-[var(--ink)] hover:border-[var(--border-strong)] hover:bg-[var(--surface-muted)]"
                >
                  {p.playerName}
                  <Icon name="chevronRight" className="h-4 w-4 text-[var(--muted)]" />
                </button>
              </li>
            ))}
          </ul>
        ) : (
          <EmptyState icon="players" title="No player found" description="Check the spelling, or ask for the name shown in the headset." />
        )}
      </div>
    </div>
  );
}

function PickSession({ player, onPick, onBack }) {
  const sessions = useApi(() => api.sessions({ player, limit: 12 }), [player]);
  const list = (sessions.data || []).filter((s) => s.status !== "live").slice(0, 6);
  return (
    <div>
      <button type="button" onClick={onBack} className="press inline-flex items-center gap-1 rounded text-xs font-semibold text-[var(--accent)] hover:underline">
        <Icon name="chevronLeft" className="h-3 w-3" strokeWidth={2.4} /> Not {player}?
      </button>
      <h2 className="mt-2 text-lg font-extrabold tracking-tight text-[var(--ink)]">Which session?</h2>
      <p className="mt-1 text-sm font-medium text-[var(--muted)]">{player}'s latest sessions, newest first.</p>
      <div className="mt-4">
        {sessions.loading && !sessions.data ? (
          <div className="space-y-2" aria-busy>
            {[0, 1, 2].map((i) => (
              <Skeleton key={i} className="h-16 w-full" />
            ))}
          </div>
        ) : list.length ? (
          <ul className="grid gap-2">
            {list.map((s) => (
              <li key={s.id}>
                <button
                  type="button"
                  onClick={() => onPick(s)}
                  className="press flex min-h-14 w-full flex-wrap items-center justify-between gap-2 rounded-xl border border-[var(--border)] bg-[var(--surface)] px-4 py-3 text-left hover:border-[var(--border-strong)] hover:bg-[var(--surface-muted)]"
                >
                  <span className="min-w-0">
                    <span className="flex items-center gap-2 text-sm font-bold text-[var(--ink)]">
                      <LevelBadge level={s.level} /> {levelName(s.level)}
                    </span>
                    <span className="mt-0.5 block text-xs font-medium text-[var(--muted)]">
                      {formatDateTime(s.startedAt)} · {formatDuration(s.durationSec)} · {formatNumber(s.score)} pts
                    </span>
                  </span>
                  <OutcomeBadge outcome={s.outcome} />
                </button>
              </li>
            ))}
          </ul>
        ) : (
          <EmptyState icon="sessions" title="No finished sessions yet" description="Finish a fight in the headset, then come back to rate it." />
        )}
      </div>
    </div>
  );
}

export default function FeedbackView() {
  const [params] = useSearchParams();
  const [player, setPlayer] = useState(() => (params.get("player") || "").trim() || null);
  const [session, setSession] = useState(null);
  const [done, setDone] = useState(null); // "saved" | "skipped"

  useEffect(() => {
    document.title = "Rate your session · STRIKEFIT";
  }, []);

  const restart = () => {
    setPlayer(null);
    setSession(null);
    setDone(null);
  };

  return (
    <div className="min-h-screen bg-[var(--bg)] text-[var(--ink)]">
      <header className="border-b border-[var(--border)] bg-[var(--surface)]">
        <div className="mx-auto flex max-w-xl items-center justify-between gap-3 px-4 py-3">
          <Link to="/" aria-label="STRIKEFIT home">
            <Brand />
          </Link>
        </div>
      </header>
      <main className="mx-auto max-w-xl px-4 py-6 sm:py-10">
        <p className="t-eyebrow">Research</p>
        <h1 className="t-h1 mt-1 text-[var(--ink)]">Rate your session</h1>
        <p className="mt-1.5 text-sm font-medium leading-relaxed text-[var(--muted)]">Two quick questions about how it felt. It takes under a minute, and you can skip it.</p>

        <Card className="mt-6 p-5 sm:p-6">
          {done === "saved" ? (
            <div className="flex flex-col items-center py-6 text-center" role="status">
              <span className="pop-in flex h-14 w-14 items-center justify-center rounded-full bg-[var(--good-soft)] text-[var(--good-ink)]">
                <Icon name="check" className="h-7 w-7" strokeWidth={2.6} />
              </span>
              <h2 className="mt-4 text-lg font-extrabold tracking-tight">Thank you</h2>
              <p className="mt-1 max-w-xs text-sm font-medium text-[var(--muted)]">Your rating is saved with the session. It helps show whether the game really gets people moving.</p>
              <div className="mt-5 flex flex-wrap justify-center gap-2">
                <Button size="md" onClick={restart}>
                  Rate another session
                </Button>
                <Link to="/" className={buttonClass("ghost", "md")}>
                  Back to STRIKEFIT
                </Link>
              </div>
            </div>
          ) : done === "skipped" ? (
            <div className="flex flex-col items-center py-6 text-center" role="status">
              <h2 className="text-lg font-extrabold tracking-tight">No problem</h2>
              <p className="mt-1 max-w-xs text-sm font-medium text-[var(--muted)]">Nothing was saved. You can come back to rate this session any time.</p>
              <Link to="/" className={`${buttonClass("secondary", "md")} mt-5`}>
                Back to STRIKEFIT
              </Link>
            </div>
          ) : !player ? (
            <PickPlayer onPick={setPlayer} />
          ) : !session ? (
            <PickSession player={player} onPick={setSession} onBack={restart} />
          ) : (
            <div>
              <button type="button" onClick={() => setSession(null)} className="press inline-flex items-center gap-1 rounded text-xs font-semibold text-[var(--accent)] hover:underline">
                <Icon name="chevronLeft" className="h-3 w-3" strokeWidth={2.4} /> Different session
              </button>
              <p className="mt-2 flex flex-wrap items-center gap-2 text-sm font-bold text-[var(--ink)]">
                {player} <LevelBadge level={session.level} /> <span className="font-medium text-[var(--muted)]">{formatDateTime(session.startedAt)}</span>
              </p>
              <div className="mt-5">
                <RatingForm sessionId={session.id} onSaved={() => setDone("saved")} onSkip={() => setDone("skipped")} />
              </div>
            </div>
          )}
        </Card>
      </main>
    </div>
  );
}
