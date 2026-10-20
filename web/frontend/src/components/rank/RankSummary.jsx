import { useRank } from "../../hooks/useRank.js";
import { formatNumber } from "../../lib/format.js";
import { Skeleton } from "../ui/Feedback.jsx";
import RankBadge from "./RankBadge.jsx";
import { rankSentence } from "../../lib/rank.js";
import RankRing from "./RankRing.jsx";

// The player page header's rank: the ring, the rank, the points and what is left to the next rung. Loading, error and
// "no fights yet" each have their own small state; nothing here is ever invented.
export default function RankSummary({ name }) {
  const { data, loading, error, notFound } = useRank(name);
  // every state reserves the size of the finished card (about 15.5 x 6.8 rem), so the rank arriving moves nothing below it
  const frame = "flex min-h-[6.8125rem] min-w-[15.5rem] items-center gap-4 rounded-2xl border border-[var(--border)] bg-[var(--surface)] px-4 py-3 shadow-[var(--shadow-card)]";
  if (loading)
    return (
      <div className={frame} aria-hidden>
        <Skeleton className="h-[4.5rem] w-[4.5rem] rounded-full" />
        <div className="space-y-2">
          <Skeleton className="h-4 w-24" />
          <Skeleton className="h-3 w-36" />
        </div>
      </div>
    );
  if (notFound) return <div className={frame}><p className="text-sm font-medium text-[var(--muted)]">No rank yet: it starts with the first fight.</p></div>;
  if (error && !data) return <div className={frame}><p className="text-sm font-medium text-[var(--muted)]">The rank will appear here when the server answers.</p></div>;
  if (!data) return null;
  return (
    <section className={frame} aria-label="Rank" data-testid="rank-summary">
      <RankRing info={data} />
      <div className="min-w-0">
        <p className="t-kicker">Rank</p>
        <p className="mt-0.5 flex flex-wrap items-center gap-2">
          <RankBadge rank={data.rank} size="md" />
        </p>
        <p className="mt-1 text-sm font-semibold tabular-nums text-[var(--ink)]">
          {formatNumber(data.effortPoints)} <span className="font-medium text-[var(--muted)]">effort points</span>
        </p>
        <p className="text-xs font-medium text-[var(--muted)]">
          {data.nextRank ? `${formatNumber(data.pointsToNext)} to ${data.nextRank.name}` : "Top rank reached"}
        </p>
        <p className="sr-only">{rankSentence(data)}. {data.rule}</p>
      </div>
    </section>
  );
}
