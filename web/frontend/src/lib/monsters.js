// Monster portrait config: one place for the rendered images, the per-level glow and the "how to beat it"
// tips, so MonsterPortrait.jsx, the /monsters page and the landing Monsters section all read the same data.
//
// Images: rendered from the real Unity models (Assets/Models/Monsters/...) by web/tools/render-portraits
// (three.js + FBXLoader, headless Chrome/Brave, not shipped — see that folder's README). Levels 1 and 2 are
// posed from a frame of the model's own animation (Level 1: its embedded dance clip; Level 2: an attack clip
// borrowed from a same-skeleton Mixamo FBX, see the README for why that's valid). Level 3's model carries no
// posed clip usable outside Unity (its combat clips are baked Unity .anim assets, not standalone FBX), so it
// is rendered in its bind pose ("T-pose"), cropped to a close bust shot so that reads as a portrait and not
// as a stiffly off-model pose.
//
// Tips and facts: Assets/Documentation/context.md and project_progress.md (2026-10-02 / 2026-10-09 entries),
// as of 2026-10-11, tuning in progress — the same rule lib/game.js follows.

export const MONSTER_IMAGES = {
  Easy: {
    width: 1600,
    height: 2000,
    aspect: 0.8,
    placeholder:
      "data:image/webp;base64,UklGRrwBAABXRUJQVlA4WAoAAAAQAAAAFwAAHQAAQUxQSCwBAAANX6AgABmG/o3ZX4mISPDVD0nStlVO9YFVxOH/k6oeB9pyrCFZwHUF6pyeQIZegcwcCe0GctuA56w4ov9BYPuylH//xwkEk7HG55YAZmP7m6GOJYCtXcxNyGOJ0diF6+VbOpYWefO0sUK95tOpmvV2qQcgnc6HWb9baBD507+RVk8bgU77fD1BL3r2ZMTxdOhmZiogpMgtejLMHcGnGBSEASnlowJAahiQYmAjhB86lXLu2GmiSUIoWT1aLf2rmrymPayddiF6YB/JGPn2nTz7FGbayljiPQ0Bomdf64Q0XunRyNRaZCEUu7mop/EWDFq5O1lOrUJ57Za9LK3tY+oets+9LLnUQ1bueesQYzkdC623Syvi20/9LZ3bOQ2o1E77+7WbAkCqLbCbCwBWUDggagAAADAEAJ0BKhgAHgA+7WqrT6mmI6IwGAgBMB2JYwClrBVFkJx6/428paoiAAD+78kkOa16bZNloRnuR8/Fl6qwKq6M+5iRWEhdQsrQ6cG7qbo1xSTiiXxkyJ0vCWatteaPYt6cHdW0TcIKAAA=",
    webp: { 480: "/monsters/level1-480.webp", 960: "/monsters/level1-960.webp", 1440: "/monsters/level1-1440.webp" },
    avif: { 480: "/monsters/level1-480.avif", 960: "/monsters/level1-960.avif", 1440: "/monsters/level1-1440.avif" },
  },
  Medium: {
    width: 1600,
    height: 2000,
    aspect: 0.8,
    placeholder:
      "data:image/webp;base64,UklGRgACAABXRUJQVlA4WAoAAAAQAAAAFwAAHQAAQUxQSCgBAAANZ6CobSOmgz/dfm+CiIgEn1NVBbjZ9sZtfqqCXBHMpXQmlav0uUDOAa4NvAfUga6cTrRzlToWSOQskL0yRET/86GUYYj4d+gqzSMAAsopa00iKoA/cAZKGpVgpiEYo21aA6c8m8+zzW6IOaKd3T67zV/fKUY0e7o/1+F3l9OWwmq1eB32faS1Th5flmiHLmnU8ePtvKrbJqkqh8UBU4wpEqH0jiojdRRcWSdVwZlEkPE2j/CMjbDlqk409gaAy0zajhB8RcyM5jsVfMCw1kLbJOydGCOiMRZFcOBcUy5W2XvIV1oTOwP2kDS0NJs6awz0p++LcF6yZdTdb2+vb+YOQD3sd3S9XHgAqd13xex27gDZpK6TMD87AKCpWeeT2cwAIE212mnIAFZQOCCyAAAAEAUAnQEqGAAeAD7tZqZNqaYjIjAYDAEwHYlkAL/ti0tYJ6pRJavOlUEejmlB2B2gAAD+7pjG2mVXQ8xC2X7jowrksIFxvdr1qk1IDtYXRN6c8/L4BoFPCsakEHOm7yUu4+cGq0zHRkVQ4HRtp5igqo3ZaiiW5iEOt1TU+RiUR8cLheewCEU+mrDagTDMrK7vzAcYs5OatMk32wA/n4KtVqhdqp/OshgLMFEHyuRxDqAAAA==",
    webp: { 480: "/monsters/level2-480.webp", 960: "/monsters/level2-960.webp", 1440: "/monsters/level2-1440.webp" },
    avif: { 480: "/monsters/level2-480.avif", 960: "/monsters/level2-960.avif", 1440: "/monsters/level2-1440.avif" },
  },
  Hard: {
    width: 1600,
    height: 2000,
    aspect: 0.8,
    placeholder:
      "data:image/webp;base64,UklGRpoBAABXRUJQVlA4WAoAAAAQAAAAFwAAHQAAQUxQSBYBAAANuQpE9D80chvbdu1GXv3X6r37ZA/rQfHCYXywmAq3ABYQMQET8Czw3Au7+0s/fPd01e6u3P3TXN88PV289Mp57ZzlfoNFBrPxPsRL2DLsLpPBIAUkUDuDVw2uq431Vuw404zJcWUHHyGqmdLVRYQ3BGqmGXGPK9A/oYgLQxWFSiVGhwWQyicKVVoGqEQ+QRB3uZ2JEj5ghONypItpIninRVmPYVczjvAZA8qRw0UTY+x/GuC0G84UsNukJYIElJYPETQQIOyG+CBRNIQd6SA9ktEUwKYnkweBYhhAd0NoWZBxCNlwhwFbYC8LbW3IYEecjVsZsYA0jNtkYzYMMgBph8Q041ZuAxIMoAABMzYEcgRCyCAlAVZQOCBeAAAAkAMAnQEqGAAeAD7tbK9QqaYkIqgKqTAdiWcAxNgYjLgGnJqxgAD+8CKzpU6UlvMhDwZ+aqOkNDcwKt0kvSwp2ooZs76HDvUlVMC17lu505HBATEXGa6IrJQlUIAAAA==",
    webp: { 480: "/monsters/level3-480.webp", 960: "/monsters/level3-960.webp", 1440: "/monsters/level3-1440.webp" },
    avif: { 480: "/monsters/level3-480.avif", 960: "/monsters/level3-960.avif", 1440: "/monsters/level3-1440.avif" },
  },
};

