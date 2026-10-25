# Content you edit without touching components

## Gameplay walkthrough: `showcaseShots.js`

The "Inside the headset" section on the landing page shows five frames. Each frame is an image or a short clip. Until a file
exists, the page shows a designed placeholder card, so nothing looks broken.

### Where to drop the files

```
web/frontend/public/showcase/
    showcase-1-start.jpg        <- put files here
    showcase-2-combat.mp4
    showcase-2-combat.jpg       <- poster (still) for that clip
```

Anything in `public/` is served from the site root, so `public/showcase/showcase-2-combat.mp4` is `showcase/showcase-2-combat.mp4`
in the config. Then open `src/content/showcaseShots.js` and, for that shot, set:

```js
{ type: "video", path: "showcase/showcase-2-combat.mp4", poster: "showcase/showcase-2-combat.jpg", alt: "...", ... }
{ type: "image", path: "showcase/showcase-1-start.jpg", alt: "...", ... }
```

| field    | meaning |
| -------- | ------- |
| `type`   | `"image"` (jpg, png, webp, avif, gif) or `"video"` (mp4, webm). Default `"image"`. |
| `path`   | File under `public/`, no leading slash. `null` keeps the placeholder. |
| `poster` | Videos only: a still shown before the clip plays, and instead of autoplay when the visitor prefers reduced motion. |
| `alt`    | What the shot shows, for screen readers. Required. |

### Recording tips

- 16:9, 1920×1080 (the frame is `aspect-video`; other ratios are cropped to fill).
- Clips: H.264 MP4, no audio track needed (they always play muted), 3–8 s, under 4 MB each. Loops automatically.
- Prefer a clip over a GIF: a GIF cannot be paused for visitors who ask for reduced motion, and is several times larger.
- A video only counts if the server answers with a `video/*` content type; a wrong file name falls back to the placeholder.
- A clip on show downloads with the page, starts by itself and loops; scrolling does not stop it. A walkthrough frame that is not
  on show fetches only its metadata.

Check it with `npm run dev` and scroll to the section; each tab swaps the frame.

### The Level 2 demo clip (`DEMO_CLIP`)

Frame 02 ("Swing to hit") and the Level 2 card in the Levels section play one clip: `public/video/level2-demo.mp4` (H.264,
720p), `level2-demo.webm` (VP9, offered first) and `level2-demo.jpg` (poster). It is scripted footage from the Unity Editor, and
the page says so on the clip ("Scripted demo footage recorded in the Unity Editor (not a live player)", with the level and the
guardian's name from `story.json`). How it was recorded and encoded, and how to swap in real Quest footage:
`docs/demo-clip.md`.

- `webm` (optional): a VP9 copy, tried before the MP4.
- `seconds` (optional): how long the walkthrough stays on that frame (the clip's length) instead of 6 s.
- `credit: "demo"`: shows the demo credit line on the clip.

Playback rules (`components/ui/DemoVideo.jsx`, `lib/videoCoordinator.js`): muted, looping, inline, and continuous like a product
page's hero clip: it starts as soon as the page has it (in the walkthrough, while its frame is on show) and keeps playing while the
visitor scrolls. Every clip has its own play / pause button; a pause lasts until Play is pressed, and pressing Play on one clip pauses
the clip the visitor started before. "Expand" takes over from the page copy while it is open. It never starts by itself under reduced
motion, Calm mode or Data Saver: the poster shows with a Play button instead, and only the metadata is fetched. A missing file, or
one that fails to load, shows the placeholder card in the same box.
