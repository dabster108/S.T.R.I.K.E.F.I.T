const TECH = [
  ["Unity", "game engine"],
  ["Meta XR SDK", "hand + controller tracking"],
  ["Meta Quest 2", "headset"],
  ["Node", "API + live stream"],
  ["Postgres", "every session and hit"],
  ["Render", "API hosting"],
  ["Vercel", "this site"],
];

function Row({ hidden }) {
  return (
    <ul className="flex shrink-0 items-baseline pr-10" aria-hidden={hidden || undefined}>
      {TECH.map(([name, role]) => (
        <li key={name} className="flex items-baseline gap-2.5 whitespace-nowrap pr-10">
          <span className="text-lg font-extrabold tracking-tight text-[var(--ink)]">{name}</span>
          <span className="text-xs font-semibold text-[var(--muted)]">{role}</span>
          <span className="ml-7 h-3 w-px self-center bg-[var(--border-strong)]" aria-hidden />
        </li>
      ))}
    </ul>
  );
}

// Label pinned on the left, two identical rows sliding under it, so the loop is seamless.
export default function TechMarquee() {
  return (
    <section aria-label="Built with" className="border-y border-[var(--border)] bg-[var(--surface-muted)]">
      <div className="flex flex-col sm:flex-row sm:items-stretch">
        <div className="flex shrink-0 items-center justify-between gap-6 border-b border-[var(--border)] px-4 py-4 sm:w-56 sm:flex-col sm:items-start sm:justify-center sm:gap-1 sm:border-b-0 sm:border-r sm:px-6 lg:w-[max(15rem,calc((100vw-72rem)/2+14rem))] lg:pl-[max(1.5rem,calc((100vw-72rem)/2+1.5rem))]">
          <p className="t-kicker">Built with</p>
          <p className="text-sm font-bold text-[var(--ink-secondary)]">
            <span className="tabular-nums">{String(TECH.length).padStart(2, "0")}</span> tools, headset to browser
          </p>
        </div>
        <div className="marquee min-w-0 flex-1 overflow-hidden py-5" tabIndex={0} aria-label={`Technologies: ${TECH.map(([n]) => n).join(", ")}`}>
          <div className="marquee-track">
            <Row />
            <Row hidden />
          </div>
        </div>
      </div>
    </section>
  );
}
