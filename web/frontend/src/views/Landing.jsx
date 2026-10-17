import { lazy, Suspense, useEffect, useState } from "react";
import { warmUp } from "../lib/api.js";
import LandingNav from "../components/landing/LandingNav.jsx";
import Hero from "../components/landing/Hero.jsx";
import TechMarquee from "../components/landing/TechMarquee.jsx";
import { Footer } from "../components/landing/FinalCta.jsx";
import { useLiveStatus } from "../components/landing/shared.jsx";

const loadRest = () => import("./LandingRest.jsx");
const Rest = lazy(loadRest);

// Mounts the below-the-fold sections after first paint + idle; at once when the visitor follows a #link or a nav anchor.
function useRestWanted() {
  const [wanted, setWanted] = useState(() => typeof location !== "undefined" && location.hash.length > 1 && location.hash !== "#top");
  useEffect(() => {
    if (wanted) return undefined;
    const go = () => setWanted(true);
    const onClick = (e) => {
      if (e.target.closest?.('a[href^="#"]')) go();
    };
    document.addEventListener("click", onClick, true);
    window.addEventListener("hashchange", go);
    window.addEventListener("scroll", go, { once: true, passive: true });
    const idle = window.requestIdleCallback ? window.requestIdleCallback(go, { timeout: 4000 }) : setTimeout(go, 2000);
    const start = () => { loadRest(); };   // fetch the chunk early; mounting waits for idle
    if (document.readyState === "complete") start();
    else window.addEventListener("load", start, { once: true });
    return () => {
      document.removeEventListener("click", onClick, true);
      window.removeEventListener("hashchange", go);
      window.removeEventListener("scroll", go);
      window.removeEventListener("load", start);
      if (window.cancelIdleCallback && window.requestIdleCallback) window.cancelIdleCallback(idle);
      else clearTimeout(idle);
    };
  }, [wanted]);
  return wanted;
}

// Public page: works the same with or without the API. Nothing here ever shows a
// server error; live data simply appears once the backend answers.
export default function Landing() {
  const status = useLiveStatus();
  const restWanted = useRestWanted();
  useEffect(() => {
    document.title = "STRIKEFIT · Swing harder, hit harder";
    warmUp();
  }, []);
  return (
    <div className="min-h-screen bg-[var(--bg)] text-[var(--ink)]">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:fixed focus:left-3 focus:top-3 focus:z-50 focus:rounded-lg focus:bg-[var(--surface)] focus:px-3 focus:py-2 focus:font-semibold focus:shadow-lg"
      >
        Skip to content
      </a>
      <LandingNav live={status?.state === "live"} />
      <main id="main">
        <Hero status={status} />
        <TechMarquee />
        {/* Placeholder keeps the page the height of a screen below the hero until the sections mount (nothing visible shifts). */}
        {restWanted ? (
          <Suspense fallback={<div style={{ minHeight: "100vh" }} aria-hidden="true" />}>
            <Rest />
          </Suspense>
        ) : (
          <div style={{ minHeight: "100vh" }} aria-hidden="true" />
        )}
      </main>
      <Footer />
    </div>
  );
}
