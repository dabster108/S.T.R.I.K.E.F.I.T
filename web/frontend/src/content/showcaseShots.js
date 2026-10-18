import { LEVELS } from "../lib/game.js";

// ─── Gameplay walkthrough: the ONE place to edit the screenshots ─────────────
// To use a real capture, drop the image into web/frontend/public/showcase/ and set that shot's `path` to
// "showcase/<file name>" (the page resolves it from the site root). While `path` is null the page shows a designed
// empty-state card for that frame instead (gradient frame, frame number, caption); an image that fails to load falls
// back to the same card, so a typo can never show a broken image.
//
//   path     image under public/, or null for the empty-state card
//   alt      what the image shows (read by screen readers; also the empty-state card's caption)
//   tab      short name for the phone tab row
//   title    heading shown with the frame
//   caption  one sentence about what the game does; keep it factual
export const SHOWCASE_SHOTS = [
  {
    path: null, // e.g. "showcase/showcase-1-start.jpg"
    alt: "The name picker and Start button in front of the arena",
    tab: "Start",
    title: "Pick a name, press Start",
    caption: "Point at your name and pull the trigger, then press Start. The fight appears on the dashboard as it begins.",
  },
  {
    path: null, // e.g. "showcase/showcase-2-combat.jpg"
    alt: "Swinging at the monster in the arena",
    tab: "Swing",
    title: "Swing to hit",
    caption: `Dual hammers on Level 1, dual swords on Level 2. Swings under ${LEVELS.Easy.swing.min} m/s don't count; faster ones hit harder.`,
  },
  {
    path: null, // e.g. "showcase/showcase-3-defend.jpg"
    alt: "The monster winding up an attack",
    tab: "Read",
    title: "Read the monster",
    caption: "Every attack winds up before it lands, and while it raises its guard your hits deal 70% less damage.",
  },
  {
    path: null, // e.g. "showcase/showcase-4-impact.jpg"
    alt: "Damage numbers popping up where a hit landed",
    tab: "Impact",
    title: "Feel the impact",
    caption: "Damage numbers appear where the hit lands, with a heavier impact sound for bigger swings.",
  },
  {
    path: null, // e.g. "showcase/showcase-5-result.jpg"
    alt: "The end-of-fight result screen",
    tab: "Result",
    title: "Win or lose",
    caption: "The fight ends in victory or defeat, and the result lands on the dashboard within a second.",
  },
];
