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
      <Route path="*" element={<Landing />} />
    </Routes>
  );
}
