import { FALLBACK_LOOK, RANK_LOOK } from "../../config/ranks.js";
import { rankSentence } from "../../lib/rank.js";
import Icon from "../ui/Icon.jsx";
import ProgressRing from "../ui/ProgressRing.jsx";

// The rank as a ring: how far through the current rung, with the rank's icon in the middle. `info` is the rank API's answer.
// The ring settles in once on mount (opacity and a small scale, the dashboard's own pop-in; nothing under reduced motion or Calm mode).
export default function RankRing({ info, size = "md", animate = true }) {
  const look = RANK_LOOK[info.rank.key] || FALLBACK_LOOK;
  return (
    <div className={animate ? "pop-in" : undefined}>
      <ProgressRing percent={info.progress * 100} done={!info.nextRank} size={size} label={rankSentence(info)}>
        <span className={`flex items-center justify-center rounded-full ${size === "lg" ? "h-14 w-14" : "h-9 w-9"} ${look.solid}`} aria-hidden>
          <Icon name={look.icon} className={size === "lg" ? "h-7 w-7" : "h-5 w-5"} strokeWidth={2.2} />
        </span>
      </ProgressRing>
    </div>
  );
}
