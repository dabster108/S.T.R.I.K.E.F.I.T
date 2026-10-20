import { usePauseOffscreen } from "../../motion/index.js";

// Simple geometric marks (not the vendors' trademarks): each tool gets a small tile so the strip reads as logos at a glance.
const MARKS = {
  Unity: <path d="M12 3 4 7.5v9L12 21l8-4.5v-9zM12 3v9m0 0-8-4.5M12 12l8-4.5M12 12v9" />,
  "Meta XR SDK": <path d="M3 12c0-3 1.7-6 4-6 3 0 4 6 5 6s2-6 5-6c2.3 0 4 3 4 6s-1.7 6-4 6c-3 0-4-6-5-6s-2 6-5 6c-2.3 0-4-3-4-6z" />,
  "Meta Quest 2": <path d="M3 8.5C3 7.7 3.7 7 4.5 7h15c.8 0 1.5.7 1.5 1.5v6c0 .8-.7 1.5-1.5 1.5H15l-1.5-2h-3L9 16H4.5C3.7 16 3 15.3 3 14.5z" />,
  Node: <path d="M12 3 4 7.5v9L12 21l8-4.5v-9zM9 10v5m0-5 4 5m0-5v5" />,
  Postgres: <path d="M5 6c0-1.7 3.1-3 7-3s7 1.3 7 3-3.1 3-7 3-7-1.3-7-3zm0 0v6c0 1.7 3.1 3 7 3s7-1.3 7-3V6M5 12v6c0 1.7 3.1 3 7 3s7-1.3 7-3v-6" />,
  Render: <path d="M5 4h8a4 4 0 0 1 0 8H5zm0 8h6l5 8M5 4v16" />,
  Vercel: <path d="M12 4 21 19H3z" />,
};

const TECH = [
  ["Unity", "game engine"],
  ["Meta XR SDK", "hand + controller tracking"],
  ["Meta Quest 2", "headset"],
  ["Node", "API + live stream"],
  ["Postgres", "every session and hit"],
  ["Render", "API hosting"],
  ["Vercel", "this site"],
];

function Mark({ name }) {
  return (
    <span className="grid h-9 w-9 shrink-0 place-items-center rounded-[10px] border border-[var(--border)] bg-[var(--surface)] text-[var(--brand)] shadow-[var(--shadow-xs,0_1px_2px_rgb(15_23_42/0.06))] transition-colors group-hover/logo:border-[var(--brand)]">
      <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
        {MARKS[name]}
      </svg>
    </span>
  );
}

function Row({ hidden }) {
  return (
    <ul className="flex shrink-0 items-center pr-6" aria-hidden={hidden || undefined}>
      {TECH.map(([name, role]) => (
        <li key={name} className="group/logo flex items-center gap-3 whitespace-nowrap pr-12">
          <Mark name={name} />
          <span className="flex flex-col leading-tight">
            <span className="text-[0.95rem] font-extrabold tracking-tight text-[var(--ink)]">{name}</span>
            <span className="text-xs font-semibold text-[var(--muted)]">{role}</span>
          </span>
        </li>
      ))}
    </ul>
  );
}

// Label pinned on the left, two identical rows sliding under it so the loop is seamless. Hover or focus pauses it;
// it also stops while off-screen (usePauseOffscreen) and shows a static wrapped list under reduced motion / Calm.
export default function TechMarquee() {
  const pause = usePauseOffscreen();
  return (
    <section ref={pause} aria-label="Built with" className="border-y border-[var(--border)] bg-[var(--surface-muted)]">
      <div className="flex flex-col sm:flex-row sm:items-stretch">
        <div className="flex shrink-0 items-center justify-between gap-6 border-b border-[var(--border)] px-4 py-4 sm:w-56 sm:flex-col sm:items-start sm:justify-center sm:gap-1 sm:border-b-0 sm:border-r sm:px-6 lg:w-[max(15rem,calc((100vw-72rem)/2+14rem))] lg:pl-[max(1.5rem,calc((100vw-72rem)/2+1.5rem))]">
          <p className="t-kicker">Built with</p>
          <p className="text-sm font-bold text-[var(--ink-secondary)]">
            <span className="tabular-nums">{String(TECH.length).padStart(2, "0")}</span> tools, headset to browser
          </p>
        </div>
        <div className="marquee min-w-0 flex-1 overflow-hidden py-4" tabIndex={0} aria-label={`Technologies: ${TECH.map(([n]) => n).join(", ")}`}>
          <div className="marquee-track">
            <Row />
            <Row hidden />
          </div>
        </div>
      </div>
    </section>
  );
}
