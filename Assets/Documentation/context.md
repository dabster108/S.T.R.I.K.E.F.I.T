# Fitness Monster Battle (VR) — Project Context

Last updated: **2026-10-10, main menu / SceneFlow, WeaponSurfaceProxy, Level 2 landing tremor, DragonHazard (off), boss glow-ups and 3 new attacks (see project_progress.md)**

## Overview

Players fight monsters with VR weapons powered by real fitness movement.  
**3 separate level scenes**, Inspector weapon loadouts via `WeaponType`.

| Level | Scene | Environment | Player weapons | Monster weapon | Monster |
|---|---|---|---|---|---|
| 1 Easy | `Level1_Easy` ✅ built | Lite Dungeon Pack (Gridness) | Dual hammers | Sword (6-attack table + defend) | Hip Hop prefab (Warrok mesh) |
| 2 Medium | `Level2_Medium` ✅ built | Lite Dungeon Pack, night sky | Dual swords | Two-handed axe (10-attack table + defend) | Vampire A Lusth |
| 3 Hard | `Level3_Hard` ✅ built | Old Sea Port dock arena (lighting escalates with the fight) | Mace / War Flail / Katana pair, grabbed in the arena | Two axes, one per hand (14-attack table: 11 melee incl. unblockable slam, + ranged Hurled Axe / Twin Hurl / Brute Rush; defend) | Twin-Axe Brute (Mixamo Ch25, Humanoid, URP Simple Lit + emissive glow) |

## What MCP built (2026-09-21)

- Folder moves for weapons/monsters into `Models/` + prefabs
- All 5 scenes + Build Settings
- Full **Level1_Easy** playable draft: dungeon arena, BattleSystems, Hip Hop monster prefab, hammer + HitDetection, world canvas
- Tags: Hammer, Monster, Player

## Weapon system

```csharp
public enum WeaponType { None, Hammer, Sword, Shield, Mace, Flail, Katana, DualAxe }
```

- `PlayerCombat`: primary/offhand, `OnBlock()` placeholder
- `MonsterController`: `weaponType` / `SetWeapon()`
- `LevelManager` per scene sets loadout

## Scene flow

```
MainMenu → Level1_Easy → Level2_Medium → Level3_Hard → Results
```

## Scripts layout

```
Assets/Scripts/
  Combat/     WeaponType, HitDetection, BattleManager, CombatFeedback (haptics + monster-only hit-stop)
  Monster/    MonsterController, MonsterHealth, MonsterAI, MonsterAnimator, BossPhaseController,
              MonsterEntrance (+ MonsterEntrance.Drop.cs: L3 rises from a circle, L2 drops from above),
              MonsterDefense (shared: awareness, swing classes + guard poses, adaptive memory, reactive guard, guard meter/break, poise),
              MonsterGuardIK (hand IK for side / low guards), MonsterPersonalSpace (head 0.6 m / hand 0.35 m clamp),
              MonsterWeakPoint, MonsterGlow (L3 emission), MonsterAxeThrow (L3 thrown axes), BossFinale (L3 slow-mo kill)
  Player/     PlayerCombat, PlayerHealth, PlayerExertionAudio (heavy breathing), PlayerLowHealthFeedback (L3 vignette + heartbeat)
  VFX/        GroundRuneVFX, DangerZoneIndicator, ArenaMoodController (L3 fight-driven lighting)
  UI/         CombatUIController
  Manager/    LevelManager, GameStateManager, RoundManager
  Legacy/     CubeTapCounter, LaserHandGrabber, LaserPointer, VRSoundButton
```

## Asset locations (after cleanup)

