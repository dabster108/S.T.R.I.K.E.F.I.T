import Icon from "../ui/Icon.jsx";
import RankBadge from "./RankBadge.jsx";

// "Rank up": the session_finished message's rankUp field, { from, to, effortPoints }. Nothing is rendered without a rise.
export default function RankUpBanner({ rankUp, className = "" }) {
  if (!rankUp || !rankUp.to) return null;
  return (
    <section className={`flex flex-wrap items-center gap-3 rounded-2xl border border-[var(--accent-ring)] bg-[var(--surface)] px-5 py-3 ${className}`} role="status" aria-label={`Rank up: ${rankUp.from.name} to ${rankUp.to.name}`} data-testid="rank-up">
      <Icon name="award" className="h-5 w-5 text-[var(--accent)]" />
      <p className="text-sm font-bold text-[var(--ink)]">Rank up</p>
      <span className="flex items-center gap-2">
        <RankBadge rank={rankUp.from} size="sm" />
        <Icon name="arrowRight" className="h-3.5 w-3.5 text-[var(--muted)]" />
        <RankBadge rank={rankUp.to} size="md" />
      </span>
    </section>
  );
}
