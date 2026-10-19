import { Reveal } from "./shared.jsx";
import Icon from "../ui/Icon.jsx";

// Quest -> Node API -> Postgres -> live dashboard. Each node says what travels into it; dots run along the lines (SVG
// stroke-dashoffset on a dashed overlay, so nothing but paint-free dash movement) and stop when the section is off-screen,
// under reduced motion and in Calm mode (the lines are still drawn, just still).
const NODES = [
  { icon: "headset", name: "Quest", carries: "Session start, one event per hit, swing speed, outcome" },
  { icon: "server", name: "Node API", carries: "Checks and clamps each hit (8 m/s cap), then fans it out" },
  { icon: "database", name: "Postgres", carries: "sessions, events and feedback: the permanent record" },
  { icon: "monitor", name: "Live dashboard", carries: "Server-sent events: the fight as it happens, then the stats" },
];

export default function DataPath() {
  return (
    <Reveal className="mt-20 lg:mt-28">
      <div className="max-w-2xl">
        <p className="t-kicker">Where the numbers go</p>
        <h3 className="t-h3 mt-3 text-[var(--ink)]">The data path, headset to browser</h3>
      </div>
      <ol className="datapath mt-8 grid gap-0 sm:grid-cols-4" aria-label="Data path: Quest, Node API, Postgres, live dashboard">
        {NODES.map((n, i) => (
          <li key={n.name} className="relative flex gap-4 pb-8 sm:block sm:pb-0 sm:pr-4">
            <div className="flex shrink-0 flex-col items-center sm:flex-row sm:items-center">
              <span className="relative z-[1] grid h-12 w-12 place-items-center rounded-2xl border border-[var(--border)] bg-[var(--surface)] text-[var(--accent-strong)] shadow-[var(--shadow-card)]">
                <Icon name={n.icon} className="h-5 w-5" />
              </span>
              {i < NODES.length - 1 && (
                <>
                  <svg className="absolute left-6 top-12 h-[calc(100%-3rem)] w-px sm:hidden" aria-hidden>
                    <line x1="0.5" y1="0" x2="0.5" y2="100%" stroke="var(--border-strong)" />
                    <line x1="0.5" y1="0" x2="0.5" y2="100%" className="flow-dots" stroke="var(--accent)" strokeWidth="5" strokeLinecap="round" />
                  </svg>
                  <svg className="absolute left-14 top-6 hidden h-px w-[calc(100%-3.5rem-0.25rem)] sm:block" aria-hidden>
                    <line x1="0" y1="0.5" x2="100%" y2="0.5" stroke="var(--border-strong)" />
                    <line x1="0" y1="0.5" x2="100%" y2="0.5" className="flow-dots" stroke="var(--accent)" strokeWidth="5" strokeLinecap="round" style={{ "--flow-delay": `${i * -0.6}s` }} />
                  </svg>
                </>
              )}
            </div>
            <div className="sm:mt-4">
              <p className="text-sm font-extrabold text-[var(--ink)]">{n.name}</p>
              <p className="mt-1 text-[0.8125rem] font-medium leading-relaxed text-[var(--ink-secondary)]">{n.carries}</p>
            </div>
          </li>
        ))}
      </ol>
    </Reveal>
  );
}
