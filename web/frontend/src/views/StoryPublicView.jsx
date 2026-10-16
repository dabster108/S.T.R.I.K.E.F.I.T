import { useEffect } from "react";
import LandingNav from "../components/landing/LandingNav.jsx";
import { Footer } from "../components/landing/FinalCta.jsx";
import StoryView from "./StoryView.jsx";

// strikefit.vercel.app/story: the same story page as /dashboard/story, public, inside the landing nav and footer.
export default function StoryPublicView() {
  useEffect(() => {
    document.title = "The story · STRIKEFIT";
  }, []);
  return (
    <div className="min-h-screen bg-[var(--bg)] text-[var(--ink)]">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:fixed focus:left-3 focus:top-3 focus:z-50 focus:rounded-lg focus:bg-[var(--surface)] focus:px-3 focus:py-2 focus:font-semibold focus:shadow-lg"
      >
        Skip to content
      </a>
      <LandingNav live={false} />
      <main id="main" className="pt-6">
        <StoryView standalone />
      </main>
      <Footer />
    </div>
  );
}
