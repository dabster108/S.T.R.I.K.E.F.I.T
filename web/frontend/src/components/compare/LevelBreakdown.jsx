import { isNum } from "../../lib/compare.js";
import { formatNumber, formatPercent } from "../../lib/format.js";
import { LEVEL_KEYS, levelName } from "../../lib/game.js";
import { CompareBars } from "../charts/Charts.jsx";
import { LevelBadge } from "../ui/Badge.jsx";
import { Panel, SIDE_A, SIDE_B } from "./shared.jsx";

const speed = (v) => `${v.toFixed(1)} m/s`;
const share = (r) => (isNum(r) ? r * 100 : null);

/** Two players on each playable level: the same six figures per level, one block per level. */
export default function LevelBreakdown({ a, b, className = "" }) {
  const find = (s, level) => s.byLevel.find((l) => l.level === level);
  return (
    <Panel className={className} title="Level by level" subtitle="The same two players on each level. A level is a different fight (weapons, monster, HP), so compare within a level. Each row has its own scale.">
      <div className="grid gap-x-8 gap-y-6 md:grid-cols-2">
        {LEVEL_KEYS.map((level) => {
          const [la, lb] = [find(a, level), find(b, level)];
          const absent = [!la && a.label, !lb && b.label].filter(Boolean);
          return (
            <section key={level} aria-label={levelName(level)} className="min-w-0">
              <h3 className="flex items-center gap-2 text-sm font-bold text-[var(--ink)]">
                <LevelBadge level={level} />
                {levelName(level)}
              </h3>
              {la || lb ? (
                <>
                  <CompareBars
                    a={{ label: a.label, color: SIDE_A.color }}
                    b={{ label: b.label, color: SIDE_B.color }}
                    rows={[
                      { label: "Sessions", a: la?.sessions, b: lb?.sessions, fmt: formatNumber },
                      { label: "Win rate", a: la?.winRate, b: lb?.winRate, fmt: formatPercent },
                      { label: "Avg score", a: la?.avgScore, b: lb?.avgScore, fmt: formatNumber },
                      { label: "Best score", a: la?.bestScore, b: lb?.bestScore, fmt: formatNumber },
                      { label: "Avg swing speed", a: la?.avgSwingSpeed, b: lb?.avgSwingSpeed, fmt: speed },
                      { label: "Full power", a: share(la?.fullPowerRatio), b: share(lb?.fullPowerRatio), fmt: formatPercent },
                    ]}
                  />
                  {absent.length > 0 && <p className="mt-2 text-xs font-medium text-[var(--muted)]">{absent[0]} hasn't played this level.</p>}
                </>
              ) : (
                <p className="mt-3 rounded-xl border border-dashed border-[var(--border-strong)] bg-[var(--surface-muted)] px-4 py-6 text-center text-xs font-semibold text-[var(--muted)]">
                  Neither player has played {levelName(level)} yet.
                </p>
              )}
            </section>
          );
        })}
      </div>
    </Panel>
  );
}
