import { useEffect } from "react";
import { warmUp } from "../lib/api.js";
import LandingNav from "../components/landing/LandingNav.jsx";
import Hero from "../components/landing/Hero.jsx";
import TechMarquee from "../components/landing/TechMarquee.jsx";
import Problem from "../components/landing/Problem.jsx";
import HowItWorks from "../components/landing/HowItWorks.jsx";
import Levels from "../components/landing/Levels.jsx";
import Combat from "../components/landing/Combat.jsx";
import LiveNumbers from "../components/landing/LiveNumbers.jsx";
import Thesis from "../components/landing/Thesis.jsx";
import { FinalCta, Footer } from "../components/landing/FinalCta.jsx";
import { useLiveStatus } from "../components/landing/shared.jsx";

// Public page: works the same with or without the API. Nothing here ever shows a
// server error; live data simply appears once the backend answers.
export default function Landing() {
  const status = useLiveStatus();
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
        <Problem />
        <HowItWorks />
        <Levels />
        <Combat />
        <LiveNumbers />
        <Thesis />
        <FinalCta />
      </main>
      <Footer />
    </div>
  );
}
