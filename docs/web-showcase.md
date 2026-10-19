# STRIKEFIT website: the viva click-order

One page. Do the setup once before the room fills, then follow the numbered list. About ten minutes at a relaxed pace.
Everything runs on this laptop against a local database; nothing here touches the production site or its database.

## Setup (two terminals, before you start)

```bash
# terminal 1: database (PGlite, no Docker), API on :4000 and the site on :5173, with 15 simulated fights already in it
cd web && npm run dev:safe
#   if 4000 or 5173 are taken:  PORT=4100 WEB_PORT=5273 npm run dev:safe     (then use :5273 / :4100 below)

# terminal 2, once: 10 study participants (ZZTEST-P01..P10) with consent, answers and fights, for the Lab's study section
cd web && npm run dev:seed-study -- --url http://localhost:4000

# terminal 2, at step 3: two fights at once, so the Live Arena has a player switcher to show (each ends by itself)
cd web
npm run simulate -- --url http://localhost:4000 --player ZZTEST-ann --level Easy --outcome victory --seconds 90 &
npm run simulate -- --url http://localhost:4000 --player ZZTEST-bo  --level Hard --outcome defeat  --seconds 100
```

Open <http://localhost:5173>. The simulated names start with `ZZTEST`; the simulator deletes its fights when it finishes.

**If the network or the server dies:** open <http://localhost:5173/?demo=1> (or press "Demo mode" in any footer). The whole site then runs
on recorded data with scripted fights looping on the Live Arena, no backend and no internet. A "Demo data" ribbon stays on screen;
"Exit demo" leaves. Refresh the recording the day before with `cd web && npm run demo:snapshot -- --url http://localhost:4000`.

## The click-order

| # | Where | What to do and say |
|---|---|---|
| 1 | `/` Landing | The headline rises word by word; the **swing ticker** loops a scripted 0 to 8 m/s swing and the tier colours change. Scroll: the **problem** card (a wrist flick does nothing, a full swing deals 29.6) → **How it works** (hover a step; the formula types in, the damage curve draws itself, drag the slider) → the **data path** (Quest → Node API → Postgres → dashboard). |
| 2 | `/#levels` | Three levels with their monsters and the speed thresholds of each. Move the cursor over a card: it tilts 4 degrees at most. |
| 3 | `/dashboard/live` Live Arena | Start the two simulated fights. **LIVE** badge, both players side by side. Click a **player tab** to look at one fight; reload: the choice is remembered. Watch a **full-power hit**: the gauge punches and a "+damage" floats up from the monster's health bar; a **clash** pulses a ring. Press **F** for full screen (projecting), **Esc** to leave; **Copy link** puts this exact view on the clipboard. |
| 4 | `/dashboard` Overview | The headline figures count up, each with a "▲ 12% vs previous 7 days" chip and a seven-point trend line. Switch the range to 7 days. |
| 5 | **Ctrl/Cmd + K** | Type `calm` (toggle Calm mode), `hard` (jump to Level 3 sessions), `export` (CSV with a toast). **G** then **L** jumps to the Live Arena; **?** lists every shortcut. |
| 6 | `/dashboard/player/Ava` | One player: journey, **effort fade** (did the swings slow toward the end?), fingerprint. |
| 7 | Open any session | The **replay**: scrub through the fight and see what each swing did. |
| 8 | `/dashboard/lab` Thesis Lab | Return rate, RPE against swing speed, level table; then **Playtest study**: SUS (73.3, "good", against the 68 benchmark), enjoyment, intent to play again, fade per participant. Point out the **small-sample** flag and the "what this tells us" sentence under every chart. **Export study CSV**: participant code joined to the answers, no IP or device column. |
| 9 | `/study` | What a participant sees: plain-language consent (the box is required), a code like P07 that is also their headset name, the before form. Stop at the form; do not submit on the demo laptop's real database unless you want a P-code used (codes are P01 to P20). |
| 10 | `/about/method` | Research question, the effort formula and thresholds, why swings are clamped at 8 m/s, the calorie method (MET × 70 kg, **labelled an estimate**), study design, limitations, ethics. **Print / Save as PDF** gives a clean A4 document. |
| 11 | Footer, "Demo mode" | Show the safety net: the same pages on scripted data with the network off. |

