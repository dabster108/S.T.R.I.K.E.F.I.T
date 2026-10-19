import { lazy, Suspense } from "react";
import { Route, Routes } from "react-router-dom";
import Landing from "./views/Landing.jsx";
import { OverviewSkeleton } from "./components/ui/Skeletons.jsx";
import { LiveProvider } from "./live/LiveProvider.jsx";

// The dashboard (charts, live stream, every inner page) is its own chunk, so the
// public landing page never downloads it.
const DashboardApp = lazy(() => import("./DashboardApp.jsx"));
const BroadcastView = lazy(() => import("./views/BroadcastView.jsx"));
const FeedbackView = lazy(() => import("./views/FeedbackView.jsx"));
const MonstersView = lazy(() => import("./views/MonstersView.jsx"));
const ChallengeView = lazy(() => import("./views/ChallengeView.jsx"));
const NotFoundView = lazy(() => import("./views/NotFoundView.jsx"));
const MethodView = lazy(() => import("./views/MethodView.jsx"));
const StudyView = lazy(() => import("./views/StudyView.jsx"));
const StoryPublicView = lazy(() => import("./views/StoryPublicView.jsx"));

function DashboardFallback() {
  return (
    <div className="min-h-screen bg-[var(--bg)] lg:pl-[var(--sidebar-w)]">
      <div className="mx-auto max-w-7xl px-4 py-20 sm:px-6 lg:px-8">
        <OverviewSkeleton />
      </div>
    </div>
  );
}

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<Landing />} />
      <Route
        path="/dashboard/*"
        element={
          <Suspense fallback={<DashboardFallback />}>
            <DashboardApp />
          </Suspense>
        }
      />
      <Route
        path="/broadcast"
        element={
          <Suspense fallback={<div className="min-h-screen bg-[var(--bg)]" />}>
            {/* the stats overlay on the video listens to the same live stream the dashboard uses */}
            <LiveProvider>
              <BroadcastView />
            </LiveProvider>
          </Suspense>
        }
      />
      <Route
        path="/feedback"
        element={
          <Suspense fallback={<div className="min-h-screen bg-[var(--bg)]" />}>
            <FeedbackView />
          </Suspense>
        }
      />
      {/* /watch: the same public broadcast page under a friendlier name (cheers included) */}
      <Route
        path="/watch"
        element={
          <Suspense fallback={<div className="min-h-screen bg-[var(--bg)]" />}>
            <LiveProvider>
              <BroadcastView />
            </LiveProvider>
          </Suspense>
        }
      />
      {/* "Challenge a friend": public, reads one stored session by id */}
      <Route
        path="/challenge/:sessionId"
        element={
          <Suspense fallback={<div className="min-h-screen bg-[var(--bg)]" />}>
            <ChallengeView />
          </Suspense>
        }
      />
      {/* the story and the three guardians, public (the same page as /dashboard/story) */}
      <Route
        path="/story"
        element={
          <Suspense fallback={<div className="min-h-screen bg-[var(--bg)]" />}>
            <StoryPublicView />
          </Suspense>
        }
      />
      {/* the playtest study: consent, participant code, before and after forms. Public, no login. */}
      <Route
        path="/study"
        element={
          <Suspense fallback={<div className="min-h-screen bg-[var(--bg)]" />}>
            <StudyView />
          </Suspense>
        }
      />
      <Route
        path="/about/method"
        element={
          <Suspense fallback={<div className="min-h-screen bg-[var(--bg)]" />}>
            <MethodView />
          </Suspense>
        }
      />
      <Route
        path="/monsters"
        element={
          <Suspense fallback={<div className="min-h-screen bg-[var(--bg)]" />}>
            <MonstersView />
          </Suspense>
        }
      />
      <Route
        path="*"
        element={
          <Suspense fallback={<div className="min-h-screen bg-[var(--bg)]" />}>
            <NotFoundView />
          </Suspense>
        }
      />
    </Routes>
  );
}
