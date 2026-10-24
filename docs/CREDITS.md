# Credits

Everything below is taken from what this repository already records: the main `README.md`'s
Acknowledgements section, `Assets/Documentation/context.md` / `project_progress.md`, and the folder and
file names under `Assets/Models/` and `Assets/Audio/`. Nothing here is invented; anything the repo doesn't
name is listed as a TODO instead of a guess. This file is linked from the website's footer Credits section.

## Environments

- **Elementary Dungeon Pack Lite** — Gridness Studios. Levels 1 and 2 (`README.md` Acknowledgements).
- **Old Sea Port** environment pack — creator not recorded in the repository. **TODO:** find the asset
  listing (store, author, licence) and add it here before relying on this for a public credit.

## Characters and animation

- **Adobe Mixamo** — every monster and player-rig model and animation clip (`README.md`: "Characters and
  animation... Mixamo clips on Humanoid rigs"). Specifically, by file name:
  - Level 1: `Assets/Models/Monsters/Hip Hop Dancing.fbx` ("Warrok" character, its own Hip Hop Dancing clip).
  - Level 2: `Assets/Models/Monsters/Level2/Vampire A Lusth.fbx`.
  - Level 3: `Assets/Models/Monsters/Level3/Ch25_nonPBR.fbx` (also `ArmoredExecutioner.fbx`, unused in the
    current build per `project_progress.md`).
  - All of the monster attack/idle/hit clips under `Assets/Animations/Monster/` (every one is literally
    named `mixamo.com` inside its FBX; see `project_progress.md`'s PART 4D for the per-state mapping).

## Weapons

Folder names under `Assets/Models/Weapons/` read as downloaded asset-pack names (the `source/` +
`textures/` layout matches how Sketchfab/CGTrader exports a model), but no licence or author file ships
alongside them, so each is a **TODO: find the original listing and confirm the licence** before any public
credit beyond "used in this prototype":
- `hammer/` — the player's Level 1/2 hammer.
- `medieval-shield/` — imported, not currently equipped in any build.
- `shattered-crystal-sword/` — the player's Level 1 crystal sword.
- `Player/Level3/Mace/`, `Player/Level3/Flail/`, `Player/Level3/Katana/` — the three Level 3 pickup weapons.
  `project_progress.md` (2026-10-02 entry) names the katana's source pack as
  `free-mace-katana-axe-weapon-pack-sketch-mesh` (Sketchfab), imported as `MaceKatanaAxe_Pack.fbx`.

## Sound

No sound-credits file exists in the repository. The sources below are read directly off each file's own
name under `Assets/Audio/`, which for several clips already carries what looks like a Pixabay-style
`uploader-description-trackid` name. **TODO: open each track's page on its source site and confirm the
licence terms before any public release build.**
- **bannythecoolio** — impactful damage (track 425132).
- **dragon-studio** — hammer smash effect (382731), sword slice (393847), sword unsheathing (393851).
- **freesound_community** — damage (40114), male hurt (48124).
- **ribhavagrawal** — heavy breathing (294195; the basis for the current `breathing_heavy_v3.wav`, levelled
  in-project, see `project_progress.md`'s 2026-10-10 breathing entry).
- **universfield** — dramatic death collapse (352720), male death scream (123078).
- **wings_of_freedom** — riser impact (606984).
- Every file prefixed `L2_` or `L3_` (for example `L2_Slam_Impact`, `L3_Roar_Enrage`) has no uploader name
  in its filename, so its source is unrecorded. **TODO.**

## Data

- **Touché** sword-strike recordings (gesture-detection set), used to train the Level 3 Expert motion forecaster in
  `ml-services/`: J. Dehesa, A. Vidler, C. Lutteroth and J. Padget, "Touché: Data-Driven Interactive Sword Fighting in Virtual
  Reality", CHI 2020. Licence CC BY 4.0. The raw data is not redistributed in this repository.

## People

- Softwarica College — resources and environment for this thesis project.
- Manoj Shrestha — supervision throughout development.

## Tools

- Unity, the Meta XR SDK, Unity Inference Engine, Mixamo, Node.js, PostgreSQL (hosted on Layerbase, earlier Neon), React, Vite and
  Tailwind CSS, per `README.md`.
- Python, PyTorch, ONNX / ONNX Runtime, NumPy, pandas, SciPy, scikit-learn, Matplotlib, JupyterLab and uv for `ml-services/`.
- The three monster portraits on this site were rendered from the project's own Unity models with
  three.js, not drawn or sourced separately — see `web/tools/render-portraits/README.md`.
