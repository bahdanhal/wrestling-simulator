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
