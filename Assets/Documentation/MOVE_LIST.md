# File Move List — Do remaining moves IN Unity Project window

Scripts + docs already moved from Cursor (with `.meta` files preserved).  
**Models / environments / weapons** should be moved in the **Unity Editor** (Project tab) so references stay valid.

---

## Already moved by Cursor (verify in Unity after open)

| Old path | New path |
|---|---|
| `Assets/CubeTapCounter.cs` | `Assets/Scripts/Legacy/CubeTapCounter.cs` |
| `Assets/LaserHandGrabber.cs` | `Assets/Scripts/Legacy/LaserHandGrabber.cs` |
| `Assets/LaserPointer.cs` | `Assets/Scripts/Legacy/LaserPointer.cs` |
| `Assets/VRSoundButton.cs` | `Assets/Scripts/Legacy/VRSoundButton.cs` |
| `Assets/RoundManager.cs` | `Assets/Scripts/Manager/RoundManager.cs` |
| `Assets/context.md` | `Assets/Documentation/context.md` |
| `Assets/project_progress.md` | `Assets/Documentation/project_progress.md` |

> After opening Unity: if any scene component shows **Missing Script**, re-assign the script from the new folder (GUID should usually survive).

---

## Move these IN UNITY (drag in Project window)

### Weapons → Models/Weapons then Prefabs/Weapons

| Old path | New path |
|---|---|
| `Assets/training/hammer/` (entire folder) | `Assets/Models/Weapons/hammer/` |
| `Assets/training/medieval-shield/` | `Assets/Models/Weapons/medieval-shield/` |
| `Assets/training/shattered-crystal-sword/` | `Assets/Models/Weapons/shattered-crystal-sword/` |

Then create prefabs (after setup):

| Prefab | Suggested path |
|---|---|
| Hammer combat prefab | `Assets/Prefabs/Weapons/Weapon_Hammer.prefab` |
| Crystal Sword prefab | `Assets/Prefabs/Weapons/Weapon_Sword.prefab` |
| Medieval Shield prefab | `Assets/Prefabs/Weapons/Weapon_Shield.prefab` |

### Monsters → Models/Monsters

| Old path | New path |
|---|---|
| `Assets/training/Hip Hop Dancing.fbx` (+ `.meta`) | `Assets/Models/Monsters/Hip Hop Dancing.fbx` |
| `Assets/training/Two Handed Sword Death.fbx` (+ `.meta`) | `Assets/Models/Monsters/Two Handed Sword Death.fbx` |
| `Assets/training/dance.controller` (+ `.meta`) | `Assets/Animations/dance.controller` |
| `Assets/training/warrok_MAT.mat` (+ variants) | `Assets/Materials/warrok_MAT.mat` |
| `Assets/training/bear_*.png` textures | `Assets/Models/Monsters/Textures/` (create folder) |
| `Assets/training/Cube.prefab` | `Assets/Prefabs/Monsters/Cube_Fallback.prefab` |

Hip Hop prefab after wiring:

| Prefab | Path |
|---|---|
| Easy monster | `Assets/Prefabs/Monsters/Monster_Easy_HipHop.prefab` |
| Medium monster | `Assets/Prefabs/Monsters/Monster_Medium_Warrok.prefab` |
| Hard monster | `Assets/Prefabs/Monsters/Monster_Hard_Warrok.prefab` |

### Environments (optional tidy — packs already imported)

| Current path | Optional new path |
|---|---|
| `Assets/Gridness Studios/Elementary Dungeon Pack Lite/` | Keep as-is **OR** move to `Assets/Models/Environments/LiteDungeonPack/` |
| `Assets/Old Sea Port/` | Keep as-is **OR** move to `Assets/Models/Environments/OldSeaport/` |

**Recommendation:** leave Asset Store packs where Unity imported them (`Gridness Studios/`, `Old Sea Port/`). Only use the `Models/Environments/*` folders as shortcuts/aliases if you prefer — moving huge packs can break internal prefab paths.

### Keep untouched

| Path | Why |
|---|---|
| `Assets/HotelRoom-Mavi3D/` | Unused for levels 1–3; do not delete |
| `Assets/Oculus/`, `Assets/XR/` | Meta XR |
| `Assets/TextMesh Pro/` | UI |

### Scenes to CREATE in Unity (File → New Scene → Save As)

| New scene file | Notes |
|---|---|
| `Assets/Scenes/MainMenu.unity` | UI + buttons |
| `Assets/Scenes/Level1_Easy.unity` | Lite Dungeon + Hip Hop |
| `Assets/Scenes/Level2_Medium.unity` | Lite Dungeon + Warrok |
| `Assets/Scenes/Level3_Hard.unity` | Old Sea Port |
| `Assets/Scenes/Results.unity` | Score UI |
| `Assets/Scenes/SampleScene.unity` | Keep as sandbox / delete later |

---

## How to move safely in Unity

1. Close Play Mode.
2. In **Project** window, drag folder/file to destination.
3. When Unity asks about updating references → **Yes**.
4. Never use Finder/Explorer for `.fbx` / `.prefab` / `.unity` unless you move the matching `.meta` too (Cursor already did this for scripts only).
