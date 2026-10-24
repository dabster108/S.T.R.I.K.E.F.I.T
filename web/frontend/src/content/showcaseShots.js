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
//   webm     video only, optional: a VP9 copy offered before the MP4 (smaller; browsers that cannot play it take the MP4)
//   seconds  video only, optional: how long the walkthrough stays on this shot (the clip's length), instead of 6 s
//   credit   video only, optional: "demo" adds the demo clip's credit line (level, monster, how it was recorded) on the clip
export const SHOT_TYPES = ["image", "video"];

// The Level 2 demo clip (Unity Editor capture, see docs/demo-clip.md): encoded by web/tools/encode-demo-video.sh into
// public/video/. Used by the walkthrough below and the Level 2 card in the Levels section. To replace it with real Quest
// footage, encode the new recording to the same three file names (or change the paths here) and update `note`.
export const DEMO_CLIP = {
  mp4: "video/level2-demo.mp4",
  webm: "video/level2-demo.webm",
  poster: "video/level2-demo.jpg",
  level: "Medium", // LEVELS key: the credit names this level and its guardian (story.json)
  seconds: 21,
  note: "Scripted demo footage recorded in the Unity Editor (not a live player)",
  alt: "A Level 2 fight against the Crimson Warden: it winds up a slam, the player sidesteps and lands a medium then a full-power sword hit, a guarded hit does 7 damage, the weapons clash, it counters, and a last full-power hit takes it to 12% health",
};
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
    type: "video",
    path: DEMO_CLIP.mp4,
    webm: DEMO_CLIP.webm,
    poster: DEMO_CLIP.poster,
    seconds: DEMO_CLIP.seconds,
    credit: "demo",
    alt: DEMO_CLIP.alt,
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