| Asset | Path |
|---|---|
| Hammer | `Assets/Models/Weapons/hammer/` |
| Shield | `Assets/Models/Weapons/medieval-shield/` |
| Sword | `Assets/Models/Weapons/shattered-crystal-sword/` |
| Hip Hop | `Assets/Models/Monsters/Hip Hop Dancing.fbx` |
| Warrok | `Assets/Models/Monsters/Two Handed Sword Death.fbx` |
| Vampire (L2) | `Assets/Models/Monsters/Level2/` (+ extracted `Textures/`) |
| L1 monster animator + L1-only clips | `Assets/Animations/Monster/Level1/` (`Monster_L1_Sword.controller`) |
| L2 monster animator + L2 clips | `Assets/Animations/Monster/Level2/` (`Monster_L2_Axe.controller`) |
| L2 swords / axe | `Assets/Models/Weapons/Player/L2_PlayerSword_Split.asset`, `Assets/Models/Weapons/Monster/L2_MonsterAxe_Split.asset` |
| L2 audio / VFX | `Assets/Audio/SFX/Level2/`, `Assets/VFX/Level2/` |
| L3 boss model (Ch25) + textures | `Assets/Models/Monsters/Level3/Ch25_nonPBR.fbx`, `Textures/Ch25/` (incl. generated `Ch25_100x_Emission.png`; optional `_Emission_Painted.png` override), weak-point mesh `Ch25_WeakPointSplit.asset` |
| L3 boss prefab / animator + clips | `Assets/Prefabs/Monsters/Monster_Hard_TwinAxeBrute.prefab`, `Assets/Animations/Monster/Level3/` (`Monster_L3_TwinAxe.controller`, `L3_*`, `Atk_*`) |
| L3 boss axe | `Assets/Models/Weapons/Monster/L3_MonsterAxe_Split.asset` (from `free-mace-katana-axe-weapon-pack`) |
| L3 player weapons / audio / VFX | `Assets/Models/Weapons/Player/Level3/`, `Assets/Prefabs/Weapons/Level3/`, `Assets/Audio/SFX/Level3/`, `Assets/VFX/Level3/` |
| L3 builders | `Editor/Level3DualAxeBuilder*.cs` (boss), `Editor/HumanoidClipBaker.cs`, `Editor/Level3Builder.cs`, `Editor/Level3SceneBuilder.cs`, `Editor/Level3EngagementBuilder.cs` (arena mood, low-HP FX, victory hold) |
| Lite Dungeon | `Assets/Gridness Studios/Elementary Dungeon Pack Lite/` (do not move) |
| Old Sea Port | `Assets/Old Sea Port/` (do not move) |
| HotelRoom | `Assets/HotelRoom-Mavi3D/` (unused) |

## Fight tuning (2026-10-05)

All per-level combat numbers live in `Editor/CombatTuning.cs` (monster prefab values + scene overrides on `LevelManager`: `monsterMaxHealthOverride` 500 / 680 / 2900, `monsterAttackRangeOverride` 1.65 / 1.85 / 1.45, `playerMaxHealthOverride` L3 1100). `Level3DualAxeBuilder` calls `CombatTuning.ConfigureMonster`, so rebuilding the boss keeps it. Re-apply with `CombatTuning.ApplyPrefabs()` + `CombatTuning.ApplyOpenScene()`. Play-mode harness: `Editor/CombatVerification.cs`.

## Docs

- `Documentation/SETUP_GUIDE.md`
- `Documentation/MOVE_LIST.md`
- `Documentation/project_progress.md`
- Root `README.md`, `.gitignore`, `.gitattributes`

---

## Web additions, 2026-10-07 (branch `feel-and-fitness`)

- Backend (web/backend): `feedbackCore.js` / `feedback.js` (survey), `progression.js` (streak with freeze, weekly goal, level, badges, personal bests held), tests in `test/progression.test.js` (`npm test`). New routes: `POST /api/sessions/:id/feedback`, `GET /api/analytics/feedback`, `GET /api/players/:name/achievements`. New table `session_feedback`. `GET /api/sessions/:id` adds `feedback` and `personalBestsSet`. Live state keys `combo`, `effort`, `kcal` are kept. Nothing existing was removed or reshaped.
- Frontend (web/frontend): public `/feedback`; Progress section on player pages; rating and recap cards on session pages; "Perceived vs measured effort" on Analytics; `LiveHud` on Live Arena cards and over the broadcast video. New dependency: `html-to-image`.
- The Unity side of "feel and fitness" (breathing tiers, blood, haptics, hit-stop, damage numbers, effort HUD, results panels, combo/effort/kcal telemetry) is NOT built; the web side already accepts those fields.

