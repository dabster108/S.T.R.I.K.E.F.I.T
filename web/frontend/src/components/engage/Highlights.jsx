import { Link } from "react-router-dom";
import { highlights } from "../../lib/highlights.js";
import { levelName } from "../../lib/game.js";
import { formatNumber, formatRelative } from "../../lib/format.js";
import { LevelBadge } from "../ui/Badge.jsx";
import { buttonClass } from "../ui/Button.jsx";
import { Card } from "../ui/Card.jsx";
import { EmptyState } from "../ui/Feedback.jsx";
import Icon from "../ui/Icon.jsx";

// Overview "Highlights": best run, longest win streak and the most played level, all computed from the recorded sessions
// (lib/highlights.js). With no fights at all the row becomes one honest empty state with the next step; a single missing
// highlight shows a dash and what would fill it.
function Tile({ icon, label, children, hint, onClick }) {
  const body = (
    <>
      <span className="flex items-center gap-2 t-kicker">
        <Icon name={icon} className="h-3.5 w-3.5" /> {label}
      </span>
      <span className="mt-2 block text-2xl font-extrabold tabular-nums tracking-tight text-[var(--ink)]">{children}</span>
      <span className="mt-1 block truncate text-xs font-semibold text-[var(--muted)]">{hint}</span>
    </>
  );
  const cls = "block w-full rounded-2xl border border-[var(--border)] bg-[var(--surface)] px-5 py-4 text-left shadow-[var(--shadow-card)]";
  return onClick ? (
    <button type="button" onClick={onClick} className={`press ${cls} hover:border-[var(--border-strong)] hover:bg-[var(--surface-muted)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--accent)]`}>
      {body}
    </button>
  ) : (
    <div className={cls}>{body}</div>
  );
}

export default function Highlights({ sessions, openSession }) {
  if (!sessions.length) {
    return (
      <Card>
        <EmptyState
          icon="trophy"
          title="No fights recorded yet"
          description="Finish a fight in the headset and your best run, longest streak and favourite level show up here."
          action={
            <div className="flex flex-wrap justify-center gap-2">
              <Link to="/dashboard/guide" className={buttonClass("primary", "md")}>
                How to play
              </Link>
              <Link to="/dashboard/live" className={buttonClass("secondary", "md")}>
                Open Live Arena
              </Link>
            </div>
          }
        />
      </Card>
    );
  }
  const { best, streak, level } = highlights(sessions);
  return (
    <div className="stagger grid gap-4 sm:grid-cols-3">
      <Tile
        icon="trophy"
        label="Best run"
        onClick={best ? () => openSession(best.id) : undefined}
        hint={best ? `${best.playerName} · ${levelName(best.level)} · ${formatRelative(best.endedAt)}` : "Finish a fight with a score"}
      >
        {best ? formatNumber(best.score) : "—"}
      </Tile>
      <Tile
        icon="flame"
        label="Longest win streak"
        hint={streak ? `${streak.player}, ${streak.length === 1 ? "one win" : `${streak.length} wins in a row`}` : "Win a fight to start one"}
      >
        {streak ? streak.length : "—"}
      </Tile>
      <Tile
        icon="target"
        label="Most played level"
        hint={level ? `${formatNumber(level.count)} fight${level.count === 1 ? "" : "s"}` : "Play any level"}
      >
        {level ? (
          <span className="flex items-center gap-2">
            <LevelBadge level={level.level} /> {levelName(level.level)}
          </span>
        ) : (
          "—"
        )}
      </Tile>
    </div>
  );
}
