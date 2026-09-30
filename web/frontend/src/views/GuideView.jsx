import { Link } from "react-router-dom";
import { LEVELS, LEVEL_KEYS, TIER_LABEL } from "../lib/game.js";
import { Card, CardHeader, PageHeader, SectionRule } from "../components/ui/Card.jsx";
import { LevelBadge } from "../components/ui/Badge.jsx";
import Icon from "../components/ui/Icon.jsx";

const isMac = typeof navigator !== "undefined" && /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent);
const MOD = isMac ? "⌘" : "Ctrl";
const link = "press rounded text-xs font-semibold text-[var(--accent)] hover:text-[var(--accent-strong)] hover:underline";

// Everything below describes what the dashboard already does; keep it in step with the views.
const SECTIONS = [
  ["start", "Quick start"],
  ["pages", "Pages"],
  ["numbers", "Reading the numbers"],
  ["keys", "Shortcuts"],
  ["help", "Troubleshooting"],
];

const STEPS = [
  ["Connect the game", "The headset sends every fight to this backend (SessionReporter → Base Url). The Connection page shows the address and checks the server and live stream."],
  ["Pick a name", "Before each fight, point at a name in the headset and pull the trigger. If nobody picks within 10 seconds, the last name used (or Player 1) is taken."],
  ["Press Start", "The fight appears in the Live Arena straight away. When it ends, the result lands in Overview, Sessions and the player's profile."],
];

const PAGES = [
  {
    to: "/dashboard",
    icon: "dashboard",
    title: "Overview",
    what: "Headline figures for the chosen time range: battles, win rate, latest score and active time, then the score trend, outcomes, the last 14 days, combat totals, personal records and recent battles.",
    how: "Switch the range with All time / 7 days / 24 hours. Click a point on the score trend or a record to open that battle.",
  },
  {
    to: "/dashboard/live",
    icon: "live",
    title: "Live Arena",
    what: "Every fight in progress, with both health bars, XP and score updating the moment the headset reports a hit, an attack or an HP change.",
    how: "Filter the event feed by All, Hits, Monster, Damage taken or Results.",
  },
  {
    to: "/dashboard/players",
    icon: "players",
    title: "Players",
    what: "One card per player, built from every recorded battle. Open a player for their results, effort over time, swing tiers, XP and Level 1 vs Level 2.",
    how: "Search by name. Hover a name and use the pencil to rename that player everywhere.",
  },
  {
    to: "/dashboard/sessions",
    icon: "sessions",
    title: "Sessions",
    what: "Every battle, grouped by day. A session opens its summary, a health-over-time chart, the monster's attacks and a timeline replay.",
    how: "Search by player or id, filter by outcome and level, sort. In a session, step through the replay at 1×, 4× or 10×, or delete it.",
  },
  {
    to: "/dashboard/leaderboard",
    icon: "trophy",
    title: "Leaderboard",
    what: "Highest scores across all recorded battles. Ties go to the faster run.",
    how: "Rank the best run per player or all runs, and filter by level and outcome.",
  },
  {
    to: "/dashboard/compare",
    icon: "compare",
    title: "Compare",
    what: "Two players, or the two playable levels, side by side on effort (how hard people swing) and engagement (how much they play).",
    how: "Choose Two players or Level 1 vs Level 2, then pick Player A and Player B.",
  },
  {
    to: "/dashboard/analytics",
    icon: "analytics",
    title: "Analytics",
    what: "The thesis view: how hard people swing, whether effort holds up through a fight, and how the two levels differ.",
    how: "Narrow it to one level or one player with the selectors at the top.",
  },
  {
    to: "/dashboard/export",
    icon: "download",
    title: "Export",
    what: "Raw sessions and events as CSV, for a spreadsheet, R or Python.",
    how: "Filter by player, level and dates (both days included); events can also be filtered by type.",
  },
  {
    to: "/dashboard/connection",
    icon: "server",
    title: "Connection",
    what: "Whether the backend is reachable, a round-trip test of the live channel, the headset setup and the API reference.",
    how: "Open it whenever the top bar says Connecting, Reconnecting or Server offline.",
  },
];

const TERMS = [
  ["Win rate", "Victories as a share of the battles in range."],
  ["Current / best streak", "Victories in a row: the run going now, and the longest one recorded."],
  ["Swing speed", "The faster of the controller's tracked speed and the weapon's own motion at contact, in metres per second."],
  ["Full power", "Share of counted hits at or above the level's full-power threshold."],
  ["—", "No data for that figure yet. Swing speed is only recorded by the live-telemetry build of the game, so older battles show “—”."],
];

const KEYS = [
  [[MOD, "K"], "Open the command palette: jump to any page or player"],
  [["↑", "↓", "Enter"], "Move through palette results and open one"],
  [["←", "→"], "Move between options in a pill switch (time range, filters)"],
  [["Esc"], "Close the palette or the mobile menu"],
];

