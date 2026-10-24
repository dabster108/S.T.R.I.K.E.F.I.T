# Engagement features: what exists (24 October 2026)

Read from the repository (web/backend routes and modules, web/frontend components, Unity scripts) before the in-headset engagement
pass, so nothing that already exists is built twice. "Headset" means the Unity game; "web" means the dashboard and its backend.

## Web (backend + dashboard)

| Feature | Where | Route(s) | Notes |
|---|---|---|---|
| XP, player level and titles | `backend/progression.js` (`TITLES`, `xpForLevel`) | `GET /api/players/:name/achievements` | Level from total XP; challenge and goal bonuses add `totalXpWithBonuses`. |
| Badges / achievements | `progression.js` `BADGES` | same | Computed from stored sessions, never invented. |
| Streaks (with a freeze) | `progression.js` `streakWithFreeze` | `achievements`, `/engagement` | Calendar days with a session, Asia/Kathmandu. |
| Ranks (Rookie 0, Brawler 300, Warrior 1200, Guardian 3500) | `backend/ranks.js`, `config/ranks.js`; `components/rank/` (RankBadge, RankRing, RankSummary, RankUpBanner) | `GET /api/players/:name/rank`, `GET /api/ranks?players=` | Effort points = counted swings (the session's `hitCount`) + 2 x full-power swings + 40 per victory + 60 extra per Hard victory. **Same formula as the brief.** |
| Daily and weekly challenges | `backend/challenges.js`, `engageStore.js`; `components/engage/Challenges.jsx` | `GET /api/players/:name/challenges`, `/challenges/history` | Three daily (easy, medium, stretch) + weekly, seeded by the date, median-scaled targets with safety caps; bonus XP stored once (`challenge_completions`). |
| Player-set daily goal (+75 XP) | `backend/goals.js`, `goalsStore.js`; `components/fitness/DailyGoal.jsx` | `PUT /api/players/:name/goal`, `GET .../goal/today`, `GET .../goals` | Also set from the headset (name picker). |
| Personal-best alerts | `backend/records.js`, `recordsStore.js`; `components/records/` (PbBanner, PbChip) | `GET /api/players/:name/records` | Per level, against earlier fights. |
| Best run per level | `engage.js` `pickBest`, `engageStore.best` | `GET /api/players/:name/best?level=&before=` | Best decided score (> 0) on one level, optionally "before this session". |
| Leaderboard | `backend/leaderboard.js`, `leaderboardRoute.js`; `LeaderboardView` | `GET /api/leaderboard` | Also shown in the headset menu (Scoreboard) and as the "Rank on this level" line. |
| Calendar, week card, mastery stars | `engage.js`; `components/engage/` (Calendar, WeekCard, Mastery) | `/api/players/:name/calendar`, `/week`, `/api/calendar` | |
| Milestones | `backend/milestones.js`; `components/milestones/MilestoneTimeline.jsx` | `GET /api/players/:name/milestones` | First fight / victory / full-power swing, swing counts, streaks, ranks. |
| Wrapped (week story) | `backend/wrapped.js`; `components/wrapped/`, `WrappedView` | `GET /api/players/:name/wrapped` | |
| Live commentary (AI) | `backend/commentaryAi.js`; Live Arena | `/api/commentary/*` | |
| Live arena, reactions / cheers, ghost pace | `stream.js`; `components/live/` | `/api/live`, `/api/live/react`, `/api/players/:name/best-pace` | |
| Activity feed, challenge a friend | `activityFeed.js`, `engage.js` | `/api/activity`, `/api/challenge/:sessionId` | |
| Study mode (consent, pre / post forms) | `backend/study.js`; `StudyView` | `/api/study/*` | |
| Thesis Lab (return rate, RPE vs speed, fade) | `backend/lab.js`; `LabView` | `GET /api/lab`, `/api/lab/sessions.csv` | Return rate is computed from sessions (player, start time). |
| Session feedback (enjoyment, Borg RPE) | `feedback.js` | `POST /api/sessions/:id/feedback` | The headset's result panel links to it. |

## Headset (Unity)

| Feature | Where | Notes |
|---|---|---|
| XP, swing tiers, combo | `Manager/XPManager.cs` | Per fight (`CurrentXP`, `HitsLanded`, `FullSwings`, `CurrentCombo`, `BestCombo`), posted with the session. No persistence. |
| Victory / Defeat panels | `UI/CombatUIController.cs` (`ShowVictory`, `ShowDefeat`, `ShowResultExtras`) | XP count-up, hits, full swings, blocks, damage; medal (victory), est. kcal, peak swing, best combo; "NEW PB" for fastest swing / energy / score against the backend's **overall** personal bests (`GET /api/players/:name`, not per level). |
| Outcome buttons | `UI/OutcomeMenuButtons.cs` | Play Again / Level Select (or Continue story / Story menu) / Exit Game, all the same size; "Rank on this level" line (`OutcomeRankLine`, leaderboard). |
| Player-set daily goal | `Manager/DailyGoal.cs`, `UI/PlayerNamePicker.Goal.cs` | One goal (kcal, full swings, active minutes or XP) chosen before a fight; the +75 XP is the backend's. |
| Name picker | `UI/PlayerNamePicker.cs`, `PlayerNameStore` (PlayerPrefs) | Known names, new player, rename. |
| Story Mode | `Combat/StoryDirector.cs`, `Manager/StoryProgress.cs` | Chapters, cards, progress on the headset. |
| Menu | `UI/MainMenuController.cs` | Story Mode, Start Game, Guardians, Scoreboard, Change Name, Exit; "Playing as <name>". |
| Colour compositor | `VFX/MonsterColorCompositor.cs`, `VFX/PlayerWeaponGlow.cs`, `VFX/Color/` | The only writers of monster / weapon MaterialPropertyBlocks. |
| Telemetry | `Networking/SessionReporter.cs` | Session start / end, hit, clash, weak swing, guard, pause and more, batched to `POST /api/events` and `POST /api/sessions`. |

## Built in this pass (on top of the above)

| Brief item | Built on |
|---|---|
| 1. Result screen with best-on-this-level, next goal, NEW BEST | `GET /api/players/:name/best?level=` + a per-player best in PlayerPrefs; the existing panels, buttons and HitBloodFx particle pool. |
| 2. Rank in the menu / HUD, RANK UP | The web's rank formula and thresholds (mirrored in `Scripts/Engagement/Core/RankLadder.cs`), `GET /api/players/:name/rank`. |
| 3. Today's three goals | `GET /api/players/:name/challenges` (daily cards) when reachable, three seeded local goals otherwise. |
| 4. Perfect clash | `HitDetection.WeaponClashed`, `MonsterAI` hit timing (read-only), the colour compositor, `CombatFeedback` haptics, `MonsterAI.BeginStagger`. |
| 5. Glow palettes by rank | `PlayerWeaponGlow` resting look (the tier / clash / hurt flashes are left alone: they carry meaning). |
| 6. Level 2 telegraph contrast, menu music slot | Colour compositor telegraph layer; `UI/MenuMusic.cs`. |
| 7. Engagement summary | `POST /api/events` (stored in `events`, payload kept verbatim). |

## How the backend stores the engagement summary (item 7)

- `POST /api/events` stores every event type it receives (`events` table: `type`, `player_name`, `message`, `payload json`
  verbatim). Nothing else changes; the backend has no special handling for the new type.
- The summary is sent **without a `sessionId`** (the fight's id travels inside the payload as `fightId`), because an event that
  names a session is folded into it: one for an unknown id creates a live session, one that arrives after an *abandoned* session
  resumes it as live, and payload keys such as `score`, `xp` or `hits` would overwrite the session's live state.
- Read it with `GET /api/events?type=engagement_summary&player=<name>` (newest first) or SQL on `events`
  (`WHERE type = 'engagement_summary'`); join to `sessions` on `payload->>'fightId'`.
- **Not yet in `/api/export/events.csv`**: that export inner-joins `sessions` on `session_ref`, which is null for these rows. A later
  web change (not made here: no `web/` edits in this task) can add them to the export and to the Lab.

## Verified in the Editor (24 October 2026)

Play mode through Unity MCP (`Assets/Editor/EngagementHarness.cs`, `StoryFlowTrace.cs`), every reporter on `http://127.0.0.1:9`.
Fights used real swings through the game's hit detection (CombatVerification "sim"), then a debug win or lethal hit.
Screenshots: `docs/screenshots/engagement-2026-10-24/` (before: `before/`).

| Check | Result |
|---|---|
| Result screen, win / lose, Levels 1-3 | Shown each time; Play Again largest; the HUD's own numbers are hidden behind it. |
| Abandon (pause > Level Select > Yes), Levels 2-3 | No result screen; result recorded as `abandoned`, not counted for a best; saved progress survives the scene change. |
| First fight on a level | "first score", no NEW BEST (L1, L2, L3). |
| NEW BEST | Best 300 > score 787: banner, sticker, sparks; next fight below it shows "score 516 more points (best 787)". |
| RANK UP | 280 > 341 (Brawler) and 1180 > 1282 (Warrior), once each; the next fights show none. Effort = swings + 2 x full + 40 per win (+60 Hard win), checked by hand. |
| Daily goals | Local goals (server unreachable): GOAL COMPLETE once per goal, bonus XP once; already-done goals never fire again. |
| Perfect clash (L2, L3) | 0.6 s early: not perfect. 0.07-0.08 s before the hit frame: +50 XP, flash, banner, one `perfect_clash` event; a second clash in the same attack gives nothing; stagger 0.03 s after the attack ends. |
| L2 telegraph rim | On for 18/18 telegraph frames, off when the flag is off and the frame after the telegraph. |
| Menu | Rank beside the name, progress panel, palette arrow Steel > Ember saved per player. |
| Story Mode + Free Play | new / defeat-retry / pause-resume-abandon / continue to "Story complete" / free play: all pass, 0 console errors. |
| Per-frame GC, result screen up | 280 B/frame, all in `UnitySynchronizationContext.ExecuteTasks`: the same baseline as before this work (section 6 of performance-audit.md); the new UI adds none. |
| EditMode tests | 107 / 107 (26 new: rank thresholds, goal generation, best-score comparison, perfect-clash window). |

Only a Quest can confirm: panel readability at arm's length, haptics feel of the perfect clash, frame rate with the new UI, and the
website goals path (the headset could not reach a backend in these tests, so only local goals were exercised).

