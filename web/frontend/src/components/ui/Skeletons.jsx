import { Skeleton } from "./Feedback.jsx";
import { PageHeader } from "./Card.jsx";

// Loading placeholders shaped like each page, so nothing jumps when data lands.

const Box = ({ className = "", children }) => <div className={`rounded-2xl border border-[var(--border)] bg-[var(--surface)] ${className}`}>{children}</div>;

function Header({ avatar = false }) {
  return (
    <div className="flex items-center gap-4">
      {avatar && <Skeleton className="h-16 w-16 rounded-2xl" />}
      <div className="space-y-2">
        <Skeleton className="h-3 w-20" />
        <Skeleton className="h-8 w-64" />
        <Skeleton className="h-3.5 w-80 max-w-[70vw]" />
      </div>
    </div>
  );
}

function KpiRow({ n = 4 }) {
  return (
    <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
      {Array.from({ length: n }, (_, i) => (
        <Box key={i} className="p-5">
          <div className="flex items-center justify-between">
            <Skeleton className="h-3 w-20" />
            <Skeleton className="h-8 w-8 rounded-lg" />
          </div>
          <Skeleton className="mt-4 h-8 w-24" />
          <div className="mt-4 flex items-end justify-between">
            <Skeleton className="h-3 w-24" />
            <Skeleton className="h-7 w-20" />
          </div>
        </Box>
      ))}
    </div>
  );
}

function ChartBox({ className = "", h = "h-56" }) {
  return (
    <Box className={`overflow-hidden ${className}`}>
      <div className="space-y-2 border-b border-[var(--border)] px-5 py-4">
        <Skeleton className="h-3.5 w-32" />
        <Skeleton className="h-3 w-52" />
      </div>
      <div className={`flex items-end gap-2 px-5 pb-5 pt-6 ${h}`}>
        {[40, 65, 50, 80, 55, 90, 70, 60, 85, 45, 75, 95].map((v, i) => (
          <div key={i} className="skeleton flex-1 rounded-md" style={{ height: `${v}%` }} aria-hidden />
        ))}
      </div>
    </Box>
  );
}

function ArenaCard() {
  return (
    <Box className="space-y-4 p-5">
      <div className="flex items-center gap-3">
        <Skeleton className="h-10 w-10 rounded-full" />
        <div className="flex-1 space-y-2">
          <Skeleton className="h-3.5 w-28" />
          <Skeleton className="h-3 w-20" />
        </div>
        <Skeleton className="h-5 w-12 rounded-full" />
      </div>
      <Skeleton className="h-2.5 w-full rounded-full" />
      <Skeleton className="h-2.5 w-full rounded-full" />
      <div className="grid grid-cols-4 gap-2">
        {[0, 1, 2, 3].map((i) => (
          <Skeleton key={i} className="h-9" />
        ))}
      </div>
    </Box>
  );
}

function Rows({ n = 6, avatar = true }) {
  return (
    <Box className="divide-y divide-[var(--border)] overflow-hidden">
      {Array.from({ length: n }, (_, i) => (
        <div key={i} className="flex items-center gap-4 px-5 py-4">
          {avatar && <Skeleton className="h-9 w-9 rounded-full" />}
          <div className="flex-1 space-y-2">
            <Skeleton className="h-3.5 w-40" />
            <Skeleton className="h-2 w-full max-w-md rounded-full" />
          </div>
          <Skeleton className="h-6 w-16" />
        </div>
      ))}
    </Box>
  );
}

const wrap = (label, children) => (
  <div className="space-y-6" aria-busy="true" aria-label={label}>
    {children}
  </div>
);

export const OverviewSkeleton = () =>
  wrap(
    "Loading overview",
    <>
      {/* The real header (it is static text), so the largest paint is not waiting for the data. */}
      <PageHeader eyebrow="Dashboard" title="Performance overview" description="How your players are doing across every battle." />
      <KpiRow />
      <div className="grid gap-6 xl:grid-cols-12">
        <ChartBox className="xl:col-span-8" />
        <ChartBox className="xl:col-span-4" />
      </div>
    </>
  );

