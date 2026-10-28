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

Wrestlers are MakeHuman (CC0) characters, scaled and shaped per wrestler with blend shapes; move animation comes from `data/animation/` (one file per technique, one clip per phase).

Player builds copy `data/` into StreamingAssets automatically (`ContentBuildStep`).

## Character models

The wrestler models in `unity/WrestlingSimulator/Assets/Characters` are built from MakeHuman CC0 assets with Blender and the MPFB2 extension. To rebuild them, install Blender 4.2+ and MPFB2, install the `makehuman_system_assets`, `skins01` and `skins02` asset packs (`tools/characters/install_assets.py`), then:

```bash
/Applications/Blender.app/Contents/MacOS/Blender --background --python tools/characters/build_wrestlers.py -- unity/WrestlingSimulator/Assets/Characters
```

Then run **Wrestling → Build Character Library** in Unity (the scene builder does this too).

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
