import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { api } from "../lib/api.js";
import { levelName } from "../lib/game.js";
import { formatKcal } from "../lib/fitness.js";
import { formatDuration, formatNumber, formatShortDate } from "../lib/format.js";
import { beatTips, cleanName, NAME_MAX, shortName } from "../lib/engage.js";
import { useApi } from "../hooks/useApi.js";
import { Brand } from "../components/landing/LandingNav.jsx";
import { buttonClass } from "../components/ui/Button.jsx";
import { EmptyState, Skeleton } from "../components/ui/Feedback.jsx";
import Icon from "../components/ui/Icon.jsx";
import Confetti from "../components/ui/Confetti.jsx";
import MonsterPortrait from "../components/ui/MonsterPortrait.jsx";

// strikefit.vercel.app/challenge/<sessionId>: a friend's stored fight, and a check whether a typed name has beaten it.
// The link carries only the session id; the score comes from the API, so it can't be forged in the URL. No accounts:
// the check matches the exact name typed on the headset.

const OUTCOME = { victory: "won", defeat: "lost", abandoned: "left", timeout: "ran out of time on" };

function Result({ r }) {
  if (!r) return null;
  if (!r.found) {
    return (
      <div className="mt-4 rounded-xl border border-[var(--border)] bg-[var(--surface-muted)] px-4 py-3 text-sm font-medium text-[var(--ink-secondary)]" role="status">
        No fights under <span className="font-bold text-[var(--ink)]">{shortName(r.name, 32)}</span> yet. Names must match the one picked on the headset exactly, capitals included.
      </div>
    );
  }
  if (r.beaten) {
    return (
      <div className="pop-in mt-4 rounded-xl border border-emerald-200 bg-[var(--good-soft)] px-4 py-3 text-sm font-semibold text-[var(--good-ink)]" role="status">
        <p className="flex items-center gap-2 text-base font-extrabold">
          <Icon name="trophy" className="h-5 w-5" /> Challenge beaten!
        </p>
        <p className="mt-1">
          {shortName(r.name, 32)}'s best on {levelName(r.level)} is {formatNumber(r.bestScore)}, {formatNumber(r.gap)} above the target.
        </p>
      </div>
    );
  }
  return (
    <div className="mt-4 rounded-xl border border-[var(--accent-ring)] bg-[var(--accent-soft)] px-4 py-3 text-sm font-semibold text-[var(--accent-strong)]" role="status">
      <p className="text-base font-extrabold">Not beaten yet, and that's fine.</p>
      <p className="mt-1">
        {r.sessionsOnLevel
          ? `${shortName(r.name, 32)}'s best on ${levelName(r.level)} so far is ${formatNumber(r.bestScore)} (${formatNumber(Math.abs(r.gap))} below the target). The tips below can help.`
          : `${shortName(r.name, 32)} hasn't fought ${levelName(r.level)} yet. Give it a go when you're ready.`}
      </p>
    </div>
  );
}

