# Olympic Wrestling Simulator

A 3D wrestling game for iOS, Android, macOS and Windows covering Greco-Roman, Freestyle and Women's wrestling under the current UWW rules. Build a wrestler, manage their career week by week, and wrestle bouts with gestures, coach them from the corner, or simulate them.

- Design: [docs/game-design.md](docs/game-design.md)
- Project rules and decisions: [CLAUDE.md](CLAUDE.md)

## Repository layout

| Path | Contents |
|---|---|
| `src/Wrestling.Core` | Simulation core: pure C# (.NET Standard 2.1, C# 9), no Unity dependency |
| `src/Wrestling.Data` | Content loading, validation and persistence |
| `src/Wrestling.Sim` | Headless command-line runner (.NET 10) |
| `tests/` | Unit and determinism tests (xUnit) |
| `unity/WrestlingSimulator` | Unity 6000.6.7f1 project (URP): presentation, input, UI |
| `data/` | Game content as JSON |
| `docs/` | Design documents |

## Requirements

- .NET SDK 10
- Unity 6000.6.7f1 with iOS, Android and Windows build support
- Xcode (iOS builds), Git LFS

## Getting started

```bash
git lfs install
dotnet build -c Release
dotnet test
```

`dotnet build` copies `Wrestling.Core.dll` and `Wrestling.Data.dll` into `unity/WrestlingSimulator/Assets/Plugins/Wrestling/`. These DLLs are not committed, so **build once before opening the Unity project**.

## Headless simulation

`Wrestling.Sim` runs bouts and competitions without the game, for balancing and checks:

```bash
dotnet run --project src/Wrestling.Sim -c Release -- bout --style fs --seed 4
```

```bash
dotnet run --project src/Wrestling.Sim -c Release -- balance --style gr --bouts 300
```

```bash
dotnet run --project src/Wrestling.Sim -c Release -- tournament --style ww --entrants 20 --seed 1
```