## What is real and what is not (say this out loud)

- Real: every number from a fight the headset sent. Swing speeds are clamped at 8 m/s, the game's own cap.
- Simulated: the `ZZTEST` fights and the demo data. They exercise the pages; they are not results.
- Estimated: calories (assumed 70 kg, assumed MET 3 to 6). No heart rate is recorded.
- Small: the pilot is at most 20 adults. Nothing here is a finding about people in general.

## Checks (all run on 2026-10-19, local only)

**Lighthouse, mobile profile** (simulated slow 4G, 4x CPU slowdown; production build served with gzip; median of 3 runs; Chrome for Testing 156).
"Before" is the site at the start of this round of work (commit `f74d67d`), "after" is the final build, both against the same dev data.

| Page | Performance | Accessibility | Largest paint | Layout shift |
|---|---|---|---|---|
| Landing `/` | 96 → 95 | 100 → 100 | 2.68 s → 2.74 s | 0.010 → 0.001 |
| Overview `/dashboard` | **85 → 95** | **97 → 100** | **4.27 s → 2.75 s** | 0 → 0 |
| Live Arena `/dashboard/live` | 91 → 91 | **97 → 100** | 3.35 s → 3.36 s | 0.003 → 0 |
| Thesis Lab `/dashboard/lab` | 95 → 92 | 100 → 100 | 2.88 s → 3.18 s | 0 → 0 |
| Study `/study` | (new) 99 | (new) 100 | (new) 1.97 s | 0 |

Landing and the Lab give a little back: the landing hero now carries a live swing ticker and a dot grid, and the Lab loads the study section.
Why the Overview was slow and what fixed it: its largest paint was the page title, which only painted after the sessions request and a
lazy chunk, and then painted again when the font swapped in. The title is now rendered by the page shell from the first frame, Inter is
self-hosted and preloaded, the part of the page under the headline figures is a separate chunk fetched near the viewport, and the events
list waits for an idle moment.

**Browsers.** Chrome for Testing, Firefox and WebKit (Playwright 1.64 builds) each opened the landing page, a scripted Live Arena, the command palette (Cmd/Ctrl + K),
the Overview, the Lab's study section, the study consent screen (button locked until the box is ticked) and the method page at 1440 and 375 px: no
console errors, no horizontal overflow, Inter loaded, the ticker, the damage curve and the marquee animating. (WebKit has no
`requestIdleCallback`, so the lower half of the landing page appears about 2 s after load, by design.) Safari on a real iPhone was not tested.

**Keyboard.** Tab order and a visible focus ring on every stop checked: landing top (14 stops), dashboard shell (22). The palette opens with focus in
its input, arrows and Enter choose, Esc closes (also in the first frames); G then L jumps to the Live Arena; the player switcher moves with the arrow
keys and keeps focus; F and Esc enter and leave full screen; the study consent and forms work with keyboard alone (radio groups move with the arrows).

**Charts.** Every chart and graphic found on the landing page and the dashboard pages (54 SVGs on 12 pages) has a role and a text label, and the
data charts have a table or a "show as a table" version for screen readers. Lighthouse accessibility is 100 on all five pages above.

**Tests.** `cd web/frontend && npm test` (157) and `cd web/backend && npm test` (131) pass.

## Screenshots

`docs/screenshots/*-1920.png` (1920 x 1080) and `*-375.png` (375 px wide): landing hero, problem, how it works, data path and levels; Overview;
command palette; Thesis Lab study section; study consent; method page; Live Arena with both players, Level 1, Level 2 and Level 3, and full screen.
They are taken in demo mode (scripted fights, names Ava and Ben) with the ribbon hidden for the picture, so they can be regenerated with no server.

## Not verified

- A real headset session against this build (the website was checked with simulated and scripted fights only).
- Safari on a real device and Firefox on Windows or Android.
- The consent wording has not been checked by an ethics board; the reference field on the consent screen is empty until you fill it in
  (`web/frontend/src/content/studyContent.js`, `ETHICS_REFERENCE`).
- The citations on the method page were written from memory and are marked "verify".
- The MET range (3 to 6) for active VR sword play is an assumption, not a sourced figure.
