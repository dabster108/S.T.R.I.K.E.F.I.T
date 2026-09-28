import { Link } from "react-router-dom";
import Icon from "../ui/Icon.jsx";
import { Brand } from "./LandingNav.jsx";
import { GITHUB, Reveal } from "./shared.jsx";

export function FinalCta() {
  return (
    <section aria-labelledby="cta-title" className="px-4 py-20 sm:px-6">
      <Reveal from="scale" className="mx-auto max-w-6xl">
        <div className="cta-band rounded-3xl px-6 py-16 text-center shadow-[0_30px_60px_-30px_rgb(30_58_138/0.6)] sm:px-12 sm:py-20">
          <div className="pointer-events-none absolute inset-0 opacity-25" aria-hidden style={{ backgroundImage: "radial-gradient(rgb(255 255 255 / 0.5) 1px, transparent 1px)", backgroundSize: "22px 22px" }} />
          <h2 id="cta-title" className="t-h2 relative mx-auto max-w-2xl text-white">
            Watch effort turn into damage, live.
          </h2>
          <p className="relative mx-auto mt-4 max-w-xl text-lg font-medium text-blue-50">Every swing, hit and result streams to the dashboard the moment the headset reports it.</p>
          <div className="relative mt-8 flex flex-wrap justify-center gap-3">
            <Link
              to="/dashboard"
              className="press inline-flex items-center gap-2 rounded-xl bg-white px-5 py-3 text-[0.95rem] font-bold text-[var(--accent-strong)] shadow-lg hover:bg-[var(--accent-soft)] focus-visible:outline-white"
            >
              Open the dashboard <Icon name="arrowRight" className="h-4 w-4" strokeWidth={2.4} />
            </Link>
            <Link
              to="/dashboard/live"
              className="press inline-flex items-center gap-2 rounded-xl border border-white/40 bg-white/10 px-5 py-3 text-[0.95rem] font-bold text-white backdrop-blur hover:bg-white/20 focus-visible:outline-white"
            >
              <Icon name="live" className="h-4 w-4" /> Live Arena
            </Link>
          </div>
        </div>
      </Reveal>
    </section>
  );
}

export function Footer() {
  const link = "rounded font-semibold text-[var(--ink-secondary)] transition-colors hover:text-[var(--accent)]";
  return (
    <footer className="border-t border-[var(--border)] bg-[var(--surface)]">
      <div className="mx-auto flex max-w-6xl flex-wrap items-start justify-between gap-8 px-4 py-12 sm:px-6">
        <div>
          <Brand />
          <p className="mt-3 max-w-sm text-sm font-medium text-[var(--muted)]">A research prototype at Softwarica College. Every number on this site comes from real sessions recorded by the game.</p>
        </div>
        <nav aria-label="Footer" className="flex flex-wrap gap-x-7 gap-y-3 text-sm">
          <Link to="/dashboard" className={link}>
            Dashboard
          </Link>
          <Link to="/dashboard/live" className={link}>
            Live Arena
          </Link>
          <Link to="/dashboard/analytics" className={link}>
            Analytics
          </Link>
          <a href={GITHUB} target="_blank" rel="noreferrer" className={`${link} inline-flex items-center gap-1.5`}>
            <Icon name="github" className="h-4 w-4" /> GitHub
          </a>
        </nav>
      </div>
      <div className="border-t border-[var(--border)] py-5 text-center text-xs font-semibold text-[var(--muted)]">STRIKEFIT · VR combat telemetry</div>
    </footer>
  );
}