// Aura, outline and ember colours per level — not game data, just the portrait's look.
export const MONSTER_GLOW = {
  Easy: { name: "Yellow-green", aura: "#c9e265", core: "#eefab0", outline: "#a8d12e", ember: "#d9f27a" },
  Medium: { name: "Violet, magenta core", aura: "#9b6bf2", core: "#e59bff", outline: "#7c3aed", ember: "#c084fc" },
  Hard: { name: "Orange-red, electric blue", aura: "#ff5a36", core: "#ffd166", outline: "#ff2d2d", ember: "#4fd6ff" },
};

// "How to beat it": from the real guard / attack-table design in context.md, not invented.
export const MONSTER_TIPS = {
  Easy: [
    "Attacks are slow and clearly telegraphed — wait for the wind-up instead of swinging early.",
    "It raises its guard from time to time; a guarded hit does no damage, so keep swinging at an open side.",
    "This is the easiest fight on purpose: use it to find a swing speed that reaches “full power” before moving on.",
  ],
  Medium: [
    "It picks from seven attacks and varies them, including a twin-strike combo and a leaping cleave — don't expect a single pattern to repeat.",
    "Below 40% health it is forced into its special attack once; expect a harder opening right after.",
    "Its guard remembers your last few swings and leans toward blocking a repeated angle, so vary your swing direction rather than always striking the same side.",
  ],
  Hard: [
    "Two axes, 14 attacks: 11 melee (including an unblockable ground slam that must be dodged, not blocked) and 3 thrown from range.",
    "Three phases: it gets faster and its telegraphs get shorter after 66% and again after 33% health, and the last phase is enraged.",
    "Like Level 2, its guard adapts to a repeated swing angle or side — and after three blocks in a row the next swing is guaranteed to land.",
  ],
};

export const MONSTER_LABEL = { Easy: "Easy", Medium: "Medium", Hard: "Hard, final boss" };