export const LiveSkeleton = () =>
  wrap(
    "Loading live arena",
    <>
      <Header />
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        <ArenaCard />
        <ArenaCard />
      </div>
      <Rows n={5} />
    </>
  );

export const SessionsSkeleton = () =>
  wrap(
    "Loading sessions",
    <>
      <Header />
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        {[0, 1, 2, 3].map((i) => (
          <Box key={i} className="space-y-2 px-4 py-3">
            <Skeleton className="h-3 w-24" />
            <Skeleton className="h-6 w-16" />
          </Box>
        ))}
      </div>
      <Rows n={7} />
    </>
  );

export const LeaderboardSkeleton = () =>
  wrap(
    "Loading leaderboard",
    <>
      <Header />
      <Box className="flex items-end justify-center gap-6 px-4 pt-8">
        {["h-24", "h-32", "h-16"].map((h, i) => (
          <div key={i} className="flex w-28 flex-col items-center gap-2">
            <Skeleton className="h-14 w-14 rounded-full" />
            <Skeleton className="h-3.5 w-20" />
            <Skeleton className={`${h} w-full rounded-b-none rounded-t-xl`} />
          </div>
        ))}
      </Box>
      <Rows n={5} />
    </>
  );

export const PlayersSkeleton = () =>
  wrap(
    "Loading players",
    <>
      <Header />
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        {[0, 1, 2].map((i) => (
          <Box key={i} className="space-y-4 p-5">
            <div className="flex items-center gap-3">
              <Skeleton className="h-11 w-11 rounded-full" />
              <div className="flex-1 space-y-2">
                <Skeleton className="h-3.5 w-28" />
                <Skeleton className="h-3 w-20" />
              </div>
            </div>
            <div className="flex items-end justify-between">
              <Skeleton className="h-8 w-20" />
              <Skeleton className="h-8 w-28" />
            </div>
            <Skeleton className="h-2 w-full rounded-full" />
          </Box>
        ))}
      </div>
    </>
  );

export const PlayerSkeleton = () =>
  wrap(
    "Loading player",
    <>
      <KpiRow />
      <div className="grid gap-6 xl:grid-cols-2">
        <ChartBox />
        <ChartBox />
      </div>
    </>
  );

export const AnalyticsSkeleton = () =>
  wrap(
    "Loading analytics",
    <>
      <ChartBox h="h-40" />
      <div className="grid gap-6 xl:grid-cols-2">
        <ChartBox />
        <ChartBox />
      </div>
    </>
  );

// Same grid as the Compare page: two diff cards, the "who leads" strip, then the chart rows.
export const CompareSkeleton = () =>
  wrap(
    "Loading comparison",
    <>
      <div className="grid gap-6 xl:grid-cols-12">
        <ChartBox className="xl:col-span-7" h="h-72" />
        <ChartBox className="xl:col-span-5" h="h-72" />
      </div>
      <Box className="space-y-4 p-5">
        <Skeleton className="h-3.5 w-28" />
        <div className="flex items-end justify-between">
          <Skeleton className="h-8 w-20" />
          <Skeleton className="h-8 w-20" />
        </div>
        <Skeleton className="h-3 w-full rounded-full" />
      </Box>
      <div className="grid gap-6 xl:grid-cols-12">
        <ChartBox className="xl:col-span-8" h="h-64" />
        <ChartBox className="xl:col-span-4" h="h-64" />
      </div>
      <div className="grid gap-6 xl:grid-cols-12">
        <ChartBox className="xl:col-span-5" />
        <ChartBox className="xl:col-span-7" />
      </div>
    </>
  );

export const SessionDetailSkeleton = () =>
  wrap(
    "Loading session",
    <>
      <Box className="grid grid-cols-2 gap-4 p-5 sm:grid-cols-5">
        {Array.from({ length: 10 }, (_, i) => (
          <div key={i} className="space-y-2">
            <Skeleton className="h-3 w-16" />
            <Skeleton className="h-5 w-12" />
          </div>
        ))}
      </Box>
      <ChartBox h="h-64" />
    </>
  );

