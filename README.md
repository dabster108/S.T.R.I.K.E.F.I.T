# Fitness Monster Battle (VR)

VR fitness combat game built in **Unity 6** (`6000.5.11f1`) with **Meta XR SDK**. Players swing real weapons (hammer / sword / shield) — movement and hit counts power damage against monsters across **3 level scenes**.

## Tech stack

| Piece | Version / notes |
|---|---|
| Unity | 6000.5.11f1 (URP) |
| Meta XR | `com.meta.xr.sdk.all` 205.0.0 |
| OpenXR | 1.17.1 |
| Input | OVRInput + Unity Input System |
| UI | uGUI + TextMesh Pro |

## Scenes

| Scene | Environment | Default weapons |
|---|---|---|
| `MainMenu` | UI only | — |
| `Level1_Easy` | Lite Dungeon Pack (Gridness) | Player: Hammer · Monster: None (Hip Hop Mixamo) |
| `Level2_Medium` | Lite Dungeon Pack | Player: Sword + Shield · Monster: Sword |
| `Level3_Hard` | Old Sea Port | Player: flexible · Monster: Hammer |
| `Results` | UI summary | — |

Weapon types are **Inspector-driven** via `WeaponType` (`Hammer`, `Sword`, `Shield`, `None`).

## Setup (clone → play)

1. Install **Unity Hub** + Editor **6000.5.11f1** (or matching major).
2. Install **Git LFS** (`git lfs install`) before cloning large FBX/textures.
3. Clone this repo and open `VR-Gamification-Fitness` in Unity Hub.
4. Wait for package import (Meta XR, URP, asset packs).
5. **File → Build Settings**: add scenes in order:  
   `MainMenu`, `Level1_Easy`, `Level2_Medium`, `Level3_Hard`, `Results`.
6. Open `Level1_Easy`, press Play (or MainMenu → Level 1).
7. Quest: Meta Quest build profile under `Assets/Settings/Build Profiles`.

See `Assets/Documentation/SETUP_GUIDE.md` for prefab wiring.

## Repo hygiene

- `.gitignore` excludes `Library/`, `Temp/`, `Logs/`, IDE project files, Burst debug folders.
- `.gitattributes` marks Unity YAML as text and large binaries for Git LFS.

## Docs

- `Assets/Documentation/context.md` — architecture
- `Assets/Documentation/project_progress.md` — status / checklist
- `Assets/Documentation/SETUP_GUIDE.md` — Unity steps
- `Assets/Documentation/MOVE_LIST.md` — folder cleanup paths
- `web/README.md` — Node backend + React stats board (`db.json` tracking)

## Live stats (optional)

```bash
cd web/backend && npm install && npm start   # http://localhost:4000
cd web/frontend && npm install && npm run dev # http://localhost:5173
```

Unity posts to `http://localhost:4000/api/sessions` (see `web/BACKEND_URL.json`). On Quest, use your Mac LAN IP.

## License / assets

Third-party packs (Lite Dungeon, Old Sea Port, Mixamo characters, Sketchfab weapons) remain under their own Asset Store / author licenses. Do not redistribute those packages outside Unity’s license terms.
