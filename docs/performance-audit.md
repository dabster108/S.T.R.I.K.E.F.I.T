# Quest 2 performance audit (static)

**Date:** 23 October 2026 · **Target:** Meta Quest 2 at 72 fps (13.9 ms per frame) · **Unity:** 6000.5.11f1, URP, OpenXR + Meta XR SDK 205

This audit was made **without a headset and without opening the Editor**. It reads the C# scripts, the project and pipeline
settings, the `.meta` import settings and the scene/prefab YAML. Every number below is labelled:

- **[static]**: counted or read from files. It says what is configured, not how fast it runs.
- **[measured, Editor]**: measured by a test or harness in the Unity Editor on a Mac. It is not a Quest number.
- **[estimate]**: worked out from a static number (for example PCM memory from a clip's length).

No Quest measurement exists yet. **The Editor pass of 24 October 2026 (changes, reverts and measured numbers) is
[section 6](#6-editor-optimisation-pass-24-october-2026).** The [profiling checklist](#5-profiling-checklist-for-a-real-quest-session) at the end says
what to measure and which numbers to read.

Gameplay values (health, damage, thresholds, telegraph times, block caps) were not touched. No scene or prefab YAML was edited.

---

## 1. Scripts

### Method

A scanner read all 112 runtime scripts in `Assets/Scripts` (Editor scripts excluded). It marked `Update`, `LateUpdate`,
`FixedUpdate`, `OnGUI` and the trigger/collision "stay" callbacks as per-frame, then every method of the same file they call
(transitively), and looked for allocations (`new` of reference types, LINQ, string concatenation and formatting, closures),
`GetComponent`/`Find*`, `Camera.main`, allocating physics queries, `Instantiate`/`Destroy`, `new WaitForSeconds`,
`SendMessage`, `Debug.Log`, `.material`, Animator calls by string, and `OnGUI`. Each hit was then read by hand to see whether it
really runs every frame or only on an event (an attack, a hit, a menu opening).

### What was already in good shape [static]

- No `SendMessage`, no `PlayClipAtPoint`, no allocating physics queries (`RaycastAll`, `OverlapSphere`...) in gameplay code.
- No per-frame `.material` access (tints use `MaterialPropertyBlock`); no Animator calls with string literals per frame.
- Damage numbers come from a fixed pool (`CombatUIController.EnsureNumberPool`). The `Instantiate` path for a
  `damageNumberPrefab` is unused: the field is empty in all three level scenes.
- Collider-to-monster lookups in `HitDetection` are cached per collider; weapon `HitDetection` lookups are cached per weapon.
- HUD text (effort, kcal, goal line, name-picker countdown, debug readout) is rewritten only when the shown value changes.
- The frequent `Debug.Log` lines (per attack, per hit, per network batch) were already behind `verbose…` flags that are off in
  the scenes, except `logTierChanges` (see below).
- Only one `OnGUI` exists (`ExpertMotionPredictor`), and it is compiled out of release builds.
- The Level 3 Expert inference path allocates nothing per frame except one small iterator per inference
  **[measured, Editor]** (EditMode test `SteadyState_FeaturePath_AllocatesNothing_AndAnInferenceAlmostNothing`).

### Changes made

| File | Change | Why |
|---|---|---|
| `Monster/MonsterAnimator.cs` | Trigger and bool parameters cached as name hashes per Animator Controller; `SetTrigger`/`ResetTrigger`/`SetBool` use the hash | `animator.parameters` builds a new array of new objects on every read, and it was read on every trigger set and every "reset all triggers": several times per monster attack, hit and guard. Now it is read once per controller. An empty read (Animator not initialised yet) is not cached, so behaviour is the same as before. |
| `Monster/MonsterCosmetics.cs`, `Monster/MonsterDefense.cs` | Their own `HasTrigger` loops use the cached lookup above | Same allocation, on idle variants and dodges. |
| `Monster/MonsterAI.cs` | `MonsterHealth` / `MonsterAnimator` looked up once (lazy cache) instead of on every think, attack, guard and stagger; the attack-pick lists in `ChooseFromTable` / `ChooseRanged` reused instead of `new List` per call; `FindAll(lambda)` replaced by a loop into a reused list; the block-flash `WaitForSeconds(0.06f)` is one shared instance | `ChooseRanged` ran on every think while the player was out of range, allocating a list each time. The picks are unchanged: same contents, same order, same random draws. |
| `VFX/ArenaMoodController.cs` | `GetComponent<MonsterHealth/MonsterAI>` cached per monster instead of every frame | Two lookups per frame in Level 3. |
| `Monster/MonsterTactics.cs` | The two "refused guard" log strings are built only when the decision log keeps them | String concatenation on every refused predicted block. |
| New `Combat/DevLog.cs` | `DevLog.Log`, a `[Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]` wrapper | In a release build the call **and its argument** disappear, so no string is formatted and no stack trace is taken. |
| 15 calls in `MonsterAI`, `MonsterController`, `MonsterAnimator`, `PlayerExertionAudio`, `CombatUIController`, `SessionReporter` | Informational, flag-gated `Debug.Log` → `DevLog.Log` | `PlayerExertionAudio.logTierChanges` is **on in all four scenes**, so release builds logged every breathing-tier change. The others are gated by flags that are off, now also free if one is left on by mistake. Lifecycle lines (battle start/end, story events), warnings and errors are unchanged. |
| New `VFX/ShaderIds.cs` + 22 calls in 11 files | `SetColor("_BaseColor", c)` → `SetColor(ShaderIds.BaseColor, c)` (also `_Color`, `_EmissionColor`) | The string form hashes the name on every call; several of these run every frame (serpent aura per trail, danger zones, ground runes, glow, low-health and damage vignettes). Same properties, same result. |

### Recommended (not changed)

- **Per-hit strings.** `CombatUIController.ShowDamageNumber` formats the "Dmg" counter and the number text for every hit,
  and `SessionReporter` builds one JSON string per event. That is a few small strings per hit, not per frame. Leave it unless
  the profiler shows GC spikes during heavy combat.
- **Coroutines per attack.** `MonsterAI` starts a coroutine and creates `WaitForSeconds` with variable durations for each attack
  and guard (every 2–3 s). They can't be cached because the durations vary; this is a small allocation per attack, not per
  frame.
- **`MonsterTactics.logDecisions`** is on by default (the Editor harnesses read the log). The ring buffer itself does not
  allocate. Consider turning it off for release if the profiler shows it.

---

## 2. Project and pipeline settings

Quest uses quality level **0 "Mobile"** (Android default) and the "Meta Quest (Build Profile)" level, both pointing at
`Assets/Settings/Mobile_RPAsset.asset` with `Mobile_Renderer.asset`. **[static]**

### Already right for Quest [static]

| Setting | Value |
|---|---|
| Scripting backend / architecture | IL2CPP, ARM64 only |
| Graphics APIs (Android) | Vulkan first, then OpenGL ES 3 (manual) |
| Stereo rendering | Single-pass instanced (multiview), multithreaded rendering on, linear colour space |
| Default texture compression (Android) | ASTC |
| Incremental GC, engine code stripping | on, on |
| URP: HDR / depth texture / opaque texture | off / off / off |
| URP: MSAA / render scale | 4× / 0.8 (OVRManager dynamic resolution is on in two scenes: Quest 2 range 0.7–1.3) |
| URP: SRP Batcher / dynamic batching | on / off |
| URP: soft shadows / cascades / shadow distance / main shadowmap | off / 1 / 20 m / 1024 |
| URP: additional-light shadows | off |
| Renderer | Forward, no renderer features, native render pass on |
| Post-processing | no Volume in any build scene, no camera with post-processing enabled |
| Audio | DSP buffer 1024, 32 real voices, Meta XR Audio spatializer |
| Physics | 50 Hz fixed step (0.02 s), auto-sync transforms off, collision callbacks reused |

### Changes made (`Assets/Settings/Mobile_RPAsset.asset`, backup in `Backups/perf-audit-2026-10-23/`)

| Setting | Before → after | Why it is safe |
|---|---|---|
| `m_SupportsTerrainHoles` | 1 → 0 | No Terrain anywhere in the project. Removes unused shader variants. |
| `m_SupportsLightLayers` | 1 → 0 | Every light uses rendering layer 1 only (35 lights checked) and every renderer includes layer 1; no script sets layers. Lighting is identical; the per-pixel layer test and its shader variants go away. |
| `m_SupportDataDrivenLensFlare` | 1 → 0 | No `LensFlareComponentSRP` in any scene or prefab. |
| `m_SupportScreenSpaceLensFlare` | 1 → 0 | Needs a post-processing Volume; there is none in the build. |

No file in `ProjectSettings/` was changed: everything there was already set as recommended for Quest, or a change would
alter how the game looks or plays (below).

### Recommended (risky, decide in the Editor)

1. **Fixed foveated rendering is probably not active.** The Meta XR Foveation feature is enabled for Android in OpenXR, but
   the level is a runtime setting (`OVRManager.foveatedRenderingLevel`) and no script sets it. The Android OpenXR settings also
   select the SRP foveation API, so check which path is live. Try `OVRManager.foveatedRenderingLevel = High` plus
   `useDynamicFoveatedRendering = true` at start-up, then confirm the FFR level in OVR Metrics Tool. This is usually the largest
   GPU win on Quest 2. The periphery gets blurrier, so look at it in the headset.
2. **Per-pixel additional lights, limit 4 per object.** Level 2 has 6 realtime lights on, Level 3 has 9 realtime or mixed
   **[static]**. Each per-pixel light costs fragment work on every lit pixel. Options: per-object limit 1–2, per-vertex
   additional lights, or bake the static torches and lamps and keep only the lights that move or animate realtime.
3. **Main-light shadows.** Level 3 has 158 active shadow-casting renderers, Main Menu 122, Level 1 51 **[static]**, all drawn a
   second time into the shadowmap when within 20 m. Turn off "Cast Shadows" on small props and on the flail chain links, or
   shorten the shadow distance, or drop realtime shadows on Quest and rely on baked ones.
4. **Render scale 0.8 with dynamic resolution on.** Pick one strategy after measuring: dynamic resolution overrides the render
   scale when it is active.
5. **Physics at 50 Hz.** Hit detection and swing momentum sample at the fixed step, so changing it changes gameplay. Leave it.
6. **Layer collision matrix.** Almost every layer collides with every other; only three pairs are off (MonsterWeapon with
   MonsterWeapon, MonsterWeapon with MonsterBody, MonsterBody with MonsterBody).
   Cheap to keep while few layers are in use; revisit if the profiler shows physics time.
7. **Application SpaceWarp** is enabled as an OpenXR feature but not switched on at runtime. Leave it off unless frame time
   cannot be met otherwise; it needs motion vectors on every material.

---

## 3. Asset import settings

Reachability: starting from the 5 build scenes, every `Resources/` folder and the settings assets, the scanner followed GUID
references through every text asset (scenes, prefabs, materials, controllers, animations...) and model `.meta` remaps.
Binary references (a texture named inside an FBX) are invisible to it, so "not reached" means "no text asset of the build
refers to it", not "certainly unused". Sizes are **source file sizes** [static], not the size after import.

**Totals [static]:**

| | All in `Assets/` | Reached by the build |
|---|---|---|
| Textures | 1,622 MB | 351 MB |
| Meshes / models | 497 MB | 311 MB |
| Audio | 14.6 MB | 13.1 MB |
| Unreached (all three) | 1,459 MB | |

Unreached assets do **not** go into the build; they only slow imports and fill the disk. The biggest are the HotelRoom-Mavi3D
pack (HDRP/URP demo textures, 4096² `.tif`/`.tga`), the AllSkyFree skyboxes (16384×8192 sources) and the Old Sea Port demo
`EffectExamples`.

### Largest textures [static]

All import at a maximum of 2048 (larger sources are downscaled on import) with ASTC on Android (the project default; no Android
override). Reached textures among the 30 largest:

| Texture | Source | Import max | Notes |
|---|---|---|---|
| `Models/Weapons/Player/Level3/Mace/Textures/mace_a_n.png` | 4096² | 2048 | a 2048 normal map on a hand-held weapon; 1024 on Android is likely enough |
| `Models/Monsters/Level3/Textures/Ch25/Ch25_1001_Normal.png`, `Ch25_1002_Normal.png` | 4096² | 2048 | **Read/Write was on** (fixed) |
| `Models/Weapons/hammer/textures/hammer_FINISH_Material_001_Normal.png` | 2048² | 2048 | |

### Changes made (backups in `Backups/perf-audit-2026-10-23/meta/`)

| Asset | Change | Why it is safe |
|---|---|---|
| `Ch25_1001_Diffuse.png`, `Ch25_1002_Diffuse.png`, `Ch25_1001_Normal.png`, `Ch25_1002_Normal.png` (Level 3 monster), `mace_a.png`, `mace_a_e.png`, `mace_a_s.png` | Read/Write **on → off** | Read/Write keeps a second, CPU-side copy of the texture in memory. No runtime script reads pixels; the Editor tools that read pixels use their own captures or load the source file bytes. |
| `Audio/Music/music.mp3` (85 s, Level 1 music) | Load type **Decompress On Load → Streaming** | Decompressed it held about 15 MB of PCM in memory **[estimate: 85 s × 44.1 kHz × 2 ch × 2 bytes]**. Streaming is the standard for music; the Level 2 music already streams. |

### Recommended

- **Mesh Read/Write is on** for the Level 3 monster `Ch25_nonPBR.fbx` (79 MB source), `WarFlail.fbx` and the Decrepit Dungeon
  and Old Sea Port environment meshes **[static]**. Turning it off frees the CPU copy of each mesh, but MeshColliders and some
  Editor builders (`Level3DualAxeBuilder` works with the monster mesh) can need it. Turn it off one asset at a time in the
  Editor and play the level.
- **Old Sea Port "Eerie" skybox** (6 faces) is imported **uncompressed** **[static]**. Uncompressed RGBA uses about 4 MB per
  1024² face in GPU memory **[estimate]**. Set an Android override (ASTC 4×4 or 5×5) and check the sky for banding.
- **Blend shapes, cameras and lights** are imported on the monster and animation FBX files (`importBlendShapes: 1`,
  `importCameras: 1`, `importLights: 1`). Turn off what the game does not use.
- **Animation compression** is "Keyframe Reduction" on the Mixamo clips; "Optimal" is usually smaller. Check the attack
  timings after changing it (the hit frames are sampled from the clips).
- **"Optimize Game Objects"** is off on the monster rigs. It speeds up the Animator but hides bones from scripts; the IK and
  weapon attachment scripts would need exposed bones first.
- **Breathing loops** (`breathing_fast_v3.wav`, `breathing_heavy_v3.wav`) are PCM, decompressed on load (~1 MB each)
  **[static]**. Fine as is; the level of these clips was tuned carefully, so do not re-encode without listening on the headset.
- **Delete or move out the unreached packs** (HotelRoom, AllSkyFree, Old Sea Port demo scenes) if they are not needed:
  1.4 GB less to import.

---

## 4. Scenes

Counts come from the scene YAML **plus the contents of every prefab instance** (resolved to the source prefab). Instance
overrides, such as an object disabled in the scene, are **not** applied, so prefab-derived numbers are approximate
**[static]**. Objects spawned at runtime (the monster, held weapons, dragons) are not included.

| | Main Menu | Level 1 | Level 2 | Level 3 | Results |
|---|---|---|---|---|---|
| Lights (active) | 14 | 3 | 6 | 9 | 0 |
| …realtime or mixed | 2 | 3 | 6 | 9 | 0 |
| …realtime with shadows | 0 | 3 | 1 | 5 | 0 |
| Mesh renderers (active) | 124 | 51 | 40 | 167 | 0 |
| …casting shadows | 122 | 51 | 38 | 158 | 0 |
| …not static | 3 | 2 | 6 | 93 | 0 |
| Particle systems (active) / sum of max particles | 4 / 168 | 2 / 170 | 2 / 170 | 2 / 2,200 | 0 |
| Audio sources (active) | 1 | 4 | 3 | 16 | 0 |
| Baked lighting data | yes | yes | yes | yes | yes |
| Light probe groups | 1 | 0 | 0 | 0 | 0 |
| Reflection probes | 0 | 0 | 1 | 0 | 0 |
| Fog | on | on | on | on | off |

Notes and recommendations:

- **Level 3 flails:** each flail prefab is **19 renderers** (chain links); the staged pair is 38, and both hands hold one in a
  fight **[static]**. That is up to ~40 draw calls, doubled by the shadow pass. Merge the chain into one skinned or combined
  mesh, or at least turn off shadow casting on the links. Most of Level 3's 93 non-static renderers are these weapons, which
  move and are rightly not static; the environment is marked static.
- **Level 3 rain:** `Rain` (max 1,200) and `Splash` (max 1,000) from `Old Sea Port/prefabs/Rain.prefab`, one with **world
  collision** **[static]**. Particle world collision is CPU-heavy on Quest. Lower the maxima, use plane collision or none, and
  check the overdraw of the rain quads.
- **Realtime lights:** the Level 1 torches (`Torch.001.prefab`) and the directional light are realtime with soft shadows; on
  Quest only the main light's shadows render (additional-light shadows are off in the URP asset), but soft shadows are also off
  there, so the "soft" setting in the scene is not used. Level 3's four `lamp_1` point lights are mixed. Consider baking static
  lamps and torches (see section 2, item 2).
- **Light probes:** only the Main Menu has a light probe group. The monster and the weapons in Levels 1–3 therefore get no
  baked indirect light. Add a light probe group around the arena of each level when you next bake.
- **Audio sources:** at most 16 active (Level 3), within the 32 real voices.
- **Main Menu:** 122 shadow-casting renderers and 2 mixed torch lights; the other 12 torches are baked (good).

---

## 5. Profiling checklist for a real Quest session

**Budget for every level (Quest 2, 72 Hz):**

| Measure | Budget |
|---|---|
| Frame rate | 72 fps steady (frame time ≤ 13.9 ms), no stale frames in a fight |
| App GPU time | under ~11 ms (leaves headroom for the compositor) |
| App CPU main thread | under ~11 ms |
| Draw calls (batches) | under 150 per eye pass with multiview (one submission covers both eyes) |
| Visible triangles | around 100k |
| GC allocations per frame | 0 B in normal fight frames |
| Level 3 Expert inference | average ≤ 1.5 ms, and Expert must not fall back for slowness (diagnostics log) |

**A. OVR Metrics Tool (on the headset, no cable needed)**

1. Install OVR Metrics Tool from the Meta Horizon Store, enable the performance HUD overlay and CSV recording.
2. Play each level for 2 minutes in a real fight (Level 3 in Normal and in Expert), plus the Main Menu.
3. Read: **FPS**, **Stale frames**, **App GPU time**, **App CPU time**, **CPU and GPU levels**, **GPU utilisation**,
   **FFR level** (confirms recommendation 2.1), **Eye buffer resolution** (shows the dynamic resolution at work),
   **Available memory** and **Temperature** over the session.
4. Save the CSV per level and note the worst 1 % frame times.

**B. Unity Profiler over USB (Development Build)**

1. Build with **Development Build** and **Autoconnect Profiler** (and "Deep Profiling" off) and install with Build And Run.
2. Window → Analysis → Profiler, target **AndroidPlayer (USB)** (`adb forward` is set up by Unity when connected by cable).
3. Record 60 s of fight per level. Read:
   - **CPU Usage → Timeline:** `PlayerLoop` total, `Update.ScriptRunBehaviourUpdate`, `FixedUpdate.PhysicsFixedUpdate`,
     `PreLateUpdate.DirectorUpdateAnimationBegin/End` (Animator), `Gfx.WaitForPresent` / `XR.WaitForGPU` (GPU bound if large).
   - **Hierarchy, sorted by GC Alloc:** every per-frame entry should be 0 B; note any entry above 0 and its call stack.
   - **Rendering:** SetPass calls, Draw calls / Batches, Triangles, Vertices (compare with the budget).
   - **Memory:** Total used, Texture, Mesh, Audio; take a Memory Profiler snapshot after each level loads.
   - **Physics:** active rigidbodies and contacts in the busiest fight (Level 3 with rain).
4. Frame Debugger (Window → Analysis) over USB: count shadow-caster draws and confirm the flail links and rain are where the
   draw calls go.

**C. Expert-mode numbers** (Level 3, Development Build): hold both thumbsticks 2 s for the panel, or after the fight
`adb pull /sdcard/Android/data/com.UnityTechnologies.com.unity.template.urpblank/files/expert_diagnostics.txt` and read average
and maximum inference ms, fallbacks and frame time.

**D. Record the results** in this file under a new heading "Measured on Quest 2 (date)", labelled **[measured, Quest 2]**, so the
static numbers above can be compared with the real ones.

---

## Files changed by this audit

Scripts (`Assets/Scripts/`): `Monster/MonsterAnimator.cs`, `Monster/MonsterCosmetics.cs`, `Monster/MonsterDefense.cs`,
`Monster/MonsterAI.cs`, `Monster/MonsterController.cs`, `Monster/MonsterTactics.cs`, `Monster/MonsterGlow.cs`,
`Monster/MonsterWeakPoint.cs`, `Monster/MonsterSerpentAura.cs`, `Monster/MonsterEntrance.Drop.cs`, `VFX/ArenaMoodController.cs`,
`VFX/DangerZoneIndicator.cs`, `VFX/GroundRuneVFX.cs`, `Combat/WeaponPickup.cs`, `Player/PlayerExertionAudio.cs`,
`Player/PlayerLowHealthFeedback.cs`, `Player/PlayerDamageVignette.cs`, `UI/CombatUIController.cs`,
`Networking/SessionReporter.cs`; new `Combat/DevLog.cs`, `VFX/ShaderIds.cs`.

Settings and import settings: `Assets/Settings/Mobile_RPAsset.asset`; the `.meta` files of the 7 textures and `music.mp3` listed
in section 3. Backups of every edited settings and `.meta` file: `Backups/perf-audit-2026-10-23/`.

---

## 6. Editor optimisation pass (24 October 2026)

Done in the Editor through Unity MCP, one change at a time, each followed by Play mode. Backups of every scene, prefab, script and
`.meta` touched: `Backups/optimisation-2026-10-24/` (untracked; `FILES.txt` lists them, paths mirror the project). Gameplay values
were not changed. During every Play run `EditorTelemetryGuard` pointed every `SessionReporter` at `http://127.0.0.1:9` from the
first scene load (the scene files still hold the production URL; the redirect is in memory only). Correction: the first PlayerPrefs
restore (a file-level `defaults import`) did not hold, because Unity's in-memory copy overwrote it. The game's keys (`StrikeFit.*`,
`FMB_*`) were restored again through Unity's PlayerPrefs API on 24 October after the engagement tests, and now match the pre-test
snapshot exactly (12 keys, 6 test keys removed; Unity's own keys untouched).

### Measured in the Editor, not on a Quest

`Assets/Editor/OptimisationHarness.cs`, `Measure(60 s)`: a real fight per level (the monster attacks, the player stands idle and is
healed when low, nothing saved), game time stepped at 1/30 s, **MacBook Editor, Metal**. "PlayerLoop" is the game's own CPU time per
frame (profiler); "GPU" is the Editor's GPU frame time; counts are per rendered frame of the Game view. The Editor's main-thread
time is frame pacing (vsync at 60 fps, and Level 1 always ran throttled to 10 fps in the background), not a cost, so it is left out.
Level 3 holds the war flails (both hands), the worst case for its chain links.

| Level | | PlayerLoop avg / p95 / worst (ms) | GPU avg / p95 (ms) | Draw calls | SetPass avg / max | Triangles avg | Shadow casters avg / max | Realtime lights | Live particles max | GC in PlayerLoop avg (B/frame) |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | before | 3.85 / 4.91 / 16.90 | 0.61 / 1.07 | 102 | 18 / 39 | 133 k | 62 / 132 | 3 | 133 | 4,838 |
| 1 | after | 3.70 / 4.73 / 11.99 | 0.56 / 0.70 | 102 | 19 / 39 | 133 k | 62 / 132 | 3 | 142 | **1,004** |
| 2 | before | 2.44 / 5.67 / 11.89 | 8.78 / 16.26 | 51 | 29 / 62 | 74 k | 25 / 46 | 6-7 | 247 | 4,381 |
| 2 | after | 2.03 / 3.29 / 8.91 | 8.45 / 12.95 | 53 | 28 / 64 | 69 k | 23 / 46 | **4-5** | 248 | **654** |
| 3 | before | 2.66 / 6.51 / 11.41 | 9.72 / 17.71 | 165 | 57 / 220 | 167 k | 75 / 144 | 10-11 | 847 | 5,274 |
| 3 | after | 2.35 / 4.83 / 15.20 | 9.89 / 15.22 | **116** | 48 / 157 | 140 k | **32 / 64** | 10-11 | **316** | **982** |

Notes: Level 2's GPU time and Level 1's realtime-light count barely move in the Editor (a Mac GPU shrugs off a few per-pixel lights;
Quest does not). Level 3's 15.2 ms worst PlayerLoop frame came with a one-off 437 KB allocation that a second 60 s run did not
reproduce (its worst frame was 12.7 KB, one `SessionReporter` event batch). Every run's console had no unexpected errors or warnings.

### What changed

| Item | Change | Evidence |
|---|---|---|
| Baked lighting, Level 2 | The two back-wall lamps (`LampLight_Left/Right`) baked (lightmap, `Assets/Scenes/Level2_Medium/Level2_Lighting.lighting`: Progressive GPU, 10 texels/unit, 1024 max, non-directional, realtime GI off); the two arena torches next to the player, the monster's rim light and the moon stay realtime. `DungeonPackLite.fbx` now generates lightmap UVs (Levels 1-2 only; the Main Menu does not use it). Two lightmaps, bake 22 s. | Arena views within +-0.5 luminance of before; same-frame A/B of the monster with and without the bake: 2-3 % darker (idle 49.3 vs 50.4, telegraphs 47.9/49.8, 45.7/47.0, 43.2/44.6). Screenshots `docs/screenshots/optimisation-2026-10-24/cmp_L2_*.jpg`. |
| Light probes, Level 2 | `LightProbes_Arena`: 196 probes (2 m grid, 0.4 / 1.4 / 2.6 m high, points inside geometry skipped). | As above. |
| Shadows, Level 3 | `QuestShadowTrim` (new, called by `Level3SceneBuilder` and `Level3Builder` so rebuilds keep it): scene props of 1.05 m or less (lamps, lamp glass, mooring holders, small crates) and the 120 m sea plane stop casting; the staged weapon prefabs stop casting; the held flails' 17 chain links each stop casting (handle and ball keep theirs). 116 -> 57 scene casters. Kept: the monster, the player's weapons, crates / barrels of 1.4 m, walls, columns, fences, boats, dock. | Shadow casters 75 -> 32 average, 144 -> 64 max. |
| Shadow distance (Quest pipeline asset) | `Mobile_RPAsset` shadow distance 20 -> 12 m (every arena ends within ~9 m of the player); shadowmap kept at 1024, so shadows get sharper (85 instead of 51 texels per metre). | Level 1 casters unchanged (its room is inside 12 m). |
| Level 3 rain | World collision (high quality, 256 shapes) replaced by one collision plane at the dock top (`Rain/RainFloor`); drops live 0.6 s at most; 200 drops/s; caps 600 + 600 -> 120 + 80. Before, the 600 cap filled with drops falling unseen under the sea (no collider there), so the rain came in bursts every ~5 s (1 to 126 drops in view); now a steady 62-64 in view and 43-44 splashes. Builder updated to match. | Live particles 847 -> 316. |
| Mesh Read/Write | Off: the boss's skinned mesh `Ch25_WeakPointSplit.asset` (the mesh the build actually uses; skinning is on the GPU and no script reads it; builder updated), `Ch25_nonPBR.fbx` (only its avatar ships, so this only affects the Editor), `WarFlail.fbx`, 28 Old Sea Port meshes, 9 Decrepit Dungeon meshes. **Kept on**: the 12 Old Sea Port meshes used by Level 3's 32 MeshColliders and Decrepit's `Object Colliders.fbx`, because **Prebake Collision Meshes is off** in Player Settings, so a Quest build cooks those colliders at load, which needs the CPU copy. | Each followed by a Level 3 (or Main Menu) Play run with no console errors; the Editor builders can still read every mesh. |
| Skybox | "Eerie" faces: Android override ASTC 4x4, max 1024 (was uncompressed RGB24). Editor-reported memory per 1024 face 4.20 -> 2.80 MB; on the device ASTC 4x4 is 1 byte per pixel (about 1.4 MB per face with mips) **[estimate]**. | Sky-only renders before / after: mean difference 0.1/255, max 4/255, no banding at 2x zoom (`cmp_sky_*`). |
| Per-frame GC | `SweepShape.BoundRadius` (`WeaponSweep.cs`) and two `MonsterPersonalSpace` radii called `Mathf.Max(a, b, c)`, whose 3-argument overload allocates a `params` array on every call; `HitDetection.Sweep` ran it for every monster shape, both weapons, every frame (3.8 KB/frame). Now `Max(a, Max(b, c))`, same values. | GC in PlayerLoop -80 to -88 % (table). Left: 280 B/frame in `UnitySynchronizationContext.ExecuteTasks` every frame (no game `await` posts to it; most likely the WebRTC broadcast package) and `SessionReporter` event batches (16 of 298 frames). |
| Console | `WebRtcStreamer`: a level loaded while the persistent BroadcastSystems already streams (every level after the first) disables its own copy as before, but logs it as a development note instead of a warning; two streamers in one scene still warn. | The menu round trip (run before this change) logged it on every level change; the Story trace after it (chapter 2 -> 3 loads a level while the persistent streamer runs) logged none. |

### Tried and reverted

- **Baking Level 3** (fills + the two warm lamps behind the player): the dock lost its wet moonlit sheen and showed blotchy lightmap
  patches (14 % darker), the monster was darker against the wall (luminance 58.9 -> 51.5, worse telegraph contrast), and
  `ArenaMoodController` animates all eight lamps and the trilight ambient per fight phase, which a bake freezes on static geometry.
  Restored byte-identical from the backup; the bake output is in the Trash. Level 3 therefore also has no light probes (probes only
  carry baked data). Cheaper Level 3 lighting without breaking the mood would need the mood controller to drive fewer, merged lights.
- **Light probes on Level 1**: Level 1 has no light to bake, so the bake only replaced the flat realtime ambient with occluded
  ambient: 10-25 % darker everywhere (the back wall nearly black), nothing gained by the monster. Restored byte-identical.

### Foveation (UNVERIFIED on the device)

`QuestFoveation` installs itself at start-up (`RuntimeInitializeOnLoadMethod`, `DontDestroyOnLoad`), so every scene gets it; one
instance was present in Play mode. `Resources/QuestFoveationSettings.asset`: enabled, level High (SRP 1.0), dynamic on. Without a
headset it waits for an XR display (up to 600 frames) and applies nothing, as designed. Whether FFR is really active can only be
read on the Quest (OVR Metrics Tool, "FFR level").

### Full working check (Play mode, Editor)

- Main Menu -> Level 1 / 2 / 3 -> real Victory -> Exit confirm (No) -> Play Again (fresh fight) -> real Defeat -> Level Select ->
  menu, all three levels (`MenuVerification` "roundtrip").
- Story Mode: new story, defeat + Retry, pause (time scale 0, audio paused) + Resume, Level Select -> Yes (abandoned), continue
  through chapters 2-3 to the Victory card and "Story complete", free play (`StoryFlowTrace`: 0 errors).
- Pause menu Exit Game -> Yes: the fight is abandoned, the session flushed, Play mode stops (the Editor's quit path).
- Monster AI toggle: Level 3 Normal -> Expert (AI model) -> Normal, saved each time; Levels 1-2 greyed out "Level 3 only".
- Expert mode, 60 s Level 3 fight (`ExpertHarness`): model loaded, brain active, **0 fallbacks** in 577 inferences, HUD "[EXPERT AI]";
  head-only (`useHandPrediction` false by default and in no asset; reads past 0.75 s refused). Editor inference 1.33 ms average,
  1.58 ms p95, 71 runs over the 1.5 ms budget: needs the Quest number.
- Colour system: on all three monsters a full-power hit flash landing during a telegraph leaves the monster telegraph-red
  (`cmp_monster_idle_vs_telegraph_hitflash.png`). Both colours are red by design; the blend reserve keeps the telegraph's share.
  Level 2's dark-red texture gives the telegraph less contrast than Levels 1 and 3 (already noted in docs/color-audit.md).
- EditMode suite: **81/81** after a forced full recompile.
- Known, not from this pass: no menu music clip assigned (`[MenuMusic]` warning); Level 3's convex barrel colliders exceed
  256 polygons ("partial hull" warning when the scene loads; replace with capsule colliders, or enable Prebake Collision Meshes).

### Removable (not deleted): 1,460 MB that no build scene, Resources folder or setting refers to

From the reachability scan (GUID references through every text asset; binary references are invisible to it). None of these go
into the Quest build; removing them only speeds up imports and frees disk.

| Folder | Size | Notes |
|---|---|---|
| `Assets/HotelRoom-Mavi3D` | 661 MB | demo pack (4096 textures), no script refers to it |
| `Assets/AllSkyFree` (unused skies) | 284 MB | keep the two skies Levels 1-2 use (`AllSky_Space_AnotherPlanet`, `AllSky_Night_MoonBurst`) |
| `Assets/Old Sea Port` (unreached part) | 124 MB | mostly `EffectExamples` (demo) and unused textures |
| `Assets/Models/Weapons` (unreached part) | 152 MB | `medieval-shield` (~70 MB) is unused; **`Player/Level3/Mace/Mace_DoomDarkAges.fbx` (Level3Builder) and `Player/Level3/Katana/MaceKatanaAxe_Pack.fbx` (Level3DualAxeBuilder) are loaded by path by Editor builders: keep while those builders are used** |
| `Assets/Models/Monsters` (unreached part) | 80 MB | **`Level3/ArmoredExecutioner.fbx` is used by ExecutionerMeshSplitter (old boss)**; unused textures (`Ch25_*_Glossiness`, `T_L3_Executioner_*`) |
| `Assets/Animations` (unreached part) | 72 MB | `One Hand Sword Combo` and `Sword And Shield …` are loaded by Level3DualAxeBuilder.Monster: keep; others (e.g. `Great Sword Idle`) unused |
| `Assets/Decrepit Dungeon LITE` (unreached part) | 37 MB | unused textures |
| `Assets/Screenshots` | 26 MB | old debug captures |
| `Assets/StylizedHandPaintedDungeon(Free)` (unreached part) | 15 MB | |

### Only a real Quest can confirm

Frame rate and frame time per level (72 Hz budget, stale frames), App GPU / CPU time, whether FFR is active and at what level, how
the Level 2 lightmaps and the shorter shadow distance look in the headset, the rain's look and overdraw, the ASTC sky on the
device's display, Expert inference time against its 1.5 ms budget, memory after the Read/Write changes, and that Level 3's
MeshColliders still cook at load (Read/Write kept on for their meshes). The checklist in section 5 still applies.

### Files changed by this pass

Scenes: `Level2_Medium.unity` (bake, probes, lights), `Level3_Hard.unity` (shadow overrides, rain). New lighting assets:
`Assets/Scenes/Level2_Medium/` (`Level2_Lighting.lighting`, `LightingData.asset`, two lightmaps, `ReflectionProbe-0.exr`; the old
`ArenaReflectionProbe.exr` is no longer referenced but was left in place). Prefabs: `PlayerFlail_Left/Right`, `Staged_Flail`,
`Staged_Mace`, `Staged_Katana`. Settings: `Mobile_RPAsset.asset` (shadow distance). Import settings: `DungeonPackLite.fbx.meta`,
`Ch25_nonPBR.fbx.meta`, `WarFlail.fbx.meta`, 28 Old Sea Port and 9 Decrepit Dungeon `.fbx.meta`, six `Eerie_*.tif.meta`, and
`Ch25_WeakPointSplit.asset`. Scripts: `Combat/WeaponSweep.cs`, `Monster/MonsterPersonalSpace.cs`, `Networking/WebRtcStreamer.cs`;
new `Manager/EditorTelemetryGuard.cs` (Editor-only, compiled out of builds). Editor: new `OptimisationHarness.cs`,
`QuestShadowTrim.cs`; `Level3Builder.cs`, `Level3SceneBuilder.cs`, `Level3DualAxeBuilder.Monster.cs`.
