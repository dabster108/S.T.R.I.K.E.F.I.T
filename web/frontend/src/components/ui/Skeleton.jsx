// The shimmering placeholder bar every loading skeleton is built from. Its own module so the first page load (the dashboard's
// loading fallback) can use it without pulling in the rest of Feedback.jsx.
export function Skeleton({ className = "h-4 w-full" }) {
  return <div className={`skeleton rounded-lg ${className}`} aria-hidden />;
}