export const GenericSkeleton = () =>
  wrap(
    "Loading",
    <>
      <Header />
      <ChartBox />
    </>
  );

// A numbered section heading ("01 ── Return rate") above a card.
function RuleSkeleton() {
  return (
    <div className="flex items-center gap-3">
      <Skeleton className="h-3 w-6" />
      <Skeleton className="h-4 w-40" />
      <span className="h-px flex-1 bg-[var(--border)]" aria-hidden />
    </div>
  );
}

// A row of figures in one bordered strip (the Lab's and Engagement's headline numbers).
function StatStrip({ n = 4 }) {
  return (
    <Box className="grid divide-x divide-[var(--border)] sm:grid-cols-4">
      {Array.from({ length: n }, (_, i) => (
        <div key={i} className="space-y-2 px-5 py-4">
          <Skeleton className="h-3 w-24" />
          <Skeleton className="h-8 w-16" />
        </div>
      ))}
    </Box>
  );
}

// Thesis Lab: header + export button, a "what this tells us" summary card, then numbered chart sections.
export const LabSkeleton = () =>
  wrap(
    "Loading the Thesis Lab",
    <>
      <div className="flex flex-wrap items-end justify-between gap-4">
        <Header />
        <Skeleton className="h-10 w-36 rounded-xl" />
      </div>
      <Box className="space-y-2 border-amber-200 bg-amber-50/50 p-4">
        <Skeleton className="h-3.5 w-72 max-w-full" />
        <Skeleton className="h-3 w-full max-w-2xl" />
      </Box>
      <StatStrip n={4} />
      {[0, 1].map((i) => (
        <div key={i} className="space-y-3">
          <RuleSkeleton />
          <ChartBox h={i ? "h-64" : "h-52"} />
        </div>
      ))}
      <div className="space-y-3">
        <RuleSkeleton />
        <Rows n={3} avatar={false} />
      </div>
    </>
  );

// Export: filters card on the left, the two download cards on the right.
export const ExportSkeleton = () =>
  wrap(
    "Loading the export page",
    <>
      <Header />
      <div className="grid gap-10 lg:grid-cols-12 lg:gap-8">
        <div className="space-y-3 lg:col-span-4">
          <Skeleton className="h-4 w-44" />
          <Box className="space-y-4 p-5">
            {[0, 1, 2, 3].map((i) => (
              <div key={i} className="space-y-2">
                <Skeleton className="h-3 w-16" />
                <Skeleton className="h-10 w-full rounded-xl" />
              </div>
            ))}
          </Box>
        </div>
        <div className="space-y-6 lg:col-span-8">
          {[0, 1].map((i) => (
            <Box key={i} className="flex items-center gap-4 p-5">
              <Skeleton className="h-12 w-12 rounded-xl" />
              <div className="flex-1 space-y-2">
                <Skeleton className="h-4 w-32" />
                <Skeleton className="h-3 w-full max-w-md" />
              </div>
              <Skeleton className="h-10 w-32 rounded-xl" />
            </Box>
          ))}
        </div>
      </div>
    </>
  );

// Engagement: header, a player picker, a week strip of cards and a calendar grid.
export const EngagementSkeleton = () =>
  wrap(
    "Loading engagement",
    <>
      <Header />
      <RuleSkeleton />
      <Box className="overflow-hidden">
        <div className="space-y-2 border-b border-[var(--border)] px-5 py-4">
          <Skeleton className="h-3.5 w-48" />
          <Skeleton className="h-3 w-56" />
        </div>
        <div className="space-y-3 p-5">
          <Skeleton className="h-3 w-64" />
          <div className="grid grid-flow-col grid-rows-7 gap-1">
            {Array.from({ length: 7 * 26 }, (_, i) => (
              <span key={i} className="skeleton aspect-square rounded-[3px]" aria-hidden />
            ))}
          </div>
        </div>
      </Box>
      <StatStrip n={4} />
    </>
  );