Styles are `fs` (Freestyle), `gr` (Greco-Roman) and `ww` (Women's). Run `help` for all options.

## Bout viewer (Unity)

`Assets/Scenes/BoutViewer.unity` plays AI bouts on the engine in 3D with a scoreboard and play-by-play. The scene and its materials are generated from code: menu **Wrestling → Build Bout Viewer Scene**, or in batch mode:

```bash
/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath unity/WrestlingSimulator -executeMethod Wrestling.Editor.BoutViewerSceneBuilder.BuildFromCommandLine -logFile -
```

Stills of notable moments from real bouts are written to `docs/images/` by **Wrestling → Render Bout Viewer Stills** (or `-executeMethod Wrestling.Editor.BoutViewerStills.RenderFromCommandLine -stillsOutput DIR`, without `-nographics`).

![Freestyle fireman's carry](docs/images/bout-freestyle-throw.png)

Wrestlers are MakeHuman (CC0) characters, scaled and shaped per wrestler with blend shapes; move animation comes from `data/animation/` (one file per technique, one clip per phase). Loading rejects any clip a body could not perform at its phase's fastest: hips dropping faster than free fall, a body lifted off the mat outside a throw, or a hip throw loaded without the thrower's hips under the opponent's and the trunk bent over them (`ClipFeasibility`, tested in `AnimationBiomechanicalTests`).

Player builds copy `data/` into StreamingAssets automatically (`ContentBuildStep`).

## Character models

The wrestler models in `unity/WrestlingSimulator/Assets/Characters` are built from MakeHuman CC0 assets with Blender and the MPFB2 extension. To rebuild them, install Blender 4.2+ and MPFB2, install the `makehuman_system_assets`, `skins01` and `skins02` asset packs (`tools/characters/install_assets.py`), then:

```bash
/Applications/Blender.app/Contents/MacOS/Blender --background --python tools/characters/build_wrestlers.py -- unity/WrestlingSimulator/Assets/Characters
```

Then run **Wrestling → Build Character Library** in Unity (the scene builder does this too).

On import, `CharacterImport` adds forearm, upper-arm and thigh twist bones, corrective shoulder (raised, across the chest, back behind the body) and hip blend shapes, six facial effort shapes (brow furrow, eye squint, nostril flare, jaw clench, lip press, grimace; measured from the model's own eyes, nose and jaw weights, so no Blender run is needed), and a raised singlet with smooth seams to the models. After changing it, run **Wrestling → Reimport Characters** (`-executeMethod Wrestling.Editor.CharacterImport.ReimportFromCommandLine`). **Wrestling → Diagnostics → Render Deformation Sheet** (`-executeMethod Wrestling.Editor.DeformationSheet.RenderFromCommandLine -deformationOutput DIR`, without `-nographics`) renders stress poses with and without these corrections, side by side.

## Recorded motion

Real motion (motion capture, or extracted from video with a tool that allows commercial use) replaces the keyed clips phase by phase. Import two people's BVH files (or a keypoint JSON) as a take, then assign a frame range to a technique phase:

```bash
dotnet run --project src/Wrestling.Sim -c Release -- motion-import --id commons.semenov-coon.throw1 --bvh-a a.bvh --bvh-b b.bvh --source-url URL --source-author NAME --source-license CC-BY-SA-4.0
```

```bash
dotnet run --project src/Wrestling.Sim -c Release -- motion-assign --clip anim.headlock_throw.flight --take commons.semenov-coon.throw1 --from 120 --to 150 --executor 0
```

Every take records its source and licence. See `docs/research/footage-sources.md`.

## Physics lab

The wrestlers' bodies are physically simulated (display only). The lab plays real bouts live and measures how the bodies follow the motion, overlap, keep their footing and hold grips, and what they cost per frame:

```bash
/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath unity/WrestlingSimulator -executeMethod Wrestling.Editor.PhysicsLab.RunFromCommandLine -labSeconds 60 -labBouts 3 -logFile -
```

Any stretch of a bout can be rendered as video, with the animation's pelvis targets marked (add `-clipLog` for a line per frame):

```bash
/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath unity/WrestlingSimulator -executeMethod Wrestling.Editor.PhysicsLab.ClipFromCommandLine -clipStyle gr -clipSeed 6 -clipFrom 2380 -clipTicks 150 -clipOutput clip.mp4 -logFile -
```

Latest lab run (4 bouts × 45 s per style, `-labBouts 4`): pelvis 7.3 cm from its animated place on average, a body off its motion (over 25 cm) in 8.2% of frames (mostly recovering from scrambles and throws), standing collapses 0.9%, mean overlap 0.1 cm, 87% of gripping hands holding the opponent, and 76% of the balance force and turn the bodies use could be made by their mat contacts alone (the rest stands in for stepping and leaning on the opponent). After the physics step each holding hand is put back on what it holds on the shown bones (`PostPhysicsReprojector`): the palms the physics leaves 9.0 cm from their holds on average are shown 0.4 cm from them (what remains is holds being torn off). Before the biomechanics pass (feasible clips, compact par terre base, anatomical seats, shoulder reach cone) the same run gave 8.3 cm, 9.8% and 80% of grips held; before the production refactoring (spine torsion, wrist and hand poses, grip sockets, load compliance) 11.2 cm, 14.5% and 67%. Bodies give under what they bear (`-labNoCompliance` turns that off to compare). The physics always steps at exactly 1/240 s whatever the frame rate (a frame takes the whole steps its time holds and carries the rest over), so the bodies behave the same on every device: `-labFps N` and `-labJitter F` play the lab at another frame rate or with each frame's time varied by up to ±F. At 30, 60 and 120 fps and at 60 fps ±50% the pelvis error stays 7–10 cm and lost frames 8–12%, within the spread between bouts (jerk figures are per frame, so they only compare at one frame rate). The physics costs about 1 ms per frame for both wrestlers on an Apple Silicon Mac (about 2 ms in the lab, which also solves the contact forces). Add `-clipSide` to a clip for a profile camera.

## Determinism check

The simulation must give identical results on every platform. A fixed workload produces a hash that every platform must reproduce:

```bash
dotnet run --project src/Wrestling.Sim -c Release -- determinism-probe
```

Inside Unity: menu **Wrestling → Diagnostics → Run Determinism Probe**, or in batch mode:

```bash
/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath unity/WrestlingSimulator -executeMethod Wrestling.Editor.DeterminismCheck.RunFromCommandLine -logFile -
```

On devices, add the `DeterminismProbeRunner` component to a scene; it logs `PASS` or `FAIL` at start.
