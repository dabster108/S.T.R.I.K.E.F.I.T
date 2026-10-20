import Reveal from "../../motion/Reveal.jsx";

// A hairline between sections that draws itself from the left (transform: scaleX) as it scrolls into view, with a short
// accent segment leading it. Decorative. Reduced motion and Calm mode show it drawn.
export default function SectionDivider() {
  return (
    <Reveal className="mx-auto max-w-6xl px-4 sm:px-6" aria-hidden>
      <div className="relative h-px">
        <span className="draw-x absolute inset-0 bg-[var(--border)]" style={{ "--w": 1, "--bar-delay": "60ms" }} />
        <span className="draw-x absolute left-0 top-0 h-px w-24 bg-[var(--accent)]" style={{ "--w": 1, "--bar-delay": "260ms" }} />
      </div>
    </Reveal>
  );
}
