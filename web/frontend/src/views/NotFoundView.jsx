import { useEffect } from "react";
import { Link, useLocation } from "react-router-dom";
import { buttonClass } from "../components/ui/Button.jsx";
import Icon from "../components/ui/Icon.jsx";
import LandingNav from "../components/landing/LandingNav.jsx";

// A real 404. Before, an unknown /dashboard/... address quietly showed the Overview and any other unknown address showed the
// landing page, so a mistyped or out-of-date link looked like it worked.

function Body({ inDashboard }) {
  const { pathname } = useLocation();
  const links = inDashboard
    ? [
        ["/dashboard", "Overview", "dashboard"],
        ["/dashboard/live", "Live Arena", "live"],
        ["/dashboard/sessions", "Sessions", "sessions"],
        ["/dashboard/players", "Players", "players"],
      ]
    : [
        ["/", "Home", "dashboard"],
        ["/dashboard", "Dashboard", "analytics"],
        ["/dashboard/live", "Live Arena", "live"],
        ["/story", "Story", "sparkles"],
      ];
  return (
    <section className="view-in mx-auto max-w-xl px-4 py-16 text-center sm:py-24" aria-labelledby="nf-title">
      <p className="t-index">404</p>
      <h1 id="nf-title" className="t-h1 mt-3 text-[var(--ink)]">
        This page doesn&apos;t exist
      </h1>
      <p className="mt-3 text-base font-medium leading-relaxed text-[var(--muted)]">
        Nothing lives at <code className="rounded-md bg-[var(--surface-muted)] px-1.5 py-0.5 text-sm font-semibold text-[var(--ink-secondary)] ring-1 ring-inset ring-[var(--border)]">{pathname}</code>. The link may be mistyped or out of date.
      </p>
      <ul className="mt-8 grid grid-cols-2 gap-3 text-left">
        {links.map(([to, label, icon]) => (
          <li key={to}>
            <Link to={to} className="press group flex items-center gap-3 rounded-xl border border-[var(--border)] bg-[var(--surface)] px-4 py-3 shadow-[var(--shadow-card)] hover:border-[var(--accent-ring)]">
              <span className="flex h-8 w-8 items-center justify-center rounded-lg bg-[var(--accent-soft)] text-[var(--accent)]" aria-hidden>
                <Icon name={icon} className="h-4 w-4" />
              </span>
              <span className="text-sm font-bold text-[var(--ink)] group-hover:text-[var(--accent)]">{label}</span>
            </Link>
          </li>
        ))}
      </ul>
      {inDashboard && (
        <p className="mt-6 text-xs font-semibold text-[var(--muted)]">
          Or press <kbd className="rounded border border-[var(--border)] bg-[var(--surface)] px-1 font-sans">Ctrl</kbd> + <kbd className="rounded border border-[var(--border)] bg-[var(--surface)] px-1 font-sans">K</kbd> and type what you were looking for.
        </p>
      )}
    </section>
  );
}

/** Inside the dashboard layout (sidebar and top bar stay). */
export function DashboardNotFound() {
  return <Body inDashboard />;
}

/** Any other unknown address: the site's own navigation, then the same message. */
export default function NotFoundView() {
  useEffect(() => {
    document.title = "Page not found · STRIKEFIT";
  }, []);
  return (
    <div className="min-h-screen bg-[var(--bg)] text-[var(--ink)]">
      <LandingNav live={false} />
      <main id="main">
        <Body inDashboard={false} />
      </main>
      <footer className="border-t border-[var(--border)] px-4 py-6 text-center text-xs font-semibold text-[var(--muted)]">
        STRIKEFIT · a VR fitness research prototype
      </footer>
    </div>
  );
}
