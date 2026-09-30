# Fitness Monster Battle (VR) — Project Context

Last updated: **2026-09-30 (see project_progress.md)**

## Overview

Players fight monsters with VR weapons powered by real fitness movement.  
**3 separate level scenes**, Inspector weapon loadouts via `WeaponType`.

| Level | Scene | Environment | Player weapons | Monster weapon | Monster |
|---|---|---|---|---|---|
| 1 Easy | `Level1_Easy` ✅ built | Lite Dungeon Pack (Gridness) | Dual hammers | Sword | Hip Hop prefab (Warrok mesh) |
| 2 Medium | `Level2_Medium` ✅ built | Lite Dungeon Pack, night sky | Dual swords | Two-handed axe (10-attack table + defend) | Vampire A Lusth |
| 3 Hard | `Level3_Hard` shell | Old Sea Port | Flexible | Hammer | TBD |

## What MCP built (2026-09-21)

- Folder moves for weapons/monsters into `Models/` + prefabs
- All 5 scenes + Build Settings
- Full **Level1_Easy** playable draft: dungeon arena, BattleSystems, Hip Hop monster prefab, hammer + HitDetection, world canvas
- Tags: Hammer, Monster, Player

## Weapon system

```csharp
public enum WeaponType { None, Hammer, Sword, Shield }
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
  Combat/     WeaponType, HitDetection, BattleManager
  Monster/    MonsterController, MonsterHealth, MonsterAI, MonsterAnimator
  Player/     PlayerCombat, PlayerHealth, PlayerExertionAudio (heavy breathing)
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
| L2 swords / axe | `Assets/Models/Weapons/Player/L2_PlayerSword_Split.asset`, `Assets/Models/Weapons/Monster/L2_MonsterAxe_Split.asset` |
| L2 audio / VFX | `Assets/Audio/SFX/Level2/`, `Assets/VFX/Level2/` |
| Lite Dungeon | `Assets/Gridness Studios/Elementary Dungeon Pack Lite/` (do not move) |
| Old Sea Port | `Assets/Old Sea Port/` (do not move) |
| HotelRoom | `Assets/HotelRoom-Mavi3D/` (unused) |

## Docs

- `Documentation/SETUP_GUIDE.md`
- `Documentation/MOVE_LIST.md`
- `Documentation/project_progress.md`
- Root `README.md`, `.gitignore`, `.gitattributes`
