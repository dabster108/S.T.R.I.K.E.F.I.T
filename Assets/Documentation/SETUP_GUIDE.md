# SETUP GUIDE — 3 Level Scenes + Weapons

Unity version: **6000.5.11f1** · Meta XR **205** · URP

---

## Cursor vs Unity (read this first)

### Do in Cursor (already done / can continue)
- Edit C# scripts
- Update markdown docs
- Maintain `.gitignore` / README
- Paste Console errors back to the agent for fixes

### Do in Unity (required — Cursor cannot finish these)
- Create `.unity` scenes
- Drag Asset Store environments into scenes
- Build prefabs, Animator wiring, lighting
- Assign Inspector references on BattleSystems
- Play Mode / Quest builds

MCP browser tools are **not** needed for this pass. Open Unity when scripts are ready.

---

## PART A — Scene setup

### Build Settings order
1. `MainMenu`
2. `Level1_Easy`
3. `Level2_Medium`
4. `Level3_Hard`
5. `Results`

### Shared BattleSystems recipe (every level scene)

Empty GameObject `BattleSystems` with:
- `RoundManager` → **Battle Mode = true**
- `BattleManager` → assign `MonsterSpawnPoint`, optional fallback cube
- `LevelManager` → set `sceneDifficulty`, weapons, `monsterPrefabForThisScene`, `autoStartBattleOnLoad`
- `GameStateManager` → scene name strings (only need DontDestroy instance once from MainMenu)
- `CombatUIController` → wire canvas
- `PlayerCombat` → assign hammer/sword/shield object refs
- `PlayerHealth`

Also in scene:
- OVR Camera Rig (Meta XR)
- `MonsterSpawnPoint` empty (~2.5m ahead)
- World Space Canvas (HP / score / timer / victory-defeat)
- Directional light (or pack lighting)

---

### Level1_Easy

1. File → New Scene → Save as `Assets/Scenes/Level1_Easy.unity`
2. Environment: from `Assets/Gridness Studios/Elementary Dungeon Pack Lite/` drag a demo/scene prefab or modular pieces.
3. Place OVR Camera Rig at a clear floor spot.
4. BattleSystems as above:
   - `sceneDifficulty = Easy`
   - Player weapons: Hammer / None
   - Monster weapon: None
   - Monster prefab: Hip Hop Easy prefab
5. Hip Hop Mixamo:
   - Drag `Hip Hop Dancing.fbx` → CapsuleCollider + Monster* scripts
   - Animator: `dance.controller`
   - Save `Prefabs/Monsters/Monster_Easy_HipHop.prefab`
6. Player: enable Hammer object; disable Sword/Shield
7. Play — battle should auto-start (or press **B**)

### Level2_Medium

1. New scene `Level2_Medium.unity`
2. Same Lite Dungeon environment (duplicate Level1 scene then retarget — fastest)
3. LevelManager: Medium · Player Sword+Shield · Monster Sword
4. Monster: Warrok / Two Handed Sword Death prefab with sword child enabled
5. Player: enable Sword + Shield objects; HitDetection on sword blade

### Level3_Hard

1. New scene `Level3_Hard.unity`
2. Environment from `Assets/Old Sea Port/` (demo scene pieces / prefabs)
3. LevelManager: Hard · Monster Hammer · Player flexible (default Hammer)
4. Lighting: seaport scenes often need reflection probes / fog tweaks from the pack

### MainMenu / Results

- MainMenu buttons → `GameStateManager.LoadLevel1/2/3`
- Results: show score; button → MainMenu
- Victory UI button → `GameStateManager.LoadNextLevelOrResults`

---

## PART B — Weapon prefabs

| Model | Source | Prefab |
|---|---|---|
| Hammer | `training/hammer/source/*.fbx` | `Prefabs/Weapons/Weapon_Hammer` |
| Shield | `medieval-shield/.../SM_Medieval_Shield.fbx` | `Prefabs/Weapons/Weapon_Shield` |
| Sword | `shattered-crystal-sword/.../Crystal Sword.fbx` | `Prefabs/Weapons/Weapon_Sword` |

Each attack weapon: Collider (trigger) + `HitDetection`.  
Parent hammer to right hand, shield to left hand for Level 2.

---

## PART C — Hip Hop = Level 1 monster (Mixamo)

You already have the Mixamo Hip Hop FBX. Use it as Easy:

1. Import scale / Avatar = Humanoid if animations need retarget later.
2. For v1, looping dance as Idle is fine (`MonsterAnimator` falls back if Attack/Hit triggers missing).
3. Add combat scripts on root; tag `Monster`.
4. WeaponType **None** = fists/claws flavor (no weapon mesh).

---

## PART D — Testing

| Key | Action |
|---|---|
| B | Start Easy battle (editor) |
| K | 25 dmg to monster |
| H | Fitness tap |
| Left Shift | Simulate shield block |

Checklist: hits register → HP drops → death → victory → unlock Level 2.

---

## PART E — Git (when ready)

```bash
cd VR-Gamification-Fitness
git lfs install
git init
git add .gitignore .gitattributes README.md Assets Packages ProjectSettings
git status   # confirm Library/ is NOT listed
```

Ask Cursor to create the first commit when you want it.
