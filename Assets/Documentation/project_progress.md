# Project Progress — Fitness Monster Battle

Last updated: **2026-09-27 — Level 2 final pre-demo check (live Unity MCP + Play-mode physics traces)**

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
