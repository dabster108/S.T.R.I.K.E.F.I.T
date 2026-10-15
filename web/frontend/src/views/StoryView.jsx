// StoryView — /dashboard/story, and the public /story (standalone, inside the landing nav and footer).
// The game lore: the three atmospheric slides from content/story.json, then the three guardians
// (components/story/Guardians.jsx: portrait, intro line from the same file, weapon, habits).
// Read-only; no backend calls. The source of truth is content/story.json (sync-story.js).

import story from "../content/story.json";
import { Card, CardHeader, PageHeader } from "../components/ui/Card.jsx";
import Guardians from "../components/story/Guardians.jsx";

function SlideCard({ slide, index }) {
  const shade = [
    "border-l-indigo-400/60",
    "border-l-purple-400/60",
    "border-l-rose-400/60",
  ][index] ?? "border-l-[var(--border)]";
  return (
    <div
      className={`rounded-xl border border-[var(--border)] border-l-4 ${shade} bg-[var(--surface)] px-6 py-5`}
    >
      <p className="mb-1 text-xs font-semibold uppercase tracking-wider text-[var(--ink-secondary)]">
        {slide.id}
      </p>
      <p className="text-base leading-relaxed text-[var(--ink-primary)] sm:text-lg">
        {slide.text}
      </p>
    </div>
  );
}

export default function StoryView({ standalone = false }) {
  const slides = story.slides ?? [];

  return (
    <div className="mx-auto max-w-5xl px-4 py-8 sm:px-6 lg:px-8">
      <div className="mx-auto max-w-2xl">
        <PageHeader
          eyebrow="Lore"
          title="The Legend"
          description="The story the headset shows before the first battle. Three slides, then three guardians."
        />

        {/* ── Slides ── */}
        <section className="mt-8">
          <Card>
            <CardHeader title="Story" subtitle={`${slides.length} slide${slides.length !== 1 ? "s" : ""} · ${story.slideSeconds ?? 7} s each`} />
            <div className="flex flex-col gap-3 px-4 pb-4">
              {slides.length === 0 ? (
                <p className="text-sm text-[var(--ink-secondary)]">No slides in story.json.</p>
              ) : (
                slides.map((s, i) => <SlideCard key={s.id} slide={s} index={i} />)
              )}
            </div>
          </Card>
        </section>
      </div>

      {/* ── Guardians: one per screen on desktop, stacked cards on phones ── */}
      <Guardians />

      {/* ── Source note (developer-facing: not on the public page) ── */}
      {!standalone && (
        <p className="mt-10 text-xs text-[var(--ink-tertiary)]">
          Source of truth:{" "}
          <code className="rounded bg-[var(--surface-muted)] px-1 py-0.5 font-mono">content/story.json</code> — run{" "}
          <code className="rounded bg-[var(--surface-muted)] px-1 py-0.5 font-mono">node content/sync-story.js</code> to push changes to the headset and this page at once.
        </p>
      )}
    </div>
  );
}
