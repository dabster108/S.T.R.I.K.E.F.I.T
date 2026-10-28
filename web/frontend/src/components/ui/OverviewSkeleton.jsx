import { Skeleton } from "./Skeleton.jsx";

// The dashboard's loading fallback (shown while its chunk downloads), with the boxes it is built from. A module of its own so the
// first page load carries this one skeleton, not every page's (Skeletons.jsx re-exports it and shares the boxes).

export const Box = ({ className = "", children }) => <div className={`rounded-2xl border border-[var(--border)] bg-[var(--surface)] ${className}`}>{children}</div>;

export function KpiRow({ n = 4 }) {
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

export function ChartBox({ className = "", h = "h-56" }) {
  return (
    <Box className={`overflow-hidden ${className}`}>
      <div className="space-y-2 border-b border-[var(--border)] px-5 py-4">
        <Skeleton className="h-3.5 w-32" />
        <Skeleton className="h-3 w-52" />
      </div>
      <div className={`px-5 pb-5 pt-6 ${h}`}>
        <Skeleton className="h-full w-full rounded-xl" />
        <div className="-mt-12 flex items-end justify-between px-2" aria-hidden>
          {[20, 34, 18, 26, 10, 24].map((v, i) => (
            <div key={i} className="skeleton w-3 rounded-full" style={{ height: `${v}%` }} />
          ))}
        </div>
      </div>
    </Box>
  );
}

export const OverviewSkeleton = () =>
  (
    <div className="mt-6 space-y-6" aria-busy="true" aria-label="Loading overview">
      {/* the title is OverviewHeader, rendered by the dashboard shell above this */}
      <KpiRow />
      <div className="grid gap-6 xl:grid-cols-12">
        <ChartBox className="xl:col-span-8" />
        <ChartBox className="xl:col-span-4" />
      </div>
    </div>
  );