const HELP = [
  ["The top bar says “Waking the server”", "The hosted backend sleeps when idle. It can take up to a minute to answer; the page fills in on its own when it does."],
  ["A fight I played isn't listed", "If the headset's Wi-Fi dropped, the game keeps playing but that fight isn't recorded. Check the Connection page before the next one."],
  ["A player shows up twice", "Names must match exactly, including spaces and capitals, so “player 1” and “Player 1” are two players. Merging isn't supported: a rename onto a name that already exists is refused."],
  ["A rename is refused", "Players in a live fight can't be renamed, because the headset keeps reporting under the old name. Rename after the fight ends."],
  ["Swing speed shows “—”", "That battle came from a build without live telemetry. New fights record it."],
];

function Kbd({ children }) {
  return (
    <kbd className="inline-flex min-w-[1.6rem] items-center justify-center rounded-md border border-[var(--border)] bg-[var(--surface)] px-1.5 py-0.5 font-sans text-xs font-bold text-[var(--ink-secondary)] shadow-sm">
      {children}
    </kbd>
  );
}

function StepNo({ n }) {
  return <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-[var(--accent-soft)] text-xs font-extrabold text-[var(--accent-strong)] ring-1 ring-inset ring-[var(--accent-ring)]">{n}</span>;
}

function TierTable() {
  const tiers = ["tap", "light", "medium", "full"];
  const range = (s, t) => {
    const from = { tap: s.min, light: s.tiers.light, medium: s.tiers.medium, full: s.tiers.full }[t];
    const to = { tap: s.tiers.light, light: s.tiers.medium, medium: s.tiers.full }[t];
    return to ? `${from.toFixed(1)}–${to.toFixed(1)}` : `${from.toFixed(1)}+`;
  };
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-sm">
        <caption className="sr-only">Swing tiers per level, in metres per second</caption>
        <thead>
          <tr className="border-b border-[var(--border)]">
            <th scope="col" className="t-kicker px-5 py-3 font-bold">
              Tier
            </th>
            {LEVEL_KEYS.map((k) => (
              <th key={k} scope="col" className="px-5 py-3">
                <span className="flex items-center gap-2 text-xs font-bold text-[var(--ink)]">
                  {LEVELS[k].name} <LevelBadge level={k} />
                </span>
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-[var(--border)]">
          <tr>
            <th scope="row" className="px-5 py-3 font-semibold text-[var(--muted)]">
              {TIER_LABEL.none}
            </th>
            {LEVEL_KEYS.map((k) => (
              <td key={k} className="readout px-5 py-3 font-bold text-[var(--muted)]">
                under {LEVELS[k].swing.min.toFixed(1)} m/s
              </td>
            ))}
          </tr>
          {tiers.map((t) => (
            <tr key={t}>
              <th scope="row" className={`px-5 py-3 font-semibold ${t === "full" ? "text-[var(--accent-strong)]" : "text-[var(--ink-secondary)]"}`}>
                {TIER_LABEL[t]}
              </th>
              {LEVEL_KEYS.map((k) => (
                <td key={k} className="readout px-5 py-3 font-bold text-[var(--ink)]">
                  {range(LEVELS[k].swing, t)} m/s
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export default function GuideView() {
  return (
    <div>
      <PageHeader eyebrow="Help" title="User guide" description="What each page shows, how to read the numbers, and what to do when something looks off." />

      <div className="mt-8 grid gap-8 lg:grid-cols-12">
        <nav aria-label="On this page" className="lg:sticky lg:top-20 lg:col-span-3 lg:self-start">
          <Card className="p-2">
            <ul className="flex gap-1 overflow-x-auto lg:flex-col">
              {SECTIONS.map(([id, label], i) => (
                <li key={id} className="shrink-0">
                  <a href={`#guide-${id}`} className="press flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-semibold text-[var(--ink-secondary)] hover:bg-[var(--surface-muted)] hover:text-[var(--ink)]">
                    <span className="t-index">{String(i + 1).padStart(2, "0")}</span>
                    {label}
                  </a>
                </li>
              ))}
            </ul>
          </Card>
        </nav>

        <div className="min-w-0 space-y-12 lg:col-span-9">
          <section aria-labelledby="guide-start" className="scroll-mt-20">
            <SectionRule index="01" id="guide-start" title="Quick start" aside={<Link to="/dashboard/connection" className={link}>Connection</Link>} />
            <ol className="stagger mt-4 grid gap-4 md:grid-cols-3">
              {STEPS.map(([title, body], i) => (
                <li key={title}>
                  <Card className="h-full p-5">
                    <div className="flex items-center gap-3">
                      <StepNo n={i + 1} />
                      <h3 className="text-[0.95rem] font-bold tracking-tight text-[var(--ink)]">{title}</h3>
                    </div>
                    <p className="mt-3 text-sm font-medium leading-relaxed text-[var(--ink-secondary)]">{body}</p>
                  </Card>
                </li>
              ))}
            </ol>
          </section>

          <section aria-labelledby="guide-pages" className="scroll-mt-20">
            <SectionRule index="02" id="guide-pages" title="Pages" />
            <ul className="stagger mt-4 grid gap-4 md:grid-cols-2">
              {PAGES.map((p) => (
                <li key={p.to}>
                  <Card className="lift flex h-full flex-col p-5 hover:border-[var(--accent-ring)]">
                    <div className="flex items-center justify-between gap-3">
                      <span className="flex items-center gap-3">
                        <span className="inline-flex h-8 w-8 items-center justify-center rounded-lg bg-blue-50 text-blue-600">
                          <Icon name={p.icon} className="h-4 w-4" />
                        </span>
                        <h3 className="text-[0.95rem] font-bold tracking-tight text-[var(--ink)]">{p.title}</h3>
                      </span>
                      <Link to={p.to} className={`${link} inline-flex items-center gap-1`} aria-label={`Open ${p.title}`}>
                        Open <Icon name="arrowRight" className="h-3 w-3" strokeWidth={2.4} />
                      </Link>
                    </div>
                    <p className="mt-3 text-sm font-medium leading-relaxed text-[var(--ink-secondary)]">{p.what}</p>
                    <div className="mt-auto pt-3">
                      <p className="border-t border-[var(--border)] pt-3 text-xs font-semibold leading-relaxed text-[var(--muted)]">
                        <span className="t-kicker mr-1.5 text-[var(--accent-strong)]">How</span>
                        {p.how}
                      </p>
                    </div>
                  </Card>
                </li>
              ))}
            </ul>
          </section>

          <section aria-labelledby="guide-numbers" className="scroll-mt-20">
            <SectionRule index="03" id="guide-numbers" title="Reading the numbers" />
            <div className="mt-4 grid gap-6 xl:grid-cols-12">
              <Card className="overflow-hidden xl:col-span-7">
                <CardHeader title="Swing tiers" subtitle="How the game grades each hit by swing speed. Damage rises with speed; medium and full-power hits also earn bonus XP." />
                <TierTable />
              </Card>
              <Card className="overflow-hidden xl:col-span-5">
                <CardHeader title="Terms" subtitle="The figures you'll see most" />
                <dl className="divide-y divide-[var(--border)]">
                  {TERMS.map(([k, v]) => (
                    <div key={k} className="px-5 py-3.5">
                      <dt className="t-kicker">{k}</dt>
                      <dd className="mt-1 text-sm font-medium leading-relaxed text-[var(--ink-secondary)]">{v}</dd>
                    </div>
                  ))}
                </dl>
              </Card>
            </div>
          </section>

          <section aria-labelledby="guide-keys" className="scroll-mt-20">
            <SectionRule index="04" id="guide-keys" title="Shortcuts" />
            <Card className="mt-4 overflow-hidden">
              <ul className="divide-y divide-[var(--border)]">
                {KEYS.map(([keys, what]) => (
                  <li key={what} className="flex flex-wrap items-center justify-between gap-3 px-5 py-3.5">
                    <span className="text-sm font-medium text-[var(--ink-secondary)]">{what}</span>
                    <span className="flex items-center gap-1">
                      {keys.map((k) => (
                        <Kbd key={k}>{k}</Kbd>
                      ))}
                    </span>
                  </li>
                ))}
              </ul>
              <p className="border-t border-[var(--border)] px-5 py-2.5 text-xs font-medium text-[var(--muted)]">The time range and a collapsed sidebar are remembered on this browser.</p>
            </Card>
          </section>

          <section aria-labelledby="guide-help" className="scroll-mt-20">
            <SectionRule index="05" id="guide-help" title="Troubleshooting" />
            <div className="mt-4 space-y-3">
              {HELP.map(([q, a]) => (
                <details key={q} className="group rounded-2xl border border-[var(--border)] bg-[var(--surface)] shadow-[var(--shadow-card)] open:border-[var(--accent-ring)]">
                  <summary className="flex cursor-pointer list-none items-center justify-between gap-4 px-5 py-4 text-sm font-bold text-[var(--ink)] [&::-webkit-details-marker]:hidden">
                    {q}
                    <Icon name="chevronDown" className="h-4 w-4 shrink-0 text-[var(--muted)] transition-transform duration-300 group-open:rotate-180" />
                  </summary>
                  <p className="px-5 pb-4 text-sm font-medium leading-relaxed text-[var(--ink-secondary)]">{a}</p>
                </details>
              ))}
            </div>
          </section>
        </div>
      </div>
    </div>
  );
}