export default function ChallengeView() {
  const { sessionId } = useParams();
  const { data, error } = useApi(() => api.challengeCard(sessionId), [sessionId]);
  const [name, setName] = useState("");
  const [checking, setChecking] = useState(false);
  const [result, setResult] = useState(null);
  const [problem, setProblem] = useState(null);
  const [burst, setBurst] = useState(0);

  useEffect(() => {
    document.title = data?.session ? `Beat ${shortName(data.session.playerName, 20)} on ${levelName(data.session.level)} · STRIKEFIT` : "Challenge · STRIKEFIT";
  }, [data]);

  const s = data?.session;
  const check = async (e) => {
    e.preventDefault();
    const typed = cleanName(name, NAME_MAX);
    if (!typed) return setProblem("Type the name you use on the headset.");
    setChecking(true);
    setProblem(null);
    try {
      const r = await api.challengeCheck(sessionId, typed);
      setResult(r);
      if (r.beaten) setBurst(Date.now());
    } catch (err) {
      setProblem(err.status === 400 ? err.message : "Couldn't check right now. Try again in a moment.");
    } finally {
      setChecking(false);
    }
  };

  return (
    <div className="min-h-screen bg-[var(--bg)] text-[var(--ink)]">
      <header className="border-b border-[var(--border)] bg-[var(--surface)]">
        <div className="mx-auto flex max-w-5xl items-center justify-between gap-3 px-4 py-3 sm:px-6">
          <Link to="/" aria-label="STRIKEFIT home">
            <Brand />
          </Link>
          <Link to="/monsters" className={buttonClass("secondary", "sm")}>
            <Icon name="flame" className="h-3.5 w-3.5" /> The monsters
          </Link>
        </div>
      </header>
      <main id="main" className="mx-auto max-w-5xl px-4 py-8 sm:px-6 sm:py-12">
        {error ? (
          <div className="rounded-2xl border border-[var(--border)] bg-[var(--surface)]">
            <EmptyState
              icon="search"
              title={error.status === 404 ? "This challenge doesn't exist" : error.status === 409 ? "This fight is still being played" : "The challenge will appear here"}
              description={error.status === 404 ? "The fight may have been deleted. Ask your friend for a new link." : error.status === 409 ? "Open the link again once it has finished." : "It loads as soon as the server answers (a sleeping server can take a minute)."}
            />
          </div>
        ) : !s ? (
          <div className="grid gap-8 lg:grid-cols-12">
            <Skeleton className="h-80 w-full lg:col-span-5" />
            <Skeleton className="h-80 w-full lg:col-span-7" />
          </div>
        ) : (
          <div className="grid items-start gap-8 lg:grid-cols-12 lg:gap-12">
            <div className="flex justify-center lg:col-span-5">
              <MonsterPortrait level={s.level} size="md" eager className="max-w-full" />
            </div>
            <div className="min-w-0 lg:col-span-7">
              <p className="t-eyebrow">Challenge a friend</p>
              <h1 className="t-h1 mt-2 text-[var(--ink)]">
                Beat {shortName(s.playerName, 28)} on {levelName(s.level)}
              </h1>
              <p className="mt-2 text-sm font-medium text-[var(--muted)]">
                {shortName(s.playerName, 28)} {OUTCOME[s.outcome] || "fought"} {levelName(s.level)} on {formatShortDate(s.startedAt)}.
              </p>

              <dl className="mt-6 grid grid-cols-2 gap-px overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--border)] sm:grid-cols-4">
                {[
                  ["Score to beat", formatNumber(s.score)],
                  ["Fight length", formatDuration(s.durationSec)],
                  ["Energy", data.estimatedCalories != null ? `${formatKcal(data.estimatedCalories)}` : "—", data.estimatedCalories != null ? "estimated" : "no swings recorded"],
                  ["Full-power swings", formatNumber(data.fullSwings)],
                ].map(([k, v, sub]) => (
                  <div key={k} className="bg-[var(--surface)] px-4 py-3">
                    <dt className="t-kicker">{k}</dt>
                    <dd className="mt-1 text-xl font-extrabold tabular-nums tracking-tight text-[var(--ink)]">{v}</dd>
                    {sub && <dd className="text-[0.7rem] font-semibold text-[var(--muted)]">{sub}</dd>}
                  </div>
                ))}
              </dl>

              <form onSubmit={check} className="relative mt-6 rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-5 shadow-[var(--shadow-card)]">
                <Confetti fire={burst} />
                <label htmlFor="challenger" className="text-sm font-bold text-[var(--ink)]">
                  Already played? Check your name
                </label>
                <p className="mt-0.5 text-xs font-medium text-[var(--muted)]">Looks for any of your finished fights on {levelName(s.level)} with a higher score.</p>
                <div className="mt-3 flex flex-wrap gap-2">
                  <input
                    id="challenger"
                    value={name}
                    onChange={(e) => setName(e.target.value.slice(0, NAME_MAX))}
                    maxLength={NAME_MAX}
                    autoComplete="nickname"
                    placeholder="Your headset name"
                    className="min-w-0 flex-1 rounded-lg border border-[var(--border)] bg-[var(--surface)] px-3 py-2 text-sm font-semibold text-[var(--ink)] focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-ring)]"
                  />
                  <button type="submit" className={buttonClass("primary", "md")} disabled={checking}>
                    {checking ? "Checking…" : "Check"}
                  </button>
                </div>
                {problem && (
                  <p role="alert" className="mt-2 text-sm font-semibold text-[var(--bad)]">
                    {problem}
                  </p>
                )}
                <Result r={result} />
              </form>

              <section className="mt-8" aria-labelledby="tips-title">
                <h2 id="tips-title" className="text-lg font-extrabold tracking-tight text-[var(--ink)]">
                  How to beat this score
                </h2>
                <ul className="mt-3 space-y-2.5">
                  {beatTips(s.level).map((tip) => (
                    <li key={tip} className="flex gap-2.5 text-sm font-medium leading-relaxed text-[var(--ink-secondary)]">
                      <Icon name="check" className="mt-0.5 h-4 w-4 shrink-0 text-[var(--accent-strong)]" />
                      <span>{tip}</span>
                    </li>
                  ))}
                </ul>
                <p className="mt-4 flex items-center gap-1.5 text-sm font-semibold text-[var(--ink-secondary)]">
                  <Icon name="heart" className="h-4 w-4" /> Stop if you feel dizzy or in pain.
                </p>
                <p className="mt-2 text-xs font-medium text-[var(--muted)]">
                  Tips come from the game's design notes; tuning is still in progress. kcal is an estimate from fight length and swing strength, not a measurement.
                </p>
              </section>
            </div>
          </div>
        )}
      </main>
    </div>
  );
}
