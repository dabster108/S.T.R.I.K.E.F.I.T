import { LEVELS } from "../lib/game.js";

// ─── Gameplay walkthrough: the ONE place to edit the screenshots and clips ───
// Drop a file into web/frontend/public/showcase/ and set that shot's `path` to "showcase/<file name>" (the page resolves it
// from the site root). See README.md in this folder for file names, sizes and encoding. While `path` is null, or the file is
// missing or not really an image/video, the page shows a designed empty-state card for that frame instead; a typo can never
// show a broken image.
//
//   type     "image" (jpg, png, webp, avif, gif) or "video" (mp4, webm). Defaults to "image".
//   path     file under public/, or null for the empty-state card
//   poster   video only: a still under public/ shown until the clip plays, and instead of it when the visitor prefers reduced motion
//   alt      what the shot shows (read by screen readers; also the empty-state card's caption)
//   tab      short name for the phone tab row
//   title    heading shown with the frame
//   caption  one sentence about what the game does; keep it factual
export const SHOT_TYPES = ["image", "video"];
export const shotType = (shot) => (shot.type === "video" ? "video" : "image");

export const SHOWCASE_SHOTS = [
  {
    type: "image",
    path: null, // e.g. "showcase/showcase-1-start.jpg"
    alt: "The name picker and Start button in front of the arena",
    tab: "Start",
    title: "Pick a name, press Start",
    caption: "Point at your name and pull the trigger, then press Start. The fight appears on the dashboard as it begins.",
  },
  {
    type: "video", // a swing clip works best here
    path: null, // e.g. "showcase/showcase-2-combat.mp4"
    poster: null, // e.g. "showcase/showcase-2-combat.jpg"
    alt: "Swinging at the monster in the arena",
    tab: "Swing",
    title: "Swing to hit",
    caption: `Dual hammers on Level 1, dual swords on Level 2. Swings under ${LEVELS.Easy.swing.min} m/s don't count; faster ones hit harder.`,
  },
  {
    type: "image",
    path: null, // e.g. "showcase/showcase-3-defend.jpg"
    alt: "The monster winding up an attack",
    tab: "Read",
    title: "Read the monster",
    caption: "Every attack winds up before it lands, and while it raises its guard your hits deal 70% less damage.",
  },
  {
    type: "image",
    path: null, // e.g. "showcase/showcase-4-impact.jpg"
    alt: "Damage numbers popping up where a hit landed",
    tab: "Impact",
    title: "Feel the impact",
    caption: "Damage numbers appear where the hit lands, with a heavier impact sound for bigger swings.",
  },
  {
    type: "image",
    path: null, // e.g. "showcase/showcase-5-result.jpg"
    alt: "The end-of-fight result screen",
    tab: "Result",
    title: "Win or lose",
    caption: "The fight ends in victory or defeat, and the result lands on the dashboard within a second.",
  },
];
