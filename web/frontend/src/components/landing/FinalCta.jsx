import { Link } from "react-router-dom";
import Icon from "../ui/Icon.jsx";
import { Brand, SECTIONS } from "./LandingNav.jsx";
import { GITHUB, Reveal } from "./shared.jsx";

export function FinalCta() {
  return (
    <section aria-labelledby="cta-title" className="px-4 pb-16 pt-4 sm:px-6">
      <Reveal from="scale" className="mx-auto max-w-6xl">
        <div className="cta-band grid gap-10 rounded-3xl px-6 py-12 shadow-[0_30px_60px_-30px_rgb(30_58_138/0.6)] sm:px-10 sm:py-14 lg:grid-cols-12 lg:items-end lg:px-14 lg:py-16">
          <div className="pointer-events-none absolute inset-0 opacity-25" aria-hidden style={{ backgroundImage: "radial-gradient(rgb(255 255 255 / 0.5) 1px, transparent 1px)", backgroundSize: "22px 22px" }} />
          <div className="relative lg:col-span-7">
            <p className="t-kicker text-[var(--blue-100)]">The dashboard</p>
            <h2 id="cta-title" className="t-h2 mt-4 max-w-xl text-white">
              Watch effort turn into damage, live.
            </h2>
            <p className="measure-tight mt-4 text-lg font-medium text-blue-50">Every swing, hit and result streams to the dashboard the moment the headset reports it.</p>
          </div>
          <div className="relative flex flex-col gap-3 sm:flex-row lg:col-span-5 lg:flex-col lg:items-stretch">
            <Link
              to="/dashboard"
              className="press inline-flex items-center justify-between gap-6 rounded-xl bg-white px-5 py-3.5 text-[0.95rem] font-bold text-[var(--accent-strong)] shadow-lg hover:bg-[var(--accent-soft)] focus-visible:outline-white"
            >
              Open the dashboard <Icon name="arrowRight" className="h-4 w-4" strokeWidth={2.4} />
            </Link>
            <Link
              to="/dashboard/live"
              className="press inline-flex items-center justify-between gap-6 rounded-xl border border-white/40 bg-white/10 px-5 py-3.5 text-[0.95rem] font-bold text-white backdrop-blur hover:bg-white/20 focus-visible:outline-white"
            >
              Live Arena <Icon name="live" className="h-4 w-4" />
            </Link>
            <p className="readout text-xs font-semibold text-[var(--blue-100)]">Server-Sent Events · heartbeat every 15 s</p>
          </div>
        </div>
      </Reveal>
    </section>
  );
}

export function Footer() {
  const link = "rounded font-semibold text-[var(--ink-secondary)] transition-colors hover:text-[var(--accent)]";
  const head = "t-kicker mb-4";
  return (
    <footer className="border-t border-[var(--border)] bg-[var(--surface)]">
      <div className="mx-auto grid max-w-6xl gap-10 px-4 pb-10 pt-14 sm:grid-cols-2 sm:px-6 lg:grid-cols-12">
        <div className="sm:col-span-2 lg:col-span-4">
          <Brand />
          <p className="measure-tight mt-4 text-sm font-medium leading-relaxed text-[var(--muted)]">
            A research prototype at Softwarica College. Every number on this site comes from real sessions recorded by the game.
          </p>
        </div>
        <nav aria-label="Sections" className="lg:col-span-2">
          <p className={head}>On this page</p>
          <ol className="space-y-2 text-sm">
            {SECTIONS.map(([id, label], i) => (
              <li key={id} className="flex items-baseline gap-3">
                <span className="t-index w-5 text-[var(--muted)]">{String(i + 1).padStart(2, "0")}</span>
                <a href={`#${id}`} className={link}>
                  {label}
                </a>
              </li>
            ))}
            <li className="flex items-baseline gap-3">
              <span className="t-index w-5 text-[var(--muted)]">{String(SECTIONS.length + 1).padStart(2, "0")}</span>
              <Link to="/monsters" className={link}>
                Monsters
              </Link>
            </li>
            <li className="flex items-baseline gap-3">
              <span className="t-index w-5 text-[var(--muted)]">{String(SECTIONS.length + 2).padStart(2, "0")}</span>
              <Link to="/story" className={link}>
                Story and guardians
              </Link>
            </li>
          </ol>
        </nav>
        <nav aria-label="Dashboard" className="lg:col-span-2">
          <p className={head}>Dashboard and research</p>
          <ul className="space-y-2 text-sm">
            {[
              ["/dashboard", "Overview"],
              ["/dashboard/live", "Live Arena"],
              ["/dashboard/leaderboard", "Leaderboard"],
              ["/dashboard/analytics", "Analytics"],
              ["/dashboard/lab", "Thesis Lab"],
              ["/about/method", "Method"],
              ["/study", "Join the study"],
            ].map(([to, label]) => (
              <li key={to}>
                <Link to={to} className={link}>
                  {label}
                </Link>
              </li>
            ))}
          </ul>
        </nav>
        <div className="lg:col-span-2">
          <p className={head}>Source</p>
          <a href={GITHUB} target="_blank" rel="noreferrer" className={`${link} inline-flex items-center gap-1.5 text-sm`}>
            <Icon name="github" className="h-4 w-4" /> GitHub
          </a>
        </div>
        <div className="lg:col-span-2">
          <p className={head}>Credits</p>
          <p className="measure-tight text-xs font-medium leading-relaxed text-[var(--muted)]">
            Gridness Studios, Adobe Mixamo, and the sound and weapon packs listed in full on{" "}
            <a href={`${GITHUB}/blob/main/docs/CREDITS.md`} target="_blank" rel="noreferrer" className={link}>
              CREDITS.md
            </a>
            .
          </p>
        </div>
      </div>
      <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-2 border-t border-[var(--border)] px-4 py-5 text-xs font-semibold text-[var(--muted)] sm:px-6">
        <span>STRIKEFIT · VR combat telemetry</span>
        <span>Meta Quest 2 · Unity · Node · Postgres</span>
      </div>
    </footer>
  );
}
