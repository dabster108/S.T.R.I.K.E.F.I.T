// StoryView — /dashboard/story
// The game lore: the three atmospheric slides from content/story.json and the per-level
// guardian info (name + one intro line). Same data the headset's Story page shows.
// Read-only; no backend calls. The source of truth is content/story.json (sync-story.js).

import story from "../content/story.json";
import { Card, CardHeader, PageHeader } from "../components/ui/Card.jsx";
import { LevelBadge } from "../components/ui/Badge.jsx";
import { LEVELS, LEVEL_KEYS } from "../lib/game.js";

// Match the Unity level scene names to the backend keys used in story.json.
const LEVEL_SCENE_KEY = {
  Easy: "Easy",
  Medium: "Medium",
  Hard: "Hard",
};

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

function LevelCard({ entry }) {
  const meta = LEVELS[entry.key] ?? {};
  return (
    <div className="flex flex-col gap-3 rounded-xl border border-[var(--border)] bg-[var(--surface)] p-5">
      <div className="flex items-center gap-3">
        <LevelBadge level={entry.key} />
        <h3 className="text-lg font-semibold text-[var(--ink-primary)]">
          {entry.guardian}
        </h3>
      </div>
      {entry.intro && (
        <p className="text-sm italic text-[var(--ink-secondary)]">
          &ldquo;{entry.intro}&rdquo;
        </p>
      )}
      {(meta.weapons || meta.environment) && (
        <dl className="mt-1 grid grid-cols-2 gap-x-6 gap-y-1 text-xs text-[var(--ink-tertiary)]">
          {meta.weapons && (
            <>
              <dt className="font-medium text-[var(--ink-secondary)]">Weapons</dt>
              <dd>{meta.weapons}</dd>
            </>
          )}
          {meta.environment && (
            <>
              <dt className="font-medium text-[var(--ink-secondary)]">Arena</dt>
              <dd>{meta.environment}</dd>
            </>
          )}
          {meta.hp && (
            <>
              <dt className="font-medium text-[var(--ink-secondary)]">Guardian HP</dt>
              <dd>{meta.hp.toLocaleString()}</dd>
            </>
          )}
        </dl>
      )}
    </div>
  );
}

export default function StoryView() {
  const slides = story.slides ?? [];
  // Sort levels to Easy → Medium → Hard order.
  const levels = (story.levels ?? []).slice().sort(
    (a, b) => LEVEL_KEYS.indexOf(a.key) - LEVEL_KEYS.indexOf(b.key)
  );

  return (
    <div className="mx-auto max-w-2xl px-4 py-8 sm:px-6 lg:px-8">
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

      {/* ── Guardians ── */}
      <section className="mt-8">
        <Card>
          <CardHeader title="Guardians" subtitle="One per level — name and intro line shown on the headset's level card and pre-battle screen" />
          <div className="flex flex-col gap-3 px-4 pb-4">
            {levels.length === 0 ? (
              <p className="text-sm text-[var(--ink-secondary)]">No level entries in story.json.</p>
            ) : (
              levels.map((e) => <LevelCard key={e.key} entry={e} />)
            )}
          </div>
        </Card>
      </section>

      {/* ── Source note ── */}
      <p className="mt-6 text-xs text-[var(--ink-tertiary)]">
        Source of truth:{" "}
        <code className="rounded bg-[var(--surface-muted)] px-1 py-0.5 font-mono">
          content/story.json
        </code>{" "}
        — run{" "}
        <code className="rounded bg-[var(--surface-muted)] px-1 py-0.5 font-mono">
          node content/sync-story.js
        </code>{" "}
        to push changes to the headset and this page at once.
      </p>
    </div>
  );
}
