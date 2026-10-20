// The player page's avatar and title. PlayerTitleShell is the same title drawn from the URL alone, so the page has its heading from the
// first frame (while its code and the profile are still loading) and nothing moves when the real header takes over.

export function Avatar({ name }) {
  return (
    <span className="pop-in flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl bg-gradient-to-br from-[var(--accent)] to-[var(--blue-800)] text-xl font-extrabold text-white shadow-[var(--shadow-glow)] sm:h-16 sm:w-16">
      {name.slice(0, 2).toUpperCase()}
    </span>
  );
}

export function PlayerTitleShell({ name }) {
  return (
    <header className="flex flex-wrap items-end justify-between gap-4">
      <div className="flex min-w-0 flex-1 items-center gap-4">
        <Avatar name={name} />
        <div className="min-w-0 flex-1">
          <p className="t-eyebrow">Player</p>
          <div className="mt-1 max-w-xl">
            <h1 className="t-h1 truncate text-[var(--ink)]">{name}</h1>
          </div>
        </div>
      </div>
    </header>
  );
}
