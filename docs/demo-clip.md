# Landing-page demo clip (Level 2)

**Date:** 23 October 2026. A 21 s scripted fight against the Crimson Warden, recorded in the Unity Editor and shown on the landing
page. It is labelled on the page as "Scripted demo footage recorded in the Unity Editor (not a live player)".

## 1. Capture (Unity)

- **What records it:** `Assets/Scripts/Manager/CinematicCapture.cs`, an Editor-only component whose whole body is inside
  `#if UNITY_EDITOR`. It records through Unity Recorder 5.1.7 via `Assets/Editor/CinematicCaptureRecorder.cs`. The output is the
  Game view at 1920x1080 and 30 fps, H.264 MP4 with no audio, written to `Captures/Demo/level2_demo_raw.mp4`. That folder is in
  `.gitignore`.
- **How to run it:**
  1. `CinematicCaptureRecorder.Arm()` (menu: **StrikeFit > Demo > Arm**).
  2. Open `Level2_Medium` and press Play.
  3. `CinematicCapture.Run()` (menu: **StrikeFit > Demo > Run**).
  4. Poll `CinematicCaptureRecorder.Status()` until it reports DONE.

  Arming redirects `SessionReporter` to `http://127.0.0.1:9` from the first scene load, so its wake-up ping and the battle's
  session events go nowhere; the redirect is restored when the run ends. No demo session reaches the live server.
- **Cameras:** an over-the-shoulder camera at head height shows both swords and the monster, then a clean cut to a wider side view
  for the clash and the counter, then back. Both cameras skip the in-headset damage vignette, and the side camera skips the HUD,
  which is unreadable edge-on. The HUD, damage numbers and hit effects stay in shot. The Expert overlay, debug readout, pause hint
  and name picker are hidden.
- **The fight, all through the game's own code:** the swings move the real weapons through `HitDetection`. The monster's own
  attack picks are held off by parking its cooldown, as `DefenseHarness` does, and the cooldown is opened only for the forced
  attacks.

| t (s) | Beat | Result in the recorded take |
|---|---|---|
| 3.0 | Executioner Slam forced: wind-up and telegraph | red telegraph and fire |
| 4.4 | Sidestep out of its 35 degree arc | 50 degrees off its aim; not hit |
| 4.6 | Medium chop in its recovery | 19 damage, MEDIUM |
| 4.8 | Full-power chop with the other sword | 29 damage, FULL |
| 8.9 | Guard (`MonsterAI.BeginGuard`), then a swing | "BLOCKED 7" (70 % reduction) |
| 11.8 | Horizontal Sweep into the crossed swords (side view) | weapon clash, sparks and colour flash |
| 12.9-14.1 | Counter (`MonsterAI.BeginCounter`: Twin Strike) | hits the player twice |
| 16.3 | HP set to 16 % (wound tint), Overhead Chop dodged | not hit |
| 17.3 | Final full-power chop | 28 damage, FULL; HP 16 % to 12 % |

  The fight is not perfectly repeatable: the AI, the idle animation and `MonsterTactics` vary from run to run. The table is
  this take's log. A re-take may need a retry if a swing meets the axe instead of the body.

- **Checked:**
  - 21.000 s, 1920x1080, 30.00 fps (634 frames), avc1, no audio track, 20.99 MB (AVFoundation; ffprobe was not available).
  - Console: no errors. The only warnings were the expected failed sends to `127.0.0.1:9` and the ISDK telemetry notice.
  - Scenes and prefabs unchanged (git status before and after).
- **Stills:** `docs/screenshots/demo/level2_demo_*.jpg` (six frames).

## 2. Encode for the web

`bash web/tools/encode-demo-video.sh` needs ffmpeg (`brew install ffmpeg`). It writes these to `web/frontend/public/video/`:

- `level2-demo.mp4`: H.264 High, 1280x720, 30 fps, no audio, `+faststart`, two-pass at a bitrate worked out from the clip length
  so it stays under 4 MB.
- `level2-demo.webm`: VP9 at the same size.
- `level2-demo.jpg`: the poster, under 150 KB.

The script prints each size, codec and resolution, and fails if a limit is broken.

## 3. On the page

See `web/frontend/src/content/README.md`. `DEMO_CLIP` in `src/content/showcaseShots.js` drives frame 02 of "Inside the headset" and
the Level 2 card.

## Replacing it with real Quest footage

1. Record on the Quest (casting or the headset's own recorder), trim it to about 20 s, and keep it 16:9.
2. Run `bash web/tools/encode-demo-video.sh <your file> <poster second>`. It writes the same three file names, so no code changes.
3. In `DEMO_CLIP`:
   - Change `note` (the clip will no longer be Editor footage).
   - Update `seconds` and `alt` to match the new clip.
   - Keep `level` if it is still a Level 2 fight.