---

## Unity feel and fitness, 2026-10-07 (branch `unity-feel`)

- New scripts: `Player/PlayerExertionAudio.cs` (rewritten: `Exertion01`, tiers calm/medium/high, two sources, hysteresis), `Player/AudioGainFilter.cs`, `Player/PlayerDamageVignette.cs`, `Combat/HitBloodFx.cs`, `Manager/BattleStats.cs` (peak speed, damage taken, running kcal), `Manager/FitnessEstimateConfig.cs` (mirror of `web/backend/fitness.js`), Editor `FeelSetup.cs` (idempotent scene wiring) and `FeelVerification.cs` (play-mode probes).
- Changed: `SessionReporter` (combo / effort / kcal in the state block, `FetchPlayerBests`), `CombatUIController` (damage-number colours + FULL POWER, effort HUD, result extras, medals), `CombatFeedback` (hit-stop skips attacks), `PlayerHealth` (`flashReplacedByVignette`), `CombatVerification` (`backendUrl`).
- Audio: `Assets/Audio/SFX/Player/breathingfast.mp3`. FX: `Assets/Materials/FX/` (`M_HitBlood`, `M_HitSparks`, `M_DamageVignette`), `Assets/VFX/T_DamageVignette.png`.
- Exertion is one value (`PlayerExertionAudio.Exertion01`): breathing, effort bar and telemetry `effort` all read it.
- Test harness note: run the harness against a local backend with `http://127.0.0.1:4100`, not `localhost` (Unity resolved `localhost` to IPv6 first and timed out); delete the test player's sessions and restore PlayerPrefs `StrikeFit.PlayerNames` afterwards.

---

## Polish pass 2, 2026-10-08 (branch `unity-feel`, backup `backup-before-polish-2`)

- New scripts: `Combat/SfxTrace.cs` (editor-only sound audit hook), `Monster/MonsterFireAura.cs` (L1 yellow-gold / L2 violet body fire, one particle system, per-prefab profile), `Monster/MonsterHitShake.cs` (hips-bone shudder), Editor `PolishVerification.cs` (play-mode probes: sound, cross, centre, blood, fire, breath, shake, jitter, regress, alloc).
- Changed: `HitDetection` (valid targets only, one contact per swing, entrance gate, shared clash sound, `hitSounds`), `PlayerCombat` (`IgnoreOwnWeaponCollisions`), `WeaponSwingAudio` (sustain + 0.25 s), `MonsterAI` (`InStrikeWindow`, `braceSoundOnReactiveGuard`), `MonsterDefense` (`dampedTurn`, `recentre`), `BattleManager` (`spawnOnPlayerForwardLine`), `HitBloodFx` (rewritten: droplets with gravity, drips, death burst, clash sparks), `PlayerDamageVignette` (flash and pulse default off), per-scene exertion thresholds on `PlayerExertionAudio`.
- Probe note: Editor swings driven by moving a transform read about 40 % lower than the drive speed (0.12 s hand-speed smoothing), and the Editor has no controller velocity; feed `PlayerExertionAudio.RegisterSwing` for effort tests, and do not set `Application.runInBackground` in edit mode (it rewrites ProjectSettings).

---

## Adaptive guard, Level 2 entrance, guard telemetry, dashboard (2026-10-09, branch `unity-feel`, checkpoint commit `61f9ff7` before it)

