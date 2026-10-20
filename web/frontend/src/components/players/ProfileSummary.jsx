import { Link } from "react-router-dom";
import { api } from "../../lib/api.js";
import { useApi } from "../../hooks/useApi.js";
import { FADE_TONE, formatFade, speakFade } from "../../lib/effort.js";
import { formatKcal } from "../../lib/fitness.js";
import { levelName } from "../../lib/game.js";
import { formatDuration, formatNumber, formatShortDate } from "../../lib/format.js";
import { Skeleton } from "../ui/Feedback.jsx";
import Icon from "../ui/Icon.jsx";
import RadarChart from "../effort/RadarChart.jsx";

// The player at a glance, right under their name: the effort fingerprint as a small radar, the last-10-fights fade, personal
// records and badges. Every figure links to (or sits above) the full card further down the page; nothing here is computed
// again, it is the same API data. Fixed height while loading, so the page doesn't jump.

const MIN_H = 300;
const Kicker = ({ children }) => <p className="t-kicker">{children}</p>;

const RECORDS = [
  ["highestScore", "Highest score", (b) => `${formatNumber(b.value)} pts`],
  ["highestSwingVelocity", "Fastest swing", (b) => `${b.value.toFixed(1)} m/s`],
  ["longestSession", "Longest fight", (b) => formatDuration(b.value)],
  ["highestEstimatedCalories", "Most energy (est.)", (b) => formatKcal(b.value)],
];

// In-page jump that works while lazy cards above the target are still settling: scroll on the next frame, to the element itself.
const jumpTo = (id) => (e) => {
  e.preventDefault();
  requestAnimationFrame(() => document.getElementById(id)?.scrollIntoView({ block: "start" }));
};

function Column({ children, className = "" }) {
  return <div className={`min-w-0 bg-[var(--surface)] p-5 ${className}`}>{children}</div>;
}

export default function ProfileSummary({ name, bests }) {
  const fp = useApi(() => api.playerFingerprint(name), [name]);
  const fade = useApi(() => api.playerFade(name, 10), [name]);
  const ach = useApi(() => api.playerAchievements(name), [name]);
  const axes = fp.data?.status === "ok" ? fp.data.axes.filter((a) => a.score !== null) : null;
  const avg = fade.data?.playerName === name ? fade.data.average : null;
  const unlocked = (ach.data?.badges || []).filter((b) => b.unlocked).sort((a, b) => new Date(b.unlockedAt) - new Date(a.unlockedAt));
  const records = RECORDS.filter(([k]) => bests?.[k]);

  return (
    <section aria-label={`${name} at a glance`} className="mt-6 overflow-hidden rounded-2xl border border-[var(--border)] shadow-[var(--shadow-card)]" style={{ minHeight: MIN_H }}>
      <div className="grid gap-px bg-[var(--border)] md:grid-cols-2 xl:grid-cols-[minmax(0,1.1fr)_minmax(0,0.9fr)_minmax(0,1.2fr)]">
        <Column>
          <div className="flex items-center justify-between gap-2">
            <Kicker>Effort fingerprint</Kicker>
            <a href="#player-fingerprint" onClick={jumpTo("player-fingerprint")} className="press rounded text-xs font-semibold text-[var(--accent)] hover:underline">
              How it's scored
            </a>
          </div>
          {!fp.data && !fp.error ? (
            <Skeleton className="mx-auto mt-4 h-48 w-56 rounded-full" />
          ) : axes && axes.length >= 3 ? (
            <div className="mt-1 flex justify-center">
              <RadarChart axes={axes} size={220} scale={0.82} caption={`${name}'s effort fingerprint`} />
            </div>
          ) : (
            <p className="mt-4 text-sm font-medium text-[var(--muted)]">{fp.error ? "Not available right now." : "Not enough swings yet: it appears after a few fights."}</p>
          )}
        </Column>

        <Column>
          <Kicker>Fade · last 10 fights</Kicker>
          {!fade.data && !fade.error ? (
            <Skeleton className="mt-4 h-24 w-full rounded-xl" />
          ) : avg?.status === "ok" ? (
            <>
              <p className={`mt-3 text-4xl font-extrabold tabular-nums tracking-tight ${FADE_TONE[avg.label].text}`} aria-label={`Average fade ${speakFade(avg.fadePct)}`}>
                {formatFade(avg.fadePct)}
              </p>
              <p className={`mt-2 inline-flex rounded-full px-2.5 py-1 text-xs font-extrabold ring-1 ring-inset ${FADE_TONE[avg.label].chip}`}>{avg.labelText}</p>
              <p className="mt-3 text-xs font-medium text-[var(--muted)]">
                Average of {avg.sessionsUsed} of {avg.sessionsConsidered} fights with enough swings ·{" "}
                <a href="#player-fade" onClick={jumpTo("player-fade")} className="press rounded font-semibold text-[var(--accent)] hover:underline">
                  each fight
                </a>
              </p>
            </>
          ) : (
            <p className="mt-4 text-sm font-medium text-[var(--muted)]">{fade.error ? "Not available right now." : "Not enough swings yet: fade needs 6 or more per fight."}</p>
          )}
        </Column>

        <Column className="md:col-span-2 xl:col-span-1">
          <Kicker>Personal records</Kicker>
          {records.length ? (
            <ul className="mt-3 grid grid-cols-2 gap-x-4 gap-y-3">
              {records.map(([key, label, fmt]) => (
                <li key={key} className="min-w-0">
                  <Link to={`/dashboard/session/${encodeURIComponent(bests[key].sessionId)}`} className="press group block rounded">
                    <span className="block text-xs font-semibold text-[var(--muted)]">{label}</span>
                    <span className="block truncate text-lg font-extrabold tabular-nums tracking-tight text-[var(--ink)] group-hover:text-[var(--accent)]">{fmt(bests[key])}</span>
                    <span className="block truncate text-xs font-medium text-[var(--muted)]">
                      {levelName(bests[key].level)} · {formatShortDate(bests[key].date)}
                    </span>
                  </Link>
                </li>
              ))}
            </ul>
          ) : (
            <p className="mt-3 text-sm font-medium text-[var(--muted)]">Records appear after a finished fight with swings.</p>
          )}
          <div className="mt-4 border-t border-[var(--border)] pt-3">
            <div className="flex items-center justify-between gap-2">
              <Kicker>Badges</Kicker>
              {ach.data && (
                <span className="text-xs font-bold tabular-nums text-[var(--ink-secondary)]">
                  {ach.data.badgesUnlocked} of {ach.data.badgesTotal}
                </span>
              )}
            </div>
            {!ach.data && !ach.error ? (
              <Skeleton className="mt-2 h-7 w-full rounded-full" />
            ) : unlocked.length ? (
              <ul className="mt-2 flex flex-wrap gap-1.5">
                {unlocked.slice(0, 5).map((b) => (
                  <li key={b.id} className="inline-flex items-center gap-1 rounded-full bg-[var(--warn-soft)] px-2.5 py-1 text-xs font-bold text-[var(--warn-ink)] ring-1 ring-inset ring-amber-200">
                    <Icon name="medal" className="h-3 w-3" /> {b.name}
                  </li>
                ))}
                {unlocked.length > 5 && <li className="px-1 py-1 text-xs font-semibold text-[var(--muted)]">+{unlocked.length - 5} more</li>}
              </ul>
            ) : (
              <p className="mt-2 text-sm font-medium text-[var(--muted)]">{ach.error ? "Not available right now." : "No badge yet."}</p>
            )}
          </div>
        </Column>
      </div>
    </section>
  );
}
