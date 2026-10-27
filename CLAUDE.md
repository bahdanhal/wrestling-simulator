# Olympic Wrestling Simulator

A 3D, cross-platform (mobile first, plus desktop) game about Olympic wrestling: Greco-Roman, Freestyle and Women's wrestling.

The full game design document is [docs/game-design.md](docs/game-design.md). It is the source of truth for systems and rules (based on the UWW rulebook, January 2026 edition).

## Core principle: full product, no MVP

- This is built as a **full-scale product from day one**. There is no MVP phase.
- **No hardcoded stubs, placeholders, fake data, or "TODO: implement later" shortcuts.** Every system that ships is real, data-driven and complete for its scope.
- Work can be delivered in stages, but each stage is production quality, not a throwaway prototype.

## Product decisions so far

- **Styles:** Greco-Roman, Freestyle, Women's. Style differences are rule configurations, not separate code paths. Rules are versioned data files (UWW rules change often).
- **Game shape:** career management + playable matches + coaching. Not a pure stats simulator.
- **Match modes (all on the same engine):**
  - **Play:** gesture control. Drag sets push direction and pressure; gestures trigger techniques. Success depends on gesture quality plus wrestler variables (skills, fatigue, grips, position, opponent defense).
  - **Coach:** watch the AI wrestle and change tactics.
  - **Sim:** instant result.
- **Player roles:** one career arc: wrestler → coach → federation manager.
- **Career time scale:** week by week (training plans, weight management) with tournaments in the calendar.
- **Wrestler creation:** built from scratch by allocating skills.
- **Realism:** realistic but playable.
- **Wrestlers:** fictional, or user-imported rosters (no real likenesses).

## Architecture rules

- The simulation core (rules, match engine, career, tournaments) is independent of rendering and input, deterministic and seeded, and can run headless (for tests, balancing, mass simulation).
- Play mode replaces the AI's decisions with player input; it never uses different resolution logic.
- The match engine uses a fixed tick rate and fixed-point math, so a bout replays identically on every platform (replays, cloud saves, future online play).

## Techniques and counters

- **The simulation owns the move; animation follows it.** Outcomes are never decided by an animation clip.
- Each technique is a sequence of **phases** (for example: setup → grip → entry → commitment → flight → landing → control). At every phase the engine evaluates both wrestlers' state: grips, hip height and position, balance, momentum, fatigue.
- The defender can act in any phase. Each phase defines which counters are valid (for example: sprawl or whizzer during entry, re-arch or roll-through during flight, bridge or turn-out on landing).
- A counter is a technique that starts from the current state, so counter-to-counter chains arise naturally, with each level less likely.
- **Scoring is decided from the end state** (who lands in control, what exposure happened), not from who started the move, matching how referees score.
- Techniques, phases and counter windows are defined in data, not code.

## Animation

- Short paired segments per phase (about 0.3–1 s), not full-move clips, stitched with **motion matching** so any phase can flow into any valid next phase from the current pose.
- **IK contact constraints** keep hands on real grip points and bodies in contact.
- **Active ragdoll** (physics muscles following the animation) for balance loss, landings and scrambles.
- Motion capture is recorded per phase, including counters and transitions.
- Gesture input gives the defending player reactive windows during the opponent's entry and flight.

## Tech stack (decided)

- **Engine: Unity (C#)**, URP renderer, targets iOS, Android, macOS, Windows.
- **`Wrestling.Core`: a pure C# library with no Unity dependency** (rules, match engine, career, tournaments, AI). It has its own unit tests and a headless command-line runner for mass simulation and balancing.
- **Limit engine lock-in:**
  - Unity components only display and collect input; no gameplay logic in them.
  - Data in plain files (JSON or similar), not Unity-only formats.
  - Animation assets in standard FBX/glTF.
  - Goal: the core could move to another .NET-capable engine (for example Godot) with only the presentation layer rewritten.
- Physics: Unity's built-in PhysX (Havok is not needed).

## Working in this repo

- Layout and setup: see `README.md`. Unity project: `unity/WrestlingSimulator` (Unity 6000.6.7f1, URP).
- Build and test: `dotnet build -c Release` then `dotnet test`. Building copies the core DLLs into `unity/WrestlingSimulator/Assets/Plugins/Wrestling/` (not committed).
- `src/` libraries target .NET Standard 2.1 with C# 9 (Unity's limit). Warnings are errors.
- Simulation code uses `Fix64` (Q32.32) and `Pcg32` from `Wrestling.Core.Numerics`. Never use `float`, `double`, `System.Random` or `DateTime.Now` in simulation paths; floats are for rendering only.
- `DeterminismProbe.ReferenceHash` must match on every platform. Changing `Fix64` or `Pcg32` behaviour breaks replays; update the hash only on purpose.
- Unity scripts live in `Assets/Scripts/<Area>/` with their own `.asmdef`, referencing `Wrestling.Core.dll` through `precompiledReferences`. No gameplay logic in Unity code.
- Every rule from `docs/game-design.md` section 3 gets unit tests, using the rulebook's own examples where they exist.
