<div align="center">

# STRIKEFIT

**Design, Development, and Evaluation of a Virtual Reality-Based Gamified Fitness System**

A Meta Quest 2 combat game where damage depends on how fast you swing, with a web dashboard that records every fight.

[Live site](https://strikefit.vercel.app) · [Dashboard](https://strikefit.vercel.app/dashboard) · [Story](https://strikefit.vercel.app/story) · [Web docs](web/README.md) · [Credits](docs/CREDITS.md)

</div>

<p align="center">
  <img src="docs/images/hero.gif" alt="Ten second scroll through the STRIKEFIT site: the landing hero with a live arena preview, the How it works section with the swing-speed slider, and the three monsters" width="860">
  <br>
  <sub>The landing page, top to bottom. <a href="docs/images/hero-poster.png">Still frame</a>.</sub>
</p>

---

## What it is

STRIKEFIT is a thesis project from Softwarica College. It asks whether tying damage to swing velocity makes young adults aged 18 to 24 move more and stay engaged longer than an exergame that only checks whether a hit happened.

The name is a compound of two words, **strike** and **fit**. A swing only counts when real controller speed is behind it, and the only way to play well is to keep moving. The dots in S.T.R.I.K.E.F.I.T. are a stylisation, not an abbreviation.

### The problem

Most people in this age group know exercise is good for them and still do not do it consistently. Gyms feel repetitive, home workouts get boring within a couple of weeks, and apps that count reps or minutes give almost nothing back in the moment.

VR fitness games try to fix this by making exercise feel like play, but many detect that a gesture happened rather than how much effort went into it. Players learn the smallest motion that still counts, and the workout disappears. STRIKEFIT starts from a different question: what if the game could not be played well without real physical effort, because effort is what the game scores?

---

## How it works

**Damage follows swing speed.** The player holds a weapon in each hand and the game tracks each controller's velocity. A swing slower than 1.2 m/s does nothing. Above that, damage rises with speed until the 8 m/s cap, up to 2.5 times the base value on Level 1 and 2.2 times on Level 2. Each hit is labelled light, medium or full power by speed (Level 1: from 1.5, 3.0 and 5.0 m/s; Level 2: from 1.4, 2.8 and 4.6 m/s), and experience points scale with it.

**The monster fights back.** It attacks on its own timer with a visible wind-up, so there is a window to react. An attack only hurts if the player is still in range when it lands, and on Level 2 it also needs the player to be inside the swing arc, so stepping back or sidestepping works as a dodge. Every level has a defend state that reduces incoming damage. Which attack the monster picks changes as it loses health.

**Feedback comes through several senses.** Hits on the body, weapon clashes, taking damage and the finishing blow each have their own sound. Swings whoosh whether or not they land. Being hit gives a screen flash and camera shake. Health, score, timer and XP sit on a panel fixed in the world in front of the player rather than glued to the camera.

**Every fight is recorded.** The headset reports each session, with live hits and a final result, to the web backend. The dashboard shows it as it happens and keeps the history.

| Input | Action |
|---|---|
| Swing either controller | Attack with the weapon in that hand |
| Laser and trigger on a name | Choose who is playing (Player 1, Player 2, Player 3 or Guest) |
| Laser and trigger on **Start**, or **B** | Start the fight |
| **A** after Victory or Defeat | Replay the level |
| Step back or sidestep | Dodge |

There is no joystick locomotion. The player moves by stepping, which keeps the effort real.

---

## Monsters

<p align="center">
  <img src="docs/images/monsters.jpg" alt="The three guardians side by side: the Gatekeeper (Level 1), the Crimson Warden (Level 2) and the Harbor Tyrant (Level 3)" width="100%">
  <br>
  <sub>Rendered from the Unity models. Level 3 is a bust crop of the model.</sub>
</p>

| Level | Name | Weapon | Notable behaviour |
|---|---|---|---|
| 1 | The Gatekeeper | Sword | Slow, clearly telegraphed attacks. Raises its guard from time to time. The easiest fight on purpose. |
| 2 | The Crimson Warden | Two-handed axe | Faster, with a twin-strike combo and a leaping cleave. Forced into its special attack once when it drops below 40% health. Its guard leans toward blocking an angle you repeat. |
| 3 | The Harbor Tyrant (Twin-Axe Brute in the game files) | Two axes, one per hand | 14 attacks: 11 melee, including an unblockable ground slam that has to be dodged, and 3 thrown from range. Three phases, breaking at 66% and 33% health; the last is enraged. Its guard adapts to a repeated angle, and after three blocks in a row the next swing lands. |

Names come from [`content/story.json`](content/story.json), weapons and habits from [`web/frontend/src/lib/monsters.js`](web/frontend/src/lib/monsters.js). Behaviour is as built today; tuning is in progress.

---

## Levels

| Level | Difficulty | Environment | Player weapons | Full power from |
|---|---|---|---|---|
| 1 | Easy | Elementary Dungeon Pack Lite (low poly) | Dual hammers | 5.0 m/s |
| 2 | Medium | Elementary Dungeon Pack Lite, night sky | Dual swords | 4.6 m/s |
| 3 | Hard | Old Sea Port dock arena, lighting escalates with the fight | Mace, war flail or katana, picked up in the arena | Depends on the weapon |

All levels share the same combat scripts. A level changes the environment, weapons, monster and tuning, not the core loop.

---

<div align="center">

## Tools and technologies

<table>
  <tr>
    <th>Game</th>
    <th>Web</th>
    <th>Backend</th>
    <th>Hosting and tooling</th>
  </tr>
  <tr>
    <td align="center">Unity 6<br>C#<br>Universal Render Pipeline 17.5<br>Meta XR SDK 205<br>OpenXR 1.17<br>Input System 1.20<br>Mixamo clips<br>Meta Quest 2 (Android)</td>
    <td align="center">React 18<br>React Router 6<br>Vite 6<br>Tailwind CSS 4<br>Lucide icons</td>
    <td align="center">Node.js<br>Express 4<br>PostgreSQL<br>Server-Sent Events<br>Socket.IO<br>ws</td>
    <td align="center">Vercel<br>Render<br>Neon<br>Git and GitHub<br>Unity MCP</td>
  </tr>
</table>

</div>

---

## Web dashboard

<p align="center">
  <a href="docs/images/dashboard-overview.png"><img src="docs/images/dashboard-overview.png" alt="Performance overview: battles, win rate, latest score and active time, with today's challenges and an activity feed" width="49%"></a>
  <a href="docs/images/dashboard-live.png"><img src="docs/images/dashboard-live.png" alt="Live Arena with one simulated fight in progress: player and monster health bars, score, XP, hits, combo, swing speed and cheer buttons, beside an event feed" width="49%"></a>
</p>
<p align="center">
  <a href="docs/images/dashboard-player.png"><img src="docs/images/dashboard-player.png" alt="A player page with the Challenges card: three daily challenges and one weekly challenge with progress, and a next-step suggestion" width="49%"></a>
  <a href="docs/images/dashboard-scoreboard.png"><img src="docs/images/dashboard-scoreboard.png" alt="Leaderboard with a podium for the top three players and the full standings below" width="49%"></a>
</p>
<p align="center"><sub>Overview, Live Arena, a player page and the leaderboard (click for full size). Shot from a local backend with simulated fights for players named Player 1, Player 2, Player 3 and Guest. The Live Arena fight is simulated data.</sub></p>

What the dashboard does:

- **Live arena.** Every fight in progress, with both health bars, XP and score moving as the headset reports hits. Viewers can send cheers.
- **Estimated calories.** Calculated from fight length and swing strength with an assumed body mass. It is an estimate, not a measurement.
- **Challenges.** Daily and weekly targets per player, mastery stars per level and a suggested next step.
- **Scoreboard.** Best score per player, by week, all time or level.
- **Survey.** A short rating form for players after a session.
- Also: session history, player pages, compare, analytics, calendar and weekly recap, CSV export and the story page.

The backend is Node and Express on Render, storing every session and event in Postgres on Neon. The dashboard is React on Vercel and listens to the backend over Server-Sent Events.

---

## Run it locally

**Game.** Unity **6000.5.11f1** with Android Build Support, and a Meta Quest 2 in developer mode (Link or Air Link works for editor testing).

1. `git clone https://github.com/dabster108/vr-fitness-monster-battle.git`
2. Open the `VR-Gamification-Fitness` folder in Unity Hub. The first import takes a while.
3. Open a scene under `Assets/Scenes/` (`MainMenu`, `Level1_Easy`, `Level2_Medium` or `Level3_Hard`) and press Play. Without a headset the weapons rest in front of the camera so a scene can still be inspected.
4. To build for Quest, switch the platform to Android, put the scenes in the build list with `MainMenu` first, and choose Build And Run.

**Web.** Node.js 18 or newer.

```bash
# terminal 1: API on http://localhost:4000 (needs web/backend/.env)
cd web/backend && npm install && npm start

# terminal 2: site and dashboard on http://localhost:5173, proxying /api to the API
cd web/frontend && npm install && npm run dev

# tests
cd web/backend && npm test
cd web/frontend && npm test

# optional: play a simulated fight so the live views move
cd web/backend && npm run simulate -- --url http://localhost:4000 --level Medium --seconds 30
```

Environment variables, names only. Set them in `web/backend/.env` (git-ignored) or in the host's dashboard, never in the repository.

| Where | Names |
|---|---|
| Backend | `DATABASE_URL` (required), `PORT`, `ALLOWED_ORIGINS`, `REACTIONS_ENABLED`, `BROADCAST_KEY`, `LEADERBOARD_BLOCKLIST` |
| Backend, optional relay for the live broadcast | `CLOUDFLARE_TURN_KEY_ID`, `CLOUDFLARE_TURN_API_TOKEN`, `TURN_URLS`, `TURN_USERNAME`, `TURN_CREDENTIAL` |
| Frontend | `VITE_API_URL` (build), `BACKEND_URL` (dev proxy) |

The headset build points at the hosted backend through **SessionReporter, Base Url** (https is required on Quest). Deployment steps and API routes are in [`web/README.md`](web/README.md) and [`web/backend/README.md`](web/backend/README.md).

| Folder | Contents |
|---|---|
| `Assets/` | Unity scenes, scripts, prefabs, models, animation, audio and VFX |
| `web/backend/` | Express API, Postgres schema, live stream |
| `web/frontend/` | React site and dashboard |
| `web/tools/` | Local-only tools: monster portrait renderer, README media capture |
| `content/` | `story.json`, the story text shared by the game and the site |
| `docs/` | Credits and the images used in this README |

---

## Thesis details

*Applying Immersive Virtual Reality and Gamification Techniques to Deliver Interactive, Movement-Based Gameplay Experiences to Promote Physical Activity and Long-Term Fitness Engagement Among Young Adults Aged 18 to 24.*

Undergraduate thesis at **Softwarica College**, supervised by **Manoj Shrestha**. The audience is researchers studying motivation in exergames, VR users who want something more physically demanding than gesture-matching fitness titles, and young adults who find ordinary exercise routines hard to stick with.

What the game records for every session, which is the evidence the evaluation will use:

| Measure | What it shows |
|---|---|
| Hit count and damage dealt | How much and how hard the player swung |
| Damage per hit, sent live | Intensity of effort over the session, since damage scales with swing speed |
| Session duration | How long the player stayed engaged |
| Outcome and HP remaining | Whether the difficulty felt achievable |
| Repeat sessions per player | Whether players come back |

### Acknowledgements

Softwarica College, for the resources and environment to build this project, and Manoj Shrestha, for supervision throughout. Thanks to Gridness Studios for the Elementary Dungeon Pack Lite, the creators of the Old Sea Port pack, Adobe Mixamo for characters and animation, and the Unity and Meta XR SDK communities. Full attribution is in [`docs/CREDITS.md`](docs/CREDITS.md).

---

Development notes and progress: [`Assets/Documentation/project_progress.md`](Assets/Documentation/project_progress.md)
