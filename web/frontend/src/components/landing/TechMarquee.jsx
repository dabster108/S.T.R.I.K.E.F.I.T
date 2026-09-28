import Icon from "../ui/Icon.jsx";

const TECH = [
  ["Unity", "layers"],
  ["Meta XR SDK", "crosshair"],
  ["Meta Quest 2", "headset"],
  ["Node", "server"],
  ["Postgres", "database"],
  ["Render", "cloud"],
  ["Vercel", "monitor"],
];

function Row({ hidden }) {
  return (
    <ul className="flex shrink-0 items-center gap-4 pr-4 sm:gap-6 sm:pr-6" aria-hidden={hidden || undefined}>
      {TECH.map(([name, icon]) => (
        <li
          key={name}
          className="flex items-center gap-2.5 whitespace-nowrap rounded-full border border-[var(--border)] bg-[var(--surface)] px-4 py-2 text-sm font-bold text-[var(--ink-secondary)] shadow-[var(--shadow-card)] transition-colors hover:border-[var(--accent-ring)] hover:text-[var(--accent-strong)]"
        >
          <Icon name={icon} className="h-4 w-4 text-[var(--accent)]" />
          {name}
        </li>
      ))}
    </ul>
  );
}

// Two identical rows slide left by exactly one row's width, so the loop is seamless.
export default function TechMarquee() {
  return (
    <section aria-label="Built with" className="border-y border-[var(--border)] bg-[var(--surface-muted)] py-8">
      <p className="mb-5 text-center text-xs font-bold uppercase tracking-[0.14em] text-[var(--muted)]">Built with</p>
      <div className="marquee overflow-hidden" tabIndex={0} aria-label="Technologies: Unity, Meta XR SDK, Meta Quest 2, Node, Postgres, Render, Vercel">
        <div className="marquee-track">
          <Row />
          <Row hidden />
        </div>
      </div>
    </section>
  );
}
