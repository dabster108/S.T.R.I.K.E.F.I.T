import { FALLBACK_LOOK, RANK_LOOK } from "../../config/ranks.js";
import Icon from "../ui/Icon.jsx";

// A rank as a small pill: icon and name (or just the icon, with the name as its text alternative). rank = { key, name } from the rank API.
const SIZES = {
  xs: { box: "gap-1 px-1.5 py-0.5 text-[0.65rem]", icon: "h-3 w-3" },
  sm: { box: "gap-1.5 px-2 py-0.5 text-xs", icon: "h-3.5 w-3.5" },
  md: { box: "gap-1.5 px-3 py-1 text-sm", icon: "h-4 w-4" },
};

export default function RankBadge({ rank, size = "sm", iconOnly = false, className = "" }) {
  if (!rank) return null;
  const look = RANK_LOOK[rank.key] || FALLBACK_LOOK;
  const s = SIZES[size] || SIZES.sm;
  return (
    <span className={`inline-flex shrink-0 items-center rounded-full font-extrabold ring-1 ring-inset ${look.chip} ${s.box} ${className}`} title={`Rank: ${rank.name}`} data-testid="rank-badge" data-rank={rank.key}>
      <Icon name={look.icon} className={s.icon} strokeWidth={2.4} />
      {iconOnly ? <span className="sr-only">Rank: {rank.name}</span> : <span>{rank.name}</span>}
    </span>
  );
}