- **MonsterDefense** classes every incoming swing as OVERHEAD, DIAGONAL, HORIZONTAL_LEFT / RIGHT (as the player sees it), LOW_SWEEP, UPPERCUT or THRUST and computes the predicted contact (weapon + velocity x reaction delay, stopped at the body axis, clamped to `guardReach`). Guard poses HIGH / SIDE_LEFT / SIDE_RIGHT / LOW; a rule-based memory of the last 6 swings raises the chance for a repeated class or side (+15 points, capped, decays when the player varies). Fairness extras: after 3 blocks in a row the next swing cannot be guarded; opening cue (white flash + sound). Static events `GuardEnded` / `OpeningLanded`. Numbers in `Editor/CombatTuning.ConfigureAdaptiveGuard` (chance 15 / 35 / 50 %, +5 % per L3 phase; gap 6 / 3.5 / 2.5 s; uptime 12 / 22 / 30 %).
- **MonsterGuardIK** (on the Animator's object): side / low guards by hand IK (the guard clips are left/right symmetric, so Mirror cannot make them). Level 1 clip only, Level 2 side + low, Level 3 side only; the HIGH guard is always the clip. IK Pass is on for L2 / L3 base layers only.
- **Level 2 entrance** (`MonsterEntrance` style DropFromAbove, `Editor/Level2EntranceBuilder.cs`): lights dim, violet mist, eyes in the dark, drop from above (Mutant Jump from its apex), landing at 1.85 m with dust / ring / haptics, roar + violet glow-up, lights back; 7.94 s, 1.5 s after a Retry (`GameStateManager.FlagRetry`). Controller states EntranceDrop / EntranceRoar, real transitions only.
- **Telemetry**: `monster_guard`, `opening_hit` events (`SessionReporter.sendGuardEvents`), same batches; `clash` already existed.
- **Web**: backend `insights.js` + `GET /api/players/:name/insights`, `GET /api/players/:name/checklist`; frontend `lib/leaderboard.js` (weekly / all-time / per level / most improved, you vs last week), `Insights.jsx`, `FirstRunChecklist.jsx`, `SwingTimeline.jsx`, `Confetti.jsx`; `npm test` now exists in the frontend too.
- Harnesses (Editor, Play mode): `AdaptiveGuardVerification` (fight:varied | repeat | varied-idle | repeat-idle, look, alloc), `EntranceVerification` (full, retry).

## Menu, weapon proxy, tremor, dragons (2026-10-10, branch `unity-feel`)
- **Scenes / flow:** `MainMenu` (build 0) + three levels; `SceneFlow` (static, one load at a time, `PlayAgain` sets the retry flag, `QuitNow`), `OutcomeMenuButtons` on Victory / Defeat, `ExitConfirmPanel` flushes `SessionReporter` first. `Application.Quit` on Quest is untested.
- **Visual only:** `WeaponSurfaceProxy` (on every player weapon with a `HitDetection`, added by `PlayerCombat.EnsureWeaponProxies`) moves only the drawn mesh; the logical weapon stays on the controller. Toggle `proxyEnabled`.
- **Level 2 entrance:** `MonsterEntrance.Drop.cs` tremor (`Off / Light / Strong`), rumble, falling dust, torch dip, flame dimming.
- **Level 3:** `DragonHazard` on `DragonDirector` in `Level3_Hard` (**disabled**, no model within budget, see project_progress.md); `BossPhaseController.PhaseShiftBegan / Ended`; `MonsterAI.mustDodgeGate`; `MonsterGlow` power-up / ripple / death collapse; `MonsterAxeFlames` charge flare / ember trail; three new rows (Twin Cyclone, Twin Overhead Chain, Lunge Strike) in `Level3DualAxeBuilder` (builder-driven: rebuild with Tools > Level 3 > Build Dual-Axe Monster, never hand-edit the prefab).
- **Harnesses (Editor, MCP-driven):** `WeaponSinkVerification`, `EntranceVerification`, `DragonVerification`, `Level3AttackVerification`, `PerfProfile`, `MenuVerification`.
