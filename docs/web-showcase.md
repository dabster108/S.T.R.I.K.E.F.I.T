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

**Stories for the engagement features** (local dev database only; the player name must start with `ZZTEST`). Each ends with a LIVE fight, so
keep the Live Arena open (`/dashboard/live`, and the player's page in a second tab) while it runs. These fights are kept, so the pages have history;
add `--cleanup` to delete the player's fights when the story ends.

```bash
cd web
npm run simulate -- --url http://localhost:4000 --scenario pb     --player ZZTEST-pb     # two weaker fights, then a live fight that sets records
npm run simulate -- --url http://localhost:4000 --scenario rankup --player ZZTEST-rank   # quiet fights up to a few points short of the next rank, then one that crosses it
npm run simulate -- --url http://localhost:4000 --scenario phase  --player ZZTEST-phase  # a Level 3 fight through the boss's 66% and 33% HP breaks
node backend/scripts/rank-distribution.js --url http://localhost:4000                     # how many players sit in each rank
```

| Scenario | What to watch |
| --- | --- |
| `pb` | The result moment shows the **New PB** banner with each metric, its new value and the old one, with light confetti (24 pieces, 1.5 s; none in Calm mode). The player's battle page shows the same banner for the last fight only, and the history has a **PB** chip on the fights that set a record. In the commentary: "ahead of / behind the personal best pace" |
| `rankup` | A **Rank up** banner on the result moment (Rookie to Brawler), the badge next to the player's name changes, the player page's ring moves, and the activity list gains "<Name> reached Brawler" |
| `phase` | The commentary announces "The Tyrant enters phase 2!" at 66% HP and "…phase 3, enraged!" at 33% |

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

## The engagement features: where to find them

| Feature | Where | What to say |
| --- | --- | --- |
| **Live commentary** | Live Arena (also full screen, `F`) and `/watch`: a large caption bar under each fight and a **Commentary** log of the last 20 lines | One line at most every 1.2 s, three waiting, the most important first (result, phase change, monster nearly dead, player low, full-power swing, wind-up, combo, clash). It uses the monster's real name and attack names and the level's own full-power speed. **Commentary: on/off** is remembered in the browser; **Spoken commentary** (the browser's own voice) is off until switched on and reads the first fight only |
| **Rank ladder** | Player page header (ring and badge), leaderboard rows, next to the name on the Live Arena | Rookie 0, Brawler 300, Warrior 1,200, Guardian 3,500 effort points: counted swings x 1, full-power swings x 2, victories x 40, Level 3 victories 60 more. The thresholds live in `backend/config/ranks.js`; the pages read them from the API |
| **Personal-best alerts** | Live Arena result moment, battle page, **PB** chip in the Recent battles, Sessions and player history lists | A record beats everything the player did earlier on the same level: peak swing speed (clamped to 8 m/s), damage, score, effort fade. A first fight on a level has nothing to beat; equal values and abandoned fights never count |
| **STRIKEFIT Wrapped** | `/wrapped/<player>` (a button on the player page) | A week (Monday to Sunday, Kathmandu time) as five slides: swings, fastest swing, favourite monster, best day, effort personality. Tap or arrow keys; auto-play is off; **Save this slide as PNG** (1080 x 1350) and **Copy link**. The card shows the pseudonym only |
| **Milestone timeline** | Player page, under the badges | First fight, first victory (and on each level), first full-power swing, 100 / 500 / 1,000 swings, 10 fights, 3- and 7-day streaks, each rank reached, with the date; the next three dimmed with a progress bar. Swing milestones use the running total at the time of each session |

Demo mode (`?demo=1`) works with all five: the recorded snapshot carries the rank, milestones, records and every week of Wrapped for the demo players, and the scripted
fights finish with **sample** records and one rank-up (scripted values, like everything in demo mode). Refresh the recording with `npm run demo:snapshot`.

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

**Tests.** `cd web/frontend && npm test` (157) and `cd web/backend && npm test` (131) pass. After the engagement features: frontend 204, backend 183.

### Engagement features (checked 2026-10-20, local only)

Everything below ran against the local PGlite dev database with the simulator (`--scenario pb | rankup | phase`) and in demo mode. Nothing touched production, and nothing is pushed.

**Browser checks** (Chrome for Testing 156; Firefox and WebKit for the Wrapped page, the player page and a live fight):
- *Commentary:* captions type in and keep at least 1.2 s apart (measured from the log's own timestamps); two fights on stage each have their own caption and log; speech is off by default, speaks only the first fight when turned on and stops when turned off (checked with a spy on `speechSynthesis`: no audio was listened to); hide is remembered across a reload; full screen and `/watch` (the bar under the player) show it; reduced motion and Calm mode show plain text; the idle nudge ("Swings under 1.6 m/s don't count" on Level 3), victory, defeat, abandoned, the Level 3 phase 2 and 3 lines, "One more hit!" and the Ghost Pace lines all appeared in real fights.
- *Rank:* a `rankup` fight changed the badge next to the player's name, moved the ring on the player page and added "<Name> reached Brawler" to the activity list; re-posting the finished fight twice left exactly one such row. The leaderboard shows a badge on all 18 rows from one request.
- *Personal bests:* the banner, the PB chips and the confetti (24 pieces, 1,500 ms, only `transform` and `opacity`, gone after 2 s); in Calm mode the banner shows and no confetti piece exists; the first fight on a level shows no banner; an abandoned fight with huge numbers sets none.
- *Wrapped:* five slides by arrow keys, tap zones and the progress bars; auto-play off by default and advancing when on; week picker and next / previous week; PNG export at exactly 1080 x 1350 in all three engines; copy link; no fights, unknown player, server unreachable and not-enough-data states; no horizontal overflow at 375 px.
- *Milestones:* 12 reached and 3 upcoming for a player with history, the dates checked by hand against the raw sessions; the "No milestones yet" state; the error state; it updates by itself when a fight ends.
- *Demo mode:* all of the above run from the recorded snapshot with zero real network requests.

**Lighthouse, mobile profile** (same method as above, Chrome for Testing 156, production build, median of 3; before = the site at commit `463b782`, after = the finished build, same dev data):

| Page | Performance | Accessibility | Largest paint | Layout shift |
|---|---|---|---|---|
| Landing `/` | 95 → 95 | 100 → 100 | 2.73 s → 2.73 s | 0.0006 → 0.0006 |
| Live Arena `/dashboard/live` | 91 → 91 (runs 89, 91, 91) | 100 → 100 | 3.34 s → 3.34 s | 0 → 0 |
| Player page `/dashboard/player/ZZTEST-ann` | **86 → 86** (runs 89, 86, 86) | 100 → 100 | 3.97 s → 3.93 s | 0.006 → 0.009 |

The player page was already under 90 in this setup before any of this work, so the "nothing below 90" line is **not met for that page**, before or after. Without the network
throttling it is fast (first paint 0.1 s, largest paint 0.26 s); the simulation counts the page's many small script chunks and its API calls (about 20 on load) in its estimate.
Two things that did not move the score were tried and removed: starting the live stream and the session list when the browser is idle, and asking for the page's chunk earlier.
What did stay: the page draws its heading from the URL on the first frame, and the rank card reserves its size, so nothing jumps when it fills.

## Screenshots

`docs/screenshots/*-1920.png` (1920 x 1080) and `*-375.png` (375 px wide): landing hero, problem, how it works, data path and levels; Overview;
command palette; Thesis Lab study section; study consent; method page; Live Arena with both players, Level 1, Level 2 and Level 3, and full screen.
The engagement features: `commentary-live-arena` (the caption bar and the Commentary log), `pb-banner-result` (the result moment with the New PB and Rank up banners),
`rank-player-header` (badge and ring), `rank-leaderboard`, `milestones` (the timeline) and `wrapped-1-swings` to `wrapped-5-personality` (every slide).
They are taken in demo mode (scripted fights, names Ava and Ben) with the ribbon hidden for the picture, so they can be regenerated with no server.

## Not verified

- A real headset session against this build (the website was checked with simulated and scripted fights only).
- Safari on a real device and Firefox on Windows or Android (Firefox and WebKit builds from Playwright were checked for the new pages).
- The commentary bar drawn **over the headset video** on `/watch`: it needs a live WebRTC stream, which cannot be produced without a headset. The same bar was checked under the player (no video) and in the Live Arena.
- That the spoken commentary is audible: only the calls to the browser's speech API were checked.
- The guard's "70% absorbed" in the commentary is the code default (`MonsterAI.defendDamageMultiplier` = 0.3); a scene override was not ruled out.
- Rank thresholds against real players: only simulated data exists (14 dev players: Rookie 1, Brawler 12, Warrior 1, Guardian 0). Brawler (300 points) is reached in one or two fights.
- "The Marathoner" is the most common personality: the Stamina axis is 100 plus the fade percentage, so anyone who does not slow down scores 100.
- The consent wording has not been checked by an ethics board; the reference field on the consent screen is empty until you fill it in
  (`web/frontend/src/content/studyContent.js`, `ETHICS_REFERENCE`).
- The citations on the method page were written from memory and are marked "verify".
- The MET range (3 to 6) for active VR sword play is an assumption, not a sourced figure.
