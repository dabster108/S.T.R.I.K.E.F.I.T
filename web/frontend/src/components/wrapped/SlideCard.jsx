import { CARD } from "../../config/wrapped.js";
import { MONSTER_IMAGES } from "../../lib/monsters.js";
import { weekLabel } from "../../lib/wrapped.js";
import { Reveal } from "../../motion/index.js";
import RadarChart from "../effort/RadarChart.jsx";
import RankBadge from "../rank/RankBadge.jsx";
import RankRing from "../rank/RankRing.jsx";
import { formatNumber } from "../../lib/format.js";

// One Wrapped slide as a 1080 x 1350 card. On screen it is scaled down to the page (a static transform) and its parts rise in with the
// shared <Reveal> motion; the PNG is rendered from an identical card with `animate` off, so nothing is captured half way through an entrance.
// Inline styles on purpose, as the other poster cards do: the PNG is made from this exact node. The card shows the pseudonym only.

// the big value shrinks with its length so a name like "The Harbor Tyrant" stays on one line of the 1080 px card
const valueSize = (v) => (v.length <= 6 ? 250 : v.length <= 9 ? 190 : v.length <= 12 ? 140 : v.length <= 17 ? 100 : 84);

function Part({ animate, index, children, style }) {
  return animate ? (
    <Reveal index={index} style={style}>
      {children}
    </Reveal>
  ) : (
    <div style={style}>{children}</div>
  );
}

export default function SlideCard({ slide, data, monsterLevel, rank, animate = true, nodeRef }) {
  const center = { display: "flex", flexDirection: "column", alignItems: "center", textAlign: "center" };
  const image = slide.id === "monster" && monsterLevel ? MONSTER_IMAGES[monsterLevel] : null;
  return (
    <div
      ref={nodeRef}
      style={{ position: "relative", width: CARD.w, height: CARD.h, boxSizing: "border-box", padding: "70px 84px 56px", background: "#ffffff", color: "var(--ink)", fontFamily: "var(--font-ui)", display: "flex", flexDirection: "column", overflow: "hidden" }}
    >
      <div style={{ position: "absolute", left: 0, top: 0, right: 0, height: 20, background: "var(--accent)" }} />
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginTop: 10 }}>
        <div style={{ fontSize: 42, fontWeight: 800, letterSpacing: "0.12em", color: "var(--accent-strong)" }}>STRIKEFIT</div>
        <div style={{ fontSize: 30, fontWeight: 700, color: "var(--muted)" }}>Wrapped · {weekLabel(data.weekStart, data.weekEnd)}</div>
      </div>
      <div style={{ marginTop: 26, fontSize: 60, fontWeight: 800, letterSpacing: "-0.03em", lineHeight: 1.05, overflowWrap: "anywhere" }}>{data.playerName}</div>

      <div style={{ ...center, flex: 1, justifyContent: "center", gap: 10, paddingBottom: 20 }}>
        <Part animate={animate} index={0} style={{ fontSize: 34, fontWeight: 800, letterSpacing: "0.14em", textTransform: "uppercase", color: "var(--accent-strong)" }}>
          {slide.kicker}
        </Part>

        {image && (
          <Part animate={animate} index={1} style={{ width: 430, aspectRatio: String(image.aspect), margin: "6px 0 4px" }}>
            <img src={image.webp[960]} alt="" width={430} style={{ display: "block", width: 430, height: "auto" }} />
          </Part>
        )}

        <Part animate={animate} index={image ? 2 : 1} style={{ fontSize: valueSize(slide.value), fontWeight: 800, letterSpacing: "-0.04em", lineHeight: 1, color: "var(--ink)", overflowWrap: "anywhere" }}>
          {slide.value}
        </Part>
        {slide.unit && (
          <Part animate={animate} index={2} style={{ fontSize: 52, fontWeight: 700, color: "var(--ink-secondary)" }}>
            {slide.unit}
          </Part>
        )}

        {slide.id === "personality" && !slide.waiting && slide.axes.length >= 3 && (
          <Part animate={animate} index={2} style={{ marginTop: 6 }}>
            <RadarChart axes={slide.axes} size={560} scale={1.5} caption="Effort fingerprint for the week" />
          </Part>
        )}

        <Part animate={animate} index={3} style={{ marginTop: 12, fontSize: slide.id === "personality" ? 34 : 46, fontWeight: 600, lineHeight: 1.25, color: "var(--ink-secondary)", maxWidth: 860 }}>
          {slide.headline}
        </Part>

        {slide.lines.length > 0 && (
          <Part animate={animate} index={4} style={{ marginTop: 10, fontSize: 38, fontWeight: 600, lineHeight: 1.45, color: "var(--ink)" }}>
            {slide.lines.map((l) => (
              <div key={l}>{l}</div>
            ))}
          </Part>
        )}

        {slide.id === "personality" && rank && (
          <Part animate={animate} index={5} style={{ marginTop: 14, display: "flex", alignItems: "center", gap: 24 }}>
            <RankRing info={rank} size="lg" animate={false} />
            <div style={{ textAlign: "left" }}>
              <RankBadge rank={rank.rank} size="md" />
              <div style={{ marginTop: 8, fontSize: 30, fontWeight: 700, color: "var(--ink-secondary)" }}>{formatNumber(rank.effortPoints)} effort points</div>
              {rank.nextRank && <div style={{ fontSize: 26, fontWeight: 500, color: "var(--muted)" }}>{formatNumber(rank.pointsToNext)} to {rank.nextRank.name}</div>}
            </div>
          </Part>
        )}

        {slide.note && (
          <Part animate={animate} index={5} style={{ marginTop: 6, fontSize: 28, fontWeight: 500, color: "var(--muted)" }}>
            {slide.note}
          </Part>
        )}
      </div>

      <div style={{ fontSize: 24, fontWeight: 500, color: "var(--muted)", display: "flex", justifyContent: "space-between" }}>
        <span>Counted from finished fights</span>
        <span>strikefit.vercel.app</span>
      </div>
    </div>
  );
}
