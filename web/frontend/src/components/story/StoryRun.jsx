import story, { interludes, slides } from "../../lib/story.js";
import { Card, CardHeader } from "../ui/Card.jsx";

// "How a story run works": what the VR build does today (Story Mode in the headset's main menu), step by step. Written from what was
// built and tested, not from the plan: chapters cleared are kept per player name on the headset, the chapter card comes before
// the name picker, a defeat saves nothing, free play (Start Game) is separate. Counts come from the shared story file.
const STEPS = [
  ["Open Story Mode", "In the main menu, choose Story Mode. New Story starts at Chapter 1 (it asks first if you already have progress); Continue picks up at the next chapter you have not cleared; Watch intro replays the story slides."],
  ["The intro", `${slides.length} short slides, ${story.slideSeconds ?? 7} seconds each. Skip is on the laser for every slide, and the trigger shows the next one. A new story plays the intro once, then loads Chapter 1.`],
  ["The chapter card", "Before each fight a card shows the guardian: portrait, name, weapon, a line about the arena and a few lines of lore. Press Begin (or pull the trigger away from the buttons). The usual name picker, daily goal and Start follow."],
  ["The fight", "The same fight as in free play, with the same rules. The pause menu (B) works in the fight; it stays closed while a card, an interlude or a slide is up."],
  ["After a victory", "The chapter is saved for that player name. The end panel offers Continue story: a short interlude (with Skip), then the next chapter loads. After Chapter 3 the " + (interludes.ending.length || "closing") + " ending slides play, then a Story complete panel with your chapters cleared (3 / 3) and this fight's score, damage, hits, full swings and fastest swing."],
  ["After a defeat", "A defeat saves nothing: the panel shows a line of rest and offers Retry (the same chapter) or Story menu."],
  ["Free play", "Start Game in the main menu is unchanged: pick any level, no cards, no interludes, and it never changes your story progress."],
];

export default function StoryRun() {
  return (
    <section aria-labelledby="story-run-title" className="mx-auto mt-14 max-w-2xl">
      <Card>
        <CardHeader title="How a story run works" subtitle="Story Mode in the Meta Quest 2 build, three chapters, one guardian each" />
        <ol className="space-y-5 px-5 pb-6 pt-5" id="story-run-title">
          {STEPS.map(([title, text], n) => (
            <li key={title} className="flex gap-4">
              <span className="t-index mt-0.5 w-6 shrink-0 text-[var(--accent-strong)]" aria-hidden>
                {n + 1}
              </span>
              <div className="min-w-0">
                <p className="text-sm font-extrabold text-[var(--ink)]">{title}</p>
                <p className="mt-1 text-[0.95rem] font-medium leading-relaxed text-[var(--ink-secondary)]">{text}</p>
              </div>
            </li>
          ))}
        </ol>
        <p className="border-t border-[var(--border)] px-5 py-3 text-xs font-medium text-[var(--muted)]">Progress is stored on the headset, per player name; it is not sent to the dashboard.</p>
      </Card>
    </section>
  );
}
