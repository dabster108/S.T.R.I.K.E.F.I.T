import { useEffect } from "react";
import Problem from "../components/landing/Problem.jsx";
import HowItWorks from "../components/landing/HowItWorks.jsx";
import GameplayShowcase from "../components/landing/GameplayShowcase.jsx";
import Levels from "../components/landing/Levels.jsx";
import StoryMode from "../components/landing/StoryMode.jsx";
import Combat from "../components/landing/Combat.jsx";
import LiveNumbers from "../components/landing/LiveNumbers.jsx";
import Thesis from "../components/landing/Thesis.jsx";
import HowTo60 from "../components/landing/HowTo60.jsx";
import Faq from "../components/landing/Faq.jsx";
import { FinalCta } from "../components/landing/FinalCta.jsx";

// Everything below the first screen. Landing loads this chunk once the first screen has painted and the browser is idle (or at
// once for a #section link / nav click), so the first load carries only the hero.
export default function LandingRest() {
  useEffect(() => {
    // Anchors did not exist before this chunk mounted: go to the one the visitor asked for, and tell the nav to watch the sections.
    const id = decodeURIComponent(location.hash.slice(1));
    if (id && id !== "top") document.getElementById(id)?.scrollIntoView();
    window.dispatchEvent(new Event("landing:sections"));
  }, []);
  return (
    <>
      <Problem />
      <HowItWorks />
      {/* the story: the problem, how it works, playing it (the headset, then the fight), the levels, the story, what was recorded, the thesis */}
      <GameplayShowcase />
      <Combat />
      <Levels />
      <StoryMode />
      <LiveNumbers />
      <Thesis />
      <HowTo60 />
      <Faq />
      <FinalCta />
    </>
  );
}
