# Project Progress — Fitness Monster Battle

Last updated: **2026-10-10 — main menu + exit, weapon surface proxy, Level 2 landing tremor / rumble, Level 3 dragon fire hazard (shipped disabled: model over budget), boss power-up / charge flare / ember trail / death collapse, 3 new boss attacks. Editor simulation, not a headset.**

---

## 2026-10-10 — Main menu, weapon surface proxy, Level 2 landing tremor, Level 3 dragons (disabled), boss glow-ups and attacks

Branch `unity-feel`; commits `dd5bbd8` (Part 1), `4f1f17b` (Part 2), `2bbdb92` (Part 3), then Part 4/5 on top. **Everything below was measured in the Editor in Play mode (Mono, no headset).** Harnesses: `MenuVerification`, `WeaponSinkVerification`, `EntranceVerification`, `DragonVerification`, `Level3AttackVerification`, `PerfProfile` (all `Assets/Editor`, Editor-only).

### Carry-over from the last pass
- Adaptive blocking guard, Level 2 entrance, guard telemetry: **DONE**, verified last pass.
- Sweep allocation fix, Level 3 difficulty knob, breathing check: present in checkpoint `61f9ff7`, never written up (see the 2026-10-09 section). Not re-verified here.

### Part 1: main menu, exit, play again (DONE)
- `Assets/Scenes/MainMenu.unity` (build index 0), `SceneFlow` (static loader, one load at a time, retry flag only for Play Again), `MainMenuController`, `ExitConfirmPanel` (confirm, flushes `SessionReporter` for <= 0.8 s, then quits), `OutcomeMenuButtons` (Play Again / Level Select / Exit Game on Victory and Defeat). Build Settings: **MainMenu, Level1_Easy, Level2_Medium, Level3_Hard enabled; Results disabled**.
- Verified by round trips through all three levels with the real OVR laser path (raycaster + pointer events): menu -> level -> victory -> Exit (No) -> Play Again -> defeat -> Level Select -> menu fingerprint identical to the first load (1 rig, 1 listener, 1 reporter, 1 event system, 0 particles, timeScale 1).
- **Not verifiable here:** `Application.Quit()` on Quest (the Editor path stops Play mode instead). Test on the headset: Exit Game must return to Home.

### Part 2: weapons never sink into the monster (DONE)
- **Found:** with the controller pushed 30 cm past first contact, the drawn weapon sank **17-23 cm (L1 hammer), 3-20 cm into the monster's weapon (L2 sword), 10-31 cm (L3 mace / katana), 5-33 cm (flail)** into body and weapon colliders; nothing stops a controller.
- **Built:** `WeaponSurfaceProxy` (visual only). The logical weapon (collider, Rigidbody, `HitDetection`) stays exactly on the controller, so swing speed, damage and hit / clash events are untouched. Only the drawn mesh moves, out along the contact normal, to <= 3 cm of visible sink (target 1 cm), released by a critically damped ~35 ms spring, capped at 0.35 m. A faint unlit ghost of the real pose shows when the offset is > 6 cm; a light haptic tick scales with penetration through the existing `CombatFeedback` path. Weapon-vs-weapon: the drawn weapon rests on the monster's weapon colliders the same way. Applied at equip (all L3 weapons). Flail: the Verlet ball is offset and the links bend toward it (t^2 weight). Toggles: `proxyEnabled`, `ghostEnabled`, `hapticEnabled`, `includeMonsterWeapons`, `maxSink`, `maxOffset`.
- **Measured (3 / 8 / 15 / 25 m/s, thrust and flat-on, head / torso / legs / weapon):** drawn sink into body **L1 0.5-2.6 cm** (was 17-23), **L2 0.0-2.0** (was up to 20 into the weapon), **L3 mace 0.8-2.6, katana 0.5-2.3, flail 1.0-8.3** (cap-limited when the controller is > ~35 cm inside). Hit / clash / damage / peak-speed columns **identical off vs on in 126 of 128 rigid-weapon cells** (L1 and L2 all 64; the two differing cells are the first sweep of a fresh fight in the OFF baseline itself, which varies between two OFF runs: h1c1 / h1c0 / h0c2; ON never produced a hit plus a clash on one swing). The flail is a chaotic pendulum: 14 of 32 cells already differ between two OFF runs.
- **Cost:** 4-15 us/frame off, 120-290 us/frame on in the worst case (weapon continuously buried), no GC. Captures `Captures/WeaponProxy/` (off / on, side and head view, per level).
- **Spacing (stand-off):** fully extended arm sits 7-28 cm inside the monster surface (hammer ~14, sword 7-9, mace 7, katana 28): the stand-off was left alone; report for the headset.
- **Headset to confirm:** feel of the offset and the ghost, haptic tick strength.

### Part 3: Level 2 entrance, landing (DONE)
- **Added** to `MonsterEntrance` (DropFromAbove): lights dim over **1.2 s** (was 1.0), arena flame particles dim with them (`dimFlames`, property block, no new lights), one smooth torch dip at the landing (single event, 0.5 s, well under 2 Hz), **tremor** `Off / Light / Strong` (default Light): positional only on the tracking space, 6-8 Hz, squared decay over 0.5 s, **0.87 cm Light, 2.17 cm Strong** measured, only while the entrance is in progress (combat locked), the world-space HUD canvas moves with it (0.0 mm shift relative to the head), tracking space restored to 0.000 mm; **rumble** on both hands (0.5 amplitude for 0.12 s into 0.22 for 0.4 s, low frequency; both calls seen); **12 dust motes** falling around the player (0.7-1.5 m out).
- **Verified:** weapon speed from the tremor alone 0.44 m/s (Light) / 1.10 m/s (Strong), against the 2.2 m/s whoosh trigger; 0 hits, 0 clashes, only growl / impact / roar sounds; fight starts at **8.15 s** (7.94 s before: +0.2 s from the longer dim), Retry **1.50 s**; live particles max **76** (64 before, +12 dust); draw calls **49.2 vs 49.9** FX on / off, **49.5 vs 49.5** flame dimming on / off (no increase); torches back to 100.0 %; landing distance 1.85 m (head 1.57 m, hands 1.26 m, clamp limits 0.60 / 0.35 m). Captures `Captures/Level2Entrance/`.
- **Missing assets (none invented):** a dedicated landing thud, vampire roar / screech, rumble sound (now: `L2_Slam_Impact`, `L2_Special_FireRoar`, `L2_Heavy_Growl`; search freesound.org "heavy body fall thud", "creature screech", "monster growl low", verify licences); animation clips `Falling To Landing`, `Hard Landing`, `Zombie Scream`, `Standing Taunt Battlecry` (all (verify), Mixamo needs the user's login).
- **Headset to confirm:** tremor comfort (Off / Light / Strong), rumble feel, the dim reading as a build-up and not as an error.

### Part 4: Level 3
**4A dragons (SHIPPED DISABLED).** The source model `_SourceDownloads/black-dragon-with-idle-animation` is over every budget: **37,986 triangles (budget ~15k), 3 materials (budget 2), two 4096 x 4096 JPGs (budget 1024)**, 167 bones, one 22.5 s non-looping "Dragon Idle" clip whose wings move slowly (peaks every 4-7 s; **no flap clip**), model units ~100x. Not decimated, not imported (a probe import was deleted). **To provide:** a skinned dragon FBX <= ~15k triangles, <= 2 materials, textures <= 1024, with a loop (idle or flap) clip, a child named `Mouth`, then drop it into `DragonHazard.dragonPrefab` and tick `dragonsEnabled` (Level 3 scene, `DragonDirector`). Or approve a Blender pass on this model: decimate ~14k, bake to 1024, merge to body + eyes.
- **Built (all wired, off):** `DragonHazard` + `DragonSetup`. Count by phase `{0,1,2}`; dragons fly in at the phase-shift roar and circle **9 m** up at 13 m radius (never < 6 m from the head: min measured 13.3 m); fire event = ground marker (zone fixed when shown, **1.5 s**) -> flame cone **1.6 s** -> ticks every 0.4 s of **1.5 % max HP (18 of 1200)** only while the head is inside the **1.6 m radius (3.2 m wide)** zone; flames within 1.0 m (+ half the puff size) of the head are removed. Fairness: one event at a time; gap after an event **7 s** (phase 1) / **3.5 s** (phase 2, dragons alternate); never during a phase pause (a running event is cut the moment HP crosses the threshold), the entrance, or within **2.5 s** of a boss must-dodge attack (`MonsterAI.mustDodgeGate` stops the boss starting one while a fire event or its tail runs). Not hittable (colliders disabled; `hittable` only enables them, no damage pipeline exists for dragons). Telemetry `dragon_fire` (zoneHit, damage, ticks, phase, dragon) plus `player_hurt` with attack "Dragon fire" through the existing queue.
- **Audio (existing clips):** roar `L3_Roar_Enrage` (pitch 0.8), warning `L3_Slam_Warning` (0.85), flame `L2_Special_FireRoar` (loop), wing `L2_Axe_Whoosh_Heavy` (pitch 0.45). **Missing:** a real wing flap / whoosh loop, a fire-breath loop (freesound "large wing flap creature", "flamethrower loop", "dragon breath" (verify)).
- **Verified with the stand-in emitter (no model):** unit run: phase 0 = 0 dragons, phase 1 = 1, enrage = 2; stay in zone 4/4 ticks 72 HP, step out at the marker 0/4, step out after tick 2 = 2/4 (36 HP), leave and come back = 4/4; every event marker 1.50 s, zone drift 0.0 cm, gaps 7.00 / 3.50 s exactly, 0 overlaps; boss death ends flames and marker at once. 100 s fight (varied swings, HP staged to phase 1 at 25 s and enrage at 60 s): 10 events, 1 aborted by the phase pause, 0 events during a pause after the fix, closest flame centre 1.59 m. Stress (60 s, must-dodge weight x40): 2 slams, closest event 2.53 s.
- **Cost:** stand-in, 2 dragons: **+1.2 draw calls, +2 SetPass, 13.5 us/frame script, up to 50 live flame particles, 0 triangles**. The source model as-is would add **75,972 triangles for two dragons and +8.9 draw calls** (3 submeshes each): not for Quest as it stands.

**4B glow-ups (DONE).** `MonsterGlow`: phase-shift power-up (emission ramp to hot gold, flare 4.4 at 72 % of the roar, ground ripple radius 0.8 -> 5.5 m over 1.5 s, settles into the new stage; measured: intensity 0.65 -> 4.40, ripple 108 frames, ends with the 2.42 s shift), death flash 3.4 then collapse to 0 in 1.4 s. `MonsterAxeFlames`: charge flare on both axes by tier (standard x1.5 / combo x1.8 / heavy x2.6 / must-dodge x3.0, ramping to the hit frame; measured flame rate 41 -> 70, 28 -> 83, 28 -> 202, 28 -> 235 per s), ember trail on a blade faster than 6 m/s (81 embers in one Twin Whirlwind, 0 idle). Colours: gold / white-yellow, distinct from the cool white-blue guard flash and the red telegraph. Toggles: `powerUp`, `chargeFlare`, `emberTrail`, `deathCollapse`.

**4C attacks (DONE, no suitable unused clip).** Unused clips in the project: `Sword And Shield Attack (L1)` / `Player_Future` copies (one-handed cut, not a dual-wield thrust or spin), `Great Sword Impact`, `Great Sword Idle`, `Hit Reaction`, `Monster_SwordIdle` (not attacks). So the three new attacks are **new compositions of the boss's existing mocap**: **Twin Cyclone** (heavy, phase 2+, two spins, hits 0.88 s and 1.70 s), **Twin Overhead Chain** (combo, phase 2+, slam, snatch up, second faster slam, hits 0.94 s and 1.45 s), **Lunge Strike** (heavy, 1.8-3.0 m, a short rush and the crossing sweep, hit 0.78 s). All existing rows unchanged (prefab diff shows only the three new rows). Clips wanted if you want truly new motion: spin `Great Sword Spin Attack` / `Dual Wield Spin` (verify), double overhead `Standing Melee Attack Downward` pair / `Two Handed Sword Attack` (verify), lunge `Sword And Shield Stab` / `Standing Melee Attack Forward` (verify).
- **Animator:** controller 25 states, Any State only -> Death; every new state is entered by its trigger from Idle, IdleEnraged, Hit, Defend, Roar and returns to the matching idle (traced live; hips 0.80-0.93 m against 0.92 idle: no sitting pose).
- **Chains (30 chains: 15 single, 10 of 2 links, 5 of 3, no overlap):** telegraph of every link >= 0.53 s (AI minimum seen 0.45), aim at the wind-up start <= 3.2 deg and not re-aimed during the swing; when the player stepped 3 m aside after a link's wind-up began, that link **missed** (aim 53-56 deg off at the hit) while unstepped links landed: each hit is checked on its own.

**4D (DONE).** Zone guards and adaptive guard unchanged by the dragons (100 s fight with dragons: guards 8, poses H6 SL1 SR1, uptime 7.6 % of 30 % cap, 0 guard during attack / stagger / phase shift, 0 attacks cancelled into Hit, 20 player hits landed while the boss attacked). 
**Part 5, per scene (Editor Play mode, alternating 4 s windows, features OFF = this pass's behaviour switched off, ON = all on; the Editor's counts move +-3 draw calls and +-4 % triangles between windows with the monster doing different things, so read them as "no measurable change"):**

| scene | triangles OFF / ON | draw calls | SetPass | shadow casters | realtime lights | live particles (max) | new scripts per frame |
|---|---|---|---|---|---|---|---|
| Main menu (new) | 853 | 6 | 3 | 0 | 0 | 0 | 0 |
| Level 1 | 133,235 / 138,780 | 104.5 / 101.8 | 17.2 / 17.6 | 62 / 61 | 3 | 127 / 126 | proxy 43 us (7 off) |
| Level 2 | 68,892 / 71,848 | 48.2 / 50.7 | 12.3 / 12.7 | 23 / 23 | 6 | 199 / 247 | proxy 64 us (4 off) |
| Level 3 (+2 stand-in dragons) | 205,833 / 214,184 | 85.0 / 87.6 | 33.8 / 35.4 | 34 / 34 | 10 | 917 / 993 | proxy 82 us (5), flames 12 us (8), dragons 14 us (0) |

No new realtime light, shadow caster or post-processing anywhere. Android: `PlayerBuildInterface.CompilePlayerScripts` for Android compiles (no errors); Editor API in runtime code is only inside `#if UNITY_EDITOR` (`SceneFlow.QuitNow`, `GameStateManager`, `BattleManager`, `WeaponPickup`, `WebRtcStreamer`, `MonsterController`, `PlayerCombat`; `SfxTrace` is `[Conditional]`).

**Regression re-checks (how):** clash / parry / no damage on clash: the sink sweeps (`WeaponSinkVerification`) count hits and clashes per swing: 0 swings both clash and damage with the proxy on; weapons ignore each other and make no air / floor sounds: entrance run sounds list only growl / impact / roar, the tremor run (hand still) none; wind-ups never cancelled: 0 of 15-20 landed hits per 100 s fight cancelled into Hit; full-body hit coverage, personal-space clamp, zone guards, adaptive fairness: the 100 s fight report (guard during attack / stagger / phase shift / entrance all 0, gap >= 2.5 s, length 0.8-1.2 s); L3 entrance / flame / aura / enrage untouched (additions only; the existing 14 attack rows identical in the rebuilt prefab); no allocations: proxy and dragon scripts use pooled buffers (flame emission only through `Emit`, `GetParticles` into a preallocated array), `FindObject` only once at wiring; SessionReporter URL, wake ping, 5 s timeouts and silent failure unchanged (only a new event type and `FlushNow` for the quit).

**OVR Metrics steps (each level, dragons off then on once a model exists):** install OVR Metrics Tool, enable the overlay in `Developer Runtime Tools`, play a full fight (2-3 minutes, normal swing intensity), record FPS (72 flat), **stale frames (0)**, CPU level and GPU level (Level 3 is the highest; keep <= 3 / 4), and read the CSV. Repeat in the main menu. Compare the new weapon proxy by toggling `proxyEnabled` on `PlayerCombat`'s weapons.


### Headset-unconfirmed (honest list)
Application.Quit on Quest; haptic feel (proxy tick, landing rumble); tremor comfort; proxy / ghost feel; dragon performance and fire fairness (no model exists); power-up / ripple readability in headset lighting; all Quest frame times (OVR Metrics steps below).

---

## 2026-10-09 — Adaptive guard (Levels 2-3), Level 2 drop entrance, guard telemetry, dashboard features

Branch `unity-feel`; the working tree was committed first as checkpoint `61f9ff7`. This pass is uncommitted. **Everything below was measured in the Editor in Play mode (Mono, no headset)**: the "player" is the real weapon collider driven through the real `HitDetection` path by `Assets/Editor/AdaptiveGuardVerification.cs` and `EntranceVerification.cs`. Swing speeds come from the live backend (549 hit events, read-only, 2026-10-09: median Easy 4.9, Medium 2.1, Hard 6.3 m/s; gaps between hits median 1.2 / 0.6 / 1.0 s).

**Last pass's items (allocation fix, Level 3 difficulty knob, breathing check):** all present in the checkpoint commit (Level 3 HP 5600 / player 1200 / damage 14 / cooldown 2.4 s overrides, the levelled fast-breathing clip and self-test, `ClashVerification` perf probe), never written up and not re-verified in this pass.

### Part 1: adaptive blocking guard (shared `MonsterDefense`, new `MonsterGuardIK`)
**Status of the shared guard before this pass:** DONE: reactive guard with reaction delay, guard meter / break / stagger / lockout, counter, fairness gate, opening bonus + callout, cool white-blue raise flash, per-zone block pitch. PARTIAL: swing classes (3 zones HIGH / MID / LOW, not 7), guard directions (one clip, so every zone looked the same), opening cue (callout only, no flash or sound). NOT DONE: predicted contact point, adaptive memory, blocks-in-a-row rule, telemetry events.

**Built.**
- Seven classes from the weapon's velocity in the monster's space: OVERHEAD, DIAGONAL, HORIZONTAL_LEFT / RIGHT (as the player sees it: LEFT comes in from the player's left), LOW_SWEEP, UPPERCUT, THRUST. **Predicted contact** = striking part + velocity x reaction delay, stopped at the swing's closest approach to the body axis (a fast swing is not predicted out through the back), clamped to `guardReach` (computed from shoulder + arm + weapon: 1.75 / 1.17 / 1.36 m).
- **Detection fix:** the old 40-degree cone aimed at the body at the weapon's own height rejected nearly every overhead and uppercut (velocity mostly vertical). Swing classes use a path test instead: the swing line passes within 0.45 m of the body axis in the next 0.5 s.
- Poses: HIGH for overhead / diagonal, SIDE_LEFT / SIDE_RIGHT for the horizontals, LOW for low sweep / uppercut, the pose nearest the predicted contact for a thrust. Coverage: HIGH everything above the waist (what the guard clip shields), LOW below the upper torso, a SIDE guard its own half of the torso. A pose that cannot be shown falls back to HIGH.
- **Adaptive memory (rule-based, not learned):** last 6 swing classes with time stamps; 3 of one class, or 3 from one side, within 8 s add +15 points to that class (cap +15, total chance cap 80 %); every different class takes 5 points off the others, and boosts lose 2 points per second. Varied swings keep the base chance.
- Base chance 15 / 35 / 50 % (+5 % per Level 3 phase), reaction 0.35 / 0.25 / 0.18 s, gap 6 / 3.5 / 2.5 s, guard 0.8-1.2 s, uptime cap 12 / 22 / 30 %, break after 5 / 4 / 3 absorbed hits (weapon parries never count), stagger 2.5 / 2.0 / 1.6 s at x1.25 then 4 s without guarding, counter 0 / 20 / 35 % after 2+ absorbed hits within 0.5 s with at least 0.45 s of telegraph. **New: after 3 blocks in a row the next detected swing cannot be guarded** (1 s window for it to land; checked deterministically: refused, not covered, landed at full damage, rule released).
- Cues: guard = the existing cool white-blue flash; opening = +10 % damage, a 0.08 s white flash and its own sound (L1 / L2 `L3_WeakPoint_Hit`, L3 the sword-unsheathing ring; no dedicated clip exists); block clang pitched per pose (1.18 / 1.06 / 0.95 / 0.82) with +-4 % variation.
- One "is guarding" flag: `MonsterAI.IsDefending`. Toggles: `swingClasses`, `adaptiveGuard`, `sideGuards`, `lowGuard`, `maxBlocksInARow` (0 = off), `openingClip`, `MonsterGuardIK.ikIntercept`, `ikOnHighPose`, `keepClipHandRotation`.

**Clips and the IK decision (1B).** Only one guard clip per monster exists (`Monster_GuardPose` L1 / L2, `L3_Defend` L3) and **both are left / right symmetric**: rendered with the Animator's Mirror flag the pose is identical (hands at the same +-0.25-0.35 m), so Mirror cannot make side guards. The hand-IK layer (`MonsterGuardIK`, position IK toward a pose anchor pulled 50 % toward the predicted contact, clip hand rotation kept, 2.5 m/s hand speed, 90 % arm reach, hand at least 0.3 m in front of the chest) was judged in Play mode from the player's head and a 3/4 view, plus weapon samples inside body capsules (gripping hands excluded):

| | clip guard | IK side L | IK side R | IK low | IK high | shipped |
|---|---|---|---|---|---|---|
| L1 (270 samples) | 48 (chest 16) | 71 (chest 30) | 46 | 48 (hips, chest) | not used | **clip only** (the long sword already runs through the chest) |
| L2 (225) | 14 (head 4) | 18 (head 2, torso 3) | 18 (leg 4) | 14 / 17 (torso 1-2) | 30 (head) | **IK side + low** |
| L3 (450) | 10 (forearms) | 11 | 15 (own upper arm 5) | 57 (chest 13, head 6) | not used | **IK side only** |

The HIGH guard is always the clip (IK put the blade into the head). No elbow flips seen in the captures (elbow angles 75-145 degrees). The weapon colliders ride on the hand, so parries work unchanged with either approach (L3 parry: `CurrentAttackParried = True`). IK Pass was switched on for the L3 base layer (L2 already had it); L1 left as it was. Captures: `Captures/AdaptiveGuard/`.

**Proof (1E): 100 s per run, bursts of 2-3 swings 1.0-1.5 s apart with 1.5-3.5 s pauses, speeds from the live histograms.** "Varied" cycles the seven classes, "repeat" is HORIZONTAL_LEFT every time. Normal fights (monster attacking):

| | L1 var | L1 rep | L2 var | L2 rep | L3 var | L3 rep |
|---|---|---|---|---|---|---|
| swings / detected | 44 / 35 | 44 / 35 | 43 / 23 | 42 / 18 | 48 / 44 | 44 / 40 |
| rolls (mean chance) | 1 (15 %) | 1 (15 %) | 6 (35 %) | 4 (38.8 %) | 1 (50 %) | 1 (65 %) |
| guards (reactive / timed) | 7 (0/7) | 6 (0/6) | 9 (1/8) | 10 (0/10) | 8 (0/8) | 8 (1/7) |
| poses | H7 | H6 | H5 SR1 L3 | SL10 | H4 SL2 SR2 | SL8 |
| uptime (cap) | 7.6 % (12) | 6.2 % | 9.5 % (22) | 9.9 % | 7.2 % (30) | 8.2 % |
| blocked hits / damage prevented | 3 / 65 | 0 / 0 | 3 / 42 | 1 / 15 | 4 / 86 | 1 / 16 |
| openings / bonus damage | 1 / 2.1 | 0 | 0 | 0 | 1 / 2.3 | 0 |
| guard breaks / counters | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0 | 1 / 0 | 0 / 0 |
| monster free (not attacking, busy, flinching) | 25 % | 28 % | 32 % | 32 % | 12 % | 11 % |

**Why so few rolls:** the monster is free only 11-32 % of a fight, and a detected swing during a wind-up or attack gets no roll (fairness). No-roll reasons at detection, e.g. L3 varied: attacking 32, already guarding 4, busy 5. So in a real fight the guard is mostly the timed fallback, and Level 2's slow real swings (median 2.1 m/s) are often under the 2.5 m/s detection gate (18-23 of ~43 detected).

Attacks paused ("-idle"), to show the adaptive rule on its own:

| | L1 var | L1 rep | L2 var | L2 rep | L3 var | L3 rep |
|---|---|---|---|---|---|---|
| detected / rolls | 37 / 4 | 40 / 6 | 20 / 7 | 19 / 9 | 41 / 15 | 41 / 17 |
| **mean rolled chance** | **15 %** | **30 %** | **35 %** | **40.2 %** | **50 %** | **64.1 %** |
| boost active (share of frames) | 0 % | 95 % | 0 % | 36 % | 0 % | 91 % |
| guards (reactive, of which boosted) | 8 (0) | 9 (3, 3) | 11 (1) | 12 (1, 0) | 16 (13) | 17 (12, 11) |
| uptime | 8.6 % | 7.8 % | 11.5 % | 11.6 % | 15.3 % | 16.8 % |
| blocked / prevented | 5 / 115 | 8 / 193 | 4 / 56 | 2 / 39 | 9 / 204 | 6 / 235 |
| openings / bonus | 0 | 0 | 0 | 0 | 4 / 17.7 | 8 / 37.8 |
| guard breaks | 1 | 1 | 1 | 0 | 3 | 2 |

Per class, Level 3 varied with attacks paused (swung / detected / guards / blocked + prevented / openings + bonus): OVERHEAD 7/7/2/1+16/0; DIAGONAL 7/7/4/3+58/1+2.2; HORIZONTAL_LEFT 7/5/2/0/2+10.2; HORIZONTAL_RIGHT 6/5/1/0/1+5.4; LOW_SWEEP 6/6/4/4+92/0; UPPERCUT 6/6/3/1+37/0; THRUST 6/5/0/0/0. (Full per-class tables for every run are in the harness output; Level 1 always shows HIGH because it has no side / low pose.) **Counters: 0** in all runs: a counter needs 2+ hits absorbed by one guard, which happened once (L2, the 20 % roll failed).

**Fairness, every run, every frame:** guarding during an attack / wind-up 0 frames, stagger 0, phase shift 0, entrance 0; no guard started in a flinch or inside a break lockout; minimum gap L1 6.9-9.9 s (rule 6), L2 5.2-6.1 s (rule 3.5), L3 2.8-7.2 s (rule 2.5); guard length 0.83-1.23 s (rule 0.8-1.2; 1.23 is 1.2 plus one 1/30 s sample); max blocks in a row 2 in these runs (rule checked separately above). **Wind-ups:** 13-25 player hits landed during attacks per normal run, **0 attacks cancelled into Hit**; `CombatVerification windup` 6/6, 10/10, 11/11. **Animator:** no controller states were added for Part 1; transitions seen are existing ones (Idle / Hit -> Defend, Defend -> Idle / Hit / Atk_*; a per-frame sample can skip the short Idle between an attack and a guard); Any State -> Death only (all three controllers); hips in Defend 0.86-0.98 of idle (no sitting). **Allocation** (GC per frame, guard + IK on vs off, alternating rounds, guards and IK exercised): L2 29,289 vs 29,333 B, L3 30,315-30,729 vs 30,545 B (within 1 %, noise), L1 25,581 vs 25,581 B (no guards raised in that probe; IK off on L1). These are the Editor's own per-frame allocations; the new code adds none measurably.

**Found and fixed during the proof:** the blocks-in-a-row flag stayed on until a body hit landed, and on Level 3 (swings mostly clash on the axes) it refused 23 later swings; it now covers exactly the next detected swing. HIGH coverage first excluded the lower torso, which turned the regression probe's chest strike into an opening; it now covers everything above the waist (body block back to "Blocked").

**Missing clips (1D, nothing downloaded; Mixamo needs the user's login):**
| need | search | alternatives |
|---|---|---|
| side guard, one weapon (L1 / L2) | `Sword And Shield Block` (verify) | `Great Sword Blocking` (verify), `Standing Block Idle` (verify) |
| low guard | `Sword And Shield Crouch Block` (verify) | `Crouching Block` (verify), `Great Sword Crouch` (verify) |
| twin-axe side / low guard (L3) | `Dual Weapon Block` (verify) | `Standing Melee Block` (verify) |
| counter (riposte) | `Sword And Shield Kick` (verify) | `Great Sword Slide Attack` (verify); the counter uses the fastest existing table attack now |
Side / low guards ship on through the IK where it passed the look check, and off (`sideGuards` / `lowGuard` false, falling back to HIGH) where it did not.

### Part 2: Level 2 entrance, "dropping in from the dark" (`MonsterEntrance` style `DropFromAbove`)
- **Built.** `Monster/MonsterEntrance.Drop.cs` (a partial of `MonsterEntrance`, so every existing gate keeps working: HitDetection's entrance gate, MonsterDefense, the fire aura and hit-shake wait for `Complete`). Level 3 keeps style `RiseFromCircle` (0, the default): its code path only gained the style check. `Editor/Level2EntranceBuilder.cs` (idempotent) adds to `Monster_L2_Axe.controller` two states reached only through real transitions: **Idle -> EntranceDrop** (trigger `EntranceDrop`, duration 0, transition offset 0.270 = the jump clip's apex) **-> EntranceRoar** (trigger `EntranceRoar`, 0.15 s) **-> Idle** (exit time 0.95), plus EntranceDrop / EntranceRoar -> Idle on the `Idle` trigger. No Any State transition. Material `Assets/VFX/Level2/M_L2_EntranceMist.mat` (same URP Particles/Unlit shader as the fire, alpha blended). Retry flag: `GameStateManager.FlagRetry / ConsumeRetry` (set by `ReloadCurrentLevel`). Haptics: `CombatFeedback.PulseBoth` (obeys the `haptics` toggle). Toggles: `style`, `alwaysFull`, every timing / colour / count field, `landAtDistance`.
- **Clips measured on the vampire (preview scene):** `MutantJump_HeightBaked` (3.70 s) apex at 1.0 s with the hips 2.0 m above standing, lands at 1.6 s, slam crouch to 2.4 s, standing again at 2.9 s; `L3_Roar` keeps the hips at standing height (no sitting).
- **Timeline (Play mode, 1/72 s frames, real numbers):**

| step | full | Retry |
|---|---|---|
| arena lights dim to 30 % (slow flicker 1.2 Hz, under 2 Hz), violet ground mist (24 particles) | 0 -> 1.01 s | skipped |
| two violet eyes in the dark above the landing spot (body emission map tinted violet), low growl | 1.01 -> 2.63 s | eyes already lit |
| drop (clip apex -> landing, + 1 m extra eased fall of the root) | 2.63 s | 0.01 s |
| landing at **1.85 m** (dust 16 + ground ring 24 thrown away from the player, impact sound, 0.35 / 0.12 s pulse on both hands) | 3.24 s | 0.63 s |
| crouch and rise (same clip) | 3.24 -> 4.54 s | at 1.8x speed |
| roar + 2.0 s violet glow-up: body emission ramps, axe fire ignites from the hand up the haft (33 live particles), eye flare at 2.0 s | 4.54 -> 6.94 s | short glow |
| lights back to 100 % (1.0 s) | 6.94 -> 7.94 s | never dimmed |
| **fight starts** (colliders + AI on) | **7.94 s** | **1.50 s** |

- **Checks.** During the sequence 8 full-speed sweeps of the real sword through the chest: **0 hits, 0 clashes, monster HP 680 -> 680**; the only sounds were the growl, the impact and the roar (plus the player's own swing whooshes). Controls free the whole time (PlayerCombat enabled, weapons active); the camera is never touched; no screen shake. Closest approach to the player's head during the landing crouch: monster head **1.57 m**, hands **1.26 m** (limits 0.60 / 0.35 m). Lights: all 5 (`LampLight_Left/Right`, `ArenaTorch_Left/Right`, `MonsterRimLight`) to 30 % and back to **100.0 %** exactly (each read back). Loudness at the listener (computed from clip peaks, source volumes 0.8 vs 0.7, spatial blend and distance, not by ear): quietest Level 2 hit peak about **-5.2 dBFS**; entrance impact and roar **-10.0 dBFS**, growl **-15.3 dBFS** (all at least 4.8 dB under). Particles: 0 before, max **64** live during (40 in the Retry version), the system is destroyed 3 s after the fight starts. Draw calls: one system with one material is at most one batch; paired FX-renderer on/off frames while particles were alive gave **49.2 vs 49.9** (no measurable increase; Editor counts are noisy). Captures: `Captures/Level2Entrance/`.
- **Level 3's own entrance unchanged:** re-run after the change, fight starts at **6.93 s** (same as the earlier verified pass), summon / rise / roar sounds at 0.01 / 1.42 / 4.53 s.
- **Different from Level 3:** arrives from above, not from below; low violet mist on the floor instead of the glowing summoning circle; the reveal is two eyes in the dark (the arena lights dim) instead of a circle charging; landing dust + ground ring + a pulse on both hands (Level 3's rise has none); violet glow-up with the axe igniting from the hand (Level 3 has no glow-up in its entrance); sounds are Level 2's own growl, slam impact and fire roar instead of the summon / grind / entrance roar; 7.94 s against 6.93 s; a 1.5 s Retry version (Level 3 always plays the full one). **Same:** the roar animation is `L3_Roar` retargeted (the only roar clip in the project).
- **Missing assets (none invented):** a vampire roar or taunt clip (Mixamo `Zombie Scream`, `Mutant Roaring`, `Standing Taunt Battlecry`, all **(verify)**); a landing clip (`Falling To Landing`, `Hard Landing` **(verify)**; the jump clip from its apex is used now); dedicated sounds: heavy body-landing thud, vampire screech / roar, low growl (now reused: `L2_Slam_Impact`, `L2_Special_FireRoar`, `L2_Heavy_Growl`; sources: freesound.org searches "heavy body fall thud", "creature screech", "monster growl low" (verify licences)).

### Part 3: guard and clash telemetry
- **Sent before this pass:** session start / result (`POST /api/sessions`), and in batched `POST /api/events`: `hit`, `weak_swing`, `clash` (hand, swingSpeed), `monster_attack`, `dodge`, `monster_defend`, `player_hurt` / `block`, `hp` snapshots every 1 s.
- **Added (`SessionReporter.sendGuardEvents`, on):** `monster_guard` (one per guard, when it drops: swingClass, guardType, blocked, blockedHits, damagePrevented, openings, reactive, broken, seconds) and `opening_hit` (swingClass, bonusDamage). `clash` (hand, speed) already existed and is unchanged. Same bounded buffer, same 0.4 s flush, same batches, so the request rate cannot rise; failures are dropped silently as before.
- **ZZTEST fight (local backend on the project database, game pointed at it at runtime only):** 40 s Level 3 fight, then a kill. Read back 216 events: `monster_guard` 4 (the game reported 4), `opening_hit` 1 (1), `clash` 80, every payload field stored verbatim, session stored as a finished victory. Requests: **18 POSTs in 32 s, never more than 1 in any second** (216 events in 16 batches). Older data: a pre-telemetry session still loads; the new insights route on a real player returns its sentences with the guard sentence pending. **Deleted** by id afterwards: database back to **47 sessions / 8,930 events, 0 ZZTEST events**; the headset's remembered-names list never gained ZZTEST and its last-session keys were cleared the way the game does for a deleted session.

### Part 4: dashboard (web/frontend, web/backend)
- **Insights** (`web/backend/insights.js`, `GET /api/players/:name/insights`, `components/fitness/Insights.jsx` on the player page): a sentence only with enough data, the minimum shown under each one, the rest listed as "what's missing". Effort trend (3 fights with 10+ hits, 30+ hits; a third must lead the others by 10 %), full-power share (20 tiered hits), best and hardest level by win rate (2 levels with 2+ won or lost fights, different rates), guards and openings (20 hits in fights with guard telemetry).
- **Leaderboard tabs** (`lib/leaderboard.js`): This week (from Monday), All time, Per level (top 5 per level), Most improved (latest third of a player's won or lost fights against their earlier ones, minimum 3; by score as % of the level's median, or by full-power share; abandoned fights left out), and a "You vs last week" line with a player picker.
- **Session replay chart** (`components/sessions/SwingTimeline.jsx`): swing speed per hit (tier colours) and monster HP over the fight, marker ticks for hits, monster guards, openings and clashes, cursor shared with the existing scrubber, click or drag to seek; fallback text when a session has no samples (the existing "No fight telemetry" card covers sessions with no events). Scrubber and event list know `monster_guard` / `opening_hit`.
- **First-run checklist** (`GET /api/players/:name/checklist`, `components/players/FirstRunChecklist.jsx`): first fight, a chosen name, a rated session, a badge, all from the database; only the dismissal is kept in localStorage (`strikefit.checklist.dismissed.<name>`).
- **Polish:** count-up on the player's results strip, the leaderboard leader and per-level scores (existing `useCountUp`, immediate under reduced motion); confetti (`components/ui/Confetti.jsx`) when a badge unlocks (on a live reload that unlocks more, or on opening a player whose latest fight earned one); none under reduced motion. Existing colour classes / tokens only, no new fonts, no dark mode; estimates untouched ("estimated" labels unchanged).
- Backend additions only: two GET routes and `analytics.playerInsights / playerChecklist`; no existing route, field or response changed.

### Part 5: verification
- `npm test` backend **31/31** (8 new for the insight rules), frontend **9/9** (new: `node --test`, the leaderboard / most-improved rules); `vite build` passes.
- Headless Chrome (DevTools protocol, local backend on the real data, 1280 px and 390 px): **0 console errors or warnings**, no horizontal scroll at 390 px. Seen: Iij's insights (3 sentences, guard pending) and checklist (4 of 4); dismissal hides it after a reload and stores only the dismissal key; confetti 36 pieces on strikewhat (latest fight earned First Blood), gone after 2.5 s, none with reduced motion; count-up 0 -> 1,836 -> 3,899 as the strip scrolls in, 3,899 at once with reduced motion; all four leaderboard tabs and both improvement measures; the replay chart (26 swing dots, 62 markers) seeking 00:39 -> 00:19 from a click; the oldest session shows the no-telemetry fallback.
- **Not checked on a real phone:** touch dragging on the chart and scrubber, the confetti and count-up on a phone GPU, the Select / segmented controls with a phone keyboard; only Chrome's 390 px emulation.

### Regression checks (re-run at the end, see the final report)
Full-body coverage 13/13, 12/12, 13/13; body block Blocked with its sound on all three; L3 parry `CurrentAttackParried = True` (L1 / L2 parry off as before); wind-ups 27/27 kept; Level 3 flame / aura / enrage scripts untouched and its entrance 6.93 s; no post-processing, no new lights or realtime shadows (the entrance dims existing lights; FX shadows off); no per-frame allocation (probe above); FindObject only at the start of the entrance; no new runtime logs; SessionReporter production URL, wake ping, 5 s timeouts, silent failure unchanged in all three saved scenes; backend: two GET routes added, nothing else changed; web: existing colour classes / tokens only, estimates untouched; test data deleted.

### Headset checks, most important first
1. The Level 2 entrance in the headset: looking up at the eyes (about 4 m), the landing at 1.85 m, the roar loudness.
2. Side / low guard IK on Levels 2 and 3 at 72 Hz (hand speed 2.5 m/s), any weapon clipping a 3/4 view missed.
3. The opening cue (flash + reused chime) reads as different from the guard flash.
4. Whether the guard feels present at all: in real fights it is mostly the timed one (the monster is free 11-32 % of the time).

---

Last updated: **2026-10-05 — all three levels: personal-space clamp, shared MonsterDefense (awareness, reactive guard, guard meter / break, poise), Level 3 rebalance with phase-shift pauses and a single-hit cap, game feel. Everything below was measured in Play mode in the editor (simulation, not a headset).**

---

## 2026-10-05 — Overlap, reactive defence, Level 3 rebalance, game feel

**Method.** Unity MCP over HTTP, Play mode on each scene, `SessionReporter.baseUrl` redirected to a dead port at runtime only (scenes still ship the production URL). Test harness: `Assets/Editor/CombatVerification.cs` (Play-mode only, never in a build). **Everything here is an editor simulation: the "player" is the real weapon collider driven through the real `HitDetection` trigger path, not a headset.** Swing speeds in the sims are drawn from the live backend histogram per level.

### New / changed files
| File | What |
|---|---|
| `Monster/MonsterPersonalSpace.cs` (new) | LateUpdate clamp (no physics): head >= 0.6 m, hands >= 0.35 m (monster-caused only), slides the root back, adds the push-back to MonsterAI's range check so attacks still land |
| `Monster/MonsterDefense.cs` (new) | ONE shared component, parameterised per level: facing, swing detection (speed + cone + range), reactive guard, timed fallback guard, guard meter / break, poise, optional dodge (shipped OFF) |
| `Combat/CombatFeedback.cs` (new) | haptics on the striking hand, clash pulse, both-hand damage pulse, monster-only hit-stop. Each behind a toggle |
| `Editor/CombatTuning.cs` (new) | the single place every per-level number lives; `Level3DualAxeBuilder` calls it so a boss rebuild keeps the tuning |
| `Monster/MonsterAI.cs` | `BeginGuard / BeginStagger / BeginDodge`, `holdActions`, aim lock (rush lane, slam zone), clamp-aware range check. When MonsterDefense is present it owns the guard: `MonsterAI.IsDefending` stays the single "is defending" flag |
| `Monster/MonsterController.cs` | poise via `MonsterDefense.TryFlinch`, stagger x1.25, phase-shift resist, single-hit cap, swing-speed aware `OnTakeDamage` |
| `Monster/BossPhaseController.cs` | phase-shift pauses (roar + resisted hits) at 66 % and 33 %, enrage shares the same hold path |
| `Combat/HitDetection.cs` | **front-hit bonus fixed**, 3 hit variants + pitch jitter, passes tier / weak point to the monster |
| `UI/CombatUIController.cs` | pooled tiered damage numbers, callouts, guard bar (redrawn only on change) |
| `Manager/LevelManager.cs` | `monsterMaxHealthOverride`, `monsterAttackRangeOverride`, `playerMaxHealthOverride` (serialized) |

### CARRY-OVER from the previous pass (checked live)
| Item | Status |
|---|---|
| (a) L1 hammer overlap fix | **PARTIAL -> DONE here.** The `useHeadTarget` + range fix held, but my consistent head-line metric put the nearest surface only 0.09 m inside the hammer reach; range 1.75 -> 1.65 now gives ~0.13 m (see Part 1) |
| (b) hammer shadow removal | **DONE** (both hammers `castShadows Off`, verified in the saved scene) |
| (c) darker L1 lighting | **DONE** (directional 0.75, gradient ambient, fog 0.012; re-checked in scene) |
| (d) L1 engagement features | **PARTIAL.** Combo counter DONE and still holds. Spacing movement / taunt need clips that do not exist. Stagger: **built now** (guard break), from the existing `Great Sword Impact` hit reaction |
| (e) L2 glow | **NOT DONE** (never started; untouched this pass on purpose) |

### PART 1 — Overlap (measured, per level)
Reach = hand 0.60 m in front of the head + weapon past the grip (measured from the colliders): **L1 hammer 0.994 m; L2 sword 1.395-1.409 m; L3 mace 1.297 / katana 1.516 / flail 1.581 m.** Metric below = horizontal distance from the head's vertical line to the nearest body capsule, monster idle.

| | before | after | reach | inside reach |
|---|---|---|---|---|
| L1 root / nearest surface (mean) | 1.74 m / 0.90 | 1.65 m / 0.87 | 0.994 | **0.13 m** |
| L2 root / nearest surface (mean) | 1.53 m / **0.86** | 1.84 m / **1.17** | 1.395 | **0.23 m** (was 0.54 = sunk into the chest) |
| L3 root / nearest surface (mean) | 1.44 m / 1.07 | 1.44 m / 1.06-1.11 | 1.297 (mace, the shortest) | **0.19-0.24 m** |
| spawn / entrance min head distance | 2.97 / - / - | L1 2.965, L2 2.906, L3 3.200; **L3 entrance (rise + roar) min 2.73 m** | | |

Forced attacks (every table row once, AI off), minimum body distance to the head: L1 0.64-0.91, L2 0.89-1.12, L3 0.79-1.30 m; **hands never below 0.35 m (monster-caused); 0 head / 0 hand breaches in all sweeps.** All attacks still land at the new spacing: **L1 6/6, L2 10/10, L3 14/14.** The L1 / L3 clamp does push the monster back up to 0.2-0.35 m per attack frame on Slash / Overhead / Spin (that is the attack's travel being shortened, not removed; the attack lands because the push-back is added to the range check). Watch it on the headset: it should read as recoil, not a stutter.
- **L2 range 1.55 -> 1.85** (scene override); L1 range 1.75 -> 1.65 (scene override). L3 unchanged.
- **Weapon / hand colliders**: all `isTrigger` on kinematic bodies (checked), the monster has no Rigidbody. Nothing physical can push the rig.
- **Finding, NOT fixed:** the monster's *own weapon* still passes within 0.03-0.06 m of the head on some attacks (L1 Overhead, L3 chops) and its swinging arm passes through the player's held weapons (that is the clash / parry). A weapon-to-head clamp (`weaponHeadClearance`) and a player-weapon clamp (`weaponClearance`) are implemented but **ship OFF**: at 0.22 / 0.02 they shoved the monster out of its attack range and still left breaches, because a swing path crosses the head zone by design.
- **L3 long weapons** (katana 1.52, flail 1.58) sink 0.2-0.3 m into the boss at full extension; that follows from sizing to the mace so every loadout can reach.

100 s simulated fights, all attacks forced at least once, swing every 1.3 s:
| | min head | min hand | breaches head/hand | attacks seen |
|---|---|---|---|---|
| L1 | 0.641 (limit 0.6) | 0.350 (0.35) | 0 / 0 | 6/6 |
| L2 | 0.928 | 0.466 | 0 / 0 | 10/10 |
| L3 mace | 0.600 | 0.350 | 0 / 0 | 12/14 in the natural fight, 14/14 in the forced sweep (ranged rows need the player out of reach) |

### PART 2 — Reactive defence (shared `MonsterDefense`)
| | L1 | L2 | L3 |
|---|---|---|---|
| reaction delay | 0.35 s | 0.25 s | 0.18 s |
| guard chance (enraged) | 15 % (15) | 30 % (30) | 45 % (**50**, +5: it is free less often) |
| min gap between guards | 6 s | 4.5 s | 3.5 s |
| guard length | 0.8-1.2 s | 0.8-1.2 | 0.8-1.2 |
| uptime budget | 12 % | 18 % | 25 % |
| **measured uptime** (100 s fight / 60 s idle-poke worst case) | 5.6 % / 6.9 % | 8.4 % / 9.1 % | 8.0 % / 14.4 % |
| timed fallback guard | 12 s | 9 s | 8 s (all +-30 %, same rules) |
| detection range | 2.5 m | 2.2 m | 1.9 m |
| turn speed | 90 deg/s | 120 | 150 |
| guard meter to break | 5 absorbed hits | 4 | 3 |
| stagger (damage x1.25) / lockout | 2.5 s / 4 s | 2.0 s / 4 s | 1.6 s / 4 s |
| poise: flinches from | Light and up | Medium | Full only (+ phase breaks) |

- **Never during entrance, wind-up, attack, flinch, phase shift, stagger or guard break** (`CanGuardNow`). Enforced by construction (`CanGuardNow` checks `isAttacking`, `IsBusy`, `holdActions`); the wind-up-hit test (27/27) shows no attack was disturbed, but I did not log guard-vs-attack overlap separately.
- **One source of truth:** `MonsterAI.IsDefending` only. The old per-MonsterAI reactive path runs only when MonsterDefense is absent.
- **Awareness proof:** max yaw change inside one attack **0.0 deg** on all levels; free turn rate never above the cap (90 / 61 / 97 deg/s). **Brute Rush with the player standing still: landed 3/3. With a 1.3 m sidestep 0.3 s in: landed 0/3** (the charge lane is locked at the wind-up).
- **Guard direction:** High guard for downward swings, Side guard otherwise (`velocity.y < -0.6|v|`). **Only ONE guard clip exists per monster** (`Monster_GuardPose` L1/L2, `L3_Defend` L3), so both directions use it; the counters (`HighGuards` / `SideGuards`) still record the choice. The Animator hook is `highGuardTrigger` / `sideGuardTrigger`.
- **Cue:** 0.12 s cool white-blue flash + the block-raise sound `L2_Defend_Brace` (L1 had none; added). Never the red telegraph.
- **Guard meter:** weapon parries never fill it (`OnGuardAbsorbedHit` is only reached from the body-block branch). Thin bar under the monster HP bar, built on first use, `SetGuardMeter` early-outs when unchanged. Guard breaks observed: L1 1, L2 2, L3 1-2 per 100 s sim. Break = white flash, `GUARD BREAK!` callout, hit-reaction played at ~0.45-0.63x speed to fill the stagger, x1.25 damage.
- **Reactive vs timed in the sims:** L2 1-3 reactive, L3 4-6 reactive (reaction measured 0.27 s / 0.20 s); **L1 gets 0 reactive in a hard-poke test** because it flinches on most hits (by design) and is almost never free; its guard is mostly the timed one. First version had range 1.5-1.7 and almost no reactive guards at L1/L2 (swing arrives before the 0.35 / 0.25 s delay); widened to 2.5 / 2.2.
- **Dodge:** implemented (`BeginDodge`, arena-radius limited, 8 s cooldown, never while busy), **shipped OFF** (`dodgeEnabled = false`): no sidestep / backstep clips exist.
- **Animator discipline:** no controller was edited. No Any State transitions besides Death (re-read all three). Every new state path reuses existing transitions (Defend / Hit / Idle). **Trigger -> animation start: <= 1 frame (0.000-0.033 s at 30 fps) for every attack in every sim; no attack missing.**

**Missing clips (all unverified, I cannot log in to Mixamo):**
| need | search | alternatives |
|---|---|---|
| guard high | `Sword And Shield Block` (verify) | `Great Sword Blocking`, `Standing Block Idle` (verify) |
| guard side | `Sword And Shield Block Left` (verify) | `Standing Block Left`, `Great Sword Parry` (verify) |
| sidestep | `Sword And Shield Strafe Left` / `Right` (verify) | `Left Strafe`, `Dodge Left` (verify) |
| backstep | `Sword And Shield Walk Back` (verify) | `Stand To Roll Back`, `Backwards Dodge` (verify) |
| stagger | `Great Sword Impact` is ON DISK and used | `Standing React Large From Front` |

### PART 3 — Level 3 rebalance
**Diagnosis (read, not guessed).**
- **Front-hit bonus was effectively DEAD**: `ComputeFacingMultiplier` used a 3-D direction from the monster's foot-level root to a chest-height hit point, so the dot with `forward` was ~0.2 and the 0.35 threshold almost never passed. In the sims it fired on **6/49 hits (old formula) vs 49/49 (horizontal fix)**. Fixing it made every front hit +25 %, so L1 hammer base 12 -> 9.5 to compensate.
- Max single hit (everything stacked: full speed, angle 1.4, front 1.25, weak 1.75, full momentum, 40 taps): **mace 181 (12 % of 1500 HP, before), flail 145, katana 59.** Now capped at **6 % of max HP = 174 of 2900** (`MonsterController.maxSingleHitFraction`); the real maxima after rebalance are 4.9 % / 4.6 % / 2.5 % (+x1.25 in a stagger = 6.1 % / 5.7 % / 3.2 %), so the cap only bites in the corner case.
- Live backend (read-only GET): Easy 62 swings mean 6.5 m/s, Medium 204 swings mean 2.1 m/s, **Hard 61 swings mean 4.2 m/s** (so L3 has real data this time; tiers: 43 % light / 31 % medium / 26 % full). Dmg per hit L1 ~41, L2 ~21, L3 ~44-64.
- Round timer = **40 s** on all three levels (`RoundManager.roundDuration`). Both `timerExpireMeansDefeat` and `timerExpireMeansSurvivalWin` are **false** in all scenes, so the battle just continues, but **a ~2 min Level 3 fight does not fit the 40 s timer** (HUD shows 0 s, `RegisterTap` stops counting, tap damage bonus is frozen at the first 40 s). Not changed (decision pending).

**TTK (simple Monte-Carlo, 2,500 fights each; inputs: live swing histograms, measured guard / stagger shares, hit rate 0.43 / 0.59 per s L1 / L2, L3 0.55 / 0.65 / 0.85 per s for mace / flail / katana (ASSUMPTION), tap bonus capped at 40 s). A model, not a headset.**
| | before | after |
|---|---|---|
| L1 hammer | 35 s | 34 s (HP 500 unchanged, base dmg 12 -> 9.5) |
| L2 sword | 76 s (**2.2x** L1) | **54 s (1.5x)** (HP 800 -> 680) |
| L3 mace | 59 s | **122 s** (3.5x old L1) |
| L3 flail | 64 s | **115 s** |
| L3 katana | 85 s | **116 s** |
Weapon spread before 59-85 s (**44 %**), after 115-122 s (**6 %**) via damage multipliers mace 1.35 -> 1.05, flail 1.1 -> 1.0, katana 0.9 -> 1.12. **L3 HP 1500 -> 2900.** I took the "2-3 minutes" goal over the "2.2-2.5x L1" suggestion (that would be 77-88 s); to hit 85 s set `monsterMaxHealthOverride` to about 2000 in Level3_Hard.

**Phase shifts (66 % and 33 %):** hits are x0.25 and show `RESISTED n` (grey) for the roar (2.4 s); the roar starts after the current swing / guard / stagger ends. Verified: 2 phase shifts and 2-5 resisted hits per sim; enrage uses the same hold.
**Enrage:** tempo 1.3 -> **1.15** (shortest wind-up 0.525 s / 1.15 = **0.457 s >= 0.45 s**; at 1.3 it was 0.40 s), cooldown scale 0.8 -> 0.7. The slam keeps its warning sound + red ring + purple tint (unchanged).
**Player HP loss (model + sim):** never blocks or dodges: **12.5 HP/s** measured (slam only 135 of 1,247 damage, 11 %) -> ~1,500-1,700 over a 2 min fight, so with **player HP 800 -> 1100 (L3 only)** that player dies at ~85 s, roughly two thirds through. A player who dodges the slam and half the other arcs (ASSUMPTION): ~5.6 HP/s = ~670 of 1,100 HP (about 40 % left). Hard, winnable.

| | L1 | L2 | L3 |
|---|---|---|---|
| monster max HP | 500 | 680 | 2900 |
| typical TTK (model) | 34 s | 54 s | 115-122 s |
| telegraph range (first hit) | 0.89-1.6 s | 0.57-1.48 s | 0.525-1.3 s (enraged 0.457-1.13) |
| attack cooldown | 3.2 s | 2.2 s | 1.9 s |
| monster damage / hit (x flavour) | 8 (x1.1) | 14 (x1.2) | 18 (x1.25) |
| attacks | 6 | 10 | 14 |
| reactive chance | 15 % | 30 % | 45 % |
| guard meter threshold | 5 | 4 | 3 |
| poise | Light | Medium | Full |
| player HP | 500 | 800 | 1100 |
Every row ascends L1 < L2 < L3 (telegraph time and cooldown descend, as required) **except** that L2's first-hit telegraph minimum (0.57 s) is below L3's (0.525 s) in the wrong direction by 0.05 s; L3's minimum is the 0.45 s readability floor, so I left it.

### PART 4 — Game feel (all behind toggles)
| item | status |
|---|---|
| Haptics (`CombatFeedback.haptics`) | **ON.** 174 pulses in a 40 s L3 sim, light 0.25 / medium 0.5 / full+weak 0.9 / blocked 0.35 / clash 1.0 (0.03 s) / damage both hands 0.7; every pulse stopped by a realtime coroutine, newest pulse wins. **Untested on a controller** (editor has none) |
| Hit-stop (`hitStop`, 0.07 s) | **ON**, monster Animator + AI clock only. 3 hit-stops in the sim, `Time.timeScale` stayed 1 |
| Sound variation (`HitDetection.varyHitSounds`) | **ON.** 3 + 3 variants per weapon from existing clips, +-6 % pitch. **No new files needed; flagged: variants are reused clips, so true variety wants new recordings** |
| Damage numbers by tier (`builtInDamageNumbers`) | **ON.** There was NO number system (`damageNumberPrefab` is null in every scene, numbers only logged). Built a pooled one: size / colour by tier, grey `RESISTED`, blue `BLOCKED`, `GUARD BREAK!`; 0 allocation per hit after the pool is built |

### PART 5 — Performance (editor, Play mode; NOT Quest numbers)
| | draw calls | triangles | setPass | shadow casters | script ms/frame |
|---|---|---|---|---|---|
| L1 before -> after | 104 -> 114 | 134k -> 134k | 16 -> 14 | 62 -> 66 | 0.11 -> 0.74 |
| L2 before -> after | 51 -> 49 | 74k -> 69k | 11 -> 11 | 25 -> 23 | 0.15 -> 0.18 |
| L3 before -> after | 91 -> 89 | 209k -> 207k | 34 -> 33 | 40 -> 38 | 0.19 -> 0.34 |
The L1 "after" script figure is the noisiest (one sampled window, while the monster was mid-leap); treat 0.1-0.7 ms as the range. Draw-call changes are within run-to-run noise (the guard bar and numbers are screen-space / pooled). **No new lights, no new realtime shadows, no post-processing.** Console: **0 errors, 0 warnings** after the last Play session (the one pre-existing editor-only deprecation warning in `Level3EngagementBuilder` reappears on a recompile). The new components do not allocate per frame: `MonsterPersonalSpace` / `MonsterDefense` use preallocated arrays and cached references, `CombatUIController` numbers are pooled.

### Regression checklist (how each was checked)
| item | result |
|---|---|
| Defend uses `Monster_GuardPose` (L1/L2), no sitting | **holds** - no controller was edited; guard raise uses the same Defend state |
| Player hit never cancels a wind-up | **holds: 27/27** mid-wind-up full-tier hits (L1 6, L2 10, L3 11 melee): state unchanged, no Hit state, damage applied, attack resolved |
| Full-body hit coverage, weapon colliders tagged MonsterWeapon | **holds** (collider dump of all three prefabs: 13-15 hit volumes, `MonsterWeapon` triggers untouched) |
| Weapon parry / body block / normal hit | **holds** - in every sim all three counters were non-zero (e.g. L3 mace: 156 clashes, 4 body-blocked, 55 hits); L3 slam stays unblockable |
| Log spam / HUD / allocations | **holds** - no new `Debug.Log` outside `verboseAttackLogs`; guard bar and numbers redraw only on change |
| SessionReporter | **holds** - all scenes: production URL, wake ping, 60 s wake / 5 s timeout, verbose off (re-read from the loaded scenes after saving) |
| Scenes separate, all three enabled in Build Settings | **FIXED.** Level 1 only was enabled (`ede881d`); L1 + L2 + L3 are now enabled (MainMenu / Results left disabled as found) |
| L3 flame / aura look unchanged | **holds** - no change to `MonsterGlow`, `MonsterAxeFlames`, `MonsterSerpentAura`, materials |

### Headset checks, most important first
1. **Recoil feel** of the personal-space clamp on L1 / L3 attacks (up to ~0.3 m back-slide in a frame). If it reads as a stutter, set `MonsterPersonalSpace.iterations` lower or add a push-speed limit.
2. **L1 spacing**: nearest surface now 0.13 m inside the hammer reach. If you cannot reach it comfortably, raise the L1 scene's `monsterAttackRangeOverride` toward 1.75.
3. **Reaction fairness**: the guard goes up about 0.35 / 0.25 / 0.18 s after a fast swing comes in. A fast swing should still beat it.
4. **Haptics** (never run on a controller) and the **0.07 s monster hit-stop** comfort.
5. **L3 length**: ~2 min and 1100 HP; the 40 s round timer will have expired long before the end.
6. **Phase-shift roars** at 66 % / 33 % (the hits are resisted; check the "RESISTED" cue reads).
7. Damage-number size at arm's length; the guard bar visibility under the HP bar.
8. Frame time on Quest with OVR Metrics (below).

**OVR Metrics on the headset** (each level): install OVR Metrics Tool, enable the overlay in `Developer Runtime Tools`, log FPS, **stale frames**, CPU level, GPU level, app GPU time; fight for 2 minutes at normal swing intensity, then read the CSV. Pass: FPS 72 flat, stale frames 0, GPU level <= 3 (L3 will be the highest: ~207k tris, 33 setPass).

---

Last updated: **2026-10-04 — Level 1: monster spacing root-caused and fixed (useHeadTarget), hammers removed from the shadow pass (-36% triangles), darker battle lighting, combo counter shipped. Parts 4B1/4B2/4B3/4C/4E and Part 5 NOT built — see "Not done this pass".**

---

## 2026-10-04 — L1 spacing, L1 rendering, L1 mood lighting, combo counter

**Method.** Unity MCP again over raw HTTP (the configured client link still fails with ECONNREFUSED; the server on 127.0.0.1:8080 works: initialize → mcp-session-id → tools/call). Every number below was measured on live objects in Play mode or read off the real assets. Test fights ran with `SessionReporter.baseUrl` redirected at runtime to `http://127.0.0.1:9`; the saved scenes still carry the production URL (re-checked after saving).

**Two Play-mode traps, both mine, both now avoidable:**
- **The editor does not tick while unfocused.** Play mode entered but froze at frame 172. `Application.runInBackground = true` (set at runtime, first thing after entering Play) fixes it. Without this every "nothing happened" result is meaningless.
- **`PlayerNamePicker` sits ON `BattleSystems`.** Calling `pnp.gameObject.SetActive(false)` to clear the name gate **deactivates BattleSystems**, which silently kills `Update` on BattleManager, RoundManager, SessionReporter and XPManager. Disable the *component* (`pnp.enabled = false`) — `BlocksStart` already checks `isActiveAndEnabled`. This produced one false "combo never expires" bug and the session's only console error.

### CARRY-OVER from the previous pass

| Item | Status |
|---|---|
| (a) Level 1 animation diagnosis | **Done and still holding.** `Monster_Easy_HipHop` Animator `cullingMode = AlwaysAnimate`, same as L2/L3. |
| (b) Monster colour / material fix | **Done and still holding.** `bear_normal.png` and `hammer_FINISH_Material_001_Normal.png` are both `NormalMap`, sRGB off. No material under the monster uses an error shader. |
| (c) Subtle glowing hammers | **Still not built** (see Part 4E below). |

**Checked while there, not a bug:** the Level 1 monster's sword renders bright pink/magenta. That is the shattered-crystal-sword's own `lambert2` texture, **not** the URP magenta error shader — every material under the monster resolves to `Universal Render Pipeline/Lit` or `/Unlit`. Worth an eye on the headset, but nothing is broken. (Minor: `pPlane1..4` still use `lambert2` while `pPlane8..13,21` use `M_L1_SwordGlow`; only the latter 7 were rewired last pass.)

### PART 1 — "hammer/hand overlapping the monster": ROOT CAUSE = L1 measured distance to the rig ROOT in 3D

**The cause is one serialized flag.** `MonsterAI.useHeadTarget` was **false** on the Level 1 monster and **true** on Levels 2 and 3. With it false, `DistanceToTarget()` is the **3-D** distance from the monster's feet (y≈0) to the `OVRCameraRig` root at **y = 1.6**. The monster closed until that 3-D distance hit `attackRange` 2.2, which left it only

    sqrt(2.2² − 1.6²) = **1.509 m away horizontally**

That is why only Level 1 had the problem: L2 (1.55) and L3 (1.45) already measure *horizontal* metres to the headset, so their numbers mean what they say.

**Measured before the fix** (Play mode, fight running, monster settled):

| | before |
|---|---|
| monster root, horizontal from head | 1.509 m |
| nearest body surface (R forearm) | 0.618–0.744 m |
| monster's head collider | 0.740 m |
| chest | 1.067 m |
| **nearest body surface during its own attacks** | **0.189 m** (Overhead Strike), 0.217 Spin Slash, 0.228 Slash |

**Player reach, measured from the real transforms** (not assumed): the hammer mesh `HIGH` is 0.575 m long at scale 0.085; the striking `BoxCollider` is 0.272 × 0.187 × 0.238 m sitting 0.017 m from the grip pivot, and the grip pivot is 0.216 m from the hand anchor — so the weapon adds **0.392 m beyond the hand anchor**. Sweeping the real hand anchor forward gives:

| hand forward of head | hammer box far face |
|---|---|
| 0.38 m (editor rest pose) | 0.799 m |
| 0.55 m | 0.964 m |
| **0.60 m (arm extended)** | **1.013 m ← max reach** |
| 0.65 m (extended + lean) | 1.062 m |

**Fix (two edits):**
1. `Monster_Easy_HipHop.prefab` → `MonsterAI.useHeadTarget = **true**` (now matches L2/L3).
2. `LevelManager.GetParams(Easy).monsterAttackRange` 2.2 → **1.75** — now horizontal metres to the headset.

**Measured after the fix** (monster settles at 1.733 m, 493 frames of idle sampled):

| | before | after | target |
|---|---|---|---|
| monster root, horizontal | 1.509 | **1.733** | — |
| nearest body surface | 0.618–0.744 | **0.771 min / 0.845 mean / 1.006 max** | — |
| → inside max reach (1.013 m) | 0.27–0.43 | **0.168 m mean (0.007–0.242 over the idle cycle)** | 0.15–0.25 ✓ |
| monster head collider | 0.740 | **1.053** | ≥ 0.5 ✓ |
| chest | 1.067 | **1.332** | — |

**Personal space during the monster's own attacks**, all six forced and sampled per frame:

| attack | nearest ANY collider (before → after) | nearest torso/head (after) |
|---|---|---|
| Slash | 0.228 → **0.473** | 0.867 |
| Side Sweep | 0.396 → **0.697** | 0.896 |
| Overhead Strike | 0.189 → **0.495** | 0.794 |
| Cut and Chop | 0.270 → **0.537** | 0.758 |
| Spin Slash | 0.217 → **0.442** | 0.908 |
| Leaping Slam | 0.432 → **0.601** | 0.771 |

**The monster's body (torso, hips, head, body capsule) never comes closer than 0.758 m** — comfortably past the 0.5 m rule. What still dips below 0.5 m is the **swinging forearm** on Slash / Overhead / Spin Slash (0.442–0.495 m), i.e. the sword arm passing through the swing, not the body arriving. Caveat: the editor was running ~10 fps during that sweep (42–92 frames per attack), so the true minimum could be a few cm lower than sampled.

**No lunge exists to shorten.** The monster's root Z was **constant at 1.509** through every one of the six attacks, including Leaping Slam (which lifts the head to 3.56 m but lands on the spot). The airborne attacks are vertical; nothing carries the body through the head or hand volume.

**Item 3 — contact forces: already clean, nothing to change.** Both hammer colliders are `BoxCollider` **isTrigger = true** on **kinematic** Rigidbodies (gravity off). The monster has **no Rigidbody and no CharacterController** at all, so its 14 hit volumes are static colliders that cannot push anything. The layer collision matrix has **zero** ignore pairs — everything is on `Default` — which is harmless here because there is no dynamic body in the fight. `Physics.ComputePenetration` between each hammer and every monster collider reports **no overlap** in the idle pose.

**Item 4 — attacks still land, both directions, verified in Play mode:**
- **Monster → player:** 70 s natural fight, AI choosing: 18 attacks started, **17 of 17 resolved attacks reached the player**, resolve distance 1.685–1.781 m, 9 defends. Mix: Slash ×5, Side Sweep ×5, Cut and Chop ×4, Spin Slash ×3, Overhead ×1.
- **Player → monster:** driving the real hand anchor through a 0.30–0.70 m thrust at 1.6 Hz for 25 s landed **26 real hits** through the real `HitDetection` trigger path (19–22 damage each), plus 10 weapon clashes and 4 correctly-rejected too-slow taps, taking the monster from **500 HP to 0**. The new spacing does not put the monster out of reach.

### PART 2 — Level 1 rendering: hammers removed from the shadow pass

**Measured in Play mode, Level 1, fight active, before → after:**

| | before | after | change |
|---|---|---|---|
| triangles | 208,783 | **133,147** | **−75,636 (−36.2 %)** |
| vertices | 177,622 | 125,224 | −52,398 (−29.5 %) |
| draw calls | 114 | **98** | −16 |
| SetPass calls | 16 | 16 | 0 |
| shadow casters | 68 | **59** | −9 |

The 75,636 triangles saved is almost exactly the hammer pair (37,672 × 2 = 75,344): they were being drawn a **second** time for the shadow map. `PlayerHammer` and `PlayerHammer_Left` are now `castShadows = Off`, `receiveShadows = false` (both verified on the saved scene; the right one is a prefab instance, so its change is a prefab override).

**Do not repeat this measurement trap:** summing `sharedMesh` triangles over shadow-casting renderers reports ~1.4 M triangles for Level 1. That is wrong — 49 dungeon renderers share one `Combined Mesh (root: scene)` static-batch mesh and each draws only a slice. `UnityStats.triangles` (208 k → 133 k) is the real per-frame figure.

**URP asset — the shipping one was already right.** Build target is **Android**, and Android's default quality level is **0 "Mobile" → `Assets/Settings/Mobile_RPAsset.asset`**. The editor runs quality level "PC" (`PC_RPAsset`), so editor-only numbers come from a different asset.

| Mobile_RPAsset (ships to Quest) | value | verdict |
|---|---|---|
| shadow cascades | **1** | already correct |
| additional-light shadows | **off** | already correct |
| soft shadows | off | already correct |
| main-light shadow resolution | 1024 | fine |
| render scale / MSAA | 0.80 / 4× | fine |
| shadow distance | 50 m → **20 m** | **changed** |

Level 1's whole arena is 16.96 × 5.24 × 12.84 m and the furthest shadow-casting renderer sits **7.27 m** from the player, so 50 m of shadow distance was spreading one 1024² map over 2.5× more range than any level needs. **This asset is shared by all three levels** — Level 3's dock arena has not been re-checked at 20 m, so if distant dock geometry loses its shadow, raise this back toward 30 m.

**Hammer meshes: 37,672 triangles each, not decimated (as instructed).** To cut them further, provide either a lower-poly hammer model, or approval to run a Blender decimation to roughly **6–8 k triangles each** — at arm's length in VR the silhouette and the bronze banding are all that read, and ~80 % of the geometry is not visible. The mesh is a **single submesh, 25,958 vertices, Read/Write disabled**.

### PART 3 — Level 1 darker battle atmosphere

**Read first (so L1 < L2 < L3 is preserved by numbers, not by eye):**

| | L1 before | **L1 after** | L2 | L3 |
|---|---|---|---|---|
| directional intensity | 1.25 | **0.75** (60 %) | 0.55 | 0.45 |
| directional colour | 1.00, 1.00, 1.00 | **0.80, 0.86, 1.00** | 0.62, 0.72, 0.95 | 0.62, 0.72, 0.90 |
| ambient mode | Skybox | **Gradient** | Gradient | Gradient |
| ambient sky | (skybox-driven) | **0.24, 0.27, 0.33** | 0.20, 0.25, 0.36 | 0.16, 0.20, 0.26 |
| fog | **off** | **on, exp², 0.012** | on, exp², 0.022 | on, exp², 0.045 |
| fog colour | — | **0.14, 0.16, 0.21** | 0.10, 0.12, 0.17 | 0.12, 0.14, 0.17 |
| torch point lights | 2 | 2 (unchanged, **shadows Soft → None**) | 7 | 8 |

Level 1 remains the lightest of the three on every axis (brightest directional, thinnest fog, lightest ambient).

**Correction to an earlier note:** Level 1 was previously recorded as having no point lights. It has **two torches** at (±4.00, 1.60, 0.62), warm orange (0.80, 0.49, 0.16), intensity 2.86, range 5.82. They were missed because they live inside a prefab instance and so are not raw `Light` blocks in the scene YAML — they only show up on live objects. Their intensity is untouched, so they now read *more* strongly against the darker ambient. Their shadows are off, so **Part 3 adds no realtime shadow cost** (and the shipping Mobile asset disables additional-light shadows anyway).

**Measured on real renders from the headset camera** (mean luminance, 0–255, region below the HUD):

| | scene luminance | median |
|---|---|---|
| before | 100.9 | 93.4 |
| **after** | **67.7** | **64.3** |

**−33 %**, and the dungeon is still clearly readable.

**HUD contrast: measured, no change needed.** The HUD is unlit UI, so the darker scene does not dim it. Bright-pixel / panel-pixel contrast is **4.3× before and 4.3× after**; the text and bars read *better* against the darker background. Nothing was brightened.

**Not changed:** the skybox. `AllSky_Space_AnotherPlanet` uses the `Mobile/Skybox` shader, which exposes **no `_Exposure` and no `_Tint`**, so it cannot be dimmed without swapping the material — and the material may be shared. Switching ambient to Gradient already removed the skybox's contribution to scene lighting, which was the part that mattered.

### PART 4A — animation inventory (matched by asset identity, not name)

Every Mixamo FBX clip in this project is literally named `mixamo.com`, so any name-based audit of clip usage is garbage. Matched by asset reference instead:

| clip file | len | used by |
|---|---|---|
| `Great Sword High Spin Attack.fbx` | 1.87 s | L1:Atk_SpinSlash (0.87) · L2:Atk_Whirlwind (1.10) |
| `Great Sword Slash.fbx` | 1.83 s | L1:Atk_Slash (0.94) · L2:Atk_Diagonal (1.20) |
| `Monster_HitReaction.fbx` | 1.60 s | L1:Hit · L2:Hit · Vampire:Hit |
| `Monster_SwordIdle.fbx` | 3.70 s (loop) | L1:Idle · L2:Idle · Vampire:Idle |
| `Monster_GuardPose.anim` | 1.00 s | L1:Defend (0.667) · L2:Defend (1.00) |
| `Mutant Jump Attack.fbx` | 3.70 s | L1:Atk_LeapSlam |
| `Sword And Shield Attack (L1).fbx` | 2.33 s | L1:Atk_OverheadStrike |
| `Sword And Shield Slash (L1).fbx` | 2.43 s | L1:Atk_CutAndChop |
| `Standing Melee Attack Horizontal.fbx` | 2.40 s | L1:Atk_SideSweep · L2:Atk_Sweep · Vampire:Attack |
| **`Great Sword Impact.fbx`** | **0.67 s** | **only the legacy Vampire controller — free for Level 1** |
| **`Great Sword Idle.fbx`** | **3.70 s** | ***UNUSED*** — imported **Generic / NoAvatar**, so unusable on a humanoid without re-import |
| **`Hit Reaction.fbx`** | 1.60 s | ***UNUSED*** (duplicate of Monster_HitReaction) |
| **`One Hand Sword Combo.fbx`** | 4.53 s | ***UNUSED*** |

**Gaps against the requested list:**

| needed for | clip in project? |
|---|---|
| walk forward | **none** |
| walk back | **none** |
| strafe left / right | **none** |
| idle variant | `Great Sword Idle.fbx` exists but is **Generic/NoAvatar** — needs re-import as Human to be usable |
| taunt | **none** |
| big-hit stagger | **`Great Sword Impact.fbx` (0.67 s) is exactly this** — it is the knocked-back recoil that was wrongly used as a guard pose and caused the old "sitting" bug. Free to use. |

### PART 4D — Mixamo shopping list

**Import settings used by every humanoid clip in this project** (copy these exactly): Animation Type **Humanoid**, Avatar Definition **Create From This Model**, Import Materials **None**, Scale Factor **1** with **Use File Scale on**, **Resample Curves on**, Anim. Compression **Keyframe Reduction**, Import BlendShapes on, Optimize Game Objects **off**, Read/Write **off**, Mesh Compression **Off**, `keepOriginalPositionY` **on**, `lockRootHeightY` **on** for the L1 attack clips, Loop Time **on only for idle/locomotion**.

| need | primary search term | alternatives | note |
|---|---|---|---|
| walk forward | `Sword And Shield Walk` | `Great Sword Walk`, `Zombie Walk` | (verify) — pick the in-place variant and bake root motion as the L1 clips do |
| walk back | `Sword And Shield Walk Back` | `Walking Backwards` | (verify) |
| strafe left | `Sword And Shield Strafe Left` | `Left Strafe Walking` | (verify) |
| strafe right | `Sword And Shield Strafe Right` | `Right Strafe Walking` | (verify) |
| taunt | `Taunt` | `Standing Taunt Battlecry`, `Roaring` | (verify) |
| idle variant | `Sword And Shield Idle` | `Great Sword Idle` (already on disk — re-import as **Humanoid** instead of buying it again) | the on-disk file only needs its importer changed |
| big-hit stagger | *(already on disk: `Great Sword Impact.fbx`)* | `Standing React Large From Front` | no purchase needed |

I cannot log in to mixamo.com, so every search term above is marked **(verify)**.

### PART 4B4 — combo counter: BUILT AND VERIFIED

- `XPManager`: `comboWindow` **2.0 s**, `xpComboStepBonus` **8**, `comboBonusCap` **10**; `CurrentCombo`, `BestCombo`, `OnComboChanged`. Consecutive hits inside the window raise the count; the bonus is `8 × (min(combo, 10) − 1)` XP, capped so a long chain cannot run away with the score. `Update()` returns on its first line unless a combo is actually live.
- `CombatUIController.UpdateCombo(int)`: rewrites the label **only when the value changes** (`shownCombo` guard), and shows nothing below ×2, so normal trading leaves the HUD quiet. New `ComboText` on `CombatCanvas`, cloned from the XP label, gold (1.00, 0.82, 0.25) so it does not read as XP.

**Verified in Play mode.** Six hits inside the window, same 3.2 m/s swing and 20 damage:

| hit | combo | XP gained | label |
|---|---|---|---|
| 1 | 1 | 32 | *(blank)* |
| 2 | 2 | 40 | Combo x2 |
| 3 | 3 | 48 | Combo x3 |
| 6 | 6 | 72 | Combo x6 |

Three hits spaced 2.6 s apart each gave combo **1** and 32 XP with a blank label. After the window lapsed the counter cleared to 0 and the label blanked. In the **real** 26-hit fight (hits through `HitDetection`, not injected) the counter reached **BestCombo = 16**.

### Not done this pass — deliberately, not half-built

- **Part 4B1 (spacing movement):** not built. There is **no locomotion clip of any kind** in the project — no walk forward, walk back or strafe (see 4A). Per the brief this ships disabled, and it would have to ship disabled, so no toggle was added to avoid dead code. Buy the four clips in 4D first.
- **Part 4B2 (stagger on strong hits):** not built, but it is now **unblocked** — `Great Sword Impact.fbx` (0.67 s) is the right clip and is free. Needs a `Stagger` state in `Monster_L1_Sword`, a trigger, a ≥6 s cooldown and a full-tier check in `MonsterAI`.
- **Part 4B3 (taunt / idle variation):** not built. No taunt clip exists; the one candidate idle variant is imported Generic/NoAvatar.
- **Part 4C (animator discipline + 100 s trace):** only partly done. Verified: **`Any State → Death` is the only Any State transition in all three controllers**, the Defend clips are correct, and a player hit never cancels a wind-up (below). The per-attack trigger-to-animation-start delay trace was **not** re-run this pass.
- **Part 4E (hammer glow):** still not built, for the same reasons as last pass, which were re-checked and still hold: the hammer mesh is a **single submesh, 25,958 vertices, Read/Write disabled**, one material over head, haft and pommel. A head-only glow needs either Read/Write + a generated emission mask, or a head/haft submesh split. Both are real asset changes on the object the player stares at.
- **Part 5 (Level 2 glow, shared with Level 3):** **not started.** Flagged as the risky part in the brief and there was not enough of this pass left to refactor Level 3's working VFX into shared components *and* prove Level 3 is unchanged. Level 3's look is therefore untouched and still correct.

### Regression check — every item on the DO-NOT-REGRESS list

| item | result |
|---|---|
| Defend uses `Monster_GuardPose.anim`, no sitting | **holds.** L1:Defend → Monster_GuardPose (speed 0.667), L2:Defend → Monster_GuardPose (1.000), L3 → L3_Defend. Live over 461 Defend frames: hips **0.740 min / 0.757 mean**, leg extension **0.511 min** (the old sitting bug was hips 0.482, extension 0.287). Standing. |
| A player hit never cancels a wind-up | **holds.** Damage applied mid-wind-up on Slash, Overhead Strike, Spin Slash and Leaping Slam: animator state unchanged in **4 of 4**, `isAttacking` still true, damage still applied (500 → 460). |
| Full-body hit coverage on every monster | **holds.** Easy 13 volumes, Vampire 12 (Torso instead of Chest+Hips), TwinAxeBrute 13. |
| Per-hit log spam off; HUD updates only on visible change | **holds.** `verboseAttackLogs` false on all three monster prefabs for both MonsterAI and MonsterAnimator. The new combo label follows the same rule via its `shownCombo` guard. |
| SessionReporter wake ping, timeouts, production URL in all scenes | **holds.** All three scenes: `baseUrl = https://s-t-r-i-k-e-f-i-t.onrender.com`, `wakeBackendOnLoad 1`, `wakeTimeoutSeconds 60`, `requestTimeoutSeconds 5`, `verboseLogging 0`. Re-checked in the saved files after the runtime redirects. |
| Three separate scenes; Level1_Easy enabled in Build Settings | **separate scenes hold. Level1_Easy is enabled — but see the build blocker below.** |

### BUILD BLOCKER — Build Settings now ship Level 1 ONLY

    [x] Assets/Scenes/Level1_Easy.unity
    [ ] Assets/Scenes/Level2_Medium.unity
    [ ] Assets/Scenes/Level3_Hard.unity
    [ ] Assets/Scenes/MainMenu.unity
    [ ] Assets/Scenes/Results.unity

This is the mirror image of the previous pass (then L2+L3 shipped and L1 did not); commit `ede881d`/`a9a1ea0` "build settings reframed" flipped it. Level 1 **is** enabled as the DO-NOT-REGRESS list requires, so this was left exactly as found — but a build made now contains **only Level 1**, and `GameStateManager`'s L1 → L2 → L3 progression has nowhere to go. Tick L2 and L3 before building anything but a Level 1 test.

### Headset checks, most important first

1. **Level 1 spacing.** The monster should now stand about 1.73 m away and be fully visible rather than filling your view. A normal swing should connect on its arms/weapon without the hammer sinking into its chest. Its sword arm still sweeps to ~0.44–0.50 m from your face on Slash / Overhead / Spin Slash — if that feels too close in the headset, raise `monsterAttackRange` (Easy) from 1.75 toward 1.85.
2. **Level 1 frame time**, OVR Metrics overlay on, swinging steadily. The hammers no longer draw in the shadow pass (−36 % triangles, −16 draw calls). If GPU% is still pinned, the hammer meshes themselves (37,672 tris each) are the next cut and need your go-ahead.
3. **Level 1 mood.** Should read clearly darker and cooler but never murky; the two torches should now carry the room. Quest panels are dimmer than a monitor — if it is too dark, raise the directional from 0.75 toward 0.90 and/or drop fog density from 0.012.
4. **Combo counter.** Land hits under 2 s apart; a gold "Combo x2/x3/…" should appear above-left of XP and vanish about 2 s after you stop. Check it does not hitch the HUD during a long chain.
5. **Shadow distance 20 m on Level 3.** Shared asset — confirm no distant dock geometry lost its shadow.
6. **Monster sword colour** (pink crystal) — confirmed not a shader error, but worth an eye on Quest/Vulkan.
7. **Build Settings** — decide whether L2/L3 go back in before the next build.

---

## 2026-10-03 (second pass) — Level 1: animations frozen by culling, colour killed by mis-imported normal maps

**Method.** Unity MCP over HTTP again (the client link still fails with ECONNREFUSED; the server on 127.0.0.1:8080 works). Everything below was inspected on live objects or watched in Play mode, and the visual claims come from real camera renders taken in the scene, not from reading settings. Test fights ran with `SessionReporter.baseUrl` redirected at runtime to `http://127.0.0.1:9`. **0 console errors at the end.**

### PART 1 — "animations not proper": ROOT CAUSE = Animator culling mode

**Ruled out first, by direct inspection (this bug class has recurred here, so none of it was assumed):**
- `Monster_L1_Sword.controller`: 10 states, **0 null/missing motions, 0 null transition destinations, 0 transitions with neither a condition nor an exit time**. All 10 trigger parameters exist and every attack's trigger matches a real state. `Any State -> Death` only. Every clip reference resolves to a real FBX.
- **Rig/avatar is clean:** all 10 source FBXs (idle, hit, death, 6 attacks, the base model) are `animationType=Human`, `avatarSetup=CreateFromThisModel`, each with its **own valid, human avatar**. The prefab uses `Hip Hop DancingAvatar` (valid, human); the `Warrok` skinned mesh has 81 bones and **0 null bones**. No T-pose/mapping fallback anywhere.

**The actual cause:** the Level 1 monster prefab's Animator was set to **`cullingMode = CullUpdateTransforms`** — the **only** one of the three monsters set that way (`Monster_Medium_VampireLust` and `Monster_Hard_TwinAxeBrute` are both `AlwaysAnimate`). With that mode Unity stops retargeting and stops writing bone transforms whenever the renderer is not being rendered; the state machine keeps running, so the fight logic continues while the body is frozen in a stale pose.

Caught directly: the first camera render of the spawned monster showed the mesh **collapsed into a heap**. Flipping only `cullingMode` to `AlwaysAnimate` and re-rendering, with nothing else changed, produced a correct standing ogre (hips 0.750, head 1.524, left foot 0.226).

This is not only an editor artefact. `SkinnedMeshRenderer.updateWhenOffscreen = false` and its `localBounds` are 1.75 x **2.37** x 1.57 m, while **Leaping Slam lifts the head to 3.56 m** — the monster's own signature attack leaves its renderer bounds, so it could be culled (and frozen) mid-jump on device too.

**Fix:** `Monster_Easy_HipHop.prefab` Animator `cullingMode` -> **`AlwaysAnimate`**, matching Levels 2 and 3. Nothing else in the controller or rig was touched.

**Verified in Play mode, every state, after the fix** (AI attack selection and movement disabled so each state was isolated, then a natural fight):

| State | Result | Evidence |
|---|---|---|
| Idle | correct | standing, hips ~0.85, head ~1.55 |
| Atk_Slash | correct | `Idle>Atk_Slash>Idle`, hips 0.78-0.91 |
| Atk_SideSweep | correct | `Idle>Atk_SideSweep>Idle`, hips 0.73-0.85 |
| Atk_OverheadStrike | correct | hopping strike, head to 2.67 m (matches the documented hop) |
| Atk_CutAndChop | correct | ~0.5 m hop on the chop, as documented |
| Atk_SpinSlash | correct | feet return to the floor (footMin -0.01) |
| Atk_LeapSlam | correct | 2.4 m jump, head to 3.56 m — the documented signature move |
| Hit | correct | `Idle>Hit>Idle` on **4 of 4** trials from idle. It does *not* fire while an attack is running, which is deliberate (`MonsterController.OnTakeDamage` skips the flinch when `isAttacking`, so a swing is never cut mid-clip) |
| Defend | correct and **standing** (hips 0.741-0.757) | the guard-pose clip fix from the previous pass is holding |
| Death | correct | lies down (hips-to-foot extension -0.01) |

**Natural 90 s fight** (AI choosing, body hit every 1.6 s): **24 of 24 attacks reached their own animator state**, animation starting 0.100-0.117 s after the trigger (exactly the authored 0.10-0.15 s blend), **0 missing**, and all six distinct attacks appeared.

### PART 2 — "colouring not coming": ROOT CAUSE = normal maps imported as colour textures

**One root cause, two victims.** A normal-map PNG was imported as `textureType=Default` with `sRGB=true` (an ordinary colour texture) and then wired into URP/Lit's `_BumpMap` with `_NORMALMAP` enabled. URP runs `UnpackNormal` on it, gets invalid normals out of sRGB-decoded colour data, and the surface lights flat and colourless.

| Asset | Texture | Was | Now |
|---|---|---|---|
| Monster body (`warrok_MAT`) | `Assets/training/bear_normal.png` | Default, sRGB on | **NormalMap, sRGB off** |
| **Both player hammers** (`Material.001`) | `hammer_FINISH_Material_001_Normal.png` | Default, sRGB on | **NormalMap, sRGB off** |

**Measured, not assumed.** Monster torso pixel with the broken normal map: **RGBA(0.239, 0.239, 0.243)** — R=G=B, literally colourless grey. The same pixel with the normal map disabled as a control: **RGBA(0.114, 0.102, 0.090)**, a warm brown-green. After the correct re-import the body renders as the intended green mottled ogre skin with real muscle relief, maroon horned pauldron and black leather straps. The hammers went from flat white-grey blocks to carved stone heads with bronze banding.

Every material used by the Level 1 scene and the monster prefab was scanned: **only those two use a `_BumpMap` at all**, and both are now correct.

**Second, separate colour bug, also fixed:** 7 `pPlane*` effect cards on the monster's sword used the FBX-embedded `Special_effects` material — URP/Lit **transparent, `_BaseMap` NULL, `_BaseColor` alpha 1** — so they rendered as **solid white ribbons** across the blade. The intended texture (`shattered-crystal-sword/textures/special effects png.png`, a white-on-black additive glow sheet with no alpha channel) existed on disk but was never wired up. Created **`Assets/Materials/M_L1_SwordGlow.mat`** (URP/**Unlit**, Transparent + Additive, that texture) and assigned it to those 7 renderers in the prefab. The white planes are gone; the blade now carries faint energy wisps.

**Checked and NOT a problem, despite appearances:** the `bear_*` textures in `Assets/training/` really are the **correct Warrok atlas** — the PNG was rendered and inspected: a green ogre UV layout with a tusked face, torso, limbs and armour pieces. Only the file names and the `training/` location are misleading. Neither monster FBX embeds any texture, so these are the only ones there are. **Not renamed** (renaming risks the GUID references for no visual gain), but worth moving to `Models/Monsters/Textures/` and renaming `warrok_*` when there is time.

**Magenta check:** the documented `_NORMALMAP` + skinned-mesh magenta bug on this Mac's Metal editor (which is why Level 2's vampire has its normal map off) **did not reproduce** here with a correctly imported normal map: 6-8 magenta-like pixels out of 29,876 sampled (0.02-0.03%), i.e. none. Still worth a look on Quest.

### PART 3 — Glowing hammers: SKIPPED (deliberately, not half-done)

The hammer mesh `HIGH` is **25,958 vertices in a single submesh with Read/Write disabled**, and one material covers head, haft and pommel. A glow on **the head only** therefore needs either (a) enabling Read/Write and rasterising the head's UV islands into a generated emission mask, or (b) splitting the mesh into head/haft submeshes (the approach used for Level 2's axe and Level 3). Both are real asset changes with memory and build-size implications, on the weapon the player holds in front of their face, the night before a build. The cheap alternatives were rejected: a whole-material emission would violate "the head, not the whole object", and a point light per hammer would add two realtime lights on Quest (the project already disabled point-light shadows for cost).

**No colour was chosen**, since nothing was built. When this is picked up: Level 1 has no established glow identity yet (Level 2's was never built either; Level 3 owns blue/orange-red), and the dungeon's point lights are warm orange (0.80, 0.49, 0.16), so a **cool pale tint reads better against that lighting than a warm one**, and keeping it dimmer than Level 3's flame preserves the L1 < L2 < L3 escalation.

### Is Level 1 visually correct now? **YES, in the editor.**
Monster animates through all 10 states with correct poses, body and hammers are properly coloured and lit, no white planes, 0 console errors. Not yet seen on a headset.

### Headset checks
1. **The monster must stay animated when you look away and back.** That was the actual bug; `AlwaysAnimate` fixes the freeze, but watch the Leaping Slam specifically — the renderer's bounds (2.37 m) are shorter than the jump (3.56 m), so if the *mesh* visibly pops out of existence mid-jump, set `SkinnedMeshRenderer.updateWhenOffscreen = true` on the prefab (costs a per-frame bounds recompute).
2. **Monster colour:** green mottled ogre skin with visible muscle relief, maroon horned pauldron. If any part shows magenta on Quest/Vulkan, turn `_NORMALMAP` off on `warrok_MAT` exactly as Level 2's vampire does.
3. **Hammers:** carved stone heads with bronze bands, not flat white.
4. **Monster sword:** faint wispy glow along the blade, no solid white ribbons.
---

## 2026-10-03 (pre-build pass) — L2 "sitting" bug FIXED, L1 lag re-diagnosed, in-VR naming verified

**Method.** Unity MCP's own client connection failed this session (ECONNREFUSED); the server on 127.0.0.1:8080 was driven over HTTP instead (initialize -> mcp-session-id -> tools/call), which worked for everything below. Every claim was measured in Play mode or read off live objects; no prior "fixed/verified" note was taken on trust. Test fights ran with `SessionReporter.baseUrl` redirected at runtime to `http://127.0.0.1:9` so nothing reached the production backend. **0 console errors at the end** (warnings: the pre-existing Meta ISDK telemetry notice, and the deliberate "not delivered ... dropped" lines from that dead test port).

### PART 2C — Level 2 monster "stuck sitting": ROOT CAUSE FOUND AND FIXED

**The two reported bugs are not one bug, and only one of them is real.**

**(a) "Stuck sitting" — REAL, fixed.** Measured on the live monster, Defend state held directly:

| | Idle | Defend (before) | Defend (after fix) |
|---|---|---|---|
| hips Y | 1.013 m | **0.482 m** | **0.875-0.926 m** |
| head Y | 1.540 m | **1.051 m** | - |
| hips - lowest foot | 0.920 m | **0.287 m** | - |

The legs fold to 31 % of standing extension and the head drops half a metre: a deep squat, held ~1.03 s every ~5.6 s, i.e. **17.6 % of the whole fight** (1603 of 7603 sampled frames). Root Y never moves (0.000), and the feet go *up* (0.093 -> 0.203), so this is **the clip's own pose, not a sunk root** — no import/Bake-Into-Pose setting can fix it.

**Cause:** the `Defend` state was playing `Assets/Animations/Monster/Great Sword Impact.fbx` — a knocked-back impact recoil, not a guard. **Level 1 had the identical bug** (same clip, state speed 0.444, so it sits for 1.5 s). **Level 3 was already correct**: it uses a purpose-baked `L3_Defend.anim` that stands.

**Fix (brings L1/L2 in line with L3):** copied `Level3/L3_Defend.anim` to `Assets/Animations/Monster/Monster_GuardPose.anim` (the existing convention for clips shared across levels, alongside `Monster_SwordIdle` / `Monster_HitReaction`), and pointed both Defend states at it. State speed set to keep each level's existing guard window exactly: **L2 speed 0.67 -> 1.0 (1.00 s window, `defendDuration` 1.0)**, **L1 speed 0.444 -> 0.667 (1.50 s window, `defendDuration` 1.5)**. Nothing else in either controller changed. Candidate clips were measured on the vampire before choosing: L3_Defend gave hips 0.926 / head 1.474 / leg extension 0.803 with the hands raised into a guard (idle reference: 1.004 / 1.546 / 0.911).

**Verified after the fix** (fresh 100 s fight, AI choosing, body hit every 1.3 s): 17 guards, every one standing (hips min 0.875, mean 0.930), window still 1.01-1.11 s.

**(b) "Weapon swing animation not playing" — NOT REPRODUCED.** In 150 s of fighting with a hit every 1.3 s, **46 of 46 attacks reached their own animator state**, delay 0.103-0.159 s, which is exactly the authored 0.10-0.15 s blend. 0 attacks missing. Forcing all 10 table attacks one by one, the axe head travelled **11.9-27.6 m of path at 22.6-39.5 m/s peak** in every one, so the axe visibly swings; `MonsterAxe/Visual` (MeshRenderer, `M_L2_AxeEdge`, URP Lit) is enabled and bone-parented to `RightHand`. The 2026-09-30 flinch fix has **not** regressed. What probably produced the report: 18 of 46 attacks begin *out of* the Defend state, so before this fix a swing often started by blending out of that collapsed squat.

**Dead end worth recording:** a first hit-test showed 0 attacks in 60 s and looked like a stunlock. It was invalid — the player had already died at t~155 s, so `IsBattleActive` was false and the AI had legitimately stopped. Re-run with the player held alive. Don't trust a Level-2 fight trace without checking `BattleManager.IsBattleActive` in the same frame.

### PART 1 — Level 1 lag: no reproducible regression; the cost is rendering, not scripts

**Nothing has landed in Level 1 since the last lag fix.** `Level1_Easy.unity` and `Monster_Easy_HipHop.prefab` are untouched since `95b198a`; the latest commit (`d453096`) does not touch Level 1 at all. The shared scripts that *did* change during the Level 3 work were measured and are free.

**Per-script Update cost (Unity Profiler counters, Level 1, battle running):**

| Script | Idle (hands still) | Both hammers swinging |
|---|---|---|
| `PlayerCombat.Update` | 0.0081 ms | 0.0402 ms |
| **`PlayerExertionAudio.Update` (breathing)** | **0.0038 ms** | **0.0083 ms** |
| `HitDetection.Update` | 0.0017 ms | 0.0015 ms |
| `SessionReporter.Update` | 0.0012 ms | 0.0006 ms |
| `MonsterAI.Update` + `LateUpdate` | 0.0016 ms | 0.0016 ms |
| every game script, summed | ~0.025 ms | **~0.063 ms** |

- **Breathing is not the cause.** Its "rolling calculation" is a `Queue<float>` of swing timestamps with a bounded dequeue of expired entries — amortised O(1), no per-frame allocation (queue reused, arrays `readonly`). 0.0083 ms at full swing rate.
- **SessionReporter does not stall the main thread.** It uses `UnityWebRequest.SendWebRequest()` with an `op.completed += ...` callback — no `yield return op`, no `.Wait()`, no `Thread.Sleep`. Proven live: 14 "not delivered / dropped" warnings against a dead port while the fight ran at full speed.
- **No duplicates:** 1 enabled camera (of 3), 1 AudioListener, 1 BattleManager, 1 LevelManager, 1 EventSystem, 1 SessionReporter, 1 PlayerCombat, 0 monsters before Start / 1 after.
- **No log spam:** `MonsterAI.verboseAttackLogs`, `MonsterAnimator.verboseAttackLogs`, `CombatUIController.verboseDamageLogs`, `SessionReporter.verboseLogging` all **false** on the live objects. 0 console errors; nothing new logging per frame.

**Rendering load (Level 1, mid-combat, editor):** 208,945 triangles, 177,946 vertices, **115 standard draw calls + 55 static-batched (2 static batches)**, 16 SetPass, 61-68 shadow casters. The two hammers are 37,672 triangles each (**75,344 for the pair, ~64 % of all scene geometry**) and are drawn again in the shadow pass. That is the one measurable, pre-existing cost worth attacking if the headset is slow — it is a *rendering* cost the editor cannot score for Quest.

**Two measurement traps corrected (do not repeat):**
- Summing `MeshFilter.sharedMesh.triangles` gave 1.39 M triangles. Wrong: 49 renderers share one `Combined Mesh (root: scene)` static-batch mesh and each draws only a slice. Real scene geometry is ~118 k; the renderer reports 208 k drawn per frame including the shadow pass.
- `GC Allocated In Frame` read 35.3 KB idle / 35.9 KB in combat, against 0.7-1.3 KB in the 2026-09-30 notes. Not a regression: **with the game stopped, edit mode allocates 43.2 KB/frame** — it is Editor + Profiler + MCP-polling overhead. It also does not scale with combat.

**No fix was applied for Part 1,** because no cause reproduced. Only the headset can settle it: OVR Metrics Tool / `adb logcat -s VrApi` (CPU% near 100 with stale frames on hit = scripts/physics; GPU% pinned = rendering, which is what the numbers above predict).

### PART 3 — In-VR player management: ALREADY BUILT, now verified in Play mode

`PlayerNamePicker` (728 lines) already does everything asked, and `BattleManager` adds it to `BattleSystems` at runtime, so **all three levels get it with no per-scene setup** — no separate work per level. Verified live on Level2_Medium:

- Panel built on `CombatCanvas`, driven by the existing laser (`ControllerLaserPointer`, 1 active). **8 names listed from the real backend** (VfxTest, Guest, Player 1, thor, Dikshanta, Gaurav) plus presets, 6 per page with a `>` pager.
- **Start is gated:** `BlocksStart=True` and `StartBattleUIButton active=False` until a name is chosen. After the 10 s auto-select picked the default, Start appeared and a `Change name` button remained.
- **Full text entry, not auto-increment:** `New player` opens a **41-key laser keyboard** (Shift / Del / Space / Cancel / Done). Typed "ZZTEST" through the real button `onClick` path; `Done` committed it.
- **The active name is used for the session:** `playerName='ZZTEST'` -> on Start, `sessionPlayerName='ZZTEST'` with `currentSessionId=6296e4f5...`. `SetPlayerName` refuses mid-battle, so a fight cannot be split across two names.
- **Rename is wired to the backend:** `SessionReporter.RenamePlayer` sends `PATCH /api/players/{oldName}` with `{"newName":...}` and handles Renamed / NotOnServer / Conflict / Unreachable / InBattle; it refuses during a battle.
- **Network failure never blocks:** with the backend at a dead port the panel still opened from remembered names, Start worked and the fight ran; only "no answer / dropped" warnings appeared.
- **NOT verified:** a real rename persisting on the backend. That mutates live dashboard data for an existing player, so it was left for you to confirm (see headset checks).

### PARTS 4 and 5 — both verified on live scene objects

**Part 4 — backend URL: no drift in any scene.** All three have exactly one `SessionReporter` on `BattleSystems` with `baseUrl=https://s-t-r-i-k-e-f-i-t.onrender.com`, `wakeBackendOnLoad=True`, `wakeTimeoutSeconds=60`. No code anywhere writes `baseUrl` at runtime, so the serialized value is what ships. Re-checked in the saved scene files after all Play-mode redirects: unchanged.

**Part 5 — levels are separate, not merged.**

| | Level1_Easy | Level2_Medium | Level3_Hard |
|---|---|---|---|
| difficulty | Easy | Medium | Hard |
| environment root | `Environment_LiteDungeon` | `Environment_Level2_Dungeon` | `Environment_OldSeaport` |
| monster prefab | `Monster_Easy_HipHop` | `Monster_Medium_VampireLust` | `Monster_Hard_TwinAxeBrute` |
| animator controller | `Monster_L1_Sword` | `Monster_L2_Axe` | `Monster_L3_TwinAxe` |
| player loadout | Hammer / Hammer | Sword / Sword | None (grabbed in arena) |
| monsters pre-placed | 0 | 0 | 0 |

No scene contains another level's environment, monster or controller. `autoDetectDifficultyFromSceneName` is true in all three. Sharing `Monster_GuardPose.anim` between L1 and L2 is clip reuse (as `Monster_SwordIdle` and `Monster_HitReaction` already are), not scene merging.

**Minor, not changed:** `LevelManager.monsterWeapon` is `Hammer` in Level 2 (the vampire's two-handed axe is wired through the Hammer `WeaponType` slot, `hammerObject=MonsterAxe`) and `Sword` in Level 1. `WeaponType` only applies a damage *flavour* multiplier — stats come from `DifficultyParams` — so this is a naming inconsistency, not a live bug. Worth renaming when the enum is next touched.

### BUILD BLOCKER for tomorrow

**`Level1_Easy` is disabled in Build Settings** (`EditorBuildSettings.asset`: `Level2_Medium` enabled, `Level3_Hard` enabled, `MainMenu` / `Level1_Easy` / `Results` **disabled**). If Level 1 is meant to be in tomorrow's build it must be ticked and ordered. This was already noted on 2026-09-30 and is still true.

### Not done this pass
- **Part 2A** (Level 1 attack variety from unused clips) — not started.
- **Part 2B** (Level 1 subtle single-area glow) — not started.
- **Part 2D** (shared Fresnel/rim-light full-body aura for all three levels) — not started.
- Part 2C's fix has **not been seen on a headset**, only measured in Play mode in the editor.

### Headset checks
1. **Level 2, the fix that matters:** let the vampire guard (about every 5.6 s, blue tint + guard sound). It must stay **standing** with the axe raised — it previously dropped into a squat for a full second. Same check on Level 1 (guard window is 1.5 s there).
2. Keep hitting the vampire while it attacks: every attack should still show its swing (46/46 did in the editor).
3. **Rename round trip:** rename a player in the pre-battle panel, then check the dashboard that the history moved. This is the one Part 3 claim not verified here.
4. Level 1 frame time with OVR Metrics overlay on, swinging steadily vs standing still. If GPU% is pinned rather than CPU%, the hammers (75,344 triangles for the pair) are the first thing to cut.
---

## 2026-10-02 (fourth pass) — Level 3: flaming axes + coiling blue serpent aura

**Found (live, MCP):** the earlier session had already added `MonsterAxeFlames` (flame + spark emitter on each `BladeEdge`, a child of the axe, so a child of the RightHand/LeftHand bone) and `MonsterSerpentAura` (3 orbs with `TrailRenderer`s). This pass tuned and verified them. Body emission is unchanged (Simple Lit, `_EMISSION`, masks `Ch25_100x_Emission`). **One source of truth for enrage:** `MonsterAI.enraged`, written only by `BossPhaseController.EnterEnrage` (`IsEnraged` just reads it). `MonsterGlow`, `ArenaMoodController`, `MonsterAxeFlames`, `MonsterSerpentAura` all read it; no second phase check exists.

**Built / changed**
- `MonsterAxeFlames`: levels re-tuned to 15-30 particles per axe (idle ~10, Standard ~15, Heavy/Unblockable ~24, enraged ~30). Enrage shifts the flame colour over 1 s (core blue-white 0.82/0.90/1.0, then white-yellow, then orange; calm is yellow-orange-red) and raises rate x1.25 and size x1.15. Particle life shortens with blade speed (down to x0.5 at 8 m/s) so fast swings leave a short flicker. Level is chosen per attack from `CurrentAttack` (Heavy/Special/IdleSlam/unblockable = heavy; thrown axe in flight = heavy). Sparks burst on `MonsterController.OnMonsterAttackDamage`, the same hit event as the hit sounds.
- `MonsterSerpentAura`: 3 orbs, one shared helix that follows the live skeleton (feet, hips, chest, neck, head), radius from skeleton width plus margin. Margins widened (hip 0.22, chest 0.18, neck 0.20, head 0.22, top 0.22) after a clipping measurement. Appears only when `MonsterEntrance.Complete`. Enraged: climb 3.2 s to 1.9 s, 2.5 to 3 turns, radius x0.88, width 0.05 to 0.065, brightness 1.0 to 1.7. Blue/white-hot trail texture, additive.
- Builder: velocity-over-lifetime curves made all-constant (mixed modes spammed "Particle Velocity curves must all be in the same mode" every frame).

**Verified in Play mode (real Start path, forced attacks, 1/72 s)**
| Check | Result |
|---|---|
| Parenting | `Monster/RiseOffset/Hips/.../RightHand/MonsterAxe_R/BladeEdge/AxeFlame` (and `_L` / LeftHand); sparks under the same BladeEdge; emitter offset 0.088 m from the bone-parented blade, particles world-space (short life) |
| Entrance | flames off, aura orbs disabled until entrance complete (6.93 s), then both fade/ignite together; 0 overlap with summoning circle |
| Attack scaling | Alternating Chops, Crossing Swing: `standard`, rate 48; Cleave, Whirlwind, Leap: `heavy`, rate 80; Earthsplitter Slam: `heavy (unblockable)`, rate 80; back to idle after |
| Particles alive | Standard ~11-12 mean / 17 max per axe; Heavy ~19 mean / 26 max; enraged Heavy 30 per axe (60 both) |
| Sparks | one burst per landed hit: bursts == damage events in every attack |
| Enrage | HP to 400/1500: `ai.enraged` and `BossPhaseController.IsEnraged` true together, aura climb 3.2 to 1.9 s, flame gradient to blue-white core, glow to orange: all keyed off the same flag |
| Aura clipping vs tight hit volumes | before margin fix 21 % of orb samples inside a body capsule (61 % at the head); after: 10 % at idle, 6 % Heavy, 12 % slam, 8 % Brute Rush; remainder is the swinging arms crossing the coil |
| Console | 0 errors; only warnings are the Meta ISDK telemetry notice and SessionReporter drops (test backend redirect) |

**Performance (editor, Play mode; not Quest numbers):** VFX on vs off = about +5 draw calls (117.4 to 122.7 idle enraged; 123.8 in an enraged Heavy swing). Added objects: 2 flame + 2 spark particle systems, 3 trail renderers. No GC from these scripts beyond a small Gradient rebuild when the enrage blend steps (20 steps). **Concern:** none measured; the scene's ~120 draw calls and 260 k triangles come from the arena and boss, not this VFX. If Quest is tight, drop the aura to 2 orbs or `trailTime` 0.5 to 0.35 first. Only the headset can confirm frame time.

**Not done:** no eye-view render of the new VFX this pass (checked by numbers and positions). Review the look on the headset: flame size at arm's length, the aura brightness against the dock lighting.

**Ready for headset test? YES** (performance acceptable in the Play-mode test as far as the editor can tell; frame time is unmeasured on device).

---

## 2026-10-02 (third pass) — Level 3 end-to-end verification in Play mode (+ reactive defend)

**Method.** Every claim below was watched happening in Play mode on `Level3_Hard` through Unity MCP. Nothing was taken from earlier notes or from reading the code.
- **Setup:** 1/72 s steps; backend at 127.0.0.1:9; the real Start path (name picker → weapon gate → Start).
- **Player:** the "player" was the real weapon object (`PlayerCombat.weaponTransform`), moved physically each frame at 4.3–6.5 m/s. Every hit, block, clash and parry therefore went through the real `HitDetection` trigger path.
- **Monster:** in the fights, the AI chose every attack itself; nothing was forced.

### Final status

| Step | Status |
|---|---|
| 1. Weapons at scene start + grab → both hands | **VERIFIED WORKING IN PLAY MODE** (2 fixes) |
| 2. Entrance (circle → rise → roar → combat) | **VERIFIED WORKING IN PLAY MODE** (1 fix) |
| 3. Attack variety + HP escalation + unblockable | **VERIFIED WORKING IN PLAY MODE** |
| 4. Defend reactivity | **VERIFIED WORKING IN PLAY MODE** (new feature) |
| 5. Victory / defeat round trip, console | **VERIFIED WORKING IN PLAY MODE** (see console notes) |

### Step 1 — Scene start and weapon grab

**What happened (Play mode):**
- **Start of the scene:** 0 monsters, no rune circle visible, battle inactive. Start was **refused while unarmed**: `StartBattle` returned with the battle inactive and 0 monsters.
- **Weapon placement**, rendered from the head at (0, 1.6, 0):
  - two maces leaning head-up on the cargo crate (left);
  - two flails draped over the barrel with the balls on the planks (right);
  - two katanas driven into the planks (behind);
  - each pair with its label and a gold rune ring.
  - Their bounds touch only their intended crate, barrel and planks; nothing floats.
- **Reach:** I moved the rig so that the real resting hand pose landed on a grab point ("walking up"). The right hand on a Flail grab point gave `HandInReach = true` and the highlight ring lit.
- **Equip chain (what the grip calls):** `WeaponPickup.Equip` → `PlayerCombat.EquipWeaponSet`. Reaching **one** pair equipped **both hands**:
  - **Flail:** `PlayerFlail_Right` under RightHandAnchor (HitDetection hand = Right) and `PlayerFlail_Left` under LeftHandAnchor (hand = Left) both active; `weaponTransform` / `offhandWeaponTransform` set; swing gate 1.2 → 1.6 m/s; LevelManager weapons = Flail/Flail.
  - **Props:** the Flail's staged props were hidden.
  - **Start:** the Start button appeared once armed.
- **Swap:** the left hand on a Katana grab point → Katana in both hands (gate 1.3). The Flail pair went back to its spot with its props visible again.
- **Locked in battle:** after Start, `Equip(Katana)` returns **false** and the Mace stays.

**Issues found:**
1. **No idle motion at all.** All six staged weapons moved 0.0000 m, and the rings were static. No bob or glow script existed.
2. **Swap prompt went stale.** After a Flail → Katana swap, the prompt still said "WAR FLAIL READY".
3. **Two stray equips with no input.** These were observed on the first two Play entries. The only code paths to equip are grip-with-a-hand-in-reach or the editor keys 1/2/3. With a stack-trace log armed, the stray equip did not recur in 6 later sessions. Most likely this was keyboard input reaching the Game view; the editor keys exist only in the editor.

**Fixes:**
- **Idle glow:** `WeaponPickup` gained an idle glow. The pair's ground ring pulses (phase-offset per pair) and turns slowly, and it switches off when the pair is held or the battle starts. Bobbing was deliberately **not** used, because the weapons rest on crates and in planks and would float. Verified: pulse 0.48–1.00 with the yaw advancing; the held pair's ring is off.
  - Wiring: `Level3SceneBuilder.StagePair` and `Level3EngagementBuilder` (the open scene was re-applied).
- **Prompt:** `WeaponSelectionStage` refreshes the prompt on a swap too. Verified: "WAR FLAIL READY" → "KATANA READY".

**Headset:**
- Walk to a pair: its ring brightens as your hand gets in reach. Squeeze grip: **both** hands get that weapon.
- Swap to another pair: the first goes back on its crate or barrel.
- After Start, the pairs can't be picked up.
- Readability check: the dark flails on the dark barrel.

### Step 2 — Entrance (observed timing, seconds from Start)

| t (s) | Observed |
|---|---|
| 0.00 | Monster spawned 2.90 m under the floor. AI **off**, **0/18** colliders on. The circle starts fading in |
| 1.47 | Circle at full alpha; `Rise` trigger |
| 1.47 → 4.50 | Rise. Height followed the clip: clip 0.18 → 20 % of the depth, 0.50 → 76 %, 0.82 → 98 %. It reached the floor at **clip 0.995**, so it never outpaced the clip (eased: fast lunge, heavy settle) |
| 4.53 | **Roar animation transition and roar audio onset on the same frame** (audio measured from the source's output) |
| 4.53 → 5.73 | Circle fades out (now starts when fully risen) |
| 6.93 | **Entrance complete**: AI on, colliders 18/18 on, phase logic starts. The first attack follows immediately |

During the entrance:
- **0 attack-damage events.**
- **A live mace sweep through the rising body** at 4.2 s registered **no contact** (monster HP −0).
- **No combat can start early:** AI and colliders are off until 6.93 s. The Start button is what *begins* this sequence; it can't be pressed again.

**Issue / fix:** the circle stayed lit through the whole roar and only faded at 6.93 s. `MonsterEntrance` now starts the fade the moment the monster is fully risen. Verified: alpha 0 with the renderer off at 6.93 s.

**Headset:** after Start, the circle glows, the brute rises through it, the circle fades as he roars, and only then does he attack. Your swings pass through him during the rise.

### Step 3 — Sustained fight

A full fight to the kill, 83 s: the AI choosing every attack, and the player swinging every 1.6 s at 4.3 m/s.

| Tier | Attacks | Standard | Combo | Heavy | Unblockable slam | Ranged |
|---|---|---|---|---|---|---|
| Phase 1 (> 66 %) | 13 | 10 (77 %) | 1 | 2 (15 %) | 0 | 0 |
| Phase 2 (33–66 %) | 12 | 6 (50 %) | 1 | 4 (33 %) | 1 (8 %) | 0 |
| Enraged (≤ 33 %) | 9 | 4 (44 %) | 1 | 2 (22 %) | 2 (22 %) | 0 |

- **Escalation is real in the logged data:** heavy and unblockable attacks grow as HP drops.
- **Variety:** 10 different attacks were used, with **no back-to-back repeat**.
- **Ranged attacks** show up when the player is out of reach; a second fight opened with a Brute Rush from 3.19 m.
- **Animator:** **34/34 attacks reached their own distinct state**, with **0 transitions through Any State** (`GetAnimatorTransitionInfo(0).anyState` checked every frame).

**Unblockable slam (physical test):**
- Swinging the mace into the boss's axe during the slam wind-up produced a real `Clash`, but **no parry**. The **full 45** landed, flagged `LastHitWasUnblockable`.
- By contrast, the same clash during a Crossing Double Swing **parried** it: 22.5 → **7.9** (×0.35).
- The red danger ring and the warning sound play at the start of the slam wind-up; the slam's tint is purple.
- Level 3 has no shield, and `ModifyIncomingDamage` only blocks with a shield. So the parry is the only damage reduction the slam needs to ignore, and it does.

**Headset:**
- Parry a normal swing by hitting his axe during the wind-up: a clang and small damage.
- Try the same on the purple slam with the red ring: it still hits for full damage. Only stepping out of the ring works.

### Step 4 — Defend: now reactive

**What happened before:** the guard was purely timed. A baseline fight showed a guard every **~6.3 s like clockwork**, which is learnable and ignorable.

**Built (`MonsterAI`):**
- **Reactive guard:** each frame, a player weapon moving **≥ 2.5 m/s** (or its hit gate), **within 1.25 m of the chest** (Spine2) and **moving toward it** gets **one roll per swing**. On success the brute guards right before the blow lands, for 0.8 s.
- **When it can fire:** only while the brute is free, never during its own attacks. Its swings stay the guaranteed punish window. At most one reactive guard every 3 s.
- **Timed guard:** it remains as a fallback but is slower and no longer predictable: every **8 s ±30 %**, and a reactive guard resets it, so the two never stack.

| Phase | Reactive chance |
|---|---|
| Phase 1 | **40 %** |
| Phase 2 | **50 %** (it "reads" you better as the fight goes on) |
| Enraged | **30 %** |

**Direction chosen: lower when enraged.** The enraged brute trades defence for aggression. Its tempo (×1.3) and its slams (23 % of picks) are the escalation already. Defending more on top would stack two difficulty axes and make the finale feel stalled.

**Fairness, measured:**
- **Normal rhythm** (swing every 1.6 s, 83 s): 10 guards (3 reactive, 7 timed). **6 of 45 contacts body-blocked (13 %)**, below the old timed-only baseline (15 %).
- **Worst case** (the player pokes *only* while the boss is idle, every 0.6 s): 5 reactive guards from 9 rolls. **48 % clean hits, 27 % body-blocked (still 30 % damage), 24 % clanged off the guarding axes.**
  - The boss is clearly responsive without becoming a wall. The fight rewards hitting during and right after its swings.
  - **Flag:** if idle pokes feel stonewalled on the headset, lower `reactiveDefendChancePhase2` to 0.40 first.

**3-way hit distinction with the reactive layer on top** (physical sweeps):

| Case | Outcome |
|---|---|
| Reactive guard forced on, swing at the chest | Guard up **0.11 s before contact**, then `Blocked`, 14.9 damage instead of 50.5 (×0.3), block clang |
| Reactive off, same swing | `Hit` 50.5 |
| Swing into the axe during a normal wind-up | `Clash`, parried (player takes 7.9 instead of 22.5) |
| Swing into the axe during the slam wind-up | `Clash`, not parried, full 45 |

During attacks, the reactive guard did not fire (0 rolls), as designed.

**Headset:**
- Swing at him while he stands idle: about every other time he snaps the crossed axes up just before you connect (clang).
- Swing while he is attacking or recovering: it always lands.
- Enraged: he guards less and attacks more.

### Step 5 — Round trip and console

- **Victory:** in three full fights the AI was killed by real hits. Each time: slow-motion finale, Victory panel, timeScale back to 1, `L3_Victory_Sting`.
- **Defeat:** the player was left at 60 HP and killed by a real boss attack 8.3 s later. The **Defeat panel showed 0.6 s after death** with `L3_Defeat_Sting`; the low-HP vignette and heartbeat switched off.
- **Exit:** every Play exit was clean, with 0 errors on stop.

**Console:**
- **Game code: 0 errors across every session** I didn't break myself.
- **The one remaining warning** is from the Meta ISDK package (telemetry library absent in the editor).
- **Two editor-level items to know about** (neither is Level 3 code):
  - **MRUK errors:** "MRUK Shared: Global context has already been created!" / "Failed to create global context" appeared after I edited a script *during* Play mode. That mid-Play recompile dropped MRUK's exit hook, so its native context was never released.
    - Fixed by calling MRUK's own `ReleaseSharedLibrary` from edit mode. Every later Play entry was clean.
    - Rule: don't edit scripts while playing (or restart Unity if it happens).
  - **Import-worker crash:** "The AssetImportWorkerHW0 has crashed" (once in this editor session). Unity's background import process loaded the **WebRTC** native plugin (`unity::webrtc::DummyAudioDevice`, used by the broadcasting system), and that thread crashed while the worker shut down.
    - This is an editor/package issue, not game code, and doesn't run in builds.
    - If it keeps appearing, update `com.unity.webrtc` or stop its native plugin from loading in the editor.

### Code / asset changes (this pass)
| File | Change |
|---|---|
| `Monster/MonsterAI.cs` | Reactive guard (`CheckReactiveDefend`, per-phase chances, range / speed / cooldown, `DefendIsReactive`, counters), `defendCooldownJitter`. Levels 1/2: chance 0 and jitter 0, so behaviour is unchanged. |
| `Editor/Level3DualAxeBuilder.Monster.cs` | L3 values: reactive 0.40 / 0.50 / 0.30, range 1.25 m, speed 2.5 m/s, cooldown 3 s, duration 0.8 s, target Spine2; timed guard 8 s ±30 %. Prefab rebuilt; hit frames unchanged. |
| `Monster/MonsterEntrance.cs` | Circle fades out once fully risen (at the roar). |
| `Combat/WeaponPickup.cs` | Idle ring glow (pulse + turn), off when held / in battle. |
| `Combat/WeaponSelectionStage.cs` | Prompt refreshes on swaps. |
| `Editor/Level3SceneBuilder.cs`, `Editor/Level3EngagementBuilder.cs` | Wire `markerRing` (scene re-applied and saved). |
| `Materials/Level3/M_L3_ReachHighlight.mat` | Re-serialized by URP's own material validation (transparent: shadow/depth passes off). No visual change. |

### Ready for headset test? **YES**
Every step above was watched working in Play mode, and the scene is saved and clean. What only the headset can judge:
- whether the reactive guard feels fair at your real swing speeds;
- the flail's readability on the barrel;
- the ring glow at Quest brightness.

---

## 2026-10-02 (second pass) — Level 3: boss colour, glow, ranged attacks, arena escalation

Method: everything below was read from the live project through Unity MCP or traced in Play mode on `Level3_Hard` (real Start path: name picked, Mace pair equipped, Start; 1/72 s steps; backend at 127.0.0.1:9). Visual claims come from renders taken from the player's eye (`CenterEyeAnchor`) in the real scene lighting. **0 console errors** at the end (one pre-existing Meta ISDK telemetry warning).

### Part 1 — Monster colour: root cause and fix

**What was found (MCP):**
- **Textures:** the import was not missing anything. All 8 Mixamo "nonPBR" textures were extracted and assigned: Diffuse, Specular, Glossiness and Normal for each of the two sets (1001/1002).
  - `M_L3_Ch25_1001/1002` were URP **Lit**, specular workflow: Base Map = Diffuse, `_SpecGlossMap` = `Ch25_100x_SpecGloss.png` (Specular RGB + Gloss A, packed by the builder), Normal on.
  - No fallback grey or magenta material was involved.
- **The specular map** is a flat grey 0.22 (sRGB 56/255 = **0.04 linear**, the textbook skin/dielectric value). The packed map was imported **linear** (`sRGB` off), so URP Lit used **F0 = 0.22**, 5.5× too reflective. Specular-workflow energy conservation then also dimmed the albedo by 22 %.
- **The diffuse** is a very dark maroon/purple (skin around 0.25 sRGB ≈ 0.05 linear). URP Lit adds PBR environment reflections from the scene's teal "SkyEerie" skybox (reflection mode = Skybox). On an albedo this dark, the reflection outshines the diffuse.
- **Render evidence** (same pose and camera, real arena lighting):
  - Lit as built: a flat **grey-blue stone statue**, with no maroon at all.
  - Lit with F0 corrected to 0.04: still dark blue-grey.
  - Lit with corrected F0 **and** sky reflections off: maroon appears.
  - **Simple Lit**: maroon-brown skin with visible skin detail.

**Root cause:** a PBR shader reading non-PBR data:
- a mis-scaled specular map (F0 0.22);
- plus PBR sky reflections, which a Blinn-Phong texture set has no real roughness/metal data to modulate.

The texture wasn't missing; the shading model was wrong for the data.

**Fix:** option (b), **URP Simple Lit** for `M_L3_Ch25_1001`, `M_L3_Ch25_1002` and `M_L3_Ch25_WeakPoint`.
- **Inputs:** Base Map = Diffuse, `_SpecGlossMap` = the packed spec/gloss (Blinn-Phong highlight level as authored, gloss from alpha), Normal map on, emission (Part 2).
- **Why Simple Lit over Lit + flat defaults:**
  - Mixamo nonPBR maps *are* a Blinn-Phong set (diffuse / specular level / glossiness / normal), which is exactly Simple Lit's model.
  - It has no sky-reflection term to wash out the dark albedo.
  - It is cheaper on Quest than Lit, which matters for a 44 k-tri skinned boss.
  - Lit with reflections off needed the same compromises and looked flatter.
- **Builder:** `Level3DualAxeBuilder.MakeSimpleLit`; `MakeLit` was removed.

**Verified:** material dump (shader `Universal Render Pipeline/Simple Lit`, keywords `_SPECGLOSSMAP _SPECULAR_COLOR _NORMALMAP _EMISSION`, maps as above) and eye-view renders. The skin reads dark maroon with highlights at rest.

Two related findings, both fixed:
- **Light wash:** the chest point light (the old enrage aura, now the glow light) at even 0.47 washed the whole body red, because it sat 0.4 m from the skin. It now sits 0.8 m out at 0.12 at rest (1.8 at the hottest), so the resting colour stays true.
- **Telegraph tint:** the existing telegraph tint (multiplying `_BaseColor` red during wind-ups) barely shows on skin this dark. Part 2's wind-up glow surge now carries that read.

### Part 2 — Glow (emission)
**Areas (2), chosen on the mesh in bind pose and rasterised into UV space** (`Level3DualAxeBuilder.BuildEmissionMasks` → `Textures/Ch25/Ch25_100x_Emission.png`, 2048 px):
1. **The face.** Both eyeballs glow fully. Their UV discs on set 1002 were confirmed on the mesh at (±0.032, 1.67, 0.139) m, facing forward. The eyelid skin on set 1001 glows at 45 %, and the **mouth interior** at 60 %, weighted by darkness, so the throat burns and the lighter teeth barely do.
2. **Skin cracks over the shoulders, collarbones and upper back.** These are the hide's own dark crack and pebble lines, found by a band-pass (7 px blur minus 13 px blur) on the diffuse. Edges are kept only inside UV islands, so seams don't light up. They fade out toward the arms and belly.
   - The arms stay dark, so the **green right-forearm weak point stays the only "hit here" colour**.
   - Red means threat, green means target.

The **axes were not given emission.** They are untextured flat-colour meshes with no runes or edges to mask, and they already ignite (fire VFX) on heavy attacks and in flight.

**Colours:** deep red `(0.85, 0.07, 0.03)` at rest, the slam danger ring's hue. Green stays reserved for the weak point, the summoning circle's colour. Escalation (`MonsterGlow`):

| Stage | Colour | Intensity |
|---|---|---|
| Full HP | deep red | ×1.0 |
| Phase 2 (≤ 66 %) | brighter red-orange (1, 0.16, 0.04) | ×1.2 |
| Enraged (≤ 33 %) | hot orange (1, 0.42, 0.08) | ×1.55 |
| Final (≤ 15 %, enraged) | near white-hot (1, 0.78, 0.45) | ×1.9 |

**Pulse:**
- **Breathing:** the glow is phase-locked to the idle clip while idling, so it rises with the in-breath. The period is 3.2 s, or 1.2 s enraged, matching `L3_Idle` / `L3_IdleEnraged`. The pulse depth is 35 % (45 % enraged).
- **Wind-up surge:** every wind-up ramps the glow to ×1.9 on the hit frame, tinted 45 % toward the attack's telegraph colour (purple on the unblockable slam).
- **Death:** the glow dies out over 2.5 s.

**Verified live:**
- **Glow values:** calm `(0.85,0.07,0.03)` ×0.66–1.00 breathing; wind-up ×1.56–1.90; phase 2 `(1,0.16,0.04)`; enraged `(1,0.42,0.08)`; final `(1,0.78,0.45)` ×1.9 (light 2.99); dead ×0.00.
- **Renders:** glowing eyes and maw, and smouldering shoulder cracks.

**Do you need to supply a mask?** No. The generated mask uses the texture's own crack detail and gives a targeted result, not a flat-colour glow. Painting your own is optional:
- **Format:** two PNGs, **2048×2048, grayscale**, in the **same UV layout as `Ch25_1001_Diffuse.png` / `Ch25_1002_Diffuse.png`**. White = glow, black = none, soft edges.
- **What to mask:** the eyeballs (set 1002), the mouth interior (set 1002), and whatever skin markings you want (cracks, veins, the spine knobs on set 1001).
- **Where:** save them as `Textures/Ch25/Ch25_1001_Emission_Painted.png` / `Ch25_1002_Emission_Painted.png`. The builder uses them instead of the generated masks.

### Part 3 — New attacks (11 → 14)
**What was found:**
- **Imported Mixamo clips not used by L3:**
  - `Great Sword Impact` (0.67 s, in place: a reaction, not an attack);
  - `Monster_SwordIdle` / `Great Sword Idle` (idles);
  - `Sword And Shield Attack (L1)` (a 3 m leap; a third leap would duplicate Leaping Double Cleave and the slam);
  - `Hip Hop Dancing`;
  - the duplicate `One Hand Sword Combo` / `Player_Future` copies.
- **No throw or run clip exists.** The new attacks were therefore built from mocap already in the project, using the same muscle-space composer as the existing 11 (`HumanoidClipBaker`):
  - The **overhead one-handed chop** (`Standing Melee Attack Downward`) has the arc of an overhand throw. It is released on the blade's speed peak.
  - **`Standing Melee Run Jump Attack`** contains real run steps: 0–0.8 s, about 2 m of root travel.

**Ranged attacks are a new category.** Rows with `rangedKind` are chosen **only while the player is out of melee reach** (`MonsterAI.ChooseRanged`, between `minRange` and `maxRange` of the headset). They are never chosen in melee, and melee rows are never chosen at range.

| # | Attack | Built from | Category | Wind-up (enraged) | Damage | Reach / rules | Weight full → 0 HP / enraged |
|---|---|---|---|---|---|---|---|
| 1–11 | *unchanged* (Alternating Chops … Earthsplitter Slam; see the first 2026-10-02 section) | | | | | hit frames re-measured: identical | |
| 12 | **Hurled Axe** | Standing Melee Attack Downward, right arm as an overhand throw; the left axe stays gripped | Standard, **RANGED 2.4–6.5 m** | release 0.792 (0.609) | 29.3 | Axe flies at 10 m/s, spinning 2.6 rev/s, at where the upper chest was at release (no lead) | 10 → 8 / 5 (5 s cd) |
| 13 | **Twin Hurl** | same, plus the mirrored left throw 0.40 s later | Combo, **RANGED 2.4–6.5 m**, HP ≤ 50 % | 0.792 + 0.400 (0.609 + 0.308) | 22.5 × 2 | Second axe aimed at where you are when it is released | 0 → 9 / 10 (6 s cd) |
| 14 | **Brute Rush** | Run Jump Attack's run steps (axes cocked: Crossing Double Swing's wound pose), then the crossing double sweep | Heavy, **GAP-CLOSER 2.6–5.5 m** | 1.125 (0.865) | 33.8 | Runs during 0.18–0.98 s (re-aimed every frame), stops at the 1.45 m stand-off, sweep arc 70° | 8 → 10 / 10 (4 s cd) |

**Thrown axe** (`MonsterAxeThrow`):
- **Flight:** the axe burns (its own fire VFX) and trails, with a 3D whirr (`L3_AxeThrow_Spin`), so you can see and hear it coming.
- **Hit test:** it hits if its head passes within 0.32 m of the line from the eyes down 0.9 m.
- **Counters:** dodge it by stepping or ducking, or **bat it away**. A weapon clash at swing speed deflects it.
- **Return:** hit, miss or deflected, it flies back and lands in the same fist on the clip's catch frame (`L3_AxeCatch`).
- **One-handed fighting was not built.** The boss pauses in a reach-out "recall" pose until both axes are back. It never fights one-handed: that needs one-handed clip variants of every melee attack, which don't exist.

**HP-tiered placement:** the ranged rows form their own pool, weighted with the same HP lerp and enrage overrides. 4,000 picks through the real `ChooseRanged`:

| HP / state | 4 m | 6 m (beyond rush) |
|---|---|---|
| 100 % | Hurled Axe 55.5 %, Brute Rush 44.5 % | Hurled Axe 100 % |
| 60 % | Axe 50.1 %, Rush 49.9 % | — |
| 40 % | Rush 39.8 %, Axe 37.2 %, **Twin Hurl 23.0 %** | — |
| Enraged | Rush 40.0 %, **Twin Hurl 40.0 %**, Axe 20.0 % | Twin Hurl 66.8 %, Axe 33.2 % |

At 2.5 m only throws qualify (the rush needs 2.6 m); at 1.45 m nothing ranged is ever picked.

The melee pool is unchanged, with 0 ranged rows in 16,000 picks:

| HP | Standard | Combo | Heavy | Special |
|---|---|---|---|---|
| 100 % | 75.3 % | 15.2 % | 8.5 % | 1.0 % |
| 60 % | 64.7 % | 17.1 % | 16.4 % | 1.9 % |
| 35 % | 57.3 % | 18.1 % | 22.0 % | 2.7 % |
| Enraged | 33.9 % | 17.5 % | 25.3 % | 23.4 % |

These match the previous table within sampling noise.

**Animator (`Monster_L3_TwinAxe.controller`): 22 states, 154 transitions; Any State → Death only.** Audited transition by transition:
- **Entry:** each new state has its own trigger parameter, entered only from Idle / IdleEnraged / Hit / Defend / Roar on that trigger (0.08 s blend).
- **Exit:** to Idle (`Idle` & !Enraged) or IdleEnraged (`Idle` & Enraged), plus exit-time fallbacks at 0.98, all interruptible by the destination.
- **No new Any State transitions.**

**Verified in Play mode:**

*Forced runs: all 14 attacks, from Idle and 0.1 s into a flinch, in phase 1 **and** enraged (56 runs).* Every run:
- entered its state on frame 1;
- landed damage on the measured frame (within one 1/72 s frame);
- returned to the right idle.

Per-attack results:
- **Throws:** contact 0.25–0.31 s after release; caught at clip 1.72 s (catch frame 1.85 s; enraged 1.40 against 1.42) with a 0.07–0.15 m final snap. Twin catches at 2.13 s (enraged 1.69).
- **Rush:** ran 2.39–2.51 m and stopped at exactly 1.44 m.

*Throw counters:*
- **Sidestep:** stepping 0.8 m right at release made it **Missed**, with 0 damage.
- **Bat it away:** sweeping the mace across its path at ~7 m/s produced a real physics `Clash` through `HitDetection`. The throw was **Deflected**, with 0 damage, and returned.

*Natural fights (AI choosing; the player steps back 2.8 m every 9 s):*
- **Phase 1** (8 retreats): every retreat drew a Hurled Axe and/or a Brute Rush, then melee resumed. **0 late state entries.**
- **Enraged** (6 retreats): Hurled Axe / Twin Hurl, then Rush; slams frequent. **0 late state entries.**
- **Fight start:** the first action after the entrance was a Brute Rush from 3.19 m.

### Part 4 — Engagement beyond stats

| Idea | Status | What / verified |
|---|---|---|
| 1. Reactive arena lighting | **Implemented** | See below |
| 2. Weak-point hit-stop | **Implemented** (none existed in the project) | See below |
| 3. Low-HP tension | **Implemented** | See below |
| 4. Distinct victory moment | **Implemented** | See below |

**1. Reactive arena lighting** (`ArenaMoodController` on `BattleSystems/ArenaMood`). It drives the moonlight, trilight ambient, fog and all 8 lamp lights, blending over 2.5 s of real time.

| Mood | Moon | Fog | Lamps |
|---|---|---|---|
| Storm (pre-fight, phase 1) | 0.36, colder | 0.055, denser | wind flicker |
| Phase 2 | 0.38, warmer | — | — |
| Enraged | **blood-orange (1, 0.42, 0.26) 0.62** | red-brown | ×1.4, hard flicker |
| Final | **(1, 0.62, 0.38) 0.80** | — | ×1.6 |
| Victory: **storm clears** | cool and bright, 0.60 | 0.03 | steady |

- **Verified:** the mood names and values were read at each stage. The ambient SH probe actually updates in URP (coefficient 0.010 → 0.012 → 0.027 → 0.034).
- **Authored lighting** is restored on destroy.

**2. Weak-point hit-stop.** A weak-point hit triggers:
- `MonsterAI.HitStop(0.09 s)`: Animator speed 0 **and** the AI attack clock (`AiTime`) frozen together, so damage stays on the visible impact;
- a 0.12 s controller buzz on the hand that struck;
- a white-hot body-glow flash.

Nothing else is slowed: no `Time.timeScale`, so head and hand tracking and the swing-speed maths are untouched.

- **Verified physically:** the mace swept into the right forearm dealt 92.9 damage (weak ×1.75). The monster froze for 6 frames (0.083 s).
- **Sync kept:** the next damage frame moved 0.089 s later in real time and landed on the **same clip time (0.542 s) as an un-frozen run**.

**3. Low-HP tension** (`PlayerLowHealthFeedback` on `OVRCameraRig`). Below 35 % player HP, at full strength by 10 %:
- **Vignette:** a head-locked blood-red quad under `CenterEyeAnchor`, 0.3 m ahead, with a clear centre. Its alpha is up to 0.6, pulsing with each heartbeat; the renderer is off otherwise, so no fill cost.
- **Heartbeat:** a lub-dub (`L3_Heartbeat`) at 72 → 128 bpm.
- **Breathing:** `PlayerExertionAudio` gets a breathing floor, so you pant even when resting.
- **Verified:** at 15 % HP, intensity 0.80, vignette on with alpha 0.32–0.41, 13 beats in 6 s, and breathing at 0.18 volume and playing while idle. Everything turns off at the battle's end.

**4. Distinct victory moment** (`BossFinale` + `BattleManager.victoryPanelDelay`). On the killing blow:
- **Slow motion:** `Time.timeScale` drops to 0.25 for 1.3 s real time, then eases back over 0.6 s. Physics steps are scaled with it.
- **Sound:** a deep final-blow boom (`L3_FinalBlow`).
- **The world:** the glow dies like embers and the storm clears. The Victory panel and `L3_Victory_Sting` follow after **3.2 s real time**.
- **Verified:** lowest timeScale 0.25, back to 1.00 at 1.9 s real, `fixedDeltaTime` restored to 0.02, mood "cleared", glow ×0.00.
  - The panel showed at 4.03 s real because my blocking render call froze editor frames for about 1 s. The wait is `WaitForSecondsRealtime(3.2)`.
- **Levels 1 and 2:** keep 0.75 s (now real time; identical at timeScale 1).

**Flagged, not implemented:**
- **Lightning flashes for the storm:** skipped on purpose. Sudden full-field brightness spikes are a VR comfort and photosensitivity risk; the storm is done with fog, dimming and lamp gusts instead.
- **A deliberate retreat-then-throw pattern** (the boss backing off to use its ranged attacks): there is no walk-back clip. Today ranged attacks happen when *you* back off, and at the fight start.
- **One-handed melee while an axe is away:** needs one-handed variants of every clip.
- **Pitch-down audio in the slow motion, and hit-stop on non-weak hits:** left out to keep the weak-point stop distinct.

### Level 2 regression
After the shared-script changes, `Level2_Medium` was run with a natural battle for 30 s. It had its 10-row table and 0 ranged rows, victory delay 0.75, and no glow component. Normal attacks landed at L2 damage values (16.8 base) with 0 console errors. The Level 1/2 scene files are unmodified.

### Code / asset changes
| File | Change |
|---|---|
| `Editor/Level3DualAxeBuilder.cs` | Simple Lit materials, `BuildEmissionMasks` (mesh-region × crack band-pass, UV rasteriser, seam padding), painted-mask override. `MakeLit` removed. |
| `Editor/Level3DualAxeBuilder.Monster.cs` | Clips `Atk_AxeThrow`, `Atk_TwinHurl`, `Atk_Rush`; 3 states/triggers; ranged table rows; prefab wiring for `MonsterGlow`, `MonsterAxeThrow` (trails, spin sources), `BossFinale`, weak-point hit-stop/haptics. The glow light (ex-enrage aura) was moved to 0.8 m and BossPhaseController no longer drives it. |
| `Editor/Level3EngagementBuilder.cs` | **New**, idempotent: ArenaMood, low-HP vignette (texture + material) + heartbeat source, victory delay. Menu *Tools > Level 3 > Apply Engagement FX*; also called by `Level3SceneBuilder.Build`. |
| `Monster/MonsterAI.cs` | `RangedKind` rows + `ChooseRanged` (melee pool excludes them), rush travel, throw hand-off, `AiTime` + `HitStop`. L1/L2: no ranged rows, so `AiTime == Time.time` and behaviour is unchanged. |
| `Monster/MonsterGlow.cs`, `Monster/MonsterAxeThrow.cs`, `Monster/BossFinale.cs`, `VFX/ArenaMoodController.cs`, `Player/PlayerLowHealthFeedback.cs` | **New** |
| `Monster/MonsterWeakPoint.cs` | Optional hit-stop, haptics and glow flash (off by default); `NotifyHit(HitDetection)`. |
| `Combat/HitDetection.cs` | Passes itself to `NotifyHit` (the hand, for haptics). |
| `Combat/BattleManager.cs` | `victoryPanelDelay` (default 0.75, real time). |
| `Player/PlayerExertionAudio.cs` | `ExternalFloorVolume` (default 0). |
| Assets | `Ch25_100x_Emission.png`, `M_L3_AxeTrail`, `M_L3_LowHpVignette` + `T_L3_LowHpVignette`, synthesized `L3_AxeThrow_Spin` (seamless loop), `L3_AxeCatch`, `L3_Heartbeat`, `L3_FinalBlow`. |
| `Scenes/Level3_Hard.unity` | Additions only: `BattleSystems/ArenaMood`, `CenterEyeAnchor/LowHpVignette`, `OVRCameraRig/PlayerHeartbeatAudio` + `PlayerLowHealthFeedback`, `victoryPanelDelay` 3.2. |

### Headset checks
1. **The brute's skin** should look dark maroon/purple, not grey-blue. Eyes and mouth glow red, and the shoulder cracks smoulder and breathe.
2. **Each wind-up:** the glow surges (purple-tinted on the slam).
3. **Step back about 3 m:** a burning, whirring axe comes at your chest.
   - Sidestep it, or swing into it to bat it away (clang).
   - It returns to his fist.
   - Then he rushes you and sweeps.
4. **Hit the green forearm:** a short freeze, a white flash and a controller buzz.
5. **Below a third of his HP:** the moon turns blood-orange, the fog red, the lamps roar, and the glow goes orange, then white-hot at the end.
6. **Your HP under about a third:** red edges pulse with a heartbeat, and you keep panting at rest.
7. **The kill:** slow motion, a boom, the storm clears, then Victory about 3 s later.
8. **Comfort:** watch the vignette (0.3 m head-locked quad) and the slow motion. Tune `maxAlpha` / `slowTimeScale` if either feels off.

### Ready for headset test? **YES**
Every item above was verified in the editor. Feel, axe-throw readability at Quest resolution, and vignette comfort need the headset.

---

## 2026-10-02 — Level 3: Hard — Twin-Axe Brute (Mixamo Ch25, two axes)

Method: every item below was read from the live project through Unity MCP or traced in Play mode on `Level3_Hard` (1/72 s steps, backend pointed at 127.0.0.1:9, name gate passed, battle started through the real Start path). Clip timing was measured through the real Animator (`Play` + `Update(0)`) on the built prefab, not with `AnimationClip.SampleAnimation`: on a child Animator that call bakes root travel into the body and gave hit positions up to 6 m off.

**What was there before.** The Level 3 build from 2026-10-01 (scene, weapon pickups, entrance, phases, weak point, unblockable slam) was never recorded in this file, and none of it is committed yet. Its boss, the Armored Executioner, was an unrigged Tripo mesh split into two rigid parts with procedural Generic clips. No Mixamo research for Level 3 exists in this file or in any earlier session; that session stopped before its download list.

### 1. Model import + rig
| Item | Result |
|---|---|
| Source | `Ch25_nonPBR.fbx` (project root) copied to `Assets/Models/Monsters/Level3/Ch25_nonPBR.fbx`. The root copy was left in place. |
| Rig | **Humanoid, avatar created from this model, valid.** 52 human bones mapped (full body + all 30 finger bones); only LeftEye / RightEye / Jaw unmapped (Mixamo rigs have none). 0 import warnings (no T-pose or mapping errors). Hands: `mixamorig:RightHand` / `mixamorig:LeftHand`. |
| Mesh | 1 SkinnedMeshRenderer, 44,278 tris, 2 submeshes, 52 skinned bones, 1.91 m. Built at **scale 1.25 → 2.39 m**. |
| Materials | ⚠️ *Superseded by the second pass: URP Lit made the skin grey-blue (F0 0.22 + sky reflections); now URP Simple Lit.* 8 embedded 2048 px textures extracted to `Models/Monsters/Level3/Textures/Ch25/`. URP Lit, **specular workflow** (`M_L3_Ch25_1001/1002`): Specular RGB + Glossiness packed into `Ch25_100x_SpecGloss.png`. Normal maps are on and rendered correctly on the skinned mesh in the editor (the L2 Metal magenta issue did not reproduce); confirm on Quest. |

### 2. Two axes, one per hand
Source: `Axe_deci` from `free-mace-katana-axe-weapon-pack-sketch-mesh/source/3Blades_1.fbx` (imported as `Models/Weapons/Player/Level3/Katana/MaceKatanaAxe_Pack.fbx`). It is a long-bladed bearded axe: haft with a pommel disc, blade on two struts. It was decimated 49,495 → 3,326 tris and baked into grip space (`Models/Weapons/Monster/L3_MonsterAxe_Split.asset`: blade steel / iron fittings / wood haft). The same mesh is used for both hands.

Grip method (same rule as every weapon so far: correct parent, local offset, weapon axis through the grip, checked in renders). On Ch25's Mixamo fist (the sword-grip finger pose), the curled fingers wrap a 0.035 m cylinder whose axis is the knuckle line. The haft runs along that line with the head above the thumb, and the blade edge leads along wrist → knuckles, so the edge leads on every chop.

| | Parent bone | Local position | Local rotation | Size | Clash colliders (both tag `MonsterWeapon`, trigger) |
|---|---|---|---|---|---|
| `MonsterAxe_R` | `mixamorig:RightHand` | (−0.002, 0.108, 0.036) | (274.0, 272.3, 180.0) | 0.96 m in hand space, **1.20 m** in the world | head Box c (0, 0.40, 0.15) s (0.07, 0.84, 0.30) + `HaftCollider` capsule r 0.035 h 0.98 |
| `MonsterAxe_L` | `mixamorig:LeftHand` | (0.002, 0.106, 0.036) | (274.0, 87.7, 180.0) | same | same |

Each axe also carries `BladeEdge` (hit measurement + FX anchor) and its own copy of L2's `AxeFireVFX`.

### 3. Animations — how they were sourced
**Mixamo itself could not be used**: downloading needs your Adobe login. Every L3 clip is therefore baked from Mixamo mocap already in the project. The bake works in Humanoid muscle space (`Editor/HumanoidClipBaker.cs`), so it retargets exactly like an imported clip.

How dual-wield is made from one-handed mocap: the left arm plays the **mirrored** right-arm swing, using the same maths as Unity's `AnimatorState.mirror`. It is either simultaneous (body averaged, so the axes cross or slam together) or delayed (the body blends from right to left, so the chops alternate). Slow anticipation is compressed so wind-ups are shorter than Level 2's, and jumps are damped.

Poses with no mocap (idle, guard, roar) are arm muscle values **fitted numerically** to axe targets on this rig. Every clip was previewed on Ch25 itself through the prefab's Animator and retargeted cleanly (no broken limbs, feet planted, fists closed on both hafts).

| State | Mixamo clip it is built from (name as on Mixamo) | How |
|---|---|---|
| `L3_Idle` / `L3_IdleEnraged` | Standing Melee Attack Horizontal (ready stance) | Fitted pose: axes held out at the sides, blades hanging. Heavy breathing (3.2 s loop). Enraged: hunched, wider, 1.2 s breathing + tremor. |
| `L3_Underground` / `L3_Rise` | Mutant Jump Attack (landing crouch 1.70 → 3.70 s) | Crouched with both axes in the ground, rising to full height over 3.1 s |
| `L3_Roar` | (authored) | Arms flung wide, chest out, head back, tremor, 2.4 s |
| `L3_Defend` | (authored) | **Both axes crossed in front of the face**, 1.0 s |
| `L3_Hit` | Hit Reaction (`Monster_HitReaction.fbx`) | First 0.9 s, fists kept closed |
| `L3_Death` | Two Handed Sword Death | Fists kept closed |
| attacks | see §4 | |

**Optional upgrades from Mixamo** (search terms; I could not open Mixamo to confirm, so the names marked "likely" are from memory and the rest are guesses). Download as FBX for Unity, without skin, to `Assets/Animations/Monster/Level3/`, set Rig = Humanoid, and change the source line in `Level3DualAxeBuilder.BuildClips`:
- Rise: **"Zombie Stand Up"** (likely)
- Roar: **"Mutant Roaring"** (likely)
- Enraged idle: **"Mutant Breathing Idle"** (likely)
- Guard: **"Sword And Shield Block"** (likely; then mirror it for the second axe)
- Dual-weapon attacks: search **"dual"**. I could not verify that Mixamo has a dual-axe set. The mirrored composites above exist because of that.

### 4. Attack table — 11 attacks (L2: 10)
Base damage 18 × 1.25 (`WeaponType.DualAxe`) = **22.5**. Wind-up = the **measured** hit frame of the baked clip: blade-edge speed peak (Level 2 convention), the front-most blade frame for the spin and the backhand chops, and the blade reaching the ground for the slam. Enraged: ÷ 1.3 (AI tempo and Animator speed together).

| # | Attack | Built from | Category | Wind-up (enraged) | Damage per hit | Arc | Weight full → 0 HP / enraged | Rules |
|---|---|---|---|---|---|---|---|---|
| 1 | Alternating Chops | One Hand Sword Combo: right chop, then the mirrored left 0.45 s later | Standard | 0.575 + 0.350 (0.442) | 19.1 × 2 | 55° | 28 → 16 / 12 | |
| 2 | Crossing Double Swing | Standing Melee Attack Horizontal: both axes sweep in and cross | Standard | 0.558 (0.429) | 22.5 | 75° | 26 → 15 / 12 | |
| 3 | Twin Diagonal Hack | Great Sword Slash: both overhead, X-cut down | Standard | 0.525 (0.404) | 23.6 | 50° | 24 → 14 / 10 | |
| 4 | Spinning Reap | Sword And Shield Slash: spin and hop, both axes sweep | Standard | 0.708 (0.545) | 22.5 | 90° | 14 → 10 / 8 | |
| 5 | Backhand Double Chop | One Hand Sword Combo (2nd half): backswing, right then left chop | Standard | 0.525 + 0.283 (0.404) | 19.1 × 2 | 50° | 16 → 10 / 8 | |
| 6 | Reaping Flurry | Standing Melee Combo Attack Ver. 1: left, right, both | Combo | 0.608 + 0.267 + 0.967 (0.468) | 15.8 × 3 | 60/60/70° | 10 → 16 / 14 | 4 s cd |
| 7 | Rending Double Strike | Standing Melee Combo Attack Ver. 3: right, then the mirrored left 0.30 s later | Combo | 0.525 + 0.267 (0.404) | 18.0 × 2 | 55° | 10 → 14 / 12 | 3 s cd |
| 8 | Twin Whirlwind | Great Sword High Spin Attack: full spin, both axes at head height | **Heavy** | 0.883 (0.679) | 33.8 | 180° | 6 → 16 / 14 | 4 s cd |
| 9 | Twin Overhead Cleave | Standing Melee Attack Downward: both overhead, a held beat, slam | **Heavy** | 0.942 (0.724) | 40.5 | 40° | 5 → 14 / 12 | 5 s cd, axe fire |
| 10 | Leaping Double Cleave | Standing Melee Run Jump Attack: leap, double cleave | **Heavy** | 0.792 (0.609) | 38.3 | 45° | 0 → 12 / 12 | HP ≤ 66 % only, 6 s cd, axe fire |
| 11 | **Earthsplitter Slam** | Mutant Jump Attack: leap, both axes into the dock | **Special, UNBLOCKABLE** | 1.300 (1.000) | 45.0 | zone r 1.5 m at 0.6 m in front | 1.5 → 6 / **34** | 6 s cd, axe fire, own warning sound, red danger zone, forced once at 50 % HP |

Selection, 4,000 picks through the real `ChooseFromTable`:

| HP | Standard | Combo | Heavy | Special |
|---|---|---|---|---|
| 100 % | 77.0 % | 14.3 % | 7.6 % | 1.2 % |
| 60 % | 63.8 % | 17.6 % | 16.5 % | 2.2 % |
| 35 % | 58.1 % | 18.0 % | 21.3 % | 2.6 % |
| Enraged | 33.0 % | 17.4 % | 26.1 % | **23.5 %** |

**Animator (`Animations/Monster/Level3/Monster_L3_TwinAxe.controller`): 19 states, 127 transitions; Any State → Death only.** Every attack is entered on its own trigger from Idle, IdleEnraged, Hit, Defend and Roar. Every attack leaves to Idle (`Idle` & !Enraged) or IdleEnraged (`Idle` & Enraged), with exit-time fallbacks, all interruptible by the destination. Audited transition by transition; all 11 table triggers exist as parameters with real states.

Bug found and fixed in this pass, in this same bug class: returns went only to Idle, then Idle → IdleEnraged (a 0.2 s blend that could not be interrupted). While enraged, 12 actions started 0.06–0.18 s late and defends showed the guard pose late. After the fix, the same 35 s enraged run had 0 late entries.

**Verified in Play mode:**
- Forced runs: all 11 attacks forced from Idle and 0.1 s into a flinch, in phase 1 **and** enraged (44 runs). Each reached its state on the frame after the trigger, landed damage on the measured clip frame (within 2 frames at 72 fps), and returned to the right idle.
- Natural fights (AI choosing, a body hit every 1.3 s): phase 1 had 13 attacks and 5 defends; enraged had 16 attacks and 6 defends. **0 late or missing state entries.**

### 5. Weak point — one arm: the right forearm
Decision: a single weak point on the **right (lead) forearm**, not both arms:
- It opens every swing toward the player (alternating attacks start right), so hitting it means stepping into the danger.
- One glowing target keeps the bonus special and readable. With both arms glowing, the whole front of the guard (both forearms cross in front during Defend) would become a bonus zone.

Built as `HitVol_R_LowerArm` (bone-following) + `MonsterWeakPoint` ×1.75 with its own sound. Ch25's 650 right-forearm triangles (skinned mainly to `RightForeArm`) are split into their own submesh (`Ch25_WeakPointSplit.asset`), so the skin itself pulses green (`M_L3_Ch25_WeakPoint`).

Measured: left forearm 67.2 damage vs right 117.0 for the same strike (**×1.74**). `HitCount` incremented, `L3_WeakPoint_Hit` played, emission pulsing.

### 6. Level 3 systems with the new boss (all traced live)
| System | Result |
|---|---|
| Weapon scatter + grab | Start refused while unarmed. Mace / Flail / Katana each equip both hands, hide the previous pair and their own staged props, and switch stats (min swing 2.0 / 1.6 / 1.3 m/s, damage ×1.35 / 1.1 / 0.9). Unchanged by the swap. |
| Entrance | Circle fades in (1.0 s) → `Rise` trigger at 1.47 s, Y −2.90 → 0 eased over the 3.1 s clip → `Roar` animation + sound on the same frame, held 2.25 s → AI and colliders on at 6.93 s, circle fades out. |
| Phases / enrage | HP bar read live: "Enemy 1500/1500  PHASE 1" at spawn. Crossing 33 % switched `BossPhaseController` to phase 2 = Enraged. Enraged: Animator and AI tempo 1.30, IdleEnraged, red aura 2.8, chest embers, cooldown ×0.68. The 66 / 33 % break markers are created by the same `Start` call that set that label, but the marker objects themselves were not read back: the editor had been switched to Level2_Medium when that check ran. |
| Unblockable | Slam full 45 damage with `LastHitWasUnblockable`. A parry during its wind-up is ignored (normal attacks parried: 22.5 → 7.9, 23.6 → 8.3). Stepping 2.2 m sideways during the wind-up: 0 damage. With a shield block held (check only; L3 has no shield): normal 9.0 vs unblockable 45.0. |
| Clash / parry | All 4 axe colliders (R head, R haft, L head, L haft) return `Clash`, fire `WeaponClashed`, play `swords-collide` on the player weapon, and deal 0 damage. |
| Defend | Crossed-axes guard every ~5 s for 1.0 s; a chest hit during it is reduced ×0.30 with the block clang. |
| Death | Death state, all colliders off, VICTORY, `L3_Victory_Sting`, victory panel. Defeat also reached naturally (an idle player was killed while the trace polled). |

### 7. Audio
| Event | Clip |
|---|---|
| Standard / combo telegraph | `L3_Telegraph_Standard` |
| Heavy telegraph | `L3_Telegraph_Heavy` |
| Unblockable warning / impact | `L3_Slam_Warning` / `L3_Slam_Impact` |
| One-axe hit (alternating chops, flurry, rending, backhand) | **`L3_DualAxe_Swing_Single`** (new: whoosh + steel "shing") |
| Two-axe swing (crossing, X-hack, spin reap) | **`L3_DualAxe_Swing_Double`** (new: two whooshes 75 ms apart) |
| Heavy | **`L3_DualAxe_Swing_Heavy`** (new: two deep whooshes 110 ms apart, sub thump, ringing metal; L2 uses one `L2_Axe_Whoosh_Heavy`) |
| Clash / parry (either axe) | `swords-collide` on the player weapon |
| Weak point | `L3_WeakPoint_Hit` |
| Defend brace / block | `L2_Defend_Brace` / `L2_Defend_Block` |
| Entrance / enrage / phase | `L3_Summon_Circle`, `L3_Rise_Grind`, `L3_Roar_Entrance`, `L3_Roar_Enrage`, `L3_PhaseChange_Growl` |
| Death / victory / defeat | `dramatic-death-collapse` (own AudioSource), `L3_Victory_Sting` / `L3_Defeat_Sting` |

The new swing clips are synthesized (numpy) in `Audio/SFX/Level3/`.

### 8. Level 3 vs Level 2 (both read live)
| | L2 Vampire | L3 Twin-Axe Brute |
|---|---|---|
| Monster HP | 800 | **1500** |
| Base hit | 16.8 | **22.5** |
| Attacks | 10 (1 weapon) | **11 (2 weapons, 4 multi-hit)** |
| Standard wind-up | 0.59–0.63 s | **0.525–0.71 s (enraged 0.40–0.55)** |
| Heavy wind-up | 1.04 s, idle-slam 1.475 s | **0.79–0.94 s (enraged 0.61–0.72)** |
| Special | Leaping Cleave 1.025 s, blockable | **Unblockable slam 1.30 s (enraged 1.00), dodge only** |
| Cooldown start-to-start | 2.2 s (×0.85 when hurt) | **1.9 s (×0.85 hurt, ×0.68 enraged)** |
| Range (horizontal to headset) | 1.55 m | **1.45 m** (measured axe reach: blade mid-edge 1.1–1.24 m) |
| Defend | 5.5 s cd, 1.0 s, 70 % | 5.0 s cd, 1.0 s, 70 % |
| Player HP | 800 | 800 |

### 9. Code / asset changes
| File | Change |
|---|---|
| `Editor/Level3DualAxeBuilder.cs` + `.Monster.cs` | **New.** Re-runnable build of materials, axe mesh, clips, controller, prefab and measured table. Menu: *Tools > Level 3 > Build Dual-Axe Monster*. |
| `Editor/HumanoidClipBaker.cs` | **New.** Muscle-space mirror / retime / override / concat / dual-wield composer. |
| `Editor/Level3Builder.cs` | Monster prefab + Executioner attack table removed; *Build Prefabs* now builds this boss. `MonsterPrefabPath` → `Monster_Hard_TwinAxeBrute.prefab`. |
| `Editor/Level3SceneBuilder.cs` | Monster weapon `DualAxe`. |
| `Editor/MeshDecimateTool.cs` | Optional mesh name argument (existing callers unchanged). |
| `Combat/WeaponType.cs` | `DualAxe = 7` appended (monster only). |
| `Monster/MonsterController.cs` | `DualAxe` damage flavour ×1.25 (same as the tuned Hard value). |
| `Monster/MonsterAI.cs` | `extraWeaponFireVfx[]`: the second axe ignites with the first (empty = L1/L2 unchanged). |
| `Manager/LevelManager.cs` | Hard: monster weapon `DualAxe`, attack range 1.6 → **1.45**. |
| `Scenes/Level3_Hard.unity` | `LevelManager.monsterPrefabForThisScene` → `Monster_Hard_TwinAxeBrute.prefab`, monster weapon `DualAxe`. Nothing else changed (in-memory scene diffed against disk). |

**Armored Executioner assets are no longer referenced anywhere** (scene, prefabs, builders), but were **not deleted**, because they are uncommitted and could not be recovered:
- `Prefabs/Monsters/Monster_Hard_Executioner.prefab`
- `Models/Monsters/Level3/ArmoredExecutioner.fbx` (51 MB), `ArmoredExecutioner_Quest.asset`, `Executioner_Upper/Lower.asset`
- `Animations/Monster/Level3/Exec_*.anim`, `Atk_ReapSweep/OverheadChop/PolearmJab/TwinReap/ReapingWhirl.anim`, `Monster_L3_Executioner.controller`
- `Materials/Level3/M_L3_Executioner*.mat`
- `Editor/ExecutionerAnimationBuilder.cs`, `ExecutionerMeshSplitter.cs`

Note that `Atk_GroundSlam.anim` was overwritten by the new slam.

### Headset checks
1. Grab a pair → Start. The green circle appears, the brute rises through it, roars, and only then attacks.
2. Both axes sit in the fists with the edges leading. Each attack should show its own two-axe pattern (alternating, crossing, X-cut, spin, flurry, leap, slam).
3. Swing into either axe during a wind-up: clang + reduced hit. During the purple-flash slam, the clang does nothing; only stepping out of the red ring avoids it.
4. Hit the glowing right forearm: a distinct sound and much bigger damage numbers.
5. Below a third of its HP: a red tint and aura, a faster tempo, many more slams.
6. Watch for magenta on the brute's skin (normal map on a skinned mesh, Quest/Vulkan).
7. Check the 1.45 m stand-off feels like the blades reach you.

### Ready for headset test? **YES**
0 console errors. Every item above was verified in the editor; feel, reach, and the Quest rendering of the normal map need the headset.

---

## 2026-09-30 (third pass) — Level 2: flinch vs attack / guard animations (FIXED, both levels now)

**Cause.** `Monster_L2_Axe.controller` entered every attack and Defend only from `Idle`, and `Hit` had a single way out, its exit time (0.93 s). A trigger set while the monster was flinching, or during a blend back to Idle, waited; if the attack finished first, its end-of-attack `Idle` call reset the trigger and the animation never played.

**What is shared and what is per level.**
| Change | Where | Level 1 | Level 2 |
|---|---|---|---|
| Flinch's delayed return-to-Idle no longer fires during a defend | `Monster/MonsterController.cs` (shared) | applied in the second pass | applied in the second pass (same code) |
| Attacks and Defend can start out of `Hit`; blends into `Idle` (and `Idle → Hit`) can be taken over by the next trigger | Animator controller (one per level) | `Monster_L1_Sword.controller`, second pass | **`Monster_L2_Axe.controller`, this pass** |

**Applied to `Monster_L2_Axe.controller`.** 11 new transitions `Hit → <state>` copied from the matching `Idle → <state>` ones (same trigger, blend and entry offset): the 10 attacks (Executioner Slam through `Atk_ExecSlam_Raise`) and Defend. 26 blends set to *Interruption Source: Next State* (every transition into `Idle`, plus `Idle → Hit`). Nothing else changed: clips, speeds, offsets, the flinch length and **Any State → Death only** are as before. 15 states, 49 transitions.

**Verified in Play mode, same scenario that exposed it** (natural fight, AI choosing, a 3-damage body hit every 1.3 s, 45 s, 1/30 s steps), recorded per frame before and after the change.
| | Before | After |
|---|---|---|
| Attacks completed | 16 | 16 |
| Attack animation started late (more than 2 frames after the trigger) | 10 (0.17–0.87 s) | 0 (all on the trigger frame) |
| Damage resolved with no attack animation playing | 2 (Diagonal Slash while still in `Hit`, Horizontal Sweep while still in `Idle`) | 0 |
| Defends where the guard pose was late or missing while the 70% reduction was on | 7 of 8 (0.17–1.0 s, one for the whole window) | 0 of 8 |
| Attack animations cut while the attack was active | 0 | 0 |

Worst case, all 10 attacks forced 0.1 s after a body hit (1/72 s steps): every one went `Hit → own state` on the next frame and landed its damage (Sweep 16.8, Diagonal 16.8, Backhand 15.1, Twin Strike 12.6 ×2, Whirlwind 23.5, Executioner Slam 29.4 through Raise → Strike, Leaping Cleave 33.6, Reaping Combo 13.4 ×2, Lunging Cleave 18.5, Overhead Chop 16.8). Console: 0 errors.

**Headset check (Level 2).** Keep hitting the vampire's body while it attacks: every attack should still show its swing, starting with the red flash, and the blue guard pose should come up the moment the tint does.

---

## 2026-09-30 (second pass) — Level 1 lag diagnosis + 6-attack table

Everything below was read from the live `Level1_Easy` scene, the spawned `Monster_Easy_HipHop(Clone)` and the Unity Profiler (`ProfilerDriver` frame data read through MCP). **All timings are from the editor on the Mac, not from the Quest.** The editor runs throttled in the background, so the numbers are CPU milliseconds per frame (main thread, `PlayerLoop`), not frame rates. Game time was stepped at 1/72 s.

### P1. Level 1 lag

**Reference points.** Level 1 APKs were built at 12:17 (before the hit-volume / breathing / `OnTriggerStay` changes) and at 13:04 (after). Both are non-development IL2CPP Release builds. Render pipeline asset, quality, physics and XR settings did not change between them (git diff).

**Profiler, Level 1 fight (avg ms per frame).**
| Case | PlayerLoop | Physics | Scripts (Update) | Canvas rebuild | GC / frame |
|---|---|---|---|---|---|
| Hands idle, monster attacking | 2.92 (p95 3.89) | 0.25 | 0.28 | 0.04 | 0.7 KB |
| Both hammers swinging through the monster | 3.12–3.22 | 0.36 | 0.35 | 0.20 | 1.3 KB |
| Same, 13 `HitVol_*` colliders switched off | 3.07 | 0.32 | 0.34 | 0.19 | 1.2 KB |
| Same, colliders back on | 3.05 | 0.36 | 0.36 | 0.19 | 1.2 KB |

**Suspects checked.**
| Suspect | Found | Verdict |
|---|---|---|
| Attack-tier debug logs | `MonsterAI.verboseAttackLogs`, `MonsterAnimator.verboseAttackLogs`, `SessionReporter.verboseLogging` all **false** on the runtime objects; none of those logs appeared | not firing |
| Other logs | `CombatUIController.ShowDamageNumber` logged `Damage: x at (pos)` on **every hit dealt and every hit taken** (no `damageNumberPrefab` is assigned in either level). `MonsterController` logged `[Monster] Blocked!` on every defended hit. 66 + 13 log lines in ~45 s of fighting | **firing on every hit** |
| Breathing (`PlayerExertionAudio`) | runs in `Update`, 0.011–0.013 ms per frame, 1 B GC; clip is Decompress On Load, one reused looping source, not spatialized | not the cause |
| New colliders | 13 primitive capsules (no mesh colliders), no Rigidbody on the monster. On vs off: physics +0.05 ms, frame time unchanged | not the cause |
| `OnTriggerStay` retry | 3.6–3.8 calls per frame while swinging, 0.03 ms per frame | not the cause |
| Audio | 6 AudioSources in play, all reused, none created or destroyed per hit, no unintended loops | clean |
| Duplicates | 1 enabled camera, 1 AudioListener, 1 BattleManager, 1 SessionReporter, 1 EventSystem, 0 monsters before Start, 1 after; same after a Retry reload | clean |
| Environment | 1 `Environment_LiteDungeon` root, 51 pieces (25 floor, 20 wall, 2 torch, 2 column, chest, barrel), 0 pieces sharing a pose | clean |

**What the Profiler did show: the cost is on the frames where a hit lands.**
| | Frame with a landed hit | Other frames |
|---|---|---|
| PlayerLoop | 4.85 ms | 3.09 ms |
| Trigger callback (`HitDetection`) | 1.08 ms, of which 0.85 ms is `Debug.Log` + stack trace | 0.03 ms |
| Canvas rebuild | 0.42 ms | 0.18 ms |
| Garbage | 9.7 KB | 0.7 KB |

Worst frames in the captures: a garbage collection (34–46 ms in the editor, where the heap is much larger than on Quest), a one-off font atlas rebuild when new digits first appear (6.5 ms), then hit frames (5–7 ms).
The stamina bar was pushed to the HUD on every frame while stamina regenerates (each landed hit spends 8, regen is 12/s), and the timer text was rewritten every frame; every push rebuilds the whole `CombatCanvas`. With hands idle the canvas cost is 0.04 ms, in a fight 0.18–0.20 ms.

**Conclusion.** No steady per-frame slowdown reproduces in the editor: the three additions from the first pass cost under 0.1 ms per frame together. The measurable cost is per landed hit, and the hit-volume fix makes far more swings land than before. Whether this is what is felt on the headset is **not proven**; Quest GPU load and thermal state cannot be measured from the editor.

**Fix (only the measured hit-path costs).**
- `CombatUIController`: the per-hit `Damage:` log is off unless the new `verboseDamageLogs` is ticked; the stamina bar is redrawn only when it moved by `staminaUiStep` (2%) or reached empty/full; the timer label is rewritten only when the displayed second changes.
- `MonsterController`: the `[Monster] Blocked!` log only when `MonsterAI.verboseAttackLogs` is on.

**Before / after (same Play session, only the log switch changed; canvas number against the first capture).**
| | Before | After |
|---|---|---|
| Extra cost of a hit frame | +2.03 ms | +0.71 ms |
| `HitDetection` callback on a hit frame | 1.26 ms | 0.37 ms |
| Garbage on a hit frame | 11.7 KB | 4.8 KB |
| Canvas rebuild on non-hit frames | 0.18 ms | 0.05 ms |
| Console lines per fight | one per hit | 0 (154 hits, 0 log lines) |

**Not changed, for information.** Each hammer mesh is 37,672 triangles (75,344 for the pair, 70% of all scene mesh triangles; drawn again in the shadow pass). The monster sword is 44 separate MeshRenderers. Both predate the regression. `Level1_Easy` is currently **disabled** in Build Settings (only `Level2_Medium` is enabled).

**To measure on the headset.** Install *OVR Metrics Tool* from the Meta store, enable its persistent overlay (Meta Quest Developer Hub ▸ device ▸ Performance overlay also works), play a Level 1 fight and read: FPS (72 expected), stale frames, CPU and GPU utilisation, App GPU time. With a cable: `adb logcat -s VrApi` prints the same every second (`FPS=`, `Stale=`, `CPU%=`, `GPU%=`, `App=` ms). CPU% near 100 with stale frames on hit = script/physics; GPU% near 100 all the time = rendering.

### P2. Level 1 monster — 6 attacks (was 3)

**Clips under `Assets/Animations/` and who uses them.**
| Clip | Rig | Level 1 | Level 2 |
|---|---|---|---|
| Great Sword Slash | Humanoid | Slash | Diagonal Slash |
| Standing Melee Attack Horizontal | Humanoid | **Side Sweep (new)** | Horizontal Sweep |
| Sword And Shield Attack (L1) | Humanoid (was Generic) | **Overhead Strike (new)** | — |
| Sword And Shield Slash (L1) | Humanoid (was Generic) | **Cut and Chop (new)** | — |
| Great Sword High Spin Attack (`mixamo.com`) | Humanoid | Spin Slash | — |
| Great Sword High Spin Attack (`GreatSwordSpin_RotationBaked`) | Humanoid | — | Whirlwind Spin |
| Mutant Jump Attack (`MutantJump_HeightBaked`, new clip on the same FBX) | Humanoid | **Leaping Slam (signature)** | — |
| Mutant Jump Attack (`mixamo.com`) | Humanoid | only the old `Monster_Easy.controller` | — |
| Monster_SwordAttack (3 swings) | Humanoid | — | Backhand Cut, Overhead Chop, Lunging Cleave |
| Standing Melee Combo Attack Ver. 3 / Ver. 1 | Humanoid | — | Twin Strike / Reaping Combo |
| Standing Melee Attack Downward | Humanoid | — | Executioner Slam |
| Standing Melee Run Jump Attack | Humanoid | — | Leaping Cleave |
| Great Sword Impact / Monster_HitReaction / Monster_SwordIdle / Two Handed Sword Death | Humanoid | Defend / Hit / Idle / Death | same |
| Great Sword Idle, Hit Reaction, One Hand Sword Combo | Generic | unused | unused |
| `Player_Future/` Sword And Shield Attack, Sword And Shield Slash | Generic | unused (originals) | unused |

All Humanoid clips retarget to both monsters. The two sword-and-shield clips were unreferenced duplicates in `Animations/Player_Future/` (`SwordShield_*.fbx`, byte-identical to the two files still there). They were moved to `Animations/Monster/Level1/`, reimported as Humanoid and given *Bake Into Pose (Y)*, because without it the feet sank 0.34–0.39 m into the floor and floated 0.4–0.5 m above it. With it the feet are on the floor whenever the monster is grounded (lowest foot −0.03 / 0.00 m), and the clips' own jumps show: **Overhead Strike is a hopping strike (feet up to 1.4 m off the floor, still descending at the hit frame)** and Cut and Chop has a 0.5 m hop on the chop.

**Attack table (`Monster_Easy_HipHop.prefab` ▸ `MonsterAI.attackTable`).** Base damage 8 × 1.1 sword = 8.8. Cooldown 3.2 s start to start.
| Attack | Clip · state speed | Category | Trigger / state | Telegraph (trigger → hit) | Damage | Weight full → 0 HP | Rules |
|---|---|---|---|---|---|---|---|
| Slash | Great Sword Slash · 0.94 | Standard | `Atk_Slash` | 0.90 s | ×1.0 = 8.8 | 30 → 20 | |
| Side Sweep | Standing Melee Attack Horizontal · 1.0 | Standard | `Atk_SideSweep` | 0.985 s | ×1.0 = 8.8 | 22 → 15 | |
| Overhead Strike | Sword And Shield Attack · 1.0 | Standard | `Atk_OverheadStrike` | 1.15 s | ×1.1 = 9.7 | 18 → 13 | Level 1 only |
| Cut and Chop | Sword And Shield Slash · 0.7 | Combo (2 hits) | `Atk_CutAndChop` | 0.89 s, then +1.06 s | ×0.6 = 5.3 ×2 | 8 → 16 | 6 s cooldown; Level 1 only |
| Spin Slash | Great Sword High Spin · 0.87 | Heavy | `Atk_SpinSlash` | 1.28 s | ×1.2 = 10.6 | 12 → 20 | 5 s cooldown |
| Leaping Slam | Mutant Jump (height baked) · 1.0 | Special | `Atk_LeapSlam` | 1.60 s | ×1.5 = 13.2 | 8 → 16 | 9 s cooldown; forced once when HP first drops to 40%; Level 1 only |

Selection rules are the Level 2 ones (`ChooseFromTable`): weighted, no immediate repeat, a Heavy/Special forced after 4 lighter attacks, cooldown × 0.94 at 0 HP (3.2 → 3.0 s; Level 2 is × 0.85).
Leaping Slam is the signature move: with the jump height baked into the pose the monster really leaves the ground (feet 2.4 m up at the apex, lands in a crouch and drives the sword into the floor). The old version threw the height away and hovered. Level 1 therefore has two airborne attacks (Overhead Strike 1.4 m, Leaping Slam 2.4 m); Level 2 has none, its leap is not height-baked.

**Selection, 3 000 picks per HP step through the real `ChooseFromTable`.**
| Monster HP | Standard | Combo | Heavy | Special | Immediate repeats |
|---|---|---|---|---|---|
| 100% | 66% | 7% | 16% | 11% | 0 |
| 60% | 59% | 11% | 17% | 13% | 0 |
| 35% | 57% | 13% | 18% | 13% | 0 |
| 15% | 53% | 14% | 18% | 14% | 0 |
Old set for comparison: Slash 67 / Heavy 17 / Special 17% above 50% HP, 53 / 27 / 20% to 25%, 50 / 25 / 25% below.

**Animator — new `Animations/Monster/Level1/Monster_L1_Sword.controller`** (the old `Animations/Monster_Easy.controller` is untouched and unused, as a rollback). 10 states, 31 transitions.
- Each attack has its own trigger and is entered from `Idle` or from `Hit` (so a flinch cannot delay a swing), no exit time, 0.10 s blend.
- Each attack returns to `Idle` on the `Idle` trigger (`EndAttack`), with an exit-time fallback at 0.98.
- `Hit` and `Defend` are entered from `Idle` (`Defend` also from `Hit`) and return on the `Idle` trigger or by exit time.
- **Any State → Death only.** The old controller had Any State → every attack, → Hit and → Idle.
- The 17 blends that end in `Idle` (and `Idle → Hit`) can be taken over by the next trigger, so a new attack or defend starts on the same frame instead of after the blend.

**Verified in Play mode.**
- Forced run of all 6 (`DebugForceAttack`, 1/72 s steps): each went `Idle → own state → Idle`, animation began the frame after the trigger, every hit landed with the measured damage. Sword tip at the damage frame (monster space, metres right / up / forward): Slash (−0.36, −0.17, 1.44), Side Sweep (0.98, 1.33, 1.91), Overhead Strike (−0.14, 1.97, 2.12), Cut and Chop (0.32, −0.11, 1.40) then (−0.21, 1.07, 1.88), Spin Slash (−0.40, 1.91, 1.67), Leaping Slam (0.09, 0.87, 1.64).
- Natural fights with the AI choosing, hammers hitting the whole time, HP stepped 100% → 55% → 30%: 23 + 14 attacks, every one reached its own state (0 mismatches), all 6 seen, 10 + 7 attacks started out of a flinch, 0 attack animations cut while the attack was active, first attack after crossing 40% HP was Leaping Slam, guard pose held 1.4–1.6 s on every defend. After the blend change: animation start delay 0.00 s on all 14, 0 frames defending without the guard pose.
- Flinch then defend 0.1 s later: `Hit → Defend`, guard held for the full window. Flinch then attack 0.1 s later: `Hit → Atk_Slash` on the next frame.
- Death during an attack: `Atk_CutAndChop → Death`, stays in Death, colliders off, Victory panel shown.

**Level 1 vs Level 2 now** (supersedes the "Distinct attacks 3" row further down).
| | Level 1 | Level 2 |
|---|---|---|
| Distinct attacks | 6 (+ defend) | 10 (+ defend) |
| Telegraph | 0.89–1.60 s | 0.57–1.48 s |
| Damage per hit | 5.3–13.2 | 12.6–33.6 |
| Cooldown | 3.2 → 3.0 s | 2.2 → 1.87 s |
| Standard share, full → low HP | 66% → 53% | 60% → 42% |
| Dodging | lands whenever in range (unchanged) | needs range + swing arc |

### Code / asset changes (second pass)
| File | Change |
|---|---|
| `UI/CombatUIController.cs` | `verboseDamageLogs` (off), `staminaUiStep`, stamina and timer only redrawn on a visible change |
| `Monster/MonsterController.cs` | `Blocked!` log behind `verboseAttackLogs`; the flinch's delayed return-to-Idle no longer fires during a defend (it dropped the guard pose while the 70% reduction was still on; both levels) |
| `Monster/MonsterAnimator.cs` | warning when a table attack names a trigger the controller does not have |
| `Animations/Monster/Level1/Monster_L1_Sword.controller` | **New** |
| `Animations/Monster/Level1/Sword And Shield Attack (L1).fbx`, `Sword And Shield Slash (L1).fbx` | moved from `Player_Future/SwordShield_*.fbx`, Humanoid, Y baked |
| `Animations/Monster/Mutant Jump Attack.fbx` (import settings) | extra clip `MutantJump_HeightBaked`; `mixamo.com` unchanged |
| `Prefabs/Monsters/Monster_Easy_HipHop.prefab` | new controller, 6-row attack table, `lowHpCooldownScale` 0.94 |
| `Scenes/Level1_Easy.unity` | two new `CombatUIController` default fields serialized (no behaviour change) |

### Open items found in this pass (not changed)
- **Level 2: a flinch delays or drops attack animations.** `Monster_L2_Axe.controller` enters attacks only from `Idle`, and `Hit` has no way out before its exit time (0.93 s). With a body hit every 1.3 s: 8 attacks, animation started 0.10–0.97 s after the trigger, and one Horizontal Sweep dealt its damage without ever reaching its state. The Level 1 controller's two changes (`Hit → attack` transitions, interruptible blends into Idle) fix this. **✅ Fixed in the third pass (section above).**
- Level 1 Spin Slash still uses the original clip, where the body does not turn and the first of its two swings crosses the player at about 0.5 s without damage; the hit is the second swing.
- The static root capsule of the monster stays on the ground while it is airborne in Leaping Slam.
- Swing damage still grows with every landed hit (`damagePerTapBonus` 0.35 × hits so far). By the formula a hit is worth about 90 after 154 hits; in the second trace the monster went from 150 HP to dead between two frames at that point.

### Headset checks
1. Enable `Level1_Easy` in Build Settings and move it to the top before building.
2. Fight for a minute with the OVR Metrics overlay on: FPS and stale frames while hitting the monster steadily, compared with standing still.
3. Watch for all six attacks; each should start the moment the red flash starts and hit when the blade reaches you. Leaping Slam: the monster should jump clear of the floor and land in front of you. Overhead Strike also hops. Judge whether either jump is too much for the Easy level (lower `weightFullHp` / `weightLowHp` on that row to make it rarer).
4. Cut and Chop: two hits, low cut then overhead chop, about a second apart.
5. Hit the monster while it winds up: the swing must not be cancelled. Hit it as it raises its guard: the guard pose must stay up.

---

## 2026-09-30 — L1 hit coverage, breathing, block vs parry, new L2 attacks

Everything below was inspected on the runtime objects (spawned prefab clones) and exercised in Play mode at 1/72 s steps: weapons moved through the monster with real trigger contacts, attacks forced through `MonsterAI.DebugForceAttack`, Animator state + axe-head position sampled every frame. **Swing feel, loudness and readability still need the headset.**

### A4. Level 1 — hammer hits not registering (FIXED)
**Found (runtime `Monster_Easy_HipHop(Clone)`):** 2 colliders only — root `CapsuleCollider` c (0,1,0) r 0.42 h 2.0 (non-trigger, tag `Monster`, static, does not follow the animation) and `MonsterSword` trigger box (tag `MonsterWeapon`). The Warrok mesh is 1.4 m wide and leans 1.0 m forward: only **34% of its vertices were inside the capsule**; head 4 cm, forearms 31–41 cm, hands 41–47 cm outside it. So a hammer visibly connecting with the head, arms or hands touched nothing.
Not the cause: hammer colliders (trigger box 0.27 × 0.19 × 0.24 m on the head, both hands, kinematic Rigidbody — unchanged since the earlier fix), tags (`HitDetection` checks `MonsterWeapon` for clash, otherwise any collider under a `MonsterController`), layers (everything Default, collision matrix has no ignored pairs), cooldown (0.35 s per hammer).
Second cause, in `HitDetection`: a contact was evaluated **once, on `OnTriggerEnter`**. If that moment was rejected (below 1.2 m/s, or inside the 0.35 s cooldown that a sword clash or the other body part had just started) the hit was lost until the hammer left the monster and came back.
**Fix:**
- `Monster_Easy_HipHop.prefab`: 13 bone-following `HitVol_*` capsules (non-trigger, Untagged), fitted to the skinned mesh per body part by the new `Assets/Editor/MonsterHitVolumeBuilder.cs` (menu *Tools ▸ Monster ▸ Rebuild Body Hit Volumes*): Hips r 0.31, Chest r 0.41, Head r 0.14, upper arms r 0.20 / 0.27, forearms+hands r 0.13 / 0.16, thighs r 0.17, shins r 0.10, feet r 0.11 / 0.13. Root capsule kept. Sword collider untouched.
- `HitDetection`: `OnTriggerStay` retries a rejected contact while the weapon is still inside the monster; one landed hit per swing (re-arms when the hand slows below the gate), clash still only on entry. A swing that clashed cannot score through the retry path, so a parry stays a parry. Applies to both levels.
**Verified:** 98.5% of mesh vertices are now within 8 cm of a body collider (hammer head is 19–27 cm). Real contacts at 3.5 m/s, both hammers: chest, head, L forearm, R upper arm, L hand, L thigh, R shin, L foot, belly, R thigh, L upper arm — **11/11 dealt damage (21–29)**. Sword blade → `Clash`, 0 damage. Entering the chest at 0.6 m/s → 0 damage (too slow), then swinging at 3.5 m/s without leaving → hit 26.8.

### A1. Player heavy breathing — `Player/PlayerExertionAudio.cs` on `OVRCameraRig` (Level 1 + Level 2)
Clip `Audio/SFX/Player/ribhavagrawal-heavy-breathing-294195.mp3` (8.15 s, mono) on a dedicated looping 2D `AudioSource` (child `PlayerBreathingAudio`).
Logic: every swing attempt counts, hit or miss (hand speed from `PlayerCombat` ≥ `swingTriggerSpeed`, re-armed below `swingRearmSpeed`, each hand separately). Swings are kept for `exertionWindow` seconds. Breathing starts when `swingsToStart` are inside the window and continues while at least `swingsToSustain` are. While exerting, `sustainedSeconds` grows and volume goes `startVolume → maxVolume` over `rampSeconds`. No swing for `restSeconds` → volume fades to 0 and `sustainedSeconds` drops by `recoveryRate`/s. Battle not active (pre-battle, victory, defeat) → everything reset and faded out in `battleEndFadeSeconds`.
| Inspector value | Now |
|---|---|
| startVolume / maxVolume | 0.10 / 0.30 (clip peak 0.98 → 0.29 at the cap; hit SFX play at 0.8–1.0, music 0.03–0.08) |
| swingTriggerSpeed / swingRearmSpeed | 2.0 / 1.2 m/s |
| exertionWindow | 12 s |
| swingsToStart / swingsToSustain | 8 / 5 |
| rampSeconds | 25 s |
| restSeconds / recoveryRate | 3 s / 2.5 |
| fadeInSeconds / fadeDownSeconds / battleEndFadeSeconds | 2 / 4 / 0.3 s |
**Verified (L2, sword swung at 1.2 swings/s):** silent until the 8th swing (≈7 s) → 0.12 at 9 s → 0.20 at 19 s → cap 0.30 at 33 s and held. Stop swinging: held 3 s, then 0.30 → 0 in 4 s, source stopped. Resume: back on at the 8th swing. Victory: 0.265 → 0 in 0.28 s and stays 0.

### A2. Level 2 — normal hit vs body block vs parry
| Case | Found | Now (measured, 3.5 m/s) |
|---|---|---|
| Body, monster not defending | full damage, sword-slice sound, flinch | unchanged: `Hit`, 15.7 dmg, flinch |
| Body, monster defending | damage ×0.3, monster's block clang + white flash, **but the normal sword-slice hit sound also played on top** | `Blocked`, 15.7 → 4.7 (×0.30), **no slice sound**: only the block clang (`L2_Defend_Block`) + white flash on the blue defend tint, no flinch |
| Monster weapon (axe head / haft), defending or not | 0 damage, `swords-collide` | unchanged: `Clash`, 0 dmg |
`HitDetection.blockedHitSound` (optional, empty) lets a weapon play its own blocked sound instead. `HitDetection.LastResult` (`TooSlow / Clash / Blocked / Hit`) exposes the outcome for checks.

### A3. Level 2 attacks — 10 in the table (was 7)
Clips under `Animations/Monster/` and their use:
| Clip | Rig | Used by |
|---|---|---|
| Standing Melee Attack Horizontal | Humanoid | L2 Horizontal Sweep |
| Great Sword Slash | Humanoid | L2 Diagonal Slash, L1 Attack |
| Monster_SwordAttack (= One Hand Sword Combo, 3 swings) | Humanoid | L2 Backhand Cut (swing 1), **Overhead Chop (swing 2)**, **Lunging Cleave (swing 3)** |
| Standing Melee Combo Attack Ver. 3 | Humanoid | L2 Twin Strike |
| Standing Melee Combo Attack Ver. 1 | Humanoid | **L2 Reaping Combo** (was unused by the L2 controller) |
| Great Sword High Spin Attack | Humanoid | L2 Whirlwind Spin, L1 AttackHeavy |
| Standing Melee Attack Downward | Humanoid | L2 Executioner Slam |
| Standing Melee Run Jump Attack | Humanoid | L2 Leaping Cleave |
| Mutant Jump Attack | Humanoid | L1 AttackSpecial only (jump slam, same idea as Leaping Cleave — not added) |
| Great Sword Impact / Monster_HitReaction / Monster_SwordIdle / Two Handed Sword Death | Humanoid | Defend / Hit / Idle / Death |
| Great Sword Idle, Hit Reaction, One Hand Sword Combo | Generic | unused duplicates |

**Spin.** The only spin clip is Great Sword High Spin, already in the table as Whirlwind Spin. Found: the Animator has root motion off and the clip's turn was root motion, so **the body never turned** (hips yaw stayed within −11…39°) and the damage frame was 0.07 s early. Fixed with a second clip on the same FBX, `GreatSwordSpin_RotationBaked` (root rotation baked into the pose; the original `mixamo.com` clip is untouched, so Level 1 looks the same): the body now turns a full 360°, damage frame moved 0.97 → 1.04 s (axe 1.31 m in front). Weight 7→18 changed to **4→30**: measured share 9% at full HP → 18% at 15% HP. A second, different spin needs a new clip.

**New attacks.**
| Attack | Category | Clip / state | Speed · entry | Wind-up → hits | Dmg ×16.8 | Arc | Weight full→0 HP | Rules |
|---|---|---|---|---|---|---|---|---|
| Reaping Combo | Combo (2 hits) | Combo Ver. 1 (`MeleeComboV1_RotationBaked`) / `Atk_ReapCombo` | 1.15 · 1.03 s | 0.835 s, then +0.83 s | 0.8 = 13.4 ×2 | 80° then 45° | 8→16 | 5 s cooldown |
| Lunging Cleave | Standard | Monster_SwordAttack swing 3 / `Atk_LungeCleave` | 1.0 · 2.40 s | 0.63 s | 1.1 = 18.5 | 35° | 11→6 | |
| Overhead Chop | Standard | Monster_SwordAttack swing 2 / `Atk_OverheadChop` | 0.95 · 1.45 s | 0.61 s | 1.0 = 16.8 | 40° | 9→5 | |
Reaping Combo is a spinning low sweep (body turns ~230° through the hit) followed by a rising cut; each hit checks range, its own arc and the player's block separately (`AttackDefinition.followUpArcs` is new). Hit timing: axe-head position sampled per frame in Play mode, damage frame placed where the head is furthest forward — measured at the damage frames: 1.27 m / 0.98 m (Reaping), 1.24 m (Lunge), 0.90 m (Chop).
Other weights: Sweep 24→14, Diagonal 20→11, Backhand 14→8 (standard total still 78→44), Executioner Slam 7→14. Twin Strike, Leaping Cleave unchanged.

**Animator (`Monster_L2_Axe.controller`), checked for all 10:** each attack has its own trigger and an `Idle → state` transition (no exit time), `state → Idle` on the `Idle` trigger plus an exit-time fallback; **Any State → Death only**. Forced run of all 10: every one went `Idle → own state → Idle` and landed its hits (2/2 for both combos). Natural 75 s fight with the AI choosing (HP stepped 100% → 55% → 30%): 28 attacks, each reached its own state (0 mismatches), 8 of the 10 attacks seen, 13 defends, longest time away from Idle 2.2 s (nothing stuck).
**Selection (3 000 picks per HP step, real `ChooseFromTable`):**
| Monster HP | Standard | Combo | Heavy + slam + special | Whirlwind | Reaping Combo |
|---|---|---|---|---|---|
| 100% | 60% | 16% | 24% | 9% | 6% |
| 60% | 52% | 19% | 29% | 17% | 8% |
| 35% | 46% | 20% | 34% | 15% | 9% |
| 15% | 42% | 21% | 37% | 18% | 10% |
0 immediate repeats; first pick after crossing 40% HP is still Leaping Cleave.

### Code / asset changes
| File | Change |
|---|---|
| `Combat/HitDetection.cs` | `OnTriggerStay` retry, one hit per swing, body-block feedback, `blockedHitSound`, `LastResult` / `LastDamage` |
| `Monster/MonsterAI.cs` | `AttackDefinition.followUpArcs` |
| `Player/PlayerExertionAudio.cs` | **New** |
| `Editor/MonsterHitVolumeBuilder.cs` | **New** (editor only) |
| `Prefabs/Monsters/Monster_Easy_HipHop.prefab` | 13 `HitVol_*` capsules |
| `Prefabs/Monsters/Monster_Medium_VampireLust.prefab` | attack table 7 → 10, weights, Whirlwind wind-up |
| `Animations/Monster/Level2/Monster_L2_Axe.controller` | 3 states + triggers, Whirlwind uses the rotation-baked clip |
| `Great Sword High Spin Attack.fbx`, `Standing Melee Combo Attack Ver. 1.fbx` (import settings) | extra `*_RotationBaked` clip each |
| `Scenes/Level1_Easy`, `Scenes/Level2_Medium` | `PlayerExertionAudio` + `PlayerBreathingAudio` on `OVRCameraRig` |

### Headset checks
1. Level 1: hit the monster on the chest, an arm and a leg (and the head) — each should take HP off. Hit its sword — clash, no damage.
2. Either level: swing steadily for ~10 s — breathing comes in quietly, gets heavier over the next ~25 s, fades within ~7 s of stopping, cuts at victory/defeat. Tune `maxVolume` on `OVRCameraRig ▸ PlayerExertionAudio` if it is too loud/quiet against the hit sounds.
3. Level 2: hit the body normally (slice, full damage), hit it while it is tinted blue (clang only, small damage), hit the axe (clash, 0).
4. Level 2: watch for Reaping Combo (spinning sweep + rising cut), Lunging Cleave, Overhead Chop, and the Whirlwind now turning a full circle. Judge whether the spin/turns read well and whether the wind-ups give enough time.

---

## Pre-demo pass — 2026-09-29 (player naming, backend link, Level 2 re-verification)

Verified live through Unity MCP (Play mode, real UI raycasts from `UIRayOrigin`, real `HitDetection.TryHit`) and against the production backend.

**Player naming (new).** `UI/PlayerNamePicker.cs` on `BattleSystems` (both levels; `BattleManager` adds one at runtime if missing). On scene load it covers `CombatCanvas` with "WHO'S PLAYING?" and four laser-clickable preset buttons (Player 1/2/3, Guest; edit `presetNames` in the Inspector), cloned from `StartBattleUIButton` so they use the same raycaster. Start is hidden and `BattleManager.StartBattle` refuses to run (laser, B button, 3D button) until a name is chosen. Default is auto-picked after 10 s of running time (paused time doesn't count; the countdown restarts when the headset is put back on). After a pick the Start button reads "START BATTLE / as <name>" with a "Change name" button below it. Retry keeps the last name as the default (per app run). `SessionReporter.BeginSession` locks the name for the whole battle (`sessionPlayerName`); `SetPlayerName` is refused mid-fight.

**Backend link.** Single URL: `SessionReporter.baseUrl = https://s-t-r-i-k-e-f-i-t.onrender.com` (`RoundManager.backendUrl/playerName` removed). `android.permission.INTERNET` explicit in the manifest + Player Settings Internet Access = Require. Production test: session `Player 2 / Medium` live → 497 events all tagged `Player 2` → finished/defeat; then deleted. Offline test (baseUrl → unroutable IP): fight continued to Victory, requests timed out after 8 s and were dropped with 2 warnings, 0 errors.

**Level 2 re-verified:** all 7 table attacks reach their own Animator state from Idle and return (Leaping Cleave = first pick after crossing 40% HP); defend every 5.5 s for 1.0 s, 100 dmg → 30, tint (0.45,0.65,1) on/off; all 12 `HitVol_*` + root capsule take damage from both swords, axe head/haft = clash 0 dmg; gate 1.1 m/s → 0, 1.3 → hit; Victory + Defeat panels; 1 camera / 1 listener / 0 monsters at load / 0 missing scripts; eye render shows textured vampire and coloured swords.
**Changed:** `StartBattleButton3D` (legacy touch-start disc behind the player, not in L1) deactivated; `preloadAudioData` on for the 20 SFX clips (was off → synchronous decode on first play; BGM untouched).

**Dashboard:** Live Arena cards have large labelled PLAYER (blue) / MONSTER (red) bars with %; repeated "Too slow"/"Weapon clash" rows collapse to ×N; player names shown in mixed feeds; player page shows "Playing now" chip and no longer shows the previous player's stats while switching. Top bar no longer has the "Connected" pill or "Landing page" link (it shows plain status text only while connecting/offline); the sidebar's Connection card is now two ordinary menu items, "Connection" (status dot) and "Back to site". Connection page "Connect the headset" steps describe the hosted URL, the in-game name picker and offline behaviour (the LAN URL only appears when the backend runs locally).

---

## Level 2 — FINAL PRE-DEMO CHECK (2026-09-27, pass 5; supersedes all Level 2 sections below where they conflict)

Every item re-inspected live via Unity MCP and exercised in Play mode (real UI raycast from `UIRayOrigin`, real physics trigger contacts, real `HitDetection.TryHit`, forced death chains).

### 1. Monster body hit detection — FIXED
- **Found:** one static root capsule (c (0,1,0), r 0.42, h 2.0) was the only body collider — it does not follow the animation, so swinging arms (hands reach ±1 m), spread legs/feet and lunges were outside it, and it counted hits on air 0.35–0.5 m above the hunched head. (Level 1's Hip Hop monster has the same single capsule; unchanged.)
- **Fix (prefab `Monster_Medium_VampireLust`):** 12 bone-following `HitVol_*` capsules (non-trigger, Untagged, parented to the bones), radii from the skinned mesh (75th-percentile vertex distance per bone): Torso r 0.32, Head r 0.16, upper arms r 0.093, forearms r 0.104, thighs r 0.114/0.119, shins r 0.141/0.140, feet r 0.095. Root capsule trimmed to c (0,0.9,0) r 0.38 h 1.8 (kept: `BattleManager.EnsureMonsterComponents` adds a default one if the root has none). Axe `MonsterAxe` box + `HaftCollider` unchanged (trigger, `MonsterWeapon`).
- **Verified (real physics):** sword box overlapped with each part → damage registered for Torso, Head, L upper arm, R forearm, L thigh, R shin, L foot (both swords used). Axe head and haft → 0 damage (clash path). Monster killed purely by these hits → Victory panel.

### 2. Sword colliders — VERIFIED, right widened
| Sword | Collider (trigger Box, root space) | Visible blade | Blade vertices inside |
|---|---|---|---|
| Right `animated_sword` | c (0,0,0.41) s (0.07, **0.16**, 0.76) — was 0.14 wide | z 0.28–0.78 (0.49 m), 0.15 wide | 96.4% → 100% (edge verts were outside) |
| Left `SwordZbrush` | c (0,−0.115,0.531) s (0.05,0.16,0.508) | z 0.28–0.79 (0.51 m), 0.16 wide | 99.9% |

### 3. Difficulty Level 1 vs Level 2 (live numbers)
| | Level 1 (Easy) | Level 2 (Medium) |
|---|---|---|
| Monster max HP | 500 | **800** |
| Telegraph (trigger → hit) | Slash 0.90 s, Heavy 1.25 s, Special 1.50 s | Standard 0.57–0.61 s, Whirlwind 0.97 s, Leap 1.03 s, Slam 1.48 s (idle-only) |
| Attack cooldown (start→start) | 3.2 s | **2.2 s** (→ 1.87 s at low HP) |
| Damage per hit | 8.8 / 10.6 / 13.2 | **15.1–33.6** (standard 16.8) |
| Distinct attacks | 3 (+ defend) | **7** (+ defend) |
| Defend | every 8 s for 1.5 s, −70% | every 5.5 s for 1.0 s, −70% |
| Player HP | 500 | 800 (offsets part of the extra damage) |
| Player dmg per hit @ 4 m/s | 22.7 (hammer ×1.15) | ~16.8 (sword) |
| Hits needed to win (no tap bonus) | ~22 | ~48 |
| Time for monster to kill player if every attack lands | ~163 s | ~100 s |
Assessment: harder on every metric. Mitigation: L2 attacks need range + arc (side-steppable); L1 lands whenever in range. **Changed:** swing gate 1.0 → **1.2 m/s** (`PlayerCombat.minSwingVelocity` + both `HitDetection.minSwingSpeed`) — L2 was accepting slower swings than L1, the only easier value.

### 4. Swing detection — VERIFIED (feel needs headset)
Gate 1.2 m/s (1.1 → 0 dmg, 1.3 → 11.7/12.4), cooldown 0.28 s per sword (immediate repeat → 0), 5 m/s → 18.8 / 19.5. `maxVelocityDamageMultiplier` 2.2, max speed 8 m/s, angle bonus ≤ +40%, fitness tap bonus +0.35 per landed hit (same rule as L1). No hand/controller meshes are rendered, so there is nothing for a sword to clip through; grips sit on the anchors (see pass 3). **Swing feel can only be judged on the headset.**
- **Editor-only bug found + fixed:** `EditorHandPoseFallback` placed hands relative to the head *position*; `PlayerHealth`'s hit shake moves `CenterEyeAnchor` ±8 cm, so in the editor the swords jolted at up to 10.5 m/s and scored phantom hits while the player was being hit. Now relative to the tracking origin → 12 s of monster attacks: sword speed 0.0, 0 phantom hits. Not possible on Quest (shake target is `CenterEyeAnchor`, hands are not its children; the fallback is compiled out of builds).

### 5. Scene sanity — VERIFIED
Load: 1 enabled camera (`CenterEyeAnchor`), 1 AudioListener, 1 EventSystem, 1 BattleManager, 1 OVRCameraRig, **0 monsters**, 0 missing scripts, no debug/test objects. Start via laser raycast → 1 monster at (0,0,3) rotY 180, swords shown, laser hidden. Victory and Defeat chains fire with sounds + panels. Music `level2gamemusic` 0.03 (matched to L1). Console: 0 errors; warnings only ISDK telemetry + `SessionReporter` → `localhost:4000` (backend not running; on Quest this points at the headset itself). The "unsaved changes" flag seen earlier on the open scene was spurious (in-memory scene diffed identical to disk).

---

## Level 2 — editor view, music, B-to-start, build order (2026-09-27, pass 4)

| Issue | Cause (verified) | Fix |
|---|---|---|
| Game view filled by a red shape / sword after Start | In the editor with no headset/controllers, `OVRCameraRig` leaves `Left/RightHandAnchor` exactly on `CenterEyeAnchor` (0,1.6,0) → both swords rendered 0.33 m from the camera. On Quest the controllers are tracked, so this is editor-only. | New `Player/EditorHandPoseFallback.cs` on `OVRCameraRig` (Level 1 + Level 2): in `LateUpdate`, any hand whose controller is **not tracked** goes to a resting pose (R (0.22,−0.42,0.38) rot (15,8,0), L mirrored) relative to the head. Wrapped in `#if UNITY_EDITOR`, never touches tracked controllers. Verified: swords 0.77–0.87 m from the eye, both visible in the lower half of the view; laser now leaves the right hand and still hits `StartBattleUIButton`. |
| B on right controller should start | not implemented | `BattleManager.Update`: `OVRInput.GetDown(Button.Two, RTouch)` → `StartBattleFromInspector()` while no battle is running and no outcome is shown (both levels). Same path as the laser click. Needs headset to confirm (OVRInput cannot be simulated in the editor). |
| Level 2 music | used L1 `music` | `Assets/Audio/Music/level2gamemusic.mp3` (copied from project root; 230 s) on `BackgroundMusic`: loop, play on awake, 2D, **volume 0.03** = same loudness as L1 `music` at 0.08 (track is 2.55× louder RMS). Both levels' music sits ≈ −44 dBFS — faint in a room; kept matched and quiet by decision (2026-09-27). For louder, matched music: L2 0.2 + L1 0.5. Editor note: with `Run In Background` off the game and its audio pause whenever the Unity window is not focused. Import: **Streaming**, Vorbis q 0.6, load in background (Decompress On Load would hold ~40 MB PCM on Quest). |
| Pre-battle HUD said "HP 100/100 / Enemy 100/100" | scene-authored placeholder text (L1 has its real 500/500) | Set to `HP 800/800` / `Enemy 800/800` |
| Level 2 unreachable in the APK | build opened Level1_Easy; nothing loads Level 2 (Victory → A reloads the same level) | Per request (scenes tested as separate builds): **Level2_Medium is build index 0**, Level1_Easy index 1. Move Level1_Easy back to the top to build Level 1. |

---

## Level 2 player swords — two distinct models (2026-09-27, pass 3; supersedes the `PlayerSword_Left` rows below)

Before this pass **both hands used the same `animated_sword` mesh** (left only had a blue-tinted blade material), so the two swords looked identical.

| | Right — `PlayerSword` | Left — `PlayerSword_Left` |
|---|---|---|
| Source model | `Models/Weapons/Player/animated_sword.FBX.fbx` (= `_SourceDownloads` copy, byte-identical) | `Models/Weapons/Player/SwordZbrush.fbx` (byte-identical) |
| Import | Rig **None** (rigid MeshRenderer, not skinned; leftover 3ds Max bones unused) — correct for a prop | Rig **None** (rigid; unused armature) |
| What it is | Spiked-guard sword | **Zombie hand clutching a jagged blade.** The blade has no hilt of its own (it tapers to a spike at both ends, verified by rendering it without the hand); the only handle is the zombie forearm between the pommel ring and the red cuff. The player grips that forearm. |
| Mesh used | `L2_PlayerSword_Split.asset` (3 submeshes) | **New** `L2_PlayerSwordZbrush_Split.asset`: 19 connected shells grouped into 5 submeshes |
| Materials (URP/Lit — no texture files exist on disk for either model; FBX materials are plain grey 0.8/0.59) | `M_L2_SwordSteel` blade, `M_L2_SwordHilt`, `M_L2_SwordGrip` | **New:** `M_L2_ZSword_Blade` (grey-green steel, faint green emission), `M_L2_ZSword_Metal` (brass pommel/collar/9 rivets), `M_L2_ZSword_Bone` (zombie hand), `M_L2_ZSword_Wrap` (crimson cuff), `M_L2_ZSword_Grip` (dark leather forearm) |
| Root (on hand anchor) | lpos (0,0,0), rot (320,0,0), scale 1 | lpos (0,0,0), rot (320,0,0), scale 1 |
| `Visual` | lpos (0, 0, 0.306), rot (0, 0, 90), scale 0.0009 | lpos (−0.0010, −0.0055, 0.0383), rot (272.75, 270, 270), scale 0.009 |
| Grip result | grip axis = L1 hammer axis (0, 0.643, 0.766); grip centre on anchor | handle (mesh +Y) → same L1 axis; grip centre (mesh −0.14, −4.25, 0.60) on anchor; blade offset (~11 cm, mesh −Z) → forward/down so the edge leads on chops |
| Length | 0.90 m | 0.90 m (hand/guard 0.24 m wide) |
| Trigger collider (striking part only) | c (0, 0, 0.41) s (0.07, 0.14, 0.76) | c (0, −0.115, 0.531) s (0.05, 0.16, 0.508) — blade above the zombie hand |
| HitDetection | hand Right; base 11, cooldown 0.28 s, gate 1.2 m/s (was 1.0 — corrected in the final pre-demo pass) | hand Left; same values |

Verified in Play mode: swords inactive at load (`PlayerCombat.Awake` + saved inactive), laser `LineRenderer` visible and the real `UIRayOrigin` raycast hits `StartBattleUIButton`; after the click both swords active and `LaserPointer` inactive (`BattleManager.SetUiLaserPointersActive(false)`), so the laser and sword are never visible together. Real `HitDetection.TryHit` into the same monster: Right 18.94 dmg @ 5 m/s, Left 19.29 dmg @ 5 m/s; immediate repeat 0 (cooldown); 0.5 m/s → 0 (gate). Eye-view render: both swords coloured, no magenta. Console 0 errors (warnings: ISDK telemetry, `SessionReporter` localhost:4000). `M_L2_SwordSteel_Offhand` is now unused (kept).

HitDetection numbers vs Level 1 hammers: same script and logic. L2 values (base 11, cooldown 0.28) were tuned for lighter swords in the rebuild pass. ⚠️ This section originally said the gate was 1.2 m/s in both levels — wrong: L2 `PlayerCombat.minSwingVelocity` was 1.0. Now 1.2 (see final pass).

---

## Level 2 ↔ Level 1 alignment pass — 2026-09-27 (pass 2; supersedes §3 "Monster presence" below)

Method: both scenes inspected live via Unity MCP, then the same Play-mode trace run on each: scene load → real `EventSystem.RaycastAll` from `UIRayOrigin` to `StartBattleUIButton` + click dispatch → spawn check → monster killed through `MonsterController.OnTakeDamage` → player killed through `PlayerHealth.TakeDamage` → Retry reload. Level 1 was only read, not modified.

| Item | Level 1 reference (live) | Level 2 now (live) | Changed? |
|---|---|---|---|
| Weapon parent | `PlayerHammer` → `RightHandAnchor`, `PlayerHammer_Left` → `LeftHandAnchor` | `PlayerSword` → `RightHandAnchor`, `PlayerSword_Left` → `LeftHandAnchor` | no |
| Weapon transform | lpos (∓0.0013, 0.1298, 0.1734), rot (320, 0, 90) R / (320, 0, 270) L, scale 0.085 (mesh pivot is off-grip, so the offset lives on the root) | root lpos (0,0,0), rot (320,0,0), scale 1; `Visual` child lpos (0,0,0.306), rot (0,0,90), scale 0.0009 (offset lives on the child) | no — different pivots, same result ↓ |
| Grip result (measured from mesh verts in anchor space) | long axis (0, 0.643, 0.766) = 40° up from controller-forward; thin handle (r 1–3 cm) spans −0.08…+0.11 m along the axis through the anchor; head 0.11–0.35 m out; total 0.58 m | same axis (0, 0.643, 0.766); leather grip spans −0.067…+0.048 m, centroid (0, 0, 0.001) = on the anchor; blade 0.28–0.78 m; pommel −0.12 m; total 0.90 m (one-handed arming-sword length) | no — already identical alignment |
| Hide until Start | hammers saved **inactive**; `PlayerCombat.Awake` hides all weapon slots; `BattleManager.StartBattle → PlayerCombat.SetLoadout → ApplyWeaponVisuals` shows them once `IsBattleActive` | swords were saved **active** (hidden only by `Awake`) → now saved **inactive**; same generic `PlayerCombat` path, no new code | ✅ |
| Monster before Start | **0** monsters; `BattleManager.sceneMonster = null` | was a visible scene instance adopted via `sceneMonster` → **deleted**, `sceneMonster = null`, **0** monsters at load (instance had no overrides except name/active; only scene ref was `sceneMonster`) | ✅ |
| Monster at Start | `LevelManager.monsterPrefabForThisScene` instantiated at `MonsterSpawnPoint` (0,0,3) rotY 180 | `Monster_Medium_VampireLust(Clone)` spawned at **(0.000, 0.000, 3.000) rotY 180**, avatar valid, 99 bones, head 1.52 m, `Monster_L2_Axe` controller, AI + `TwoHandedWeaponIK` on, axe on `mixamorig:RightHand`; walked in, stopped at 1.54 m, Backhand 15.12 / Diagonal 16.80 dmg landed | ✅ |
| Monster death | `MonsterHealth.OnDeath` → `MonsterController.OnDeath` (`dramatic-death-collapse`, Death anim, colliders + AI off) → `BattleManager.VictorySequence` (GameState Victory) → `ShowVictory` after 0.75 s | identical chain and clip (single play) → panel "VICTORY! / Press A to Play Again / XP Earned: 320" | clip kept — no vampire vocal asset exists |
| Player death | `PlayerHealth.OnDeath` → `male-death-scream` on rig AudioSource → `NotifyPlayerDefeated` (GameState Defeat) → "DEFEAT … Press A to Retry" | identical | no |
| Victory hint | `VTitle` = `VICTORY!\n<size=24>Press A to Play Again</size>`, vOverflow Overflow | was `VICTORY!` only → copied from L1 | ✅ |
| BGM on death | `music` 0.08 keeps playing (no script stops/ducks it) | same | no |
| UI laser | 1 `LaserPointer` | had a 2nd, inactive, unreferenced `LaserPointer` under `RightHandAnchor` → deleted | ✅ |
| Retry | 0 monsters, weapons hidden, laser on | 0 monsters, swords hidden, laser on, 800 HP | no |
| Console | 0 errors; 3 warnings (ISDK telemetry native lib, 2× `SessionReporter` → `localhost:4000` not running) | same 0 errors / same 3 warnings | — |

**Level 1 finding (not changed):** `Monster_Easy_HipHop.prefab` has the death clip on **both** `MonsterHealth.deathSound` and `MonsterController.deathSound`, same AudioSource, so it fires twice in the same frame (reads as one sound at ~2× volume). Level 2 deliberately uses only the `MonsterController` path. Clear `MonsterHealth.deathSound` on the L1 prefab if you want them identical.

---

## Level 2: Medium — REBUILT & AUDITED 2026-09-27 (supersedes every earlier Level 2 status in this file except the alignment pass above)

Method: every item was inspected on the live `Level2_Medium` scene / `Monster_Medium_VampireLust.prefab` via Unity MCP and exercised in Play mode: real `EventSystem.RaycastAll` from `UIRayOrigin`, real `HitDetection.TryHit`, Animator state + clip time sampled at every damage frame, 4 000-pick Monte Carlo through the real `MonsterAI.ChooseFromTable`, forced death chains, scene reload. Editor ran throttled (~10 fps) in the background, so timing-critical checks were repeated with `Time.captureDeltaTime = 1/72` (headset rate). Evidence renders are in the session scratchpad, not the project.

### Corrections to earlier Level 2 claims (they were wrong)

| Earlier claim | What was actually true (inspected) | Fixed? |
|---|---|---|
| "No sword/monster textures exist; FBX materials had null albedo" | `Vampire A Lusth.fbx` **embeds 5 PNGs** (diffuse, diffuse_transparent, emission, normal, specular). Prefab used flat-colour `M_L2_VampireSkin/Cloth` → pink clay. Raw FBX materials render **magenta** in URP. | ✅ Extracted to `Models/Monsters/Level2/Textures/`, wired into URP/Lit |
| Player swords "fixed" with `T_crystal_Sword.png` | `SwordZbrush` mesh is a **zombie hand clutching a sword**; crystal texture on foreign UVs = pink marbled claw in the player's face | ✅ Replaced by `animated_sword` blade (see §2) |
| "Single axe on right hand, two-handed read" | All 5 `Standing Melee *` clips are **one-handed** (hands 1.1–1.9 m apart). `MonsterAxe_Left` on the left hand was the **Solitude_Ember staff** mesh. | ✅ Rebuilt axe + left-hand IK (§2) |
| "UI refs wired" (`CombatUIController`) | **All 21 refs null** → HP bars/texts never updated, Victory/Defeat panels could never show | ✅ Wired from Level 1 mapping |
| "Start button → StartBattleFromInspector" | `onClick` had **0 listeners**; `OVRInputModule.rayTransform` **null**; canvas faced away (dot −1, mirrored text); `HitFlash` drawn above the button | ✅ All four fixed (same fixes as Level 1) |
| Level 2 in build order | `Level2_Medium` **disabled** in Build Settings (not in the APK) | ✅ Enabled |
| Monster "placed at spawn point" | Scene copy **inactive**; spawn point rot Y=0 | ✅ Spawn point rot Y=180. *(Pass 2: scene copy removed — spawned from prefab on Start, as Level 1)* |
| Fight ends by death | `timerExpireMeansSurvivalWin = true` → **VICTORY after 40 s with monster at 800/800** (reproduced) | ✅ Off (same fix as Level 1) |
| "Solitude_Ember = fire VFX" | It is a **staff/sceptre mesh** with embedded colour/emission textures, not a VFX | Real particle fire built instead (§6) |
| Animator attack variety | Every attack reachable from **Any State**, `Idle→Attack` duplicated by Any State, `Any→Hit`/`Any→Idle` could cut any swing — the Level 1 bug class | ✅ New controller (§4) |

### 1. Materials — what was wrong / fixed

| Asset | Found | Fix |
|---|---|---|
| Vampire (monster) | Flat URP/Lit colours, no textures; FBX-embedded mats magenta | `_BaseMap`=Vampire_diffuse (sub 0) / Vampire_diffuse_transparent (sub 1, alpha-clip 0.5), `_EmissionMap`=Vampire_emission (glowing eyes). **Normal map deliberately OFF:** URP/Lit `_NORMALMAP` + GPU skinning renders magenta on this Mac's Metal editor (3/3 reproductions; static/baked mesh fine). Alpha-clip + double-sided on the skinned mesh also magenta → cloth is single-sided. Can't prove Quest/Vulkan is safe, so kept off. |
| Player swords | Zombie-hand mesh + crystal texture on wrong UVs | Mesh `animated_sword.FBX` (`sword_low`). Its texture `sword_dif.tga` **does not exist anywhere on disk**. Mesh split by geometry into 3 submeshes (`L2_PlayerSword_Split.asset`): polished steel blade (`M_L2_SwordSteel`, off-hand blue-steel `M_L2_SwordSteel_Offhand`), dark-iron horned guard/pommel with faint red glow (`M_L2_SwordHilt`), leather grip (`M_L2_SwordGrip`). |
| Monster axe | Flat grey `M_L2_AxeIron`, fire cards disabled | Original `Axe_texture.png`, `fire_O.jpg`, `transparency.jpg` **do not exist on disk**. Split into 4 submeshes (`L2_MonsterAxe_Split.asset`): bright blade edges, dark-iron head/socket, wood haft, leather wrap. Dead fire cards removed. |
| Metal readability | Near-black flat ambient, no reflections | Baked `ArenaReflectionProbe` + custom reflection (§7) |

**Needs from you for full fidelity:** the original `Axe_texture.png` and `sword_dif.tga` (the FBXs reference them by name). Drop them in and they can replace the region materials.

### 2. Weapon grips (Level 1 method: correct parent + local offset, weapon axis through the grip)

Level 1 reference measured live: hammer long axis in hand-anchor space = (0, 0.64, 0.77) — pitched 40° up from controller-forward, passing through the anchor origin.

| Weapon | Parent | Local position | Local rotation | Scale | Verified |
|---|---|---|---|---|---|
| `PlayerSword` | `RightHandAnchor` | (0, 0, 0) | (320, 0, 0) → axis (0, 0.64, 0.77) = L1 hammer | 1 | grip centre at anchor (0.000); tip 0.78 m out, pommel 0.12 m behind; render in hand pose |
| └ `Visual` | — | (0, 0, 0.306) | (0, 0, 90) blade width vertical (edge leads on chops) | 0.0009 (0.90 m sword) | |
| `PlayerSword_Left` | `LeftHandAnchor` | same | same | same | same |
| Sword collider | BoxCollider trigger c=(0,0,0.41) s=(0.07,0.14,0.76) | | | | |
| `MonsterAxe` | `mixamorig:RightHand` | (−0.0008, 0.0986, 0.0003) = right fist centre | (320.71, 341.23, 69.47) | 1 | haft axis = measured LH→RH line (−0.815, 0.271, −0.512); big blade faces monster-forward |
| └ `Visual` | — | (0.0364, 0.156, −0.0104) | (0,0,0) | 0.013 (1.30 m axe) | |
| └ `LeftHandGrip` (IK target) | — | (−0.0826, −0.2444, 0.0077) | (340.5, 224.0, 59.3) | | from 41 two-handed frames, spread 3.8 cm |
| Axe clash colliders | head Box c=(0.036,0.514,−0.010) s=(0.936,0.585,0.234); `HaftCollider` capsule r 0.06 h 0.715 — both tag `MonsterWeapon` | | | | |

Grip line measured by sampling 52 two-handed frames (Great Sword Idle/Slash/Impact/High Spin): fist separation 0.192 m.

**`TwoHandedWeaponIK`** (new) keeps the left hand on the haft using the right-hand IK goal of the *current* frame (no lag). Measured: idle **0.0 cm** from grip; reachable swing frames 0–1.6 cm. **Limitation (physical):** in the one-handed Standing Melee swings the right hand goes 1.1–1.4 m from the left shoulder (arm = 0.75 m) — unreachable — so the IK fades out beyond 0.95–1.2× arm length and regrips on recovery. Truly two-handed swings throughout need two-handed axe clips from Mixamo.

### 3. Monster presence
⚠️ Superseded by the alignment pass at the top: there is **no monster in the scene**; `BattleManager` instantiates `Monster_Medium_VampireLust.prefab` at `MonsterSpawnPoint` (0,0,3) rot Y=180 when Start is clicked (Level 1 pattern). Runtime: 0 before Start, 1 after; 0 after retry. `BattleManager.sceneMonster` still exists as an optional field but is unused in both levels.

### 4. Attack table (controller `Animations/Monster/Level2/Monster_L2_Axe.controller`)

Every attack: **Idle → state on its own trigger** (no exit time, fixed 0.1 s blend, entry offset), back to Idle on `Idle` trigger or exit-time 0.98 fallback. **Any State is used only for Death.** Hit only from Idle. IK Pass on. Speeds/offsets computed from the measured impact frame of each clip (axe-head peak speed).

| # | Attack | Clip | Category | Trigger / state | Speed · offset | Wind-up (trigger→hit) | Dmg ×16.8 | Arc | Weight full→0 HP | Rules |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Horizontal Sweep | Standing Melee Attack Horizontal | Standard | `Atk_Sweep` | 1.15 · 0.30 s | 0.61 s | 1.0 = 16.8 | 70° | 30→18 | |
| 2 | Diagonal Slash | Great Sword Slash | Standard | `Atk_Diagonal` | 1.2 · 0.25 s | 0.60 s | 1.0 = 16.8 | 45° | 26→14 | |
| 3 | Backhand Cut | One Hand Sword Combo (1st swing) | Standard | `Atk_Backhand` | 1.1 · 0.45 s | 0.59 s | 0.9 = 15.1 | 45° | 22→12 | |
| 4 | Twin Strike | Standing Melee Combo Attack Ver. 3 | **Combo** (2 hits) | `Atk_TwinStrike` | 1.15 · 0.35 s | 0.57 s + 0.67 s | 0.75 = 12.6 ×2 | 50° | 12→16 | |
| 5 | Whirlwind Spin | Great Sword High Spin Attack | Heavy | `Atk_Whirlwind` | 1.1 · 0 | 0.97 s | 1.4 = 23.5 | 180° | 7→18 | 4 s cooldown |
| 6 | Executioner Slam | Standing Melee Attack Downward | **Idle-slam** (heavy) | `Atk_ExecSlam` → `Atk_ExecSlam_Raise` (×0.4, slow raise) → exit-time at apex → `Atk_ExecSlam_Strike` (×1.2) | | 1.475 s | 1.75 = 29.4 | 35° | 5→14 | only after standing still ≥ 0.9 s; 6 s cd; fire VFX |
| 7 | Leaping Cleave | Standing Melee Run Jump Attack | **Special** | `Atk_LeapCleave` | 1.2 · 0.5 s | 1.025 s | 2.0 = 33.6 | 40° | 10→16 | HP ≤ 40% only; forced once on first crossing; 8 s cd; fire VFX |

Base damage = `monsterDamage 14 × 1.2` (Hammer slot = axe). Selection: weighted, no immediate repeat, heavy pity after 4 light cycles, cooldown shrinks to ×0.85 as HP drops.
Pacing: `monsterAttackCooldown 2.2 s` start-to-start (L1 3.2 s); measured cycle **2.2–2.4 s**; standard wind-up ~0.6 s (L1 slash 0.9 s).

**Verified at runtime:**
- Natural 40 s trace: every attack reached its own state and returned to Idle; no stray transitions.
- Damage-frame alignment at 72 fps: Sweep clip 0.99 s (impact 1.00), Slam 0.88 (0.87), Leap 1.72 (1.73) — within one frame.
- Monte Carlo (1 000 picks each): big hits (Heavy+Idle-slam+Special) **24.8% → 29.8% → 36.2% → 39.9%** at HP 100/60/35/15%; standard 64.8% → 45.5%; 0 immediate repeats; first pick after crossing 40% = Leaping Cleave; Idle-slam never picked while walking (0/300).
- Distance: MonsterAI `useHeadTarget` = horizontal distance to `CenterEyeAnchor` (L1 measures to the rig root, which does not move when the player steps). Monster stops at 1.48–1.55 m; hit needs ≤ 1.78 m **and** inside the arc → stepping back ~0.25 m or sidestepping out of the arc dodges.

### 5. Defend
`Great Sword Impact` (two-handed brace, closest fit — **a dedicated two-handed block clip would look better**) at speed 0.67 → pose lasts 1.0 s. Cooldown **5.5 s**, duration **1.0 s** (L1 8 s / 1.5 s), **70% reduction** (`defendDamageMultiplier 0.3`, same as L1). Visual cue: steel-blue body tint (0.45, 0.65, 1.0) via the same MaterialPropertyBlock method as the L1 telegraph flash, white flash on each blocked hit. Runtime: Idle→Defend→Idle every ~5.6 s; 100 dmg → 30 during window, 100 after; tint on/off verified.

### 6. Audio + VFX (all assignments read from live objects; trigger paths exercised)

| Event | Clip | Trigger path |
|---|---|---|
| Standard/combo wind-up | `dragon-studio-sword-unsheathing` (0.45) | `TableAttackRoutine` start |
| Standard/combo swing | **`L2_Axe_Whoosh_Light`** (new, 0.30 s bright) | 0.12 s before each hit frame |
| Heavy wind-up | **`L2_Heavy_Growl`** (new) | Whirlwind |
| Heavy/slam/special swing | **`L2_Axe_Whoosh_Heavy`** (new, low + sub) | |
| Idle-slam wind-up | **`L2_Slam_Windup`** (growl + fire ignite) | |
| Special wind-up | **`L2_Special_FireRoar`** | |
| Slam/special impact | **`L2_Slam_Impact`** (boom + debris) | on the hit frame, hit or miss |
| Fire VFX | `AxeFireVFX` particles (world-space, additive `M_L2_FireAdditive`) + `AxeFireLight` 2.5 on the axe head; `SlamImpactVFX` ground burst (45+20) | Slam + Leap: ON at trigger, OFF + burst at impact (verified) |
| Defend brace / block | **`L2_Defend_Brace`** / **`L2_Defend_Block`** (metal clang, distinct from flesh hits) | `DefendRoutine` / `MonsterController.OnTakeDamage` while defending |
| Player sword swing | **`L2_PlayerSwing_R`** / **`L2_PlayerSwing_L`** via new `WeaponSwingAudio` (fires ≥ 2.2 m/s, re-arms < 1.2 m/s) | per hand |
| Player sword hit | `sword-slice`; ≥ 4.6 m/s `impactful-damage` (not L1's hammer smash) | `HitDetection.TryHit` — measured 18.94 dmg @ 5 m/s; 0 dmg @ 0.5 m/s |
| Clash (sword vs axe head or haft) | `swords-collide` | `TryHit` on `MonsterWeapon` → **0 dmg** (verified both colliders) |
| Player hit / hurt / death | `damage-40114` / `male_hurt7` / `male-death-scream` | `PlayerHealth` (same as L1) |
| Monster death | `dramatic-death-collapse` (own AudioSource, separate from attack SFX) | `MonsterController.OnDeath` |
| BGM | `music` loop **0.08** (= L1) | playing at scene start |

New clips are synthesized (numpy) in `Assets/Audio/SFX/Level2/`; **no scream/roar vocal asset exists in the project** — flag if you want a real monster vocal. Solitude_Ember is not used.

### 7. Environment / sky lighting
Skybox **AllSky Night MoonBurst (Equirect)** (arena has no ceiling). Ambient Trilight sky (0.20,0.25,0.36) / equator (0.16,0.15,0.17) / ground (0.07,0.06,0.06) — deterministic in builds. Fog exp² 0.022, blue-grey. `Directional Light` → **Moonlight** 0.55, cool blue, soft shadows. Added `ArenaTorch_Left/Right` (warm 2.6, range 6.5 at z 1.4 — the old lamps are at z≈9) and violet `MonsterRimLight` behind the vampire. Point-light shadows off (Quest cost); 2 duplicate "Point Light" objects disabled. `ArenaReflectionProbe` baked 128 px → `Scenes/Level2_Medium/ArenaReflectionProbe.exr`, used as custom reflection.

### 8. Scene / flow fixes
Canvas (0, 2.2, 1.3) rot (335,0,0) scale 0.0014 (L1 values); HitFlash sibling 0; Start `onClick` → `StartBattleFromInspector`; `OVRInputModule.rayTransform` + `OVRRaycaster.pointer` = `UIRayOrigin` (real raycast from the laser origin hits the button and starts the battle); `CombatUIController` 21 refs; survival-win off; Build Settings: Level2 enabled. HUD verified updating (HP 623/800, Enemy 720/800, Dmg 80). Victory chain: death anim → IK released → colliders off → panel "VICTORY! / XP Earned: 320 …". Defeat chain: death scream → "DEFEAT … Press A to Retry". Retry reload: 0 monsters (spawned on Start), swords hidden, laser on, 800 HP.

### Code changes (Level 1 behaviour unchanged — regression run: Slash 8.8 / Heavy 10.6 every 3rd / Special 13.2 every 6th, defend 1.5 s every 8 s at 70%)
| File | Change |
|---|---|
| `Monster/MonsterAI.cs` | Optional `attackTable` (empty = legacy tiers), weighted/escalating selection, desperation special, idle-slam stillness rule, combo follow-up hits, swing arc + head-target horizontal distance (`useHeadTarget`), weapon fire VFX/light, defend tint + brace/block audio, `defendDamageMultiplier`, `DebugForceAttack` |
| `Monster/MonsterAnimator.cs` | `PlayAttackTrigger`; resets **all** controller triggers (not just named ones) |
| `Monster/MonsterController.cs` | `BeginAttackTrigger`; defend reduction reads `MonsterAI.defendDamageMultiplier` (default 0.3) |
| `Monster/TwoHandedWeaponIK.cs` | **New** — left-hand haft IK with reach-based release |
| `Combat/WeaponSwingAudio.cs` | **New** — per-hand swing whoosh |
| `Combat/BattleManager.cs` | `sceneMonster` adoption (optional; unused since the alignment pass) |
| `Manager/LevelManager.cs` | Medium: cooldown 2.2, range 1.55 (horizontal to headset) |

Old `Animations/Monster/Monster_Medium_VampireLust.controller`, `M_L2_EmberGlow`, `M_L2_FireSheet` are now unused (kept, not deleted).

### Not verifiable in the editor (needs headset)
- Real swing speeds / sword grip feel (40° pitch copies the L1 hammer — tune `PlayerSword` rot X if the blade feels too high/low).
- Whether 1.55 m stand-off + arcs give a fair dodge window with real movement.
- The vampire normal map is off; confirm the monster looks right on Quest (no magenta).
- Editor was throttled to ~10 fps in background; frame-rate-sensitive timing was re-checked at 1/72 s steps.

### Ready for headset test? **YES**
Nothing known blocks a headset test: scene is in the build, UI is clickable, fight runs to a real outcome, retry works, 0 project console errors. Open content gaps (not blockers): original axe/sword textures, a two-handed axe attack set for full two-hand grip on every swing, a dedicated block clip, a real monster vocal.

---

## Level 1: Easy — RE-AUDITED 2026-09-26 (supersedes every earlier Level 1 status in this file)

Method: every item below was inspected on the live `Level1_Easy` scene / spawned runtime objects via Unity MCP, and exercised in Play mode (UI ray click through `EventSystem.RaycastAll`, real `HitDetection.TryHit`, Animator state sampling, forced death chains). Evidence screenshots: `Captures/L1_final_*.png` (project root, not imported).

### Corrections to earlier claims in this file (they were wrong)

| Earlier claim | What was actually true | Fixed? |
|---|---|---|
| "Monster sword tag MonsterWeapon ✅ inspected" / "Clash wired ✅" | Collider + tag existed only as overrides on an **inactive scene copy** that is never used. `BattleManager` spawns from `Monster_Easy_HipHop.prefab`, whose `MonsterSword` had **no collider, Untagged** → clash could never fire. The scene collider also only covered the hilt (0.6 of ~2.9 local units). | ✅ Prefab: BoxCollider trigger c=(0,1.3,0.05) s=(0.35,2.6,0.3), tag `MonsterWeapon` |
| "One laser active for Start UI" / laser click works | `StartBattleUIButton.onClick` had **0 listeners** (also at git HEAD). Canvas faced **away** (rot Y=180, dot=-1) → text mirrored and `OVRRaycaster` (ignoreReversedGraphics) rejected it. Full-canvas `HitFlash` drawn above the button swallowed the ray. **Battle could not be started on headset.** | ✅ onClick → `BattleManager.StartBattleFromInspector`; canvas faces player; HitFlash moved to sibling 0 |
| "HUD @ (0,1.5,2.5) readable ✅" | Mirrored text; monster stands ~1.5 m away **in front of** the HUD and covered it. | ✅ Canvas (0, 2.2, 1.3), rot (-25,0,0), scale 0.0014 (1.26×0.70 m) |
| Hammer scale 0.3 | Actual scale is **0.09** (world 0.20×0.40×0.62 m). Hit collider sat on the **handle** (mesh z -2.8…-1.6), not the head (z -1.2…1.6), world size 3×5×11 cm. | ✅ Collider c=(0,0,0.2) s=(3.2,2.2,2.8) → 0.29×0.20×0.25 m on both hammers + `Weapon_Hammer.prefab` |
| "Attack variety fixed" | Tier selection was fine, but `OnTakeDamage` played Hit mid-attack; `Any State→Hit` cancelled Heavy/Special animations whenever the player landed a hit. | ✅ No hit-flinch while `isAttacking` |
| "Defend 1.5 s" | Defend clip ran 0.67 s; 70% reduction stayed on ~0.8 s longer while monster looked idle. | ✅ Defend state speed 0.444 → pose lasts 1.50 s |
| Victory = monster death | 40 s round timer + `timerExpireMeansSurvivalWin=true` gave **VICTORY with monster at 500/500 HP**. Timer HUD stuck at "Time 1s". | ✅ Survival-win off (fight continues until a death); timer pushes 0 |
| Defeat "R to retry" | Keyboard-only, editor-only; "ESC for Menu" handled nowhere → headset player stuck. | ✅ A button retries (Victory + Defeat); hints updated |
| "Dmg" HUD counter | Also counted damage the **player took**. | ✅ Counts damage dealt only |
| Duplicate monster in Hierarchy | Inactive scene `Monster_Easy_HipHop` + runtime `(Clone)`. The clone also spawned facing away (spawn point rot 0). | ✅ Scene copy deleted; `MonsterSpawnPoint` rot Y=180 |
| Shattered geometry screenshot | `screenshot-20260926-003844.png` / `-003758.png` are from the **Level 2** session (brick walls/door match `L2_verify_playerview.png`). Nothing like it reproduces in Level 1. | n/a — Level 2 issue |

### Verified configuration (numbers read from the live objects)

| Item | Value |
|---|---|
| Hierarchy roots | Directional Light, Environment_LiteDungeon, BattleSystems, MonsterSpawnPoint, PlayerSpawn, OVRCameraRig, EventSystem, CombatCanvas, BackgroundMusic (118 GameObjects) |
| Runtime singletons | 1 enabled camera (`CenterEyeAnchor`; Left/RightEye cameras disabled), 1 AudioListener, 1 EventSystem, 1 monster after Start, 0 before |
| Swing → damage | `baseDamage 12 × Lerp(1, 2.5, InverseLerp(1.2, 8 m/s))` × angle(1–1.4) × facing, + 0.08×speed + 0.35×taps, ×1.15 hammer. Measured: 4 m/s → 22.69, 6 m/s → 29.37, 7 m/s → 32.90 |
| Min swing / cooldown | 1.2 m/s gate (0.8 m/s → 0 dmg), 0.35 s per hammer (verified blocks 2nd hit) |
| HP | Monster 500, Player 500 |
| Monster damage | base 8 × 1.1 sword: Slash 8.8, Heavy 10.6, Special 13.2 (measured) |
| Telegraph | Slash 0.9 s, Heavy 1.25 s, Special 1.5 s; attack cooldown 3.2 s |
| Tier cadence (HP > 50%) | Heavy every 3rd, Special every 6th — runtime trace 13 cycles, each reached its Animator state |
| Defend | cooldown 8 s, duration 1.5 s, 70% reduction |
| Clash | hammer vs `MonsterSword` → 0 dmg + clash SFX (verified via `TryHit`) |
| Audio | Hammers: hit / heavy / clash · Monster: telegraph (unsheathing), swing (slice), death · Player: damage, hurt vocal, death scream · BGM `music` loop 0.08 — all clips non-null, played on the runtime objects |
| Victory chain | MonsterHealth.OnDeath → MonsterController.OnMonsterDeath → Death anim + SFX → colliders off → BattleManager (GameState Victory) → panel after 0.75 s |
| Defeat chain | PlayerHealth.OnDeath → death scream → BattleManager (GameState Defeat) → panel |
| Retry | `ReloadCurrentLevel` reloads clean (0 monsters, hammers hidden, laser on) |
| Console | 0 project errors, 0 project warnings (fixed 2 CS0618 obsolete-API warnings) |

### Not verifiable in the editor (needs headset)
- Real controller swing speeds, tracked hand positions, hammer grip feel, A-button press.
- Monster stands off ~1.06 m (capsule front) because `MonsterAI` measures 3D distance to the rig at y=1.6 — reachable with a short step/lean; confirm feel.

### Known design items (not bugs, decisions for you)
- `HitDetection.ComputeFacingMultiplier` measures from the monster's feet, so the 1.25× front-hit bonus almost never triggers for chest-height hits.
- Timer now informational only; a timed-defeat rule would need `timerExpireMeansDefeat` + a longer `roundDuration`.
- `SessionReporter` posts to `http://localhost:4000` — on Quest that is the headset itself, so dashboard reporting needs a LAN IP.

---

## Level 2: Medium — status after MCP fix pass (2026-09-26) — ⚠️ SUPERSEDED: several claims below were false (see 2026-09-27 section)

**Previous “Level 2 complete” claims were inaccurate.** Headset testing exposed broken UI wiring, missing scene monster, invisible offhand sword, flat sword materials, and wrong canvas placement. This pass inspected live `Level2_Medium` via Unity MCP and fixed those issues in-scene.

### Verified fixes applied (MCP-inspected)

| Area | Before (inspected) | After (inspected) |
|---|---|---|
| Sword materials | `M_L2_SwordSteel*` URP/Lit with **null** `_BaseMap` (flat grey clay) | `_BaseMap` = `T_crystal_Sword.png` on both sword materials *(see note below)* |
| Dual swords | Left `Visual` **localScale (0,0,0)** + full `SwordZbrush` rig (`BN_main`); only right sword readable | Left rebuilt to same mesh as right (`ZombieSwordTextureReady.001`); both ~0.25–0.37 m cross-section in hand |
| Monster | **No** `Monster_Medium_VampireLust` in scene hierarchy | Prefab instance at `MonsterSpawnPoint` `(0,0,3)`, **inactive** (Level1 pattern); `BattleManager` spawns active copy on Start |
| Start / HUD UI | `CombatUIController` on `BattleSystems` had **all UI refs null**; canvas at ~(0,2,9.6) scale 0.0072; `autoStartBattleOnLoad=true` | UI refs wired; canvas **world (0, 1.5, 2.5)**, scale **0.0022**, `worldCamera=CenterEyeAnchor`; **autoStart=false**; Start button → `StartBattleFromInspector()` |
| VR camera | OVRCameraRig confirmed at **(0, 1.6, 0)**; `CenterEyeAnchor` camera enabled | Matches Level 1 standing spawn pattern |

**Sword texture note (honest):** The external downloads (`animated_sword.FBX.fbx`, `SwordZbrush.fbx`) and the project folder `/Users/dikshanta/Documents/VR-Game/` contain **no separate PNG/JPG sword textures**, and FBX embedded materials also had **null** albedo. Until original texture files are provided, swords use the existing project albedo `Assets/Models/Weapons/shattered-crystal-sword/textures/T_crystal_Sword.png` so blades are not flat grey clay.

### Headset sign-off: **NOT YET** — re-test required

- Console after save: **0 errors** (MCP `read_console`).
- Code: `StartBattleTriggerButton` now uses **this scene’s** `LevelManager.sceneDifficulty` (was hardcoded Easy).

### Level2_Medium hierarchy (roots + combat objects, MCP snapshot)

```
+ Directional Light
+ BattleSystems [RoundManager, BattleManager, LevelManager(autoStart=OFF), GameStateManager, CombatUIController=WIRED, XPManager, SessionReporter]
+ MonsterSpawnPoint
+ PlayerSpawn
+ Monster_Medium_VampireLust [INACTIVE] — mixamorig:Hips …, Vampire (SkinnedMeshRenderer), MonsterAxe on prefab
+ OVRCameraRig @(0,1.6,0) [PlayerCombat dual swords, PlayerHealth]
  - TrackingSpace
    * CenterEyeAnchor [Camera, AudioListener]
    * LeftHandAnchor
      - PlayerSword_Left [HitDetection]
        - Visual (scale 0.01)
          - ZombieSwordTextureReady.001 [MeshRenderer → M_L2_SwordSteel_Offhand + T_crystal_Sword]
    * RightHandAnchor
      - PlayerSword [HitDetection]
        - Visual (scale 0.01)
          - Armature (DISABLED)
          - ZombieSwordTextureReady.001 [MeshRenderer → M_L2_SwordSteel + T_crystal_Sword]
      - UIRayOrigin, ControllerLaserPointer …
+ StartBattleButton3D
+ EventSystem [OVRInputModule]
+ CombatCanvas [World Space @(0,1.5,2.5), scale 0.0022] → StartBattleUIButton, HP bars, Victory/Defeat panels …
+ BackgroundMusic
+ Environment_Level2_Dungeon
```

---

## Level 2: Medium (original MCP build notes — ⚠️ SUPERSEDED by 2026-09-27 section)

Built `Level2_Medium` by reusing the Level 1 architecture and combat systems (`PlayerCombat`, `HitDetection`, `MonsterAI`, `MonsterController`, `MonsterHealth`, `PlayerHealth`, `CombatUIController`, `XPManager`, `BattleManager`) and adapting loadout/content/tuning for Medium.

### Part 1 — New assets imported/classified/moved ✅

External files imported from `/Users/dikshanta/Documents/VR-Game/` and moved into project structure:

- `animated_sword.FBX.fbx` → `Assets/Models/Weapons/Player/animated_sword.FBX.fbx`
- `SwordZbrush.fbx` → `Assets/Models/Weapons/Player/SwordZbrush.fbx`
- `axe.fbx` → `Assets/Models/Weapons/Monster/axe.fbx`
- `Solitude_Ember.fbx` → `Assets/VFX/Solitude_Ember.fbx`
- `Vampire A Lusth.fbx` → `Assets/Models/Monsters/Level2/Vampire A Lusth.fbx`
- `Standing Melee Attack Downward.fbx` → `Assets/Animations/Monster/Level2/Standing Melee Attack Downward.fbx`
- `Standing Melee Attack Horizontal.fbx` → `Assets/Animations/Monster/Level2/Standing Melee Attack Horizontal.fbx`
- `Standing Melee Combo Attack Ver. 1.fbx` → `Assets/Animations/Monster/Level2/Standing Melee Combo Attack Ver. 1.fbx`
- `Standing Melee Run Jump Attack.fbx` → `Assets/Animations/Monster/Level2/Standing Melee Run Jump Attack.fbx`

**All `Standing Me*` files found (exact list, 4 total):**
- `Standing Melee Attack Downward.fbx`
- `Standing Melee Attack Horizontal.fbx`
- `Standing Melee Combo Attack Ver. 1.fbx`
- `Standing Melee Run Jump Attack.fbx`

Asset inspection summary:
- `animated_sword` and `SwordZbrush`: weapon meshes (no usable attack clips in-file).
- `axe`: weapon mesh (no clips), suitable for monster two-handed axe attachment.
- `Solitude_Ember`: mesh/prop-like asset, no animation clip; not auto-usable as triggerable fire VFX without additional setup.
- `Vampire A Lusth`: humanoid/skinned model used as Level 2 monster base.

### Part 2 — Level2 scene setup ✅

- Scene confirmed/used: `Assets/Scenes/Level2_Medium.unity`
- Reused Level 1 architecture roots:
  - `BattleSystems` (RoundManager, BattleManager, LevelManager, GameStateManager, CombatUIController, XPManager, SessionReporter)
  - `MonsterSpawnPoint`, `PlayerSpawn`, `OVRCameraRig`, `CombatCanvas`, `BackgroundMusic`, `EventSystem`, `StartBattleButton3D`
- Environment built as enclosed dark dungeon under `Environment_Level2_Dungeon` using repeated modular pack pieces (floors/walls/door/pillars/lamps).
- Lighting tuned darker/moodier than Level 1 (dim ambient + warm lamp points + fog).
- Build settings order verified includes Level2 after Level1:
  - MainMenu → Level1_Easy → Level2_Medium → Level3_Hard → Results

### Part 3 — Monster: Medium Vampire, single two-handed axe ✅

- Created prefab: `Assets/Prefabs/Monsters/Monster_Medium_VampireLust.prefab`
- Animator controller: `Assets/Animations/Monster/Monster_Medium_VampireLust.controller`
- Attack-tier mapping:
  - Standard: `Standing Melee Attack Horizontal`
  - Standard variant (`AttackAlt`): `Standing Melee Combo Attack Ver. 1`
  - Heavy: `Standing Melee Attack Downward`
  - Special: `Standing Melee Run Jump Attack`
  - Defend: `Great Sword Impact` (fallback brace/impact pose from existing project clips)
- Axe setup:
  - Single axe attached to right-hand bone and visually oriented for two-handed read.
  - Tagged `MonsterWeapon` with trigger collider for clash/parry logic.
- Medium tuning used:
  - Monster HP: `650`
  - Monster damage: `14`
  - Attack cooldown: `2.05`
  - Range: `2.6`
  - Move speed: `0.75`
  - Aggression: `1.25`
  - AI delays: slash `0.68`, heavy `0.82`, special `0.98`
  - AI recoveries: slash `0.45`, heavy `0.62`, special `0.75`
  - AI multipliers: slash `1.05`, heavy `1.35`, special `1.65`
  - Defend cooldown/duration: `6.5` / `1.15`

### Part 4 — Player: dual swords for Level2 ✅

- Right + left sword objects attached to `RightHandAnchor` / `LeftHandAnchor`:
  - `PlayerSword`
  - `PlayerSword_Left`
- Both wired with `HitDetection`, trigger colliders, and clash-capable audio setup.
- `PlayerCombat` updated for dual-sword scene wiring (`primaryWeapon=Sword`, `offhandWeapon=Sword`) using `offhandSwordObject`.
- Sword visual scale in-hand: `0.02` (mesh-local, tunable after headset feel test).
- Swing/damage tuning for sword feel:
  - `minSwingVelocity=1.0`
  - `maxVelocityDamageMultiplier=2.2`
  - per-sword `baseDamage=11`
  - `hitCooldown=0.28`
- Shield is disabled in Level2 by design (dodging/timing focus), matching requested Medium combat style.

### Part 5 — Damage / XP / UI adaptation for Medium ✅

- Level2 health pattern kept symmetric:
  - Player HP `650`
  - Monster HP `650`
- XP tuned above Level1 baseline (`10/25/15/200`):
  - Hit XP: `15` (**1.5x**)
  - Full swing bonus: `40` (**1.6x**)
  - Block XP: `22` (**1.47x**)
  - Victory bonus: `320` (**1.6x**)
- CombatCanvas world-space kept as fixed front HUD pattern from Level1:
  - Position `(0, 1.5, 2.5)`, rotation `(0, 180, 0)`

### Part 6 — Sound setup status ⚠️ partial (asset-limited)

- Reused working PlayOneShot pattern (player hit/death, monster death, clash logic, bgm).
- Current assigned sword-vs-weapon clash clip is active.
- Distinct dedicated **axe attack** and **player hero grunt** clips were not found in current audio assets.
- Distinct **scream/fire combo** trigger remains blocked by missing suitable VFX/audio clips (and `Solitude_Ember` is mesh-like, not a drop-in attack VFX prefab).

### Validation status (superseded — see verified fix pass above)

- Earlier claim of “0 errors / playable / complete” was **not** validated on headset.
- Re-validate in VR after 2026-09-26 fix pass before marking Level 2 complete.

### Level2 hierarchy snapshot (root)

- `Directional Light`
- `BattleSystems`
- `MonsterSpawnPoint`
- `PlayerSpawn`
- `OVRCameraRig`
- `StartBattleButton3D`
- `EventSystem`
- `CombatCanvas`
- `BackgroundMusic`
- `Environment_Level2_Dungeon`

### Blockers before "fully content-complete" headset signoff

- Dedicated vampire/axe/scream/hero audio set is incomplete in current project assets.
- No dedicated two-handed axe defend/block animation clip was provided; fallback defend clip is used.
- `Solitude_Ember` is not an out-of-box attack VFX clip/prefab; needs authored VFX hookup if required for heavy/special fire moments.

---

## 🚨 CRITICAL STATUS — Item 1 Debug Overlay / Gizmo Bug

- **Root cause confirmed**: The purple/violet translucent bands and cone-like objects visible in Game view were **Editor Gizmos** from Meta XR debug visualization being rendered because **GameView Gizmos was enabled** (`GameView.showGizmos=True`). No `OnDrawGizmos`/`OnDrawGizmosSelected` debug code was found in this project's `Assets` scripts.
- **Fix applied**:
  - Added editor utility `Assets/Editor/DisableGameViewGizmosOnPlay.cs` to force GameView gizmos OFF when entering Play Mode.
  - Forced current GameView gizmos OFF (`GameView.showGizmos=False`).
  - Enabled OVR boundary suppression flag on the rig (`shouldBoundaryVisibilityBeSuppressed=true`) to avoid persistent boundary-style visualization leakage in-editor.
- **Gameplay status under the visual bug**: Health/damage/victory/defeat logic remains functional; this was a rendering/debug-visualization issue, not core combat logic corruption.
- **Build/headset behavior**: Gizmo overlays are editor-only and do not ship in player builds. This fix ensures Play Mode is also visually clean.

---

## ⚠️ AMBIGUITY TO CONFIRM — Item 5 (Monster HP vs Damage-per-hit)

**The instructor said "MONSTER MAX HEALTH / DAMAGE VALUE = 500".** This is ambiguous:

- **Current implementation**: `MonsterHealth.maxHealth = 500` (monster takes ~40+ hammer hits to kill = multi-hit fight). Also `PlayerHealth.maxHealth = 500` (item 6).
- **Alternative interpretation**: each hammer hit deals 500 damage (one-shot kill). NOT implemented.

I implemented it as **Max HP = 500** because that matches a proper combat loop. The value is Inspector-exposed on `MonsterHealth` (on the `Monster_Easy_HipHop` prefab and scene instance) AND in `LevelManager.GetParams(Easy).monsterMaxHealth`. To flip to "500 damage per hit": set `HitDetection.baseDamage = 500` (currently 12) on both hammers.

**Monster per-hit damage to player**: base 8 × 1.1 (sword flavor) = **~8.8 per hit**. With player HP 500 → **~57 hits to kill player**. This is very forgiving for Level 1 (slow, telegraphed attacks). Tune `LevelManager.GetParams(Easy).monsterDamage` if too easy/hard.

---

## Latest MCP pass (today 2026-09-23) ✅ — INSTRUCTOR FEEDBACK FIXES (9 ITEMS)

All changes executed directly via Unity MCP in the open `Level1_Easy.unity` scene. 0 console errors after all changes.

### Item 1 — Swing speed → damage link (VERIFY) ✅ PASS
- Intact from last session. `HitDetection.cs`: `minSwingSpeed=1.2`, `hitCooldown=0.35`, `baseDamage=12`, `maxForceMultiplier=2.5`.
- `PlayerCombat.cs`: `minSwingVelocity=1.2`, `maxSwingVelocity=6`, `maxVelocityDamageMultiplier=2.5`.
- Damage formula: `baseDamage × forceMul × angleMul × facingMul`, where `forceMul = Lerp(1, 2.5, InverseLerp(minVel, maxVel, swingSpeed))`.

### Item 2 — Hit cooldown (VERIFY) ✅ PASS (CHANGED)
- `HitDetection.hitCooldown` bumped 0.25 → **0.35s** on both `PlayerHammer`, `PlayerHammer_Left`, and `Weapon_Hammer.prefab`. Prevents double-registration from a single swing.

### Item 3 — Monster sword attachment ✅ PASS (FIXED)
- **Root cause**: `MonsterSword` was parented to `Monster_Easy_HipHop` root (not the hand bone) — sword floated in front of the monster instead of being gripped.
- **Fix**: Edited `Assets/Prefabs/Monsters/Monster_Easy_HipHop.prefab` via `PrefabUtility.LoadPrefabContents` to reparent `MonsterSword` under **`mixamorig:RightHand`** bone. Scene instance auto-applied the prefab change.
- **Grip transform** (local to RightHand): position `(0, -0.05, 0.15)`, rotation `(-10, 90, 0)`, scale `(0.5, 0.5, 0.5)`.
- **Collider**: BoxCollider already present (size `0.15 × 0.6 × 0.05`, center `(0, 0.3, 0)`, isTrigger=true). Tag = `MonsterWeapon`.
- **Flag for next session**: collision-based monster damage (sword collider hitting player shield/body) is NOT wired yet — monster damage still uses timer-based `PerformAttackHit()` at the hit frame. To switch to collision-based: add `HitDetection`-style script on the sword that detects `OnTriggerEnter` with player tag and calls `PlayerHealth.TakeDamage()`. Recommend keeping timer-based for Level 1 (more reliable) and adding collision as a polish/secondary channel later.

### Item 4 — Player loadout: shield+hammer → TWO HAMMERS ✅ PASS (CHANGED)
- Duplicated `PlayerHammer` as **`PlayerHammer_Left`**, parented to `LeftHandAnchor` (mirrored transform: localPos `(-0.06, -0.04, 0.12)`, localRot `(20, 0, 270)`, scale `0.3`).
- Added `HitDetection` (already on the duplicate) with **`hand = Left`** — new field on `HitDetection.cs` that selects which controller velocity to read (`CurrentOffhandSwingSpeed` for Left, `CurrentSwingSpeed` for Right).
- `PlayerCombat.cs` updated for dual-wield:
  - New fields: `offhandWeaponTransform`, `offhandHammerObject`, `offhandSwingController = LTouch`, `CurrentOffhandSwingSpeed` property.
  - `UpdateSwingVelocity()` now tracks both hands independently (each has its own smoothed velocity).
  - `ApplyWeaponVisuals()` activates `offhandHammerObject` when `offhandWeapon == Hammer`.
- Scene wiring on `PlayerCombat` (OVRCameraRig): `primaryWeapon=Hammer`, `offhandWeapon=Hammer`, `offhandHammerObject=PlayerHammer_Left`, `offhandWeaponTransform=PlayerHammer_Left`, `blockEnabled=false`.
- `LevelManager` (scene + defaults): `playerOffhandWeapon = Hammer` (was Shield/None).
- **Shield handling**: `PlayerShield` GameObject **deactivated** (not deleted) in scene — kept under `LeftHandAnchor` for easy re-enable in future levels. `OnBlock()` code preserved in `PlayerCombat.cs`, flagged "disabled — no shield in Level 1 loadout" via `blockEnabled=false`. Shield prefab remains in `Assets/Prefabs/Weapons/`.
- **No damage mitigation in Level 1**: confirmed fine — monster attacks are slow & telegraphed (3.2s cooldown, 0.9s wind-up), so dodging/moving is the defense instead of blocking.

### Item 5 — Monster max health = 500 ✅ PASS (CHANGED) — see AMBIGUITY flag at top
- `MonsterHealth.maxHealth = 500` set on `Assets/Prefabs/Monsters/Monster_Easy_HipHop.prefab` AND scene instance.
- `LevelManager.GetParams(Easy).monsterMaxHealth = 500` (this is what BattleManager actually applies at spawn via `Configure()` → `health.SetMaxHealth()`).
- Inspector-exposed on `MonsterHealth` (the `maxHealth` field is public).

### Item 6 — Player max health = 500 ✅ PASS (CHANGED)
- `PlayerHealth.maxHealth = 500` set on OVRCameraRig.
- `LevelManager.GetParams(Easy).playerMaxHealth = 500` (BattleManager applies this at `StartBattle` via `PlayerHealth.SetMaxHealth()`).
- Monster per-hit damage = 8 base × 1.1 sword = ~8.8 → ~57 hits to kill player (very forgiving for L1).

### Item 7 — Monster attack hit detection ✅ PASS (FIXED)
- **Root cause**: Two compounding issues:
  1. The `CombatCanvas` was positioned at `(0, 1.5, 0)` — exactly at the player's spawn position — so the world-space UI rendered around/inside the player. This was the "hitting screen" / "purple bar" visual: the canvas edge or HitFlash image was visible at a weird angle because it was co-located with the player's head.
  2. `PlayerHealth.FlashRoutine()` captured the hit-flash Image's starting alpha and restored it after the flash. If a second flash overlapped the first mid-animation, it captured `a=0.6` and restored to 0.6, leaving a permanent red tint.
- **Distance check**: `MonsterAI.AttackRoutine()` ALREADY had a distance check at the hit frame: `if (dist <= controller.attackRange * 1.15f)` (attackRange=2.2m → 2.53m tolerance). No change needed — it was intact.
- **Fix 1 (canvas)**: Moved `CombatCanvas` to `(0, 1.5, 2.5)` facing the player (rotation Y=180). Now it's a HUD panel 2.5m ahead at chest height, not co-located with the player. (See item 10.)
- **Fix 2 (flash)**: Rewrote `FlashRoutine()` to always start at alpha 0 and end at alpha 0 (not capture-and-restore). Overlapping coroutines now cleanly fade out instead of leaving a permanent tint.
- **Result**: Hit flash fires once per landed attack, fades out fully, no looping.

### Item 8 — Game over / victory screen ✅ PASS (VERIFIED)
- Event chain verified intact:
  `HitDetection.TryHit` → `MonsterController.OnTakeDamage` → `MonsterHealth.TakeDamage` → if HP≤0, `MonsterHealth.OnDeath` event fires (no subscribers) → back in `MonsterController.OnTakeDamage`, checks `health.IsDead` → calls `MonsterController.OnDeath()` → `OnMonsterDeath?.Invoke()` + `BattleManager.NotifyMonsterDefeated()` → `VictorySequence()` coroutine → `CombatUIController.ShowVictory(score, damage)`.
- `outcomeHandled` guard prevents double-fire (MonsterHealth.OnDeath event + MonsterController.OnDeath method can both trigger, but guard dedupes).
- Defeat chain verified: `PlayerHealth.TakeDamage` → if HP≤0, `OnDeath?.Invoke()` + `BattleManager.NotifyPlayerDefeated()` → `DefeatSequence()` → `CombatUIController.ShowDefeat(score, reason)`. R-key retry works (`GameStateManager.ReloadCurrentLevel()`).

### Item 9 — Hammer scale fix ✅ PASS (CHANGED)
- Both hammers reduced from scale `0.5` → **`0.3`** (`PlayerHammer`, `PlayerHammer_Left`, and `Weapon_Hammer.prefab`).
- Collider check: BoxCollider (size `0.3×0.3×0.8`, center `(0, 0, -1.78)`) is on the same GameObject as the mesh, so it scales proportionally with the mesh automatically. No collider adjustment needed — collider-to-mesh ratio preserved.
- Report: at scale 0.3, hammer mesh world size ≈ 1.07×0.72×2.03m (still larger than a real hammer, but the mesh itself is oversized in source units). User to fine-tune in headset.

### Item 10 — Combat UI world-space, fixed in front of player ✅ PASS (CHANGED)
- `CombatCanvas` was already `renderMode = World Space` (renderMode=2) — good for VR.
- **Position fix**: moved from `(0, 1.5, 0)` (at player) → **`(0, 1.5, 2.5)`** (2.5m ahead of player spawn at chest/eye height).
- **Rotation**: `(0, 180, 0)` — facing back toward the player (canvas forward = -Z, player faces +Z).
- **Scale**: kept `0.0022` (world-space) → canvas world size ≈ 1.98m × 1.1m (readable HUD panel).
- **Distance from player**: 2.5m (within the 2-3m recommended range).
- Canvas does NOT follow head rotation (it's a root GameObject at a fixed world position, not parented to the camera) — sits in place like a "wall" of UI in the arena.
- `worldCamera` = `LeftEyeAnchor` (VR camera), `OVRRaycaster` for ray-click interactions.

### Item 11 — Monster rig/animation general check ✅ PASS
- Animator Controller `Monster_Easy.controller`: 4 states (Idle/Attack/Hit/Death) all wired to real Mixamo clips (verified last session, intact).
- Sword now gripped in `mixamorig:RightHand` (item 3) — no longer floating.
- Monster has `Animator`, `CapsuleCollider`, `MonsterHealth`, `MonsterController`, `MonsterAI`, `MonsterAnimator` — all present.
- No T-pose (Idle clip plays on Start via `SetAnimState(Idle)`).
- **Remaining manual polish (NOT blocking)**:
  - Set "Loop Time" on `Monster_SwordIdle.fbx` import (so idle loops instead of freeze-framing at end).
  - Attack clip is a 4.53s combo but damage lands at 0.9s — trim to first swing in Mixamo re-export or Unity clip editor if the follow-through looks odd.
  - "Great Sword Idle" is a two-handed pose on a one-handed Crystal Sword rig — may look slightly off. Swap to a one-handed idle from Mixamo if desired.

### Monster Combat Variety & Defense (2026-09-24) ✅ PASS (NEW)
- **Animation Clips Moved**: Moved 4 new FBX clips into `Assets/Animations/Monster/` and configured their rig to Humanoid with the correct Avatar.
- **Attack Tiers**:
  - **Standard (Slash)**: Uses `Great Sword Slash`. Triggered most often (80% normally).
  - **Heavy**: Uses `Great Sword High Spin Attack`. Longer wind-up, higher damage. Triggered 20% normally, up to 40% when monster HP is below 50%.
  - **Special**: Uses `Mutant Jump Attack`. Very long wind-up, highest damage. Triggered only when monster HP is below 25% (30% chance).
- **Defense Mechanic**:
  - Monster now periodically enters a **Defending** state (cooldown 8s, duration 1.5s).
  - Uses `Great Sword Impact` animation.
  - While defending, incoming hammer damage is reduced by 70% (amount * 0.3).
- **Clash Sound**:
  - Added `wings_of_freedom-riser-impact-606984.mp3` for weapon-on-weapon clash.
  - When player's hammer hits the monster's sword collider (`MonsterWeapon` tag), the clash sound plays, sparks spawn, and NO damage is dealt to the monster.
- **Audio Verification**:
  - Re-assigned and verified all SFX (hit, heavy hit, telegraph, swing, death) on player and monster.
  - Background music volume further reduced to `0.15`.
- **Laser Pointer**: Added `ControllerLaserPointer` to `RightHandAnchor` for UI interaction.

---

## Level1_Easy Hierarchy (after today's pass)

```
+ Directional Light
+ Environment_LiteDungeon (51 children)
+ BattleSystems [RoundManager, BattleManager, LevelManager, GameStateManager, CombatUIController, XPManager]
+ MonsterSpawnPoint
+ PlayerSpawn [tag=Player]
+ Monster_Easy_HipHop [tag=Monster] [Animator, CapsuleCollider, MonsterHealth(max=500), MonsterController, MonsterAI, MonsterAnimator]
  - mixamorig:Hips (rig)
    - ... mixamorig:RightArm/RightForeArm/RightHand
      - MonsterSword [tag=MonsterWeapon] [BoxCollider trigger] ← NOW parented to RightHand
  - Warrok (SkinnedMeshRenderer)
+ OVRCameraRig [tag=Player] [PlayerHealth(max=500), PlayerCombat(dual-wield, blockEnabled=false)]
  - TrackingSpace
    * CenterEyeAnchor [Camera, AudioListener]
    * LeftHandAnchor
      - PlayerHammer_Left [HitDetection(hand=Left), tag=Hammer, scale=0.3] ← NEW (dual-wield)
      - PlayerShield (INACTIVE — disabled, kept for future re-enable)
    * RightHandAnchor
      - PlayerHammer [HitDetection(hand=Right), tag=Hammer, scale=0.3]
+ StartBattleButton3D [StartBattleTriggerButton]
+ EventSystem [OVRInputModule]
+ CombatCanvas [World Space, pos=(0,1.5,2.5), rot=(0,180,0), scale=0.0022] ← moved 2.5m in front
  - PlayerHPText, MonsterHPText, ScoreText, TimerText, DamageText, XPText
  - PlayerHPBar, MonsterHPBar, StaminaBar (Sliders)
  - StartBattleUIButton (Button)
  - HitFlash (red Image, alpha=0, fixed fade routine)
  - VictoryPanel [VTitle, VXP, VHits, VSwings, VBlocks, VDmg]
  - DefeatPanel [DTitle, DXP, DHits, DScore, DRetry]
+ BackgroundMusic [AudioSource]
```

Console: **0 errors** after all changes (verified via MCP `read_console`).

---

## Ready for headset test? ✅ YES

Level 1 is ready for headset test. All 9 instructor-feedback items addressed. Dual-wield hammers, 500 HP on both sides, sword gripped in monster hand, world-space HUD 2.5m ahead, hit-flash fixed, victory/defeat chains verified.

**Fine-tune in headset:**
- Hammer scale (currently 0.3) — adjust if still feels too big/small
- CombatCanvas position (currently 2.5m ahead, 1.5m height) — adjust if too close/far/high/low
- Monster HP (500) vs per-hit damage (8.8) ratio — tune if fight is too long/short

---

## Earlier MCP pass (2026-09-23 00:10) — RETARGETING FIX + WEAPON SIZING

### Critical fix: Monster "spaghettification" (collapsed mesh / magenta planes)
- **Root cause**: The 3 new Mixamo clips were set to `AvatarSetup.CopyFromOther` (no own avatar). This caused retargeting to fail — the monster mesh collapsed into a heap with stretched magenta triangles.
- **Fix**: Changed all 3 clips to `AvatarSetup.CreateFromThisModel` — each now generates its own valid Humanoid avatar:
  - `Monster_SwordIdleAvatar` (valid=True, human=True) ✅
  - `Monster_SwordAttackAvatar` (valid=True, human=True) ✅
  - `Monster_HitReactionAvatar` (valid=True, human=True) ✅
- Unity now retargets between the clips' avatars and the monster's `Hip Hop DancingAvatar` correctly — the standard Mixamo workflow.

### Weapon sizing fixes
- **Hammer**: scale 0.12 → **0.5** (was tiny/invisible in VR), repositioned to (0.06, -0.04, 0.12) with 20°/0/90° rotation
- **Shield**: scale 0.5 → **0.4** (offhand, slightly smaller), repositioned to (-0.06, -0.02, 0.10)
- **MonsterSword**: repositioned to monster's hand area (0.35,  0.9, 0.05), scale 0.5, rotation pointing up

### Editor testing fixes (from prior pass, kept)
- OVRCameraRig at standing height (1.6m) for editor preview
- OVRHeadsetEmulator enabled (simulate head look with keyboard in editor)
- LaserPointer disabled (was rendering as magenta plane artifact)
- Old `dance.controller` moved to `Assets/Animations/Legacy/` (monster uses `Monster_Easy.controller`)

---

## Latest MCP pass (2026-09-22) ✅ — MIXAMO ANIMATIONS WIRED

### What's now FULLY WORKING in Level 1
- ✅ **Player attacks monster** → velocity-based damage (faster swing = more damage)
- ✅ **Monster attacks back** → telegraphed wind-up (0.9s red flash) → hit frame → damage to player
- ✅ **Block mechanic functional** → hold left grip → 80% damage reduction → block XP awarded
- ✅ **Min swing velocity gate** → 1.2 m/s threshold (anti-spam, tiny taps don't register)
- ✅ **Player HP bar** → decreases on unblocked monster hits
- ✅ **Monster HP bar** → decreases on player hits, scaled by swing speed
- ✅ **Floating damage numbers** → on every hit (player & monster)
- ✅ **XP counter** → live XP text, awards per hit (full swing bonus), per block, victory bonus
- ✅ **Victory panel** → shows XP earned, hits landed, full swings, blocks, damage dealt
- ✅ **Defeat panel** → shows XP, hits, score, retry hint (R to retry)
- ✅ **Hit reaction (first-person)** → red screen flash + camera shake + hit sound
- ✅ **Monster wields sword** → Crystal Sword model attached, weaponType=Sword
- ✅ **Player has shield** → Medieval shield on left hand, raises on block
- ✅ **Audio** → background music loops (from prior pass)
- ✅ **Monster animations** → REAL Mixamo clips wired into all 4 Animator states (Idle/Attack/Hit/Death)
- ✅ **Configurable hand assignment** → `weaponHand` enum (Left/Right) in PlayerCombat Inspector

### Mixamo clips imported & wired (2026-09-22)
| Animator State | Trigger | Source Clip | File | Status |
|---|---|---|---|---|
| Idle | `Idle` | Great Sword Idle | `Assets/Animations/Monster/Monster_SwordIdle.fbx` | ✅ wired (loop NOT set — see note) |
| Attack | `Attack` | One Hand Sword Combo | `Assets/Animations/Monster/Monster_SwordAttack.fbx` | ✅ wired (full 4.53s combo — see note) |
| Hit | `Hit` | Hit Reaction | `Assets/Animations/Monster/Monster_HitReaction.fbx` | ✅ wired (1.60s) |
| Death | `Death` | Two Handed Sword Death | `Assets/Models/Monsters/Two Handed Sword Death.fbx` | ✅ kept (already real) |

**Import settings:** Rig = Humanoid, Avatar = Copy From Other (Hip Hop DancingAvatar). All 3 new clips retarget cleanly — **0 avatar/muscle errors**.

### Unused clips saved for future (Level 2/3 or third-person player)
- `Assets/Animations/Player_Future/SwordShield_Attack.fbx` (from "Sword And Shield Attack.fbx")
- `Assets/Animations/Player_Future/SwordShield_Slash.fbx` (from "Sword And Shield Slash.fbx")
- These include shield-holding poses — NOT compatible with the L1 monster (one-handed sword only). Kept for a future shield-carrying monster or third-person player model.

### Notes / minor issues to fix manually (NOT blocking headset test)
- ⚠️ **Idle loop**: The Idle clip (3.70s) is not set to loop via API (Unity import quirk — `clipAnimations.loop` didn't persist programmatically). The monster will play idle once then freeze at the last frame. **Fix in Inspector**: select `Monster_SwordIdle.fbx` → Animations tab → check "Loop Time" → Apply. (Or it still looks fine because the monster re-triggers Idle on state re-entry.)
- ⚠️ **Attack combo length**: The Attack clip is a 4.53s multi-hit combo, but damage lands at 0.9s (attackHitDelay). The Animator transition `Attack→Idle` is set to exitTime=0.33 (~1.5s) so only the first swing plays before returning to idle. This syncs the visual first swing with the damage frame. **If you want a cleaner single swing**, trim in Mixamo's re-export or Unity's Animation clip editor (set firstFrame=0, lastFrame=30).
- ⚠️ **Great Sword Idle on one-handed rig**: The "Great Sword Idle" is a two-handed pose. On the one-handed Crystal Sword rig it may look slightly off (hands not both on the sword). Flagged for a future idle swap — NOT blocking today.

### Hand assignment — now configurable ✅
- Added `Hand` enum (`Right`/`Left`) and `weaponHand` field to `PlayerCombat.cs`
- `ApplyHandAssignment()` auto-sets `swingController`/`shieldController` based on `weaponHand`
- `OnValidate()` flips controllers live when you change `weaponHand` in the Inspector
- **Current scene**: weaponHand=Right → hammer on RTouch (RightHandAnchor), shield on LTouch (LeftHandAnchor)
- **To swap hands**: change `weaponHand` to `Left` in Inspector (controllers auto-flip) + reparent `PlayerHammer` under `LeftHandAnchor` and `PlayerShield` under `RightHandAnchor` in scene (or ask MCP)
- **HitDetection** reads velocity from `transform.position` delta (its own transform = the hammer) + `PlayerCombat.CurrentSwingSpeed` (which uses `weaponTransform`). Both follow the weapon object wherever it's parented — **NOT hardcoded to any anchor name**. Hit detection works correctly after any hand swap.

---

## Animator Trigger Names (for reference)

| State | Trigger Name | Motion |
|---|---|---|
| Idle | `Idle` | Monster_SwordIdle |
| Attack | `Attack` | Monster_SwordAttack |
| Hit | `Hit` | Monster_HitReaction |
| Death | `Death` | Two Handed Sword Death |

Player block uses transform rotation (no Animator trigger needed for shield raise).

---

## Swing-Angle Damage Multiplier — STATUS

**PARTIAL / STRETCH GOAL PENDING.**
- ✅ Angular velocity tracking implemented (`PlayerCombat.CurrentAngularSpeed`, `GetAngleBonusMultiplier`)
- ✅ Used as secondary damage multiplier in `HitDetection` (caps at +40% over velocity damage)
- ❌ Full overhead-vs-flick classification (swing arc shape) NOT implemented — would need
  trajectory sampling over the swing window. Flagged as future polish.

Velocity-based damage is the primary, fully-working fitness mechanic today.

---

## Scripts updated across sessions

| Script | Change |
|---|---|
| `Assets/Scripts/Manager/XPManager.cs` | NEW — session XP, hit/block/full-swing/victory bonuses, swing tiers |
| `Assets/Scripts/Player/PlayerCombat.cs` | Velocity damage, min swing gate, functional shield block, angle bonus (stretch), **`Hand` enum + `weaponHand` configurable hand assignment** |
| `Assets/Scripts/Combat/HitDetection.cs` | Velocity-based damage with min velocity gate, XP awarding (reads weaponTransform, not hardcoded anchor) |
| `Assets/Scripts/Monster/MonsterAI.cs` | Telegraph wind-up (0.9s) + red flash, cooldown floor 3s, block-aware damage routing |
| `Assets/Scripts/UI/CombatUIController.cs` | XP text, victory/defeat panels with XP/hits/swings/blocks/damage stats |
| `Assets/Scripts/Player/PlayerHealth.cs` | Block-aware damage routing, hit flash + camera shake, block XP, block sound |
| `Assets/Scripts/Combat/BattleManager.cs` | XPManager reset on start, victory XP award, R-key retry |
| `Assets/Scripts/Manager/GameStateManager.cs` | Added `ReloadCurrentLevel()` for retry |
| `Assets/Scripts/Manager/LevelManager.cs` | Easy defaults: player=Hammer+Shield, monster=Sword, cooldown 3.2s |

---

## Level1_Easy Hierarchy (after all passes)

```
+ Directional Light
+ Environment_LiteDungeon (51 children: floors, walls, torches, columns, chest, barrel)
+ BattleSystems [RoundManager, BattleManager, LevelManager, GameStateManager, CombatUIController, XPManager]
+ MonsterSpawnPoint
+ PlayerSpawn [tag=Player]
+ Monster_Easy_HipHop [tag=Monster] [Animator, CapsuleCollider, MonsterHealth, MonsterController, MonsterAI, MonsterAnimator]
  - mixamorig:Hips (rig)
  - Warrok (SkinnedMeshRenderer)
  - MonsterSword (Crystal Sword model)
+ OVRCameraRig [tag=Player] [OVRCameraRig, OVRManager, PlayerCombat, PlayerHealth]
  - TrackingSpace
    * CenterEyeAnchor [Camera, AudioListener]
    * LeftHandAnchor
      - PlayerShield (medieval shield)
    * RightHandAnchor
      - PlayerHammer [HitDetection, tag=Hammer]
      - UIRayOrigin (for VR ray clicks)
+ StartBattleButton3D [StartBattleTriggerButton]
+ EventSystem [OVRInputModule]
+ CombatCanvas [Canvas, OVRRaycaster]
  - PlayerHPText, MonsterHPText, ScoreText, TimerText, DamageText, XPText
  - PlayerHPBar, MonsterHPBar, StaminaBar (Sliders)
  - StartBattleUIButton (Button — ray-clickable)
  - HitFlash (red fullscreen Image)
  - VictoryPanel [VTitle, VXP, VHits, VSwings, VBlocks, VDmg]
  - DefeatPanel [DTitle, DXP, DHits, DScore, DRetry]
+ BackgroundMusic [AudioSource — music.mp3 loop]
```

Console: **0 errors** after all import + wiring (verified via MCP `read_console`).

---

## Ready for headset test? ✅ YES

Level 1 is fully animated and ready for headset test. The combat loop is complete:
- Point at START BATTLE, pull trigger
- Monster plays real sword idle, telegraphs attack (red flash + wind-up), swings, damage applies
- Swing hammer at monster → velocity-based damage → monster plays hit reaction → XP awarded
- Kill monster → death animation → victory panel with full stats
- Take damage → red flash + camera shake → defeat panel if HP=0 → R to retry

**Optional manual polish (not blocking):**
- Set Idle clip "Loop Time" in FBX import inspector (so idle loops instead of freeze-framing)
- Trim Attack clip to single swing if the combo follow-through looks odd

---

## Next Session TODO

1. **Test full Level 1 playthrough in headset** (ready now)
2. If idle freeze bothers you: check "Loop Time" on `Monster_SwordIdle.fbx` in Inspector
3. If attack combo too long: trim `Monster_SwordAttack.fbx` to first swing (0-30 frames) in Inspector
4. If great-sword idle looks wrong on one-handed rig: download a one-handed sword idle from Mixamo and swap
5. Build Level 2 (Lite Dungeon + sword/shield + Warrok) — `SwordShield_Attack/Slash` clips in `Player_Future/` are candidates
6. Build Level 3 (Old Sea Port + hammer monster)
7. MainMenu / Results UI buttons
8. XP persistence (save to PlayerPrefs) — future session
9. Git first commit

### What's PLACEHOLDER (needs Mixamo clips — see Next Session)
- ⚠️ Monster Attack/Hit animation states use sped-up dance clip (real clips pending download)
- ⚠️ Monster Death state uses Two Handed Sword Death clip (already real ✅)
- ⚠️ Player has no body animation (first-person VR — only weapon transforms animate)
- ⚠️ Swing-angle damage multiplier is PARTIAL (stretch goal — see note below)

---

## Animator Trigger Names (for Mixamo clip mapping)

When you download Mixamo clips, drag them into `Assets/Animations/Monster_Easy.controller` states.
**No code changes needed** — these trigger names already match `MonsterAnimator.cs`:

| State | Trigger Name | Current Motion | Replace With |
|---|---|---|---|
| Idle | `Idle` | Hip Hop Dancing clip | Sword Idle clip |
| Attack | `Attack` | Dance @ 2.2x speed | Sword Attack clip |
| Hit | `Hit` | Dance @ 3x speed | Hit Reaction clip |
| Death | `Death` | Two Handed Sword Death ✅ | (keep — already real) |

Player block uses transform rotation (no Animator trigger needed for shield raise).

---

## Swing-Angle Damage Multiplier — STATUS

**PARTIAL / STRETCH GOAL PENDING.**
- ✅ Angular velocity tracking implemented (`PlayerCombat.CurrentAngularSpeed`, `GetAngleBonusMultiplier`)
- ✅ Used as secondary damage multiplier in `HitDetection` (caps at +40% over velocity damage)
- ❌ Full overhead-vs-flick classification (swing arc shape) NOT implemented — would need
  trajectory sampling over the swing window. Flagged as future polish.

Velocity-based damage is the primary, fully-working fitness mechanic today.

---

## Scripts updated today

| Script | Change |
|---|---|
| `Assets/Scripts/Manager/XPManager.cs` | **NEW** — session XP, hit/block/full-swing/victory bonuses, swing tiers |
| `Assets/Scripts/Player/PlayerCombat.cs` | Velocity damage multipliers, min swing velocity gate, functional shield block (hold grip), angle bonus (stretch) |
| `Assets/Scripts/Combat/HitDetection.cs` | Velocity-based damage with min velocity gate, XP awarding via XPManager |
| `Assets/Scripts/Monster/MonsterAI.cs` | Telegraph wind-up (0.9s) + red flash, cooldown floor 3s, block-aware damage routing |
| `Assets/Scripts/UI/CombatUIController.cs` | XP text, victory/defeat panels with XP/hits/swings/blocks/damage stats |
| `Assets/Scripts/Player/PlayerHealth.cs` | Block-aware damage routing, hit flash + camera shake, block XP, block sound |
| `Assets/Scripts/Monster/MonsterController.cs` | Comment clarifying block routing through PlayerHealth |
| `Assets/Scripts/Combat/BattleManager.cs` | XPManager reset on start, victory XP award, R-key retry |
| `Assets/Scripts/Manager/GameStateManager.cs` | Added `ReloadCurrentLevel()` for retry |
| `Assets/Scripts/Manager/LevelManager.cs` | Easy defaults: player=Hammer+Shield, monster=Sword, cooldown 3.2s |

---

## Level1_Easy Hierarchy (after today's wiring)

```
+ Directional Light
+ Environment_LiteDungeon (51 children: floors, walls, torches, columns, chest, barrel)
+ BattleSystems [RoundManager, BattleManager, LevelManager, GameStateManager, CombatUIController, XPManager]
+ MonsterSpawnPoint
+ PlayerSpawn [tag=Player]
+ Monster_Easy_HipHop [tag=Monster] [Animator, CapsuleCollider, MonsterHealth, MonsterController, MonsterAI, MonsterAnimator]
  - mixamorig:Hips (rig)
  - Warrok (SkinnedMeshRenderer)
  - MonsterSword (Crystal Sword model — 40+ child meshes)
+ OVRCameraRig [tag=Player] [OVRCameraRig, OVRManager, PlayerCombat, PlayerHealth]
  - TrackingSpace
    * CenterEyeAnchor [Camera, AudioListener]
    * LeftHandAnchor
      - PlayerShield (medieval shield)
    * RightHandAnchor
      - PlayerHammer [HitDetection, tag=Hammer]
+ StartBattleButton3D [StartBattleTriggerButton]
+ EventSystem [OVRInputModule]
+ CombatCanvas [Canvas, OVRRaycaster]
  - PlayerHPText, MonsterHPText, ScoreText, TimerText, DamageText, XPText
  - PlayerHPBar (Slider), MonsterHPBar (Slider), StaminaBar (Slider)
  - StartBattleUIButton (Button — ray-clickable)
  - HitFlash (red fullscreen Image, invisible until hit)
  - VictoryPanel [VTitle, VXP, VHits, VSwings, VBlocks, VDmg]
  - DefeatPanel [DTitle, DXP, DHits, DScore, DRetry]
+ BackgroundMusic [AudioSource — music.mp3 loop]
```

Console: **0 errors** after all wiring (verified via MCP `read_console`).

---

## MCP executed earlier (2026-09-21) ✅

### Folder cleanup (AssetDatabase moves)
| Old path | New path | Status |
|---|---|---|
| `Assets/training/hammer/` | `Assets/Models/Weapons/hammer/` | ✅ |
| `Assets/training/medieval-shield/` | `Assets/Models/Weapons/medieval-shield/` | ✅ |
| `Assets/training/shattered-crystal-sword/` | `Assets/Models/Weapons/shattered-crystal-sword/` | ✅ |
| `Assets/training/Hip Hop Dancing.fbx` | `Assets/Models/Monsters/Hip Hop Dancing.fbx` | ✅ |
| `Assets/training/Two Handed Sword Death.fbx` | `Assets/Models/Monsters/Two Handed Sword Death.fbx` | ✅ |
| `Assets/training/Cube.prefab` | `Assets/Prefabs/Monsters/Cube_Fallback.prefab` | ✅ |
| `Assets/training/dance.controller` | `Assets/Animations/dance.controller` | ✅ |

### Tags created
- ✅ `Hammer`, `Monster`, `Player`

### Scenes (Build Settings order)
0. MainMenu · 1. Level1_Easy · 2. Level2_Medium · 3. Level3_Hard · 4. Results

### Prior Level 1 work (2026-09-21)
- Monster Animator controller `Monster_Easy.controller` (Idle/Attack/Hit/Death states)
- OVR ray input (OVRRaycaster + OVRInputModule) — Start button clickable by pointing
- Background music (music.mp3 loop)
- Active Input Handling → Both

---

## Next Session TODO

1. **Download Mixamo clips** (see list below) and import to `Assets/Animations/Monster/`
2. **Drag clips into `Monster_Easy.controller`** — replace Attack/Hit placeholders, optionally Idle
3. **Test full Level 1 playthrough in headset**:
   - Point at START BATTLE, pull trigger
   - Swing hammer at monster → damage + XP + hit anim
   - Monster telegraphs (red flash) → raise shield (left grip) → block → XP
   - Kill monster → victory panel with stats
4. Build Level 2 (Lite Dungeon + sword/shield + Warrok)
5. Build Level 3 (Old Sea Port + hammer monster)
6. MainMenu / Results UI buttons
7. XP persistence (save to PlayerPrefs) — future session
8. Git first commit

### Mixamo download list (for next session)
**Character base:** Any Mixamo character (e.g. "X Bot" or "Y Bot") — they share the same
Mixamo skeleton, so clips retarget to the Hip Hop/Warrok rig automatically.

**Monster (Level 1 — sword):**
- `Sword Idle` (or `Idle Sword`)
- `Sword Slash` (or `Sword Attack`)
- `Hit Reaction` (or `Getting Hit`)
- (Death already done — `Two Handed Sword Death`)

**Player (future third-person body, if added):**
- `Idle Sword And Shield`
- `Hammer Attack` (or `2H Hammer Attack`)
- `Hit Reaction`
- `Shield Block` (or `Block`)
- `Death`

**Export settings:** FBX for Unity · With Skin · 30fps · No Keyframe Reduction · In Place (for attacks)

**Folder structure:**
```
Assets/Animations/
  Monster/
    Monster_SwordIdle.fbx
    Monster_SwordAttack.fbx
    Monster_HitReaction.fbx
  Player/
    (future)
```


---

## Dashboard: fitness-outcome features (2026-10-02)

All five features are additive (no existing API field, colour or font changed). Calculation logic lives in `web/backend/fitness.js`; routes/queries in `analytics.js` and `server.js`. Verified against the live Neon data (5 players, 26 sessions) via a local server and headless-Chrome screenshots.

**Prerequisite built here:** the previous-session calorie estimate was not present anywhere in the repo, so it was created as part of Feature 4.

1. **Engagement tracking** — `GET /api/players/:name/engagement` (sessions by day/week, days since last session, current + longest streak, first/last session). Streak unit = consecutive calendar days (day boundaries follow `?tz=`, default UTC); the current streak stays alive until a full day passes with no session. `GET /api/overview` gains `engagement` (30-day sessions-per-day, active players over rolling 7/30 days, return rate = players with >1 session / all players). Sessions = every recorded session incl. abandoned, matching the rest of the dashboard.
2. **Swing intensity + exertion trend** — `GET /api/sessions/:id` gains `fitness` (tier counts + percentages, 5 equal time-bucket exertion trend with rose/level/dropped label, needs 10+ swings). `GET /api/players/:name` gains `swingIntensity`, `exertionTrend` (average of per-session curves; null until 2+ sessions have enough swings), `intensityByLevel`. `GET /api/analytics` gains site-wide `intensityByLevel`. Tier weight: tap 1, light 2, medium 3, full 4.
3. **Personal bests / progress** — player profile gains `personalBests` (fastest swing, highest full-swing %, highest estimated kcal, longest session, highest score; each with session id + date) and `progress` (latest session vs average of earlier ones; null with <2 sessions). Full-swing % record needs 10+ tiered swings in the session.
4. **Workout equivalents** — `estimatedCalories`, `estimatedWalkingMinutes`, `estimatedJoggingMinutes` on session `fitness`, on player `series[]`, and a player total (`fitnessEstimate`). Sessions with no recorded swings get null, not a made-up figure.
   Constants (`fitness.js`): body mass 70 kg (ASSUMPTION); MET range 3.0 (all taps) to 6.0 (all full power), linear in average tier weight (ASSUMPTION, no source: replace with a cited value before relying on it); brisk walking 4.3 MET (Compendium of Physical Activities, Ainsworth et al. 2011, code 17190); jogging 7.0 MET (same, code 12020). Verify the code numbers against the Compendium before citing.
5. **Engagement page** — `/dashboard/engagement`, sidebar entry directly after Analytics (it is analysis of player behaviour, next to Players/Compare/Analytics research pages). Site-wide section on top, player selector below; honest empty states for single-day / single-session players and a single-player site.

**Data caveats for the thesis:** Level 3 (Hard) has 6 sessions but zero swing events, so the Level 1/2/3 comparison has no Level 3 data yet. Level 1 vs 2 swing samples are small (23 and 165 swings). One recorded swing speed (14.0 m/s) exceeds the game's 8 m/s cap and should be checked.

---

## Dashboard: Calorie Insights (2026-10-03)

Backup: branch `backup-before-insights` created at the prior HEAD (d453096) before any change. The one uncommitted file at the time (`Assets/XR/Settings/OpenXRPackageSettings.asset`) is not on that branch.

- **Backend:** `GET /api/insights/calories?tz=` -> `analytics.calorieInsights()` (web/backend/analytics.js). It sums `fitness.sessionSummary()` per-session estimates; the formula in fitness.js is untouched (only `safeZone/dayIn/dayNum/dayFromNum` were added to its exports for day bucketing). Returns total (+ walking/jogging equivalents), average per session, 30 calendar days, per-level, top 5 sessions.
- **Honesty:** sessions with no swings have `estimatedCalories: null` and are excluded from every total/average (not 0). A level with no session carrying swing tiers returns `insufficientData: true` and null calories (same rule as intensity-by-level).
- **Real data (Neon, read-only, 2026-10-03):** 26 non-live sessions, 6 estimated, 20 excluded; total 53.7 kcal (est.), avg 9.0/session; Easy 29.1 (2 sessions), Medium 24.6 (4), Hard `insufficientData` (6 sessions, none with swings). The total equals the sum of the three players' `/api/players/:name` fitnessEstimate (35.7 + 11.8 + 6.2).
- **Frontend:** `components/fitness/CalorieInsights.jsx`, placed on the Overview page as new section 02 (existing sections renumbered 03-06). Overview is the landing page for the dashboard; Analytics is filtered by level/player, which doesn't suit a site-wide total. Uses existing Card/BarChart/WorkoutEquivalent/EstimateNote/Tooltip components and tokens.
- **Not verified:** the UI was only built (`vite build` passes), not seen rendered; headless Chrome produced no screenshot. Check the Overview page in a browser.
- Level 3 needs real playtest sessions with swing data before its calorie bar can appear.

---

## Feel and fitness: web parts 3 to 5 (2026-10-07)

Backup: branch `backup-before-feel-and-fitness` (commit cdcd51c) holds the state before any change, including the previously uncommitted Build Settings/DevAgent edits and the audio file moves. Work is on branch `feel-and-fitness`, uncommitted.

**Unity Parts 1 and 2 were NOT done.** The Unity MCP server was unreachable (ECONNREFUSED, nothing on port 8080, Unity not running), so no breathing tiers, blood, haptics, hit-stop, damage numbers, effort HUD, Victory/Defeat results, or game-side telemetry was touched. Every earlier "done/verified" claim about those is still unconfirmed. Web code already accepts the Part 2C fields (`combo`, `effort`, `kcal`, hit `swingSpeed`/`tier`/`combo`) and degrades cleanly without them.

### Part 3: post-session survey
- Table `session_feedback` (schema.sql, `CREATE TABLE IF NOT EXISTS`, applied on boot): `session_id` PK referencing `sessions(id) ON DELETE CASCADE`, `enjoyment` 1-5, `rpe` 6-20, `comment` (<= 500), `created_at`; at least one rating required (CHECK).
- `POST /api/sessions/:id/feedback` (public, no auth): validation in `feedbackCore.js`; 400 invalid, 404 unknown session, 409 session still live or already rated (first write wins), 429 per-IP limit (12 per 10 min, in memory, `trust proxy` = 1 for Render), generic 500. Broadcasts `feedback_saved` on SSE (comment excluded).
- `GET /api/sessions/:id` now also returns `feedback` (null when unrated) and `personalBestsSet`. Sessions CSV gains `enjoyment`, `rpe`, `feedbackComment` columns at the end.
- `GET /api/analytics/feedback`: n, mean + distribution of both ratings, per-level means, anonymous points (rpe, mean swing intensity, estimated kcal/min), Spearman rho only when n >= 8 paired sessions, otherwise `status: insufficient_data`. Descriptive only; kcal/min is a fixed function of swing intensity so it is not independent evidence.
- Frontend: public `/feedback` (player search -> latest sessions -> 2 questions, skippable, thank-you state), "Rate this session" / "Player rating" card on the session page, "Perceived vs measured effort" section on Analytics (scatter, per-level means, n always shown, honest empty state).
- Borg RPE source cited in code comments: Borg GA (1982) Med Sci Sports Exerc 14(5):377-381. **Verify the citation before using it in the thesis.**
- **Limitation: there is no authentication.** Anyone who knows a player name can rate any finished session of that player, and the per-IP limit is a spam guard, not a security boundary. Because first write wins, a wrong or malicious rating cannot be corrected from the UI (delete the session or the row in the database).

### Part 4: streak, goal, level, badges
- `GET /api/players/:name/achievements?tz=&goal=` (default tz Asia/Kathmandu, goal 3), logic in `progression.js` (pure, tested). Existing engagement endpoint and fields untouched; its plain streak still exists and can differ from the freeze-aware streak.
- **Streak rule:** consecutive days with a session; one missed day between played days is forgiven (a "freeze") at most once per rolling 7 days; two missed days in a row break it. If the last session was two days ago and a freeze is free, the streak is shown alive as "play today to keep it".
- **Level curve:** total XP to reach level L = 250 x (L-1)^2 (L2 250, L3 1000, L5 4000, L8 12250). Titles: Rookie (1), Brawler (3), Gladiator (5), Warlord (8), Legend (12). Sessions do store XP (`sessions.xp`, 17 of 42 rows non-zero at check time, max 6193).
- **Badges:** First Blood, Full Power x50, 10-hit combo, Untouched (Level 2 victory with zero damage), Burned 100 kcal (sum of estimates), 3-day streak, Weekly goal met. Each returns unlocked, unlockedAt, source session and progress. Combo reports `tracking: true` ("Tracking starts after the game update") until a session carries combo data. Untouched needs max HP (live state or event) and remaining HP, else the `player_hurt` count; it is never guessed.
- `personalBestsSet` lists the records a session holds against the player's other finished sessions (none for a first session).
- Frontend: Progress section on the player page (level/title bar, streak card with "freeze used", weekly-goal ring, trophy grid), "New PB" chip and card on the session page.

### Part 5: recap card and live HUD
- Recap card (1080x1350) on the session page, built with `html-to-image` (lazy-loaded), waits for `document.fonts.ready`, Web Share where available, else download. Only recorded numbers appear; calories say "estimated" on the card; "vs your average" only with 2+ sessions.
- `LiveHud` on each Live Arena card and as an overlay on the broadcast video: combo counter, swing-speed gauge, player-vs-monster race, flash on a faster swing than the player's stored record. Transform and opacity only, reduced-motion safe, falls back when a fight has no combo or swing data.
- Backend `LIVE_KEYS` gained `combo`, `effort`, `kcal` so the live state carries them.

### Verification done (2026-10-07)
- Backend: 23 unit tests (`npm test`, `node --test`), all pass. Frontend: `vite build` passes, 0 browser console errors/warnings across the pages checked.
- Real submit with disposable player ZZTEST against the Neon database: validation 400s, 404, 409 duplicate, 429 limit, read-back, CSV columns, analytics (n=8 gave rho 1 on constructed data, 0 ratings gave insufficient_data), achievements, personalBestsSet. All ZZTEST rows deleted afterwards; database returned to 42 sessions, 7882 events, 0 feedback rows.
- Headless Chrome: survey page at 390 px (no horizontal scroll), session page, player Progress, Analytics scatter, Live Arena HUD with a simulated fight (combo 8, 7.4 m/s, PB label shown then removed) and a fight with no combo data.
- Recap export 1080x1350 vs the on-screen render: 0.55% of pixels differ (antialiasing), layout and Inter font identical.

### Not verified
- The broadcast overlay was not seen over real video (needs a headset stream); its code is the same `LiveHud` as the Live Arena card.
- Nothing was tested on a phone browser or with the Web Share API; the survey page was only checked in headless Chrome.
- Headset-side telemetry (combo, effort, kcal, hit tier) does not exist yet, so combo badge and live combo are untested on real data.

---

## Feel and fitness: Unity parts 1 and 2 (2026-10-07)

Backup: branch `backup-before-unity-feel` (commit 9d63c76, includes the web work from the earlier pass). Work is on branch `unity-feel`, uncommitted. Nothing under `web/` was touched.

**How it was driven.** The session's MCP link to Unity failed at startup, but Unity was open and the MCP server was listening on 8080, so every call went over HTTP (initialize, session id, tools/call). The Editor bridge answered normally, so nothing was edited blind. All play-mode runs were in the Editor (Mono, not IL2CPP, no headset), driven by the existing `CombatVerification` harness and a new `FeelVerification`.

### Part 2C: telemetry (the dashboard already reads it)
- **Found:** hit events already carried `swingSpeed`, `tier`, `hand`, `defended`; HP snapshots every 1 s carried HP, score, hits, xp, fullSwings, stamina, timeLeft. No combo, effort or kcal.
- **Built:** `SessionReporter.AppendStateFields` now also writes `combo` (XPManager), `effort` (0-1, `PlayerExertionAudio.Exertion01`) and `kcal` (running estimate, omitted until a swing with a tier has landed). The state block rides on hit, player_hurt, hp and session-start payloads, so no new events or requests; the 1 s snapshot cadence is unchanged.
- **Proof (local backend, disposable player ZZTEST, all of it deleted afterwards):** 53/53 hit events had swingSpeed + tier + combo, 302/303 HP snapshots had combo + effort + kcal (the one without is the snapshot before the first swing), snapshots 1.0 to 1.1 s apart, liveState carried `combo`, `effort`, `kcal`.

### Part 1A: breathing, three tiers from one value
- **Found:** one clip (`ribhavagrawal-heavy-breathing`), one looping source, volume ramped by sustained swings. All three scenes already had the component (Level 2 included).
- **Built:** `PlayerExertionAudio` now computes `Exertion01` (0-1): swings in a 12 s window weighted 0.5-1 by hand speed, divided by 14, smoothed (0.6 s). Tiers: calm silent; medium = heavy breathing (on 0.28, off 0.18); high = fast breathing (on 0.70, off 0.55) or player HP under 25% (off above 30%). Minimum 1 s hold before stepping down. Two AudioSources, 1.5 s crossfade, per-source caps (0.30 each), 0.3 s cut at victory/defeat, silent outside combat. Level 3's low-HP panting floor still applies to the heavy source, never under the fast tier. Everything is in the Inspector. The HUD bar and the telemetry read the same `Exertion01`.
- **Asset:** `breathingfast.mp3` moved to `Assets/Audio/SFX/Player/` with AssetDatabase (meta kept). Import: Vorbis, quality 0.7, Compressed In Memory, Force To Mono, preload on (it was Decompress On Load, stereo flag off, 3D on).
- **Finding: the clip is about 27 dB too quiet** (peak 0.048, RMS 0.0036 against 0.98 / 0.082 for the heavy clip). `AudioSource.volume` cannot raise it, so `AudioGainFilter` applies a fixed x18 (hard-limited) on that source only. The loop is sample-clean (jump at the wrap 0.0001), so no loop fix was needed.
- **Observed in Play mode, all three levels:** rest: exertion 0.00, silent. Level 1 sim: Calm -> Medium at 0.35 (medium volume 0.134 rising to 0.29) -> High at 0.70 with a 1.5 s crossfade (medium 0.292 -> 0, fast 0.007 -> 0.300) -> back to Medium 2 s after effort fell. HP 20%: High forced from silence, fast volume 0.050 -> 0.202 -> 0.300 in 2 s; HP 28%: still High (hysteresis); HP 40%: fast fades 0.275 -> 0 over about 4 s. Level 2: real swing speeds only reached 0.33-0.39 (Medium); injected hard swings every 0.5 s gave Calm -> Medium (0.38) -> High (0.95) -> crossfade -> Medium -> Calm; HP rule and rest identical to Level 1. Level 3: sim reached Medium (0.64); injected swings reached High (1.00); HP rule identical; fast loop cut 0.300 -> 0 in 0.30 s at victory.

### Part 1B: blood
- `HitBloodFx` (one component on BattleSystems): enum Off / Sparks / Blood (default Blood), one pooled ParticleSystem per monster created on its first hit, 6/8/10/12 particles by tier, +2 for weak-point hits (cap 14), bigger droplets for weak-point and full hits, life 0.25-0.45 s, no decals. Guarded (blocked) hits draw nothing. Materials are assets (`Assets/Materials/FX`).
- **Draw calls (Editor Game view, noisy):** paired test with particles alive, renderer on vs off on alternate frames: mean +0.93 draw call (median equal). Whole-fight means: 103.1 (Off) against 103.4 without particles / 105.6 with particles (those frames also have other hit effects). Max live particles 12 in a fight (cap 48 per system). Blood off costs nothing.
- **Player damage:** `PlayerDamageVignette` replaces the full-rect red flash with a head-locked edge vignette (own radial texture `T_DamageVignette`, clear centre, same 0.6 peak and 0.25 s fade). Below 25% HP a slow pulse (2.4 s period, alpha 0.10-0.30) runs with the fast-breathing tier (same `LowHpActive` flag). Level 3 keeps its own low-HP vignette and heartbeat, so the pulse is off there.
- Seen in Game-view captures: the droplets read clearly on the dark Level 1 monster and stay distinct from the monster's red telegraph horn.

### Part 1C: combat feel (status from the live project)
| Item | Before | Now |
|---|---|---|
| Haptics | DONE (CombatFeedback) | verified by amplitude: tap/light 0.25, medium 0.5, full 0.9, guarded 0.35, clash 1.0, damage 0.7 on both hands; each pulse stopped by a timed coroutine. Editor only: no controller to feel it. |
| Hit-stop | PARTIAL (no attack skip) | monster only, never Time.timeScale; now skipped while the monster is winding up or attacking (15 skipped, 12 applied in one fight). One timer in `MonsterAI`; it restores the monster's own speed (1 unless an enrage tempo or stagger is active, so not literally 1 in those cases). |
| Damage numbers | PARTIAL (grey/white/yellow/orange, no popup) | pooled TextMeshPro (12), white tap / yellow light / orange medium / red full, plus "FULL POWER" popup on full swings. |
Each behind a toggle: `haptics`, `hitStop`, `skipHitStopDuringAttacks`, `builtInDamageNumbers`, `scaleNumbersWithTier`, `fullPowerPopup`.

### Part 2A: live effort HUD
Built on the existing CombatCanvas by `CombatUIController`: "est. X.X kcal" and an effort bar (tier colour), under the Score / Time / Dmg column. Redrawn at most 4 Hz and only when the shown value changed. Mirror of the backend formula in `FitnessEstimateConfig` (points at `web/backend/fitness.js`; no automatic sync). `BattleStats` holds the tallies both HUD and telemetry read. **Cost (Editor, Mono, stopwatch):** gated frames 13 ns, full refresh 45 ns, `PlayerExertionAudio.Update` 0.5 us, all 0 B allocated. **HUD vs backend:** Victory run HUD/panel 8.6 kcal, backend `estimatedCalories` 8.6 (final event 8.635); Defeat run 7.2 against 7.2 (7.205). Difference under 0.01 kcal.

### Part 2B: results on the existing panels
XP count-up (1.2 s), medal, est. kcal, peak swing speed, best combo, "NEW PB" lines and "player - Rate this session: strikefit.vercel.app/feedback". The personal-bests fetch (`GET /api/players/:name`, 5 s, background at battle start) failed silently when it timed out and parsed correctly when it answered (27.86 m/s, 59.5 kcal, score 1445). PB lines appear only against an existing record and only when the fetch answered. Medal limits per scene (serialized): Level 1 gold 35 s / 15% HP lost, silver 50 s / 40%; Level 2 35 s / 25%, 55 s / 55%; Level 3 55 s / 25%, 80 s / 50%. These are development values from very few victories (n = 8, 4, 2).

### Things found that are not new
- **Level 2 has 7 enabled realtime point lights (5 active) and Level 3 has 4; Level 1 has 2.** The same in the backup commit, so the "at most 2" rule was already exceeded.
- **Level 3's Defend state uses `L3_Defend.anim`, not `Monster_GuardPose`.** Levels 1 and 2 do. Unchanged.
- **Build Settings: only Level3_Hard is enabled** (Level1, Level2, MainMenu, Results off). Not changed, as the setting is flipped on purpose.
- `GET /api/players/:name` took 3.7 to 3.9 s from this machine against the 5 s timeout. On a slow link or a cold Render the NEW PB lines may often not appear; the backend would need a lightweight "bests" route to fix it (not touched).

### Not verified
Haptics on a real controller, the audio levels by ear (fast tier x18 gain is a measured match, not a listening test), draw calls and particle cost on Quest (IL2CPP), the HUD and panels in a headset (readable in the Editor Game view only), and any Level 3 blood capture.


## Polish pass 2 (2026-10-08, branch `unity-feel`, backup branch `backup-before-polish-2`)

**How it was driven.** The session link to Unity failed at startup (ECONNREFUSED) but port 8080 listened and the Editor was open, so every call went over the HTTP route (see memory note). Nothing was edited blind. Every number below is an **Editor / Mono simulation, not a headset**: the "player" is the real weapon collider driven through the real `HitDetection` trigger path by `Assets/Editor/PolishVerification.cs` (jobs `sound`, `cross`, `centre`, `blood`, `fire`, `breath`, `shake`, `jitter`, `regress`, `alloc`; Play mode only). Nothing under `web/` was touched (read-only GETs of `/api/analytics` and `/api/sessions` only).

### Part 1: player weapons and Level 2 sounds
- **Found.** Both player weapons are trigger BoxColliders (L3 flail: SphereCollider on `Ball`) on kinematic Rigidbodies, tag `Hammer`, layer Default, children of the hand anchors. Trigger-vs-trigger pairs raise no physics push, so the "collision" was in `HitDetection` logic: any collider tagged `MonsterWeapon` or under a `MonsterController` counted, the clash fired on every contact whatever the speed, and the monster's axe has two colliders (blade + haft) so one swing could clash twice. Level 3 weapons are all in the rig and switched on by `ApplyWeaponVisuals` (equip, pickup, swap).
- **Built.** `PlayerCombat.ignoreOwnWeaponCollisions` + `IgnoreOwnWeaponCollisions()` (Physics.IgnoreCollision for every pair of active colliders under the rig, re-run at the end of every `ApplyWeaponVisuals`, so it covers battle start, L3 grab and weapon swap; 1 pair in L1/L2, the active pair in L3). `HitDetection`: only monster body colliders and `MonsterWeapon` colliders count (cached owner lookup, no per-physics-step hierarchy walk); one landed hit or clash per swing contact (`oneContactPerSwing`, re-armed when the weapon leaves the monster for 0.12 s or slows below the gate); blade-then-haft is one clash; `ignoreDuringEntrance`; `clashNeedsSwingOrAttack` (a clash needs the player's weapon above the speed gate or the monster's weapon inside its strike window, `MonsterAI.InStrikeWindow`, so an axe merely resting against a held sword is silent); `sharedClashSoundWindow` 0.15 s (one clash sound for the two swords, both hands still get their haptic); `hitSounds` toggle. `WeaponSwingAudio`: `sustainSeconds` 0.04 and minimum cooldown 0.25 s (trigger speeds kept at the originals: the backend says real Level 2 hit speeds are low, median 2.1 m/s, so a higher trigger would silence real swings). `MonsterAI.braceSoundOnReactiveGuard` (off): the brace cue no longer plays when a guard is raised against a swing that may never land (timed guards and the landed-hit block clang keep their sounds). `SfxTrace` (editor-only audit hook, compiled out of builds) next to every combat sound.
- **Proof, overlap (L2, `cross`, monster frozen so only the two swords can touch):** swords held overlapped for 2 s (60 frames of bounds overlap) and dragged through each other 10 times at 1 to 5 m/s: **0 contact sounds, 0 damage events, 0 clashes, 0 haptic pulses**, with 0 ignored pairs before and 1 ignored pair after (only the fast drags' own whoosh sounds, which is correct). **Honest reading: before this pass the two swords already produced no sound, damage or haptic when they touched** (trigger against trigger, and `HitDetection` had no branch for a weapon-tagged collider that is neither a monster body nor a `MonsterWeapon`), so the ignore is a guarantee rather than a fix. The Level 2 sounds with nothing hit came from the monster's weapon: with a live monster the old code gave 21 clashes / 37 haptic pulses in the same drag test (the monster's axe sweeping through the held swords), which is what the gating below removes.
- **Proof, 100 s simulated Level 2 fight** (14 strikes, 7 air swings, 6 crossings, 3 floor touches, 3 idles; every sound logged with its cause and checked against a real collider overlap or a real fast stroke): **0 sounds without a valid cause** (legacy values: 1). Clash sounds 166 -> 37, haptic pulses 276 -> 154, player whoosh 37 -> 23 (all on fast strokes), landed hits 12 = 12 hit sounds. Level 1 (60 s): clashes 23 -> 8, 0 invalid. Level 3 (60 s, mace): clashes 92 -> 67 (the boss swings two axes of two colliders each straight through a held mace; these are real contacts with its weapon), 0 invalid. A weapon touching the floor made no sound or haptic (floor is not a valid target).
- **Sound audit table (what plays, trigger, requirement now).**

| sound | old trigger | requirement now |
|---|---|---|
| player hit / heavy hit / variants (`HitDetection`) | OnTriggerEnter/Stay on any collider under a `MonsterController` | monster body collider, speed over the gate, damage dealt, one per swing contact, not during the entrance, `hitSounds` |
| weak-point hit clip | same | same, plus the weak-point collider |
| body-block clang (`NotifyDefendedHit`) | landed hit on a guarding monster | unchanged (landed hit only) |
| clash (`breathingsound.mp3`, L1 `wings_of_freedom` riser) | any `MonsterWeapon` collider touched at any speed, blade and haft separately, both swords separately | player weapon above the gate or the monster inside its strike window; one per contact; one sound across both weapons per 0.15 s |
| player whoosh (`WeaponSwingAudio`) | hand speed over 2.2 to 2.4 m/s for one frame, 0.18 to 0.45 s cooldown | the same trigger speed held for 0.04 s, cooldown at least 0.25 s, re-arm unchanged |
| guard brace (`defendBraceClip`) | every guard, including reactive guards raised against air swings | timed guards only (`braceSoundOnReactiveGuard`) |
| monster telegraph / swing / impact / growl | its own attack coroutine | unchanged: the monster's own action, never tied to a player collider |
| entrance (summon / rise / roar), phase, enrage, death, victory / defeat stings | their own events | unchanged |
| player damage hit + vocal | `PlayerHealth.TakeDamage` | unchanged (a monster attack that reached the player) |
| weapon pickup, breathing loops, heartbeat | their own components | unchanged |
- **Worth knowing.** The Level 2 and 3 clash clip is literally named `breathingsound.mp3` (it is the old `ribhavagrawal-swords-collide-230574.mp3`, renamed last pass, byte-identical). I did not rename it back. Level 2's monster holds its axe across the chest, so in a frozen-idle test a strike at the chest hits the axe (a clash), not flesh; a real fight moves it.

### Part 2: monster off centre and rotating side to side
- **Found (root cause, with numbers).** (1) The spawn is a fixed point per scene; a player standing 0.3 m off the play-space centre meant the monster spawned **0.30 m off the player's forward line** and 5.6 to 6 degrees off its aim; nothing ever corrected it. (2) `MonsterDefense.Turn` rotated at the full cap (90 / 120 / 150 deg/s) toward any aim error, however small, every frame, with no dead zone, so head sway made it hunt: free-yaw peak rate **90 / 120 / 117 deg/s, 6 / 9 / 7 direction reversals per 40 s** (L1 / L2 / L3). (3) Attacks that travel left the monster off the line: L1 root lateral 0.21 to 0.35 m one second after Spin Slash / Slash. `applyRootMotion` is already false in all three Animators; the rig faces the spawn point in all three scenes (rig yaw 0.0, bearing 0 deg), so that was not the cause. The hips (visual) sit up to 0.16 to 0.19 m beside the root during attacks because of the animation itself, not the root.
- **Built.** `BattleManager.spawnOnPlayerForwardLine` (spawn shifted by the head's horizontal offset from the rig origin, capped at 0.75 m, and facing the head). `MonsterDefense.dampedTurn` (aim error under 4 deg = no turn; a started turn runs to under 1 deg; rate = 4 x error, capped by `turnSpeed`; 360 deg/s^2 ease-in; never reverses through the target; no turning during wind-up, attack, stagger or flinch, entrance unchanged). `MonsterDefense.recentre` (while free, eases the root back onto the spawn line at up to 0.35 m/s once more than 8 cm off; the dead zone is wider than a standing player's 4 cm sway so the monster never chases it).
- **Before and after** (player head 0.3 m right / 0.15 m forward of the rig origin, head sway +-4 cm / +-6 deg, 40 to 60 s of attacks and guards):

| | lateral offset root mean / max | settled (free > 1.5 s after an attack) | free yaw peak / reversals per min |
|---|---|---|---|
| L1 before | 0.207 / 0.425 m | mean 0.201 m | 90 deg/s / 21 |
| L1 after | 0.057 / 0.229 m | **mean 0.003 / max 0.008 m** | 22 deg/s / 7 |
| L2 before | 0.182 / 0.290 m | mean 0.233 m | 120 deg/s / 38 |
| L2 after | 0.001 / 0.010 m | **mean 0.004 / max 0.010 m** | 0 / 0 |
| L3 before | 0.225 / 0.381 m | n/a (never idle 1.5 s) | 117 deg/s / 98 |
| L3 after | 0.034 / 0.169 m | n/a (never idle 1.5 s; 1 s after an attack: mean 0.05 m, max 0.10 m) | 15 deg/s / 0 |
  The during-attack maxima (L1 0.23 m, L3 0.17 m) are the attack travel itself (leaps and rushes), the under 0.15 m target holds whenever the monster is free. Spawn: 0.000 m lateral and 0.0 deg yaw error in all three (before 0.30 m / 5.6 deg). Personal-space clamp over the same fights: **0 head and 0 hand breaches** (min head distance 0.68 / 0.88 / 0.60 m against the 0.60 limit, hands 0.35 / 0.48 / 0.35 against 0.35).

### Part 3: blood (the pulsing vignette is off by default)
- `PlayerDamageVignette.edgeFlash` and `lowHpPulse` default **off** in the scripts and all three scenes (PlayerHealth's original brief full-rect flash is back, as before last pass); `FeelSetup` no longer forces them on.
- `HitBloodFx` rewritten: pooled ParticleSystem per monster (one material, one renderer). Droplets with gravity (6 m/s^2), 0.5 to 0.9 s life, 3.5 to 7.5 cm (x1.25 for full / weak-point hits), sprayed along the swing minus the part driving into the body; per-level colour (L1 dark red, L2 crimson with a purple cast, L3 dark red-brown). **18 / 22 / 26 / 36 particles** for tap / light / medium / full, weak-point hits at least 32 and at most 50. Drips: a wound on the struck bone (the emit point follows the bone) drips every 0.25 s for 2.5 s, 4 wounds at once. Death: 60-particle burst from the chest. A weapon clash throws 14 sparks (no blood). **No blood on a guarded hit (verified: 0 bursts)**, no decals, no pools. `Off / Sparks / Blood` kept.
- **Numbers** (Editor, noisy draw calls, renderer on vs off on alternate frames): burst sizes 22 / 26 / 26 / 36 at 1.6 / 3.0 / 4.6 / 9 m/s (live peak 17 to 37), death burst live peak 96 to 97, drips 22 to 33 per test, all gone 4 s after the last hit; **draw calls (three alternating on/off passes each; Editor counts are noisy, the first pass of a run reads highest): L1 +1.4 / +0.5 / -0.5 (an earlier run +1.7 and +2.3 while a burst was alive; +1 setpass), L2 +0.8 / 0.0 / 0.0, L3 +0.7 / -0.1 / -0.1**. One system with one material is at most one extra batch, but the +1 limit was exceeded in two noisy L1 passes; blood off costs nothing. Game-view captures at fight distance in Level 1's dark lighting read clearly (dark-red droplets out of the monster's flank; red damage numbers and the red telegraph horn stay distinct). Level 3 captures not taken.

### Part 4: fire glow around the body
- **Existing, not duplicated:** Level 3 has its own (`MonsterAxeFlames` weapon fire, `MonsterGlow` body emission, `MonsterSerpentAura` blue orbs): untouched. Level 2 already had weapon fire (`AxeFireVFX`, `SlamImpactVFX`), no body fire. Level 1 had none.
- **Built:** one shared `MonsterFireAura` (Levels 1 and 2 only), a per-level profile. One world-space ParticleSystem emitting from points that follow the skeleton (head, neck, chest, spine, hips, upper arms, forearms, thighs) on a shell sized from each bone's hit-volume capsule, on the existing additive flame material (`M_L2_FireAdditive`, so the shader already ships). Level 1 yellow-gold (orange outer, pale-yellow core), 40/s; Level 2 deep violet with a magenta core, 66/s. Flares on Heavy / Special / IdleSlam attacks (x1.8 / x2.0 rate, x1.3 size), brighter under the low-HP event (L1 35 %, L2 40 %), fades over 1.2 s on death. A faint body-emission pulse (MaterialPropertyBlock on `_EmissionColor`, adds to the material's own emission; L1's `warrok_MAT` got the `_EMISSION` keyword). Starts after the entrance; no lights, no shadows.
- **Numbers:** live particles calm / heavy flare / low HP: **L1 20 / 34 / 27, L2 36 / 65 / 50**, Level 3 own fire 20 (calm) and 20 (heavy). **Intensity order L1 < L2 < L3: L1 < L2 holds by particle count (20 < 36 calm, 34 < 65 flaring). L3 < L2 by raw count** (Level 3's own fire is 21 calm, 33 average / 49 peak on a heavy attack): its intensity is the glowing red eyes / mouth / skin cracks, the blue serpent trails, the burning axes and the enrage embers, none of which a particle count sees. In the Level 3 captures (calm and a Twin Whirlwind wind-up) L3 reads clearly the strongest of the three; Level 2's violet is a soft cloud around the body. I did not trim L2 to force the count order; say so if you want the L2 flare lowered (`MonsterFireAura.flareRate` / `lowHpRate` on the prefab). **Draw calls +1.0 in both** (limits +2 / +3). Death: 20 -> 4 -> 0 particles at 0.5 / 1.2 / 2.5 s (L1), 40 -> 8 -> 0 (L2), emission pulse back to base. **GC allocation: 0 bytes difference with the aura on (medians equal, three alternating rounds).** The `velocityOverLifetime` mode warning is avoided (only `Emit` with per-particle velocity). L1's yellow-gold is smaller and rarer than L3's roaring axe flame, so they do not read alike. Captures (900 px Game-view, Editor): in the dark Level 1 the flames read as soft orange glows around the head and flank, subtle at fight distance, with blood still visible over them and the red telegraph horn not hidden; Level 2's reads as violet / magenta. Clipping during Heavy attacks was checked only by capture of one wind-up per level; readability in a headset is not verified.

### Part 5: breathing
- **Where:** `Assets/Audio/SFX/Player/breathingfast.mp3` (moved last pass, GUID kept). Present on every `PlayerExertionAudio` (fast source created at runtime as the child `PlayerBreathingFastAudio`, clip assigned, `AudioGainFilter` x18), all three scenes; playing in Play mode in all three (fast volume 0.300 = cap).
- **Real numbers (backend, read-only, 2026-10-08, 455 hits in 16 sessions):** mean hit swing speed **Easy 6.0 m/s (peak 24.8), Medium 2.17 (peak 5.2), Hard 7.5 (peak 28.9)**; full-power share 47 % / 1 % / 58 %; in real sessions only 0.25 to 1.2 contacts per second were logged (air swings are not logged).
- **Thresholds chosen** (from simulating the real histogram: 300 runs each): count at **2.0 / 1.5 / 2.0 m/s**, full weight at **6 / 3.5 / 7 m/s**, **19 / 18 / 20 weighted swings per 30 s** window (was 14 per 12 s); fast tier (0.70) still HP < 25 % too. Model time-to-fast at 0.8 / 1.0 / 1.2 swings per second: **Easy 23 / 18 / 16 s, Medium 34 / 24 / 20 s, Hard 22 / 18 / 15 s** (before: Medium never; Easy and Hard 10 to 12 s at 1 to 1.2 swings per second, too fast for the 20 to 30 s target). Editor check feeding real-histogram speeds into the exertion model (injected, because the Editor's hand-speed smoothing under-reads transform-driven swings): fast tier at 17 to 24 s in L1 and 17 to 24 s in L3 (0.8 to 1.2 swings per second), 20 to 25 s in L2 at 1.0 to 1.2 swings per second (**L2 at 0.8 per second did not reach it in 60 s**: real Medium swings are slow).
- **Levels (dBFS, computed from the clips and the source volumes, not by ear):** fast clip peak -25.8 dB, after x18 and the 0.30 cap peak **-11.2 dB, RMS -33.8**: no clipping (the limiter never engages: 0.048 x 18 = 0.86 at full volume, 0.26 at the cap); heavy loop at its cap peak -11.1, RMS -30.0. Background music peak -30.6, RMS -43.4 (L1) and -43.6 (L2/L3): **breathing RMS sits 10 to 13 dB above the music**. Combat sounds heard: hammer hit peak 0 dB / RMS -6, sword hit -4 / -23, mace -3 / -19, clash -9 / -26 (L2/L3) and -7 / -20 (L1), monster swing -2 to -6 / -21 to -22, player damage -6 / -24: **all louder than the breathing except the Level 2 monster's wind-up cue (RMS -32, 3-D, 1.5 m minimum distance), which is level with it (breathing -30 to -34)**, so for that one quiet cue breathing is not below. Victory / defeat: breathing is cut in 0.23 to 0.27 s. Calm: silent (0.000).

### Part 6: hit feel
- **Hit-shake:** `MonsterHitShake` (one component, all three prefabs, `shakeEnabled`). Damped shake along the swing direction on the **hips bone** (the visible body), applied after the Animator and after the personal-space clamp, taken off at the start of the next frame (cannot accumulate). **Tap 0.4 cm, light 1.0, medium 2.0, full 3.5 cm (cap 4 cm), weak point x1.25; 72 ms (light) to 120 ms (full), 1.5 cycles**; x0.25 while the monster winds up or attacks; never on a guarded hit, the killing blow or during the entrance. Measured peak equals the set amplitude (1.0 / 2.0 / 3.5 cm) in all three levels; **the root and rotation never move because of it** (0.0 cm in L2/L3; L1's root moved 7.1 cm with the shake off and 7.4 cm on, so it is not the shake; I did not isolate what moves it). Caveat: the hit volumes are children of the bones, so for those 0.1 s they ride with the hips by the same few cm; the root, AI, range checks and hit logic never see it.
- **Idle shaking found and fixed?** Measured the idle monster for 6 s with and without head sway: no shaking in any level (root acceleration jitter 0.00 mm/frame^2 head still; hips (animation) 0.31 / 0.27 / 0.02 mm/frame^2; yaw travel 1.2 to 1.6 deg/s while the head sways, no reversals, personal-space clamp 0 push frames). The visible side-to-side turning was the fight-time turn code (Part 2), not an idle shake. The new recentre first chased the 4 cm sway (0.39 mm/frame^2) and was widened to an 8 cm dead zone.
- **Layered hit feedback:** hit-shake, blood, hit sound, haptics and damage number each fire on a landed hit and each has its own toggle (`MonsterHitShake.shakeEnabled`, `HitBloodFx.mode`, `HitDetection.hitSounds`, `CombatFeedback.haptics`, `CombatUIController.builtInDamageNumbers`); verified in all three levels that turning one off removes only that layer.
- **Engagement items:** combo counter + XP bonus **DONE** (`XPManager`, combo window 2 s, +8 XP per step, capped; HUD "Combo xN"); stagger **PARTIAL** (guard-break stagger and a poise flinch with a 0.6 s cooldown on swings at or above a per-level minimum tier exist; no separate full-tier stagger); taunt **NOT DONE** (needs a clip that does not exist on disk, and a clip-less taunt would only be a sound).

### Regression checks (re-run at the end)
- Full-body coverage: **13/13, 12/12, 13/13** hit volumes produce a landed hit (L1 and L3 needed a second approach angle for 1 and 2 volumes whose first line ran through the monster's own weapon). Body block: result Blocked, block clang 1, hit sound 0, clash 0, no blood. Parry: Level 3 `CurrentAttackParried = True` (Levels 1 and 2 have `allowParry` off, unchanged). A hit landed during a wind-up left the attack running in all three levels (Slash / Horizontal Sweep / Alternating Chops still the current attack right after the hit; flinch rules untouched). Animator controllers, `Monster_GuardPose`, `SessionReporter`, `web/` and the Level 3 flame / glow / aura scripts are byte-identical to the backup branch; prefab diffs are additions only. All three scenes still carry the production `baseUrl`; the harness redirects at runtime only.
- Not verified: anything on a headset (haptics, audio by ear, 72 Hz look of the shake, the fire and blood in headset lighting), IL2CPP / Quest performance, Level 3 blood and fire captures.

---

## Breathing fix, daily goals, Level 1 animations (2026-10-10, branch `daily-goals`, checkpoint commit `fcc7903`)

**How it was driven.** Unity over MCP (HTTP route), backend tests and a local backend on port 4010 for the goal feature (one disposable player, `ZZTEST`, deleted afterwards; the real players' 49 sessions / 9,804 events were checked before and after). Every Unity number below is an **Editor / Mono simulation, not a headset**. The Editor's audio output is not captured (`AudioListener.GetOutputData` reads -120 dBFS), so nothing about how loud anything is on a Quest has been heard.

### Part 1: breathing not audible on the headset
- **Found (1A, root cause).** The wiring was fine, the level was not. Checked live after loading each level from the menu (`BreathingVerification menu | level`): exactly one `PlayerExertionAudio`, one AudioListener, `AudioListener.pause` false, volume 1.0, both sources (`PlayerBreathingAudio`, `PlayerBreathingFastAudio`, children of `OVRCameraRig`) present, not duplicated, clips assigned, `spatialBlend 0`, `spatialize false`, not muted, no mixer, enabled, the trigger fires after a menu-to-level load, tier changes and the volumes follow. The fast clip itself sat at **RMS -48.6 dBFS (peak -26 dBFS)** and was lifted only by a runtime x18 gain filter (`AudioGainFilter`, capped at volume 0.30): about -34 dBFS RMS in the mix, 10 to 20 dB below every hit sound. On a phone-sized speaker that is inaudible. (Android import overrides are set on both new clips: PCM, DecompressOnLoad, mono, `preloadAudioData`.)
- **Built.** Both clips re-levelled offline (100 Hz high-pass, RMS target, look-ahead limiter at -1 dBFS): `breathing_fast_v3.wav` RMS **-16.1**, peak -1.0; `breathing_heavy_v3.wav` RMS **-19.1**, peak -1.0. The runtime gain filter is off (`fastGain` 1, no filter added). Source volumes: heavy 0.55 to 0.8, fast 0.9.
- **Levels (1C, dBFS RMS after source volume; computed from the files, not by ear).** Fast breathing at 0.9 = **-17.0 RMS, peak -1.9**. Heavy at 0.8 = -21.0. Music: Level 1 clip RMS -23.2 x source volume 0.08 = **-45.2**; Levels 2 / 3 music volume 0.03 = about -43.6. So fast breathing is **26 to 28 dB above the music** (target 12) and cannot clip (peak -1.9). Against hit sounds (after the `AudioSource` volume): Level 1 hammer hit -7.5 (**fast breathing 9.5 dB below**, target 6 to 10); Level 2 heavy hit (`impactful-damage`) -9.4 (7.6 dB below) and Level 3 variant of the same clip -9.4; but the quietest regular hit clips are lower: Level 2 sword slice -29.6, Level 3 mace -21.7, so against those the breathing is **12.6 / 4.7 dB louder** in RMS (equal in peak, -2.2 vs -1.9). That is the honest trade: the brief asks for "clearly audible" and "6 to 10 dB below the hit sounds" at once, and in Levels 2 and 3 the regular hit clips are quiet. If the headset says it is too loud, lower `PlayerExertionAudio.fastMaxVolume` per scene (0.6 is -3.5 dB); nothing else needs to change.
- **Exertion model (1D).** Counts swings ATTEMPTED (controller speed over `triggerSpeed` 1.2 m/s with re-arm 0.8), weighted by speed (0.75 at the minimum, 1.0 at 4 m/s), into a leaky accumulator: `effortPerSwing` 0.029, slow leak while swinging (`activeDecaySeconds` 200), fast drain after 2.5 s of rest (8 s to empty). Tiers with hysteresis: medium on at 0.28, fast on at 0.65 / off at 0.50; HP under 25 % forces fast. Per-level `effortScale` (Level 2 x1.2: real Medium swings are slow, median 2.1 m/s). Real swing speeds: backend `/api/events?type=hit` 2026-10-09, Easy 83 hits (mean 6.0 m/s), Medium 257 (2.2 m/s), Hard 209 (7.5 m/s), drawn from the live histogram by `BreathingVerification` at about 1 swing per second.
- **Timings (model fed with those speeds, 45 s of swinging then 28 s of rest).** Level 1: medium **11.6 s**, fast **26.8 s**; Level 3: **10.8 s** / **26.0 s**; Level 2: **15.9 s / 34.5 s before the x1.2 scale, 9.9 s / 31.3 s after it** (one random draw of the live Medium histogram; draws vary by a few seconds, the fast tier lands a little over the 25 to 30 s aim). HP 20 %: fast tier at once (0.0 s). After stopping: fast -> medium at 6.1 to 8.2 s, calm at 14.3 to 16.3 s, volumes faded 4 s later.
- **Hardware test (1B).** Hold **both grips for 3 s on the main menu** of a Development build: a small panel appears with the readout (exertion 0 to 1, tier, each source's volume, changing only on change) and a **Test breathing** laser button that plays medium then fast for 5 s each, bypassing the exertion logic (`PlayerExertionAudio.HardwareTest`). It logs only on a tier change, with the fixed prefix `[StrikeFitBreath]` (Unity's logcat tag is `Unity`): `adb logcat -s Unity:I | grep StrikeFitBreath` (`[StrikeFitBreath] TEST tier ...` lines come from the test button and end with the measured listener level). Components: `Combat/DevDebugGate.cs`, `UI/BreathingTestPanel.cs`, `CombatUIController` (readout, same gate), `Editor/MenuSceneBuilder.BuildBreathingTest`.
- **Not verified / for the headset.** Whether the fast tier is "clearly audible on the Quest's speakers at 50 % system volume". That stays **unconfirmed until you run the test button**: from here the audio can only be measured as numbers in the files.

### Part 2: daily goal
- **Backend (additions only).** Table `daily_goals` (`schema.sql`, `CREATE TABLE IF NOT EXISTS`, applied on every boot so a Render deploy creates it), unique `(player_name, goal_date)`, local day Asia/Kathmandu (`?tz=` overrides). `PUT /api/players/:name/goal`, `GET .../goal/today`, `GET .../goals?days=30`, `GET /api/export/goals.csv`, SSE `goal_set` / `goal_met`, `GET /api/overview` gains `goalsToday`, achievements gain `goalBonusXp` / `totalXpWithGoals` / `levelWithGoals` / `goalStreak` / `goalsMet` and the badges Goal Getter (3 days) and Goal Week (7 days). Bonus 75 XP, once per day (conditional `UPDATE ... WHERE completed_at IS NULL`). Presets from real data (median daily totals of players with 3+ active days) with labelled fallbacks. Existing routes, fields and response shapes unchanged; only parameterised queries; `DATABASE_URL` never printed. Details in `web/backend/README.md`.
- **Unity.** `Manager/DailyGoal.cs` (static state), `UI/PlayerNamePicker.Goal.cs` (goal step after the name, before Start: existing goal + progress + Change, or 4 metric chips, 3 presets, -/+ stepper, Skip today, "Stop if you feel dizzy or in pain."), `SessionReporter` (goal fetch / PUT with the 5 s timeout, one pending goal in PlayerPrefs retried at the next battle start, telemetry `goal_set` / `goal_progress` through the existing queue), `BattleStats.ActiveMinutes`, `CombatUIController` (HUD line "Goal 42 / 100 kcal (est.)" updated only when the text changes, result line "Goal reached! +75 XP" or percent with a local fallback; the bonus joins the XP count-up only after the backend confirms).
- **Web.** `components/fitness/DailyGoal.jsx` (Today's goal card: progress ring, edit control, bonus XP, 14-day strip, goal streak), "(incl. goal bonuses)" on the XP / level bar, Overview "Goals met today", Recap goal line, live updates over SSE, honest empty states. No new colours or fonts; every kcal figure keeps "estimated".
- **Numbers.** ZZTEST end to end on a local backend against the real Neon database: invalid input rejected (400 x6), PUT 201 then 200 on change, 409 once met, bonus **not repeated** by a second session, `goal/today` **330 ms warm** from the laptop (one Neon round trip; first cold call 1.75 s; the 500 ms target holds warm, a Render instance next to Neon is faster, not measured), SSE `goal_set` x3 + `goal_met` x1, goals.csv 1 row. Unit tests: `npm test` in `web/backend` **39 pass** (new `test/goals.test.js`), frontend **9 pass**, `vite build` ok. Unity vs backend for the same fight (Level 3, XP goal): HUD sampled mid fight 557, result panel 877 / 100 (bonus 75 XP after the server answered at +6.7 s, mostly the laptop-to-Neon round trip), backend `goal_progress` value **877**: difference **0**. Level 1 kcal run (target 5): HUD 3.7 at the end, headset counter 3.8, `goal_progress` telemetry 3.898, result panel 3.9 / 5 (78 %); backend `goal/today` after the session was posted: **3.9 kcal, 78 %, 8 full swings, 1 active minute, 893 XP**, the same 3.9 / 8 / 1 / 893 as the headset counters: difference **0.00** (3.898 vs 3.9 rounded). The Level 3 XP run: HUD mid fight 557, result panel 877, `goal_progress` 877.
- **Offline.** `SetGoal` returned in 1 ms with the backend down, pending goal retried at the next battle start (4.1 s) and accepted.
- **Limitation (also on the card).** There is no login: **anyone who knows a player's name can set that player's goal.** Rate limit (30 changes / 10 min / IP) and the target caps are the only protection. kcal is an estimate (MET method), never a measurement.
- **Web card, looked at.** Headless Brave against the local backend, 1280 px and 390 px, goal met and goal in progress: no horizontal scroll, no console error, ring, bonus line, 14-day strip, estimate / no-login note and the "(incl. goal bonuses: 75 XP)" line on the level bar all render; the Change control shows while the goal is open and is hidden once it is met (a met goal cannot be reopened). The screenshot caught a misleading red cross for today's open goal; fixed (today shows an outlined "..." until the day is over).
- **Not verified.** The goal panel on a headset (it was driven through the real laser pipeline in the Editor only), the card on a real phone, and Render / Vercel behaviour (nothing was deployed from here).

### Part 3: Level 1 animations (cosmetic; attack damage, telegraph, cooldown unchanged)
- **Earlier Level 1 items.** Stagger on strong hits: **DONE** (existing poise flinch, tier gate and 0.6 s cooldown, now with a heavier clip on a full-power swing). Taunt after 8 to 10 s without a hit: **DONE** (new). Idle variants: **DONE** (new, two procedural variants). Spacing movement (step in / back): **NOT DONE** (no suitable clip on disk, see the table). Glow-up moments: **DONE earlier** (Level 1 body fire flares on Heavy / Special). Yellow fire glow: **DONE earlier** (`MonsterFireAura`). Blood: **DONE earlier** (`HitBloodFx`).
- **Built (`Editor/Level1AnimationSetup.cs`, idempotent; `Monster/MonsterCosmetics.cs` on `Monster_Easy_HipHop`).** New states in `Monster_L1_Sword.controller` (Any State still goes only to Death): `IdleB`, `IdleC` (baked from `Monster_SwordIdle` by Humanoid muscle offsets: head look left / right; shoulder roll and head nod; 3.7 s clips played at 2.4x = 1.5 s), `Taunt` (the HipHop model's own Hip Hop Dancing clip, 1.5x, 2.9 s), `HitLeft` (existing Hit clip, Mirror on), `HitRight` (Hit), `HitHeavy` (Great Sword Impact, previously unused, with legs and hip height blended 60 % toward the idle so it is a hard flinch, not a drop to the knees). Idle -> variants and variants -> Hit / Defend / every Atk_ / Idle with the same triggers and durations as Idle's own; entering transitions are interruptible by the destination so a due attack never waits for a blend. Procedural (visual only, chest bone, after the Animator): breathing sway 1.4 deg and a 5 deg lean away from a hit. Every part has a serialized toggle: `idleVariants`, `taunt`, `directionalHits`, `heavyHitReaction`, `sway`; timings are named fields (`idleVariantInterval` 6 to 11 s, `tauntAfterNoHit` 8 to 10 s, `tauntCooldown` 14 s, `heavyCooldown` 6 s, `variantMinTimeBeforeAttack` 0.7 s, `tauntMinTimeBeforeAttack` 1.2 s). `MonsterController.SecondsUntilAttackReady` (read-only) is the only new member read from the fight; `MonsterAnimator.NextHitTrigger` picks the hit variant (Levels 2 and 3 have no such triggers and keep the plain Hit).
- **Proof (Level 1, 100 s simulated fight, real weapon through `HitDetection`, speeds from the live histogram).** 27 monster attacks, **27 reached their Atk_ state in the same frame as the attack began (0 frames, resolution 33 ms)**, from Idle 22, Defend 1, Hit 2, HitHeavy 2; none was delayed by a variant (calm run: 11 attacks forced or due, all 0 frames, 5 of them started while a Taunt played, others during an IdleB blend-in and an IdleC). A hit during a wind-up never cancelled it: 22 landed during attacks, **0 cancelled**; 3 forced full-speed hits at a wind-up: Hit state not entered, the attack ran to the end. Hit variants: left side -> `HitLeft`, right -> `HitRight`, centre -> `Hit`, full-power swing -> `HitHeavy`, a second full swing inside 6 s -> plain `Hit`, the next after 7 s -> `HitHeavy`; each reached its state 33 ms (one frame) after the hit. The baseline `ReturnToIdleIfAlive` timer (0.35 s, unchanged) ends every hit clip at about 0.45 s, so the heavy clip shows its first 0.35 s only. Hips height (sitting check): Idle 0.84 m, IdleB / IdleC 0.84 to 0.86, Taunt 0.78 to 0.84, HitHeavy 0.62 (was 0.37 before the blend), Hit 0.77 to 0.80, Defend 0.75 (no state sits). Idle variants played 2 times in the 100 s fight (the Level 1 monster is in Idle only 0.4 to 1.9 s between attacks, so they appear in the gaps and when the player is passive); 6 variants and 5 taunts in 60 s of calm (variants 0.8 to 1.5 s long when cut or complete, taunts 2.9 s), 0 delayed attacks.
- **Cost.** Per frame (Editor, profiler markers, alternating 5 s windows, component on / off): `Animators.Update` **137.9 us on / 140.2 us off** (no measurable difference), `MonsterCosmetics.Update` 8.0 us + `LateUpdate` 5.7 us. No per-frame allocation in the component (tier compare by first letter, cached trigger checks, no `FindObject` in `Update`).
- **Missing clips (not on disk).** Hit reactions from the side (left / right are the same Hit mirrored, plus a lean), death variant, step in / step back, real idle variants (the two variants are procedural offsets of the existing idle). Same export / import settings as the existing monster clips: Mixamo FBX Binary, **Without skin**, 30 fps, keyframe reduction as in `Great Sword Impact.fbx` (Humanoid, avatar "Create from this model", resample curves on, KeyframeReduction, Loop Time off, root lock off). Search terms (and alternatives, unverified): *Standing React Large From Left / Right* (alt: "Hit Reaction Left", "Sword And Shield Impact", verify); *Standing Death Forward 01* or *Dying* (alt: "Falling Back Death", verify); *Standing Walk Back* (alt: "Walking Backwards", verify) and *Standing Walk Forward* (alt: "Sword And Shield Walk", verify); *Sword And Shield Idle* or *Offensive Idle* (alt: "Breathing Idle", verify).
- **Not verified.** How any of it looks in a headset at fight distance (the captures were not taken for this part; transitions were traced, not watched).

### Part 4: regression checks (re-run at the end) and handoff
Each row says how it was checked this pass. "Diff" means `git diff e8c60c9` shows the files behind it unchanged; "run" means a Play-mode harness was run against the final code.

| item | how | result |
|---|---|---|
| Level select menu | run `MenuVerification menu` + `roundtrip` (real laser pipeline) | all three level buttons + Exit hit by the laser, scene loads, label "Loading..." and buttons locked while loading |
| Exit Game | run (roundtrip, menu and each Victory panel) | confirm opens, content locked while open, No closes it and unlocks. Not re-run: the Yes path (`MenuVerification exit`), `SceneFlow` untouched by diff. `Application.Quit` on a Quest remains untested |
| Play Again flow | run (roundtrip x3 levels) | Victory -> Play Again -> new BattleManager, fresh fight (HP full, XP 0, retry entrance on L2) -> Defeat -> Level Select -> menu fingerprint equals the first load (L1 / L3 particle count differs by 2 to 4 ambient particles, as before) |
| Build Settings order | run (`EditorBuildSettings`) | MainMenu, Level1_Easy, Level2_Medium, Level3_Hard, Results disabled |
| Clash, parry, body coverage, body block | run `PolishVerification regress` x3 | volumes producing a landed hit 13/13, 12/12, 13/13; clash + one clash sound on a weapon-on-weapon contact; L3 parry `CurrentAttackParried = True` (L1 / L2 `allowParry` off, as before); a guarded body hit: Blocked, block sound, no hit sound |
| Hit hits never cancel a wind-up | run `CombatVerification windup` x3 | 6 / 10 / 13 attacks: Hit state not entered, damage applied, attack resolved; plus the L1 fight (22 hits during attacks, 0 cancelled) and 3 forced full-power hits |
| Weapon surface proxy, zone guards, Level 3 entrance and effects | diff | none of `WeaponSurfaceProxy`, `MonsterDefense`, `MonsterEntrance*`, `BossPhaseController`, `MonsterGlow`, `MonsterAxeFlames`, `DragonHazard` or the Level 2 / 3 prefabs and controllers changed. Not re-run in Play mode (stated, not hidden) |
| Hit-shake | run `PolishVerification shake` (L1) | peaks 1.0 / 2.0 / 2.0 / 3.5 cm = set amplitudes, root moved 0.0 cm, each layer toggle removes only its own layer. L2 / L3: diff only |
| Blood | run `PolishVerification blood` (L1, alone in its Play session) | bursts 22 / 26 / 26 / 36, none on a guarded hit, death burst live peak 97, +0.5 draw calls. A first run that followed other jobs in the same Play session showed 0 for the last two strikes because the earlier jobs had left the monster in a test state (reactive guard off, low HP); not a regression, so the blood job must be run alone |
| Fire glow | run `PolishVerification fire` (L1) | live particles calm 20 / heavy 31 to 37 / low HP 27 to 31, death 19 -> 3 -> 0 at 0.5 / 1.2 / 2.5 s; draw calls +1.7 (limit +2, Editor noise). L2: diff |
| No post-processing, no new realtime lights or shadows | diff | no Light component added to any scene (`git diff` of the scenes), menu has 0 lights, no volume / post-processing asset added |
| No per-frame allocations, no `Find*` in `Update` | run `PolishVerification alloc` x3 + grep | median GC per frame L1 29.4 KB, L2 29.2 KB, L3 30.1 KB, identical with the features on and off and to the previous pass (the Editor's own baseline, not new); no `Find*` call in any new script |
| SessionReporter | grep + run | `baseUrl` is still `https://s-t-r-i-k-e-f-i-t.onrender.com` in the script and all four scenes; wake ping and timeouts untouched; goal calls use `SendNow` with the same 5 s timeout and answer through a callback; `SetGoal` returned in 1 ms with the backend unreachable; nothing in gameplay waits for the network |
| Backend routes, fields, shapes | diff + `npm test` 39 pass | `server.js` deletions: 2 lines (the CORS `methods` array gained PUT, the export map gained `goals`); everything else added. Parameterised queries only; `DATABASE_URL` never printed |
| Web colours, fonts, "estimated" | grep + screenshot | no hex colour or font in the new component (existing tokens only), every kcal figure labelled "(est.)" / "estimate" |
| Test data | run | one disposable player `ZZTEST`, deleted: the database reads 49 sessions / 9,804 events as before the pass, 0 ZZTEST rows; port 4010 server stopped; Editor PlayerPrefs cleaned |

**Headset-unconfirmed, ranked (do these in order).**
1. **Breathing audibility** (menu: hold both grips 3 s, press Test breathing). Is the fast tier clearly audible at 50 % system volume, and not unpleasant? If too loud lower `fastMaxVolume`; if inaudible, the file level is already -16 dBFS so check the Quest's own volume and the listener (the test prints the measured listener RMS).
2. Whether the exertion tiers feel right with real controller swings (the real controller reports speeds the Editor cannot; the model was fed the live histograms).
3. The goal panel: reading it with the headset on, laser clicks on the chips, the Start button still reachable; then the HUD line and the result line.
4. The Level 1 cosmetic animations at fight distance: do the two idle variants and the taunt read as intended, is the heavy flinch (hips 0.62 m) a hard flinch and not a collapse.
5. Haptics and hit-shake feel (unchanged this pass, still never felt).
6. `Application.Quit` from Exit Game on a Quest.
7. Real-network goal latency on a Render instance (measured here from a laptop to Neon: 0.3 to 1 s).

**Deploy (backend first, then the app).**
1. Merge `daily-goals` into the branch Render and Vercel build from (not visible from here; likely `main`). Nothing was pushed or merged by me.
2. Render (root `web/backend`): the deploy boots `server.js`, which applies `schema.sql` (`CREATE TABLE IF NOT EXISTS daily_goals` + two indexes) before serving, so no manual migration; `npm run db:schema` against Neon does the same by hand. No new environment variable. CORS now allows PUT: `ALLOWED_ORIGINS` is unchanged. Check `GET /api/health`, then `GET /api/players/<a real name>/goal/today` (200 with `goal: null`).
3. Vercel (`web/frontend`): rebuild so the Today's goal card and the Overview / Recap lines ship; no env change. Open a player page and confirm the card renders its empty state.
4. Then build and install the APK (Development build for the breathing test). Until the backend is updated the headset still works: the goal step shows "offline", a goal is kept as pending and retried at the next battle start.
5. Rollback is safe: the new table and routes are additive; old clients ignore the new fields.

---

## 2026-10-16: web engagement pass (branch `web-engagement`, frontend + backend only, Unity untouched)

Branch `web-engagement` was cut from `main-page` (3818186). **Nothing is committed or pushed** (the project owner commits). All numbers below were measured against the real Neon data through a local backend (port 4010) and headless Brave at 390 / 768 / 1440 px; nothing was seen on a phone. Another Claude session worked on the same checkout (story page, leaderboard, Unity menu); its files are not described here.

### What shipped
- **Part 1, daily and weekly challenges.** `web/backend/challenges.js` (config array with a "how to add one" comment, deterministic rotation seeded by the local date via FNV-1a, targets scaled from real medians, named safety caps, rest-day rule), `engageStore.js` (SQL), table `challenge_completions` (`UNIQUE (player_name, challenge_key)`, `challenge_key = id@period-start`). Progress is computed on read from finished sessions that started in the period (the streak's rule); a completion is stored once with `INSERT ... ON CONFLICT DO NOTHING`, evaluated when a session finishes (all three finish paths) and defensively on every read of `/challenges`. SSE `challenge_done`. New achievements fields: `challengeBonusXp`, `challengesCompleted`, `totalXpWithBonuses`, `levelWithBonuses` (existing `level`, `totalXpWithGoals`, `levelWithGoals` untouched). 3 daily (easy / medium / stretch, different metrics) + 1 weekly; 16 definitions, 7 metrics (full-power swings, estimated kcal, sessions, active minutes, best combo, clear Level N, clear taking at most X% damage).
- **Part 2, calendar and weekly recap.** `GET /api/players/:name/calendar?weeks=26`, `GET /api/calendar` (site-wide), `GET /api/players/:name/week[?week=last]`. GitHub-style calendar (roving-focus grid, arrow keys, tooltip, text list fallback, sessions / kcal (est.) toggle) on the player page and the Engagement page. "Your week" card with a Last week switch, "vs previous week" only when that week had a session, PNG export through the recap card's `renderPng`.
- **Part 3, challenge a friend.** Public `/challenge/:sessionId` (`GET /api/challenge/:sessionId` and `/check?name=`). Link carries only the session id. "Beat" means a strictly higher score on that level by a finished fight of the typed name, never the challenged session itself. Tips are from design facts (`lib/engage.js BEAT_TIPS`). Share button (Web Share, else copy link) on the session page and the recap card. Compare page: Rivalry card (wins per level by best score, best-score gap).
- **Part 4, mastery stars and next step.** `engage.js MASTERY` (comment "tuning in progress, set from playtest data"): star 1 cleared; star 2 cleared losing at most X% health; star 3 cleared within T seconds or with R full-power share. Starting values are the scenes' silver medal limits (L1 40% / 50 s, L2 55% / 55 s, L3 50% / 80 s, themselves from 8 / 4 / 2 victories) and **unsourced** full-power ratios 0.35 / 0.30 / 0.25. New `mastery` block on the achievements response. Journey strip, per-monster stars on `/monsters?player=`, and a "Next step" card ("Suggested by simple rules, not AI"; order: rest note, unplayed level, nearest star, level played but not won, open challenge, score to beat).
- **Part 5, best-run ghost.** `GET /api/players/:name/best?level=&before=` (one query: highest-score earlier decided fight with a score, its swing and monster HP samples). Replay: faint dots and dashed HP line on the same time axis, a readout of both runs at the cursor, a summary line ("26.1 s faster to clear, median swing speed down 25%"; **median**, because stored swing speeds include outliers such as 36 and 62 m/s against the game's 8 m/s cap), a checkbox to hide it, clean fallbacks (no earlier run, no events, request failed).
- **Part 6, feed and cheers.** `GET /api/activity` (<= 20 rows, names and event types only, derived from sessions, `daily_goals`, `challenge_completions` and the badge rules for the 8 most recently active players, 30 s cache rebuilt on finish events). Cheers: `POST /api/live/react { sessionId, kind: fire|power|respect }`, validated, live-only, per-IP token bucket (burst 5, 1 per 2 s), global bucket (10 per s), nothing stored, SSE `reaction`; `GET /api/live/reactions` reports `enabled`; env `REACTIONS_ENABLED` (default true; false returns 503 and hides the buttons). Buttons and floating icons + counter on the Live Arena card and on `/broadcast` and `/watch` over the video. The headset does not receive them.
- **Part 7, onboarding and calm mode.** Five-step tour (Overview, Live Arena, Players / player page, Challenges, Leaderboard), spotlight moved with transform, keyboard (arrows, Esc, focus trap), flag `strikefit.tour.done`, reopened from the header **Guide** menu. **Calm mode** switch in the same menu (`strikefit.calm`, defaults ON when the OS asks for reduced motion): `useReducedMotion()` now includes it, so confetti, tilt and embers stop, and `<html data-calm>` hides CSS-only motion (cheers, portrait pulses).
- **Also fixed on the way.** `liveStore` never subscribed to `goal_set` / `goal_met` (the goal card's live refresh did nothing); now it does. Landing hero blobs moved with the hero's height (Lighthouse CLS 0.215): fixed rem offsets in `styles.css` (Hero.jsx untouched), CLS now 0.007.

### New routes, tables, env
Routes (all additions): `GET /api/players/:name/challenges`, `/challenges/history?days=`, `/calendar?weeks=`, `/week`, `/best`; `GET /api/calendar`, `/api/activity`, `/api/challenge/:sessionId`, `/api/challenge/:sessionId/check?name=`, `/api/live/reactions`; `POST /api/live/react`. SSE: `challenge_done`, `reaction`. Table: `challenge_completions` (+2 indexes). Env: `REACTIONS_ENABLED` (optional). Rename keeps completions with the player (same pattern as `daily_goals`).

### Verified (what was run and seen)
- `npm test` backend **66 / 66** (39 before this pass; +12 challenges, +6 engage, +9 from the other session's leaderboard), frontend **17 / 17** (9 + 8). `vite build` passes.
- **ZZTEST end-to-end, local backend on the project database: 31 / 31.** Live session from events, reaction 202 + received over SSE, invalid kind 400 / unknown session 404 / finished session 409, per-IP limit (never more than 5 accepted in a burst, then 429, one more after ~2 s), nothing recorded while live, finish -> 2 challenges completed and recorded, `challenge_done` once each over SSE, 8 concurrent reads created no extra rows, `challengeBonusXp` equals the stored bonuses and is counted once in `totalXpWithBonuses`, week / calendar / best / challenge card / check routes, activity feed. `REACTIONS_ENABLED=false` -> 503 and `enabled:false`.
- **Row counts** (sessions / events / daily_goals / session_feedback / challenge_completions): before **56 / 11,873 / 1 / 1 / 0**, after **56 / 11,873 / 1 / 1 / 1**. The one extra completion (`d-clear-l1@2026-10-15`, ZZTEST) was created by the other session's test fight and was left for them to delete; mine (2 per run, 3 runs) are all gone. **Deleting a session does not delete its challenge completions** (no cascade; no route deletes them), so a deleted ZZTEST fight leaves its row behind.
- **Warm route timings** from this machine (Neon is ~280 ms round trip away, Render sits next to it): challenges, history, calendar, week, best, challenge card, check 0.29 s; site calendar 0.38 s; activity 1 ms cached; achievements 0.9 s (no extra sequential round trip was added; it was ~0.9 s of 3 sequential rounds before).
- **Headless Brave, 0 console errors or warnings, 0 px horizontal scroll** at 390 / 768 / 1440: Overview, player page, session replay with the ghost, `/challenge/:id`, Live Arena with a simulated live fight (cheers: icons float, counter 3), plus `/watch` (390, 1440), Engagement, Compare, Monsters (1440). Tour: 5 steps by keyboard, Esc skip stores the flag, no reopen on reload, Guide reopens it, dialog inside the viewport at 390. Calendar: one tab stop, arrows move a week, tooltip and aria-label, list fallback. Calm mode: switch sets `data-calm` and persists; OS reduced motion defaults it on; no floating icons, counter still counts; portrait tilt transform none.
- **Bundle:** initial JS the landing page loads **131.8 KB -> 133.0 KB gzip (+1.2 KB)**, CSS 19.1 -> 19.6 KB gzip. New code is lazy: ChallengeView 2.8, Tour 2.3 KB gzip; PlayerView 9.7, DashboardApp 10.8, SessionDetail 8.3, Overview 8.3 KB gzip (all already lazy).
- **Lighthouse mobile, landing page** (Lighthouse 12.8.2, Brave headless, simulated throttling, static server with brotli like Vercel): performance **99 / 99 (88 on a cold first run) after, 89 / 89 / 89 before**; accessibility 100 and best practices 100 (one run, before the CLS fix). LCP 1.9 s, TBT 20-50 ms, CLS 0.007.

### Not verified / stays open
- Nothing was seen on a real phone (touch on the calendar, the tour spotlight on a small screen, the Web Share sheet, cheers on a phone GPU). The tour's popover covers the top of a tall target (the Challenges card header) on small screens.
- **Whether the targets feel right.** Challenge targets are medians of the 4 players with 3+ active days (Iij, Guest, VfxTest, thor; mostly the developer's own test fights); star thresholds and full-power ratios are starting values. Real users will show whether "Chain it" (4 hits) or "Clear Level 3" are too easy / hard.
- Cheers were tried with one browser and the simulator; two real viewers at once, the global 10 per s limit under load (unit-tested only), and the broadcast overlay over real video are untested.
- The calorie, walking and jogging figures remain estimates (assumed 70 kg, assumed MET range 3-6); the challenge and mastery text says "estimated" wherever kcal appears.
- Badge events in the feed are re-derived for recent players only (no stored badge table), so older unlocks do not appear. Combo and damage-% based challenges and the second star need the telemetry fields; older sessions show "tracking" / "not recorded" instead of numbers.
- No accounts: "challenge a friend" matches the exact headset name, so anyone can type any name; goals and ratings keep the same caveat.

### 2026-10-16 follow-up: the Compare page that stayed on its skeleton, and the tour popover
**Symptom.** In a 30-page headless pass, `/dashboard/compare` at 1440 px once stayed on its skeleton with no console error (3 reruns loaded in 2.0 s).
**Reproduced** with 40 loads that follow the route order of that pass (player -> engagement -> compare) with a live fight posting every 3 s: 3 of 40 and 9 of 40 stuck (two runs), median 3.8 to 5.4 s, p90 19 to 25 s, max 36 s. Plain loads were never stuck (0 of 40, median 1.7 s). A network trace of a stuck load showed the document answered (200) while the **main JS bundle, or every API request, never finished**; a probe from a second client showed the server healthy at that moment (health 0.57 s, `index.html` 5 ms) and the browser holding exactly **6 ESTABLISHED sockets** to it, Chrome's per-host limit.
**Root cause (not a React race).** The browser's back/forward cache kept each previous page alive with its `EventSource` still open, so the server's stream-client count rose by one per full navigation (2, 3, 4, 5, 6 over eight navigations) until the pool of six sockets was full and the NEW page's script and requests queued behind dead pages. Two things made it worse and are fixed too: the dashboard shell fired its startup requests twice (once on mount, again ~1 s later when the stream opened: 13 requests on Compare, 22 on a player page, with `/achievements` asked for twice), and a slow poll tick could be joined by the next one.
**Fix (frontend only; no API route or field touched).**
- `lib/pagelife.js` + `liveStore.js`: close the stream on `pagehide`, reopen on `pageshow` when restored from the cache.
- `lib/inflight.js` + `api.js`: identical GETs already in flight share one request (failures are not remembered).
- `lib/startup.js` + `liveStore.js` / `useTelemetry.js`: no second snapshot on the first stream open (still retried if the first load failed or after a real reconnect); a polling tick no longer overlaps the previous one.
- Result: Compare 13 -> 8 requests, player page 22 -> 17; server stream clients flat at 1 across eight navigations (was up to 6); **0 of 40 stuck, median 2.9 s, p90 3.4 s, max 3.6 s** under the same conditions.
**Regression tests.** `npm test` (frontend 35/35): coalescer, startup decisions, page lifecycle; `npm run check:stream` (browser level, needs a running backend and Chrome/Brave): fails on the old bundle (stream clients climb to 6), passes on the new (flat at 1).
**Tour popover.** For a target taller than the viewport it was placed over the top of that target, and `measure()` re-scrolled it every frame without ever measuring (an endless loop). Placement is now `lib/tourLayout.js` (below, above, beside, or docked as a bottom sheet with the spotlight clipped above it), the real popover height is measured, and the target is scrolled once per step. Checked at 390x844, 390x600, 768x1024 and 1440x900: 5 steps each, popover always inside the viewport and never overlapping the spotlight.
**Test-setup notes.** My static test server did not close upstream streams when a browser left (it showed leaked clients that were its own); a `vite build` into the served `dist/` during a run gives blank pages; and with production on HTTP/2 the six-socket limit matters less than it did against the local HTTP/1.1 server, but the leaked server-side streams and duplicate requests were real either way.

### Deploy order
1. Merge the branch (not pushed from here). 2. **Render first** (root `web/backend`): boot applies `schema.sql` (`CREATE TABLE IF NOT EXISTS challenge_completions` + indexes). Nothing to set; `REACTIONS_ENABLED=false` switches cheers off. 3. In a browser open `https://<render-url>/api/players/<a real name>/challenges` and `/api/activity`; both must answer 200 JSON (first call after boot warms the median cache, ~1 s). 4. **Then Vercel** (`web/frontend`); `vercel.json` already rewrites `/challenge/...` and `/watch` to the SPA. 5. Open Overview, a player page, a session, and a `/challenge/<id>` link. Rollback is safe: new routes, table and fields are additive and old clients ignore them.

---

## 2026-10-16: guide tour once, guardians on the story page (branch `web-guide-story`, frontend only)

Branch `web-guide-story` was cut from `web-engagement` after a checkpoint commit (`59fa736`). Unity, the backend and `content/story.json` / `sync-story.js` are untouched.

### Part 1: the tour shows only the first time
- **Before:** `DashboardApp` opened the tour synchronously on mount when the section was the Overview and `strikefit.tour.done` was missing from localStorage (a failed storage read meant it never auto-started); no delay, no check for other dialogs, no X button; every close, replays included, wrote the flag.
- **Now (`lib/tourFlag.js`, pure and unit-tested; bound to the real storages in `onboarding/tourState.js`):** the tour starts by itself only on the Overview (`/dashboard`), when unseen, with no other modal (command palette, mobile menu, any `aria-modal` dialog), after an **800 ms** wait that re-checks all of that; leaving the Overview inside the wait cancels it. The flag is `strikefit_tour_seen_v1` (bump the version to show it to everyone again; the old `strikefit.tour.done` counts as seen, so nobody who finished it is shown it again). It is saved when the tour closes by Done, Skip, X, Esc or a click outside, **but only if the tour really mounted** (`onOpened`); a replay (Guide button) writes nothing. If localStorage throws (private mode) the flag falls back to sessionStorage, so it does not return on every page load of the same visit.
- **Keyboard / narrow screens:** new X button (`aria-label="Close tour"`), Tab trap (0 escapes in 16 Tab presses at four viewports), arrows and Esc, focus returns to the Guide button on close. The tour now follows its target until the page stops moving (instant scrolls, bounded corrections, ResizeObserver) because a target deep in a page that is still loading drifted away and put the popover off screen; a target that is not on screen gets no spotlight. Popover checked at 390x844, 390x600, 768x1024, 1440x900: never covers the highlight, always on screen.
- **Tests:** `npm test` 44/44 (9 new for the flag logic: first load, reload, cleared flag, replay writes nothing, legacy key, private mode, no storage at all, auto-start rule, 800 ms). Headless browser suite 22/22: starts at ~930 ms and not before 800 ms, nothing saved while merely open, finish / Skip / X / Esc / outside click each persist and survive a reload, Guide reopens it, replay writes 0, cleared flag brings it back, never on another page, cancelled when leaving, not over the command palette, a blocked Tour chunk saves nothing, and with localStorage throwing it falls back to sessionStorage with 0 console errors.

### Part 2: the guardians
- `components/story/Guardians.jsx` after the story slides, on **`/story` (new public route: it did not exist; `StoryPublicView` wraps the same `StoryView` in the landing nav and footer) and on `/dashboard/story`**. The landing page has no story section, so nothing was added there.
- Names and intro lines are read from the synced `content/story.json` (never retyped). Portraits are the existing `<MonsterPortrait>` (same images and level glow as `/monsters`: Level 1 yellow-green, Level 2 violet, Level 3 orange with electric-blue embers; real rendered images, no placeholders; width/height set, lazy-loaded; layout shift 0.0000 while scrolling the whole page). Weapons come from `MONSTER_WEAPON` in `lib/monsters.js` (Assets/Documentation/context.md level table: sword, two-handed axe, two axes one per hand); habits are existing `MONSTER_TIPS` lines picked by `GUARDIAN_HABITS`; the section says "Habits as the game is built today; tuning in progress". No HP or attack-count numbers beyond Level 3's "14 attacks" already in the tip text.
- "Meet all guardians" -> `/monsters`; "Play now" -> `/dashboard` with the line "The game is played in VR on a Meta Quest 2; your fights then appear on the dashboard" (there is no web-playable build and no APK link on the site).
- Layout: one guardian per screen on desktop (each 720 px at a 900 px viewport), stacked cards on phones, `Reveal` fade (none under reduced motion: transition 1e-5 s). A phone-only bug was fixed (the grid column was sized by the portrait's 420 px, clipping the intro line; `page overflow-x: clip` hid it from the scroll-width check, so the check now tests every element's right edge).
- Checked at 360 / 390 / 768 / 1440 px on both routes: 0 console errors, no element wider than the viewport, 3 guardians in order, 3 images with width and height and `loading=lazy`.

### Found, not changed
- `lib/game.js` says Level 2 HP 800; the running game shows 680 (`Monster_Medium_VampireLust` 680/680, and `monsterMaxHealthOverride` 500 / 680 / 2900 in context.md). That is why no HP is shown on the story page; fix the config before showing it anywhere.
- `MONSTER_TIPS.Medium[0]` says the Level 2 monster "picks from seven attacks"; context.md says a 10-attack table. Not shown on the story page.
- `/story` is not linked from the landing nav or footer (not asked for); `/dashboard/story` is in the sidebar.


---

## 2026-10-16: status, known limitations and roadmap moved out of the README (branch `repo-cleanup-readme`)

The README was rewritten as a project page. Its "Current status and known limitations" and "Future prospects" sections were removed there and are kept here, word for word, so nothing is lost. They were written on 2026-09-29 and **parts are out of date**; the notes after each block say what later entries changed.

### Current status and known limitations (README text, 2026-09-29)
- **Level 1** is complete and has been tested on Quest 2 hardware. Issues found during testing, including UI input wiring, audio routing and animator transition gaps, were fixed through on-device testing rather than assumed correct from code alone.
- **Level 2** is rebuilt and fully playable in the editor (start, fight, victory, defeat and replay all verified), but still needs a headset pass to confirm sword grip feel, real swing speeds, the fairness of the dodge window, the B button, and that the monster renders correctly on Quest.
- **Level 3** is a scene shell. It will reuse the same systems with a new environment, monster and attack set.
- **Balance** values are development defaults, not yet the result of playtesting.
- **Level 2 content gaps:** the original axe and sword texture files are missing, so these use hand-made materials. All available axe attack clips are one-handed, so the monster's left hand lets go during wide swings. There is no dedicated block animation and no real monster vocal sound.
- **No biometric tracking** such as heart rate or calorie estimation is implemented yet.

Changed since then (see the entries above): Level 3 is built and playable (twin-axe brute, 14 attacks, three phases, weapon pickup); the web dashboard now shows **estimated** calories (a MET-based estimate from fight length and swing strength, not a measurement); heart rate is still not measured. Whether Level 2 and Level 3 have had a full headset pass is not recorded in this file, so treat the headset-test limitations as open. Level 2 monster HP is 800 in `web/frontend/src/lib/game.js` but 680 in the running scene (see the 2026-10-16 "Found, not changed" notes).

### Future prospects (README text, 2026-09-29)
- Headset-test and balance Level 2, then build Level 3 with a distinct attack set for the hammer-wielding monster in the seaport.
- Connect the levels into one progression (menu → Level 1 → Level 2 → Level 3 → results) instead of separate builds.
- Rebalance health, damage and cooldown values using real playtesting data.
- Add heart-rate or exertion estimates from swing data to the dashboard.
- Evaluate whether the velocity-linked damage system measurably increases physical exertion and session engagement compared to a standard, binary hit-detection exergame, which is the central question this thesis sets out to answer.
- Study long-term engagement: whether players return across multiple sessions, and what that suggests about the format's potential for sustained fitness habits.

Changed since then: Level 3 now exists and a main menu scene (`MainMenu`) connects the levels. The evaluation and long-term engagement items are still open.

### Level 2 attack table (README text, 2026-09-29)
Moved from the README's Levels section because the README no longer lists per-attack detail. Selection rules at the time: the same attack is never picked twice in a row, a heavy attack is guaranteed after four light ones, heavy attacks become roughly twice as common near death, the gap between attacks shortens as the monster is hurt, and the left hand stays on the axe haft through IK except during wide swings. Attacks listed then: Horizontal Sweep (70° arc), Diagonal Slash, Backhand Cut, Twin Strike (2 hits), Whirlwind Spin (180° arc), Executioner Slam (idle only), Leaping Cleave (forced once at 40% HP, then an 8 s cooldown). Later entries describe a larger table (10 attacks in `context.md`); the number in `MONSTER_TIPS.Medium[0]` ("seven") is stale.

### Design and development process (README text, 2026-09-29)
Iterative build, verify, headset-test loop: design the level's fantasy and difficulty target; build on the shared combat scripts; verify in the editor through the Unity MCP server (real UI raycast at Start, real hit detection at known swing speeds, Animator sampled at each damage frame, 1,000-pick attack-selection simulations); test on Quest 2 anything that depends on real hands or movement; record findings, including wrong earlier claims, in this file. Problems this caught that code review alone had missed: a Start button with no click listener, a UI canvas facing away from the player, a 40-second timer that declared Victory with the monster at full health, animator transitions that could cancel attacks mid-swing, and a monster weapon collider that existed only on an unused scene copy.
