# Real motion against the keyed clips

31 October 2026. Real two-person 3D motion from Harmony4D (multi-camera, both bodies tracked through contact; see `footage-sources.md`) compared with our keyed clips. Tools and how to repeat it: `tools/motion/harmony4d/`. The athletes are recreational, not elite, so stance and speed figures lean upright and slow against international wrestlers.

## Data

- 127 sequences fetched (31 MB of poses): grappling 67 sequences (11.8 minutes), MMA 60 (13.4 minutes). All pass the checks: confidence 1.0, thigh and shin 0.38–0.48 m, standing hips about 0.8 m.
- Detected situations: stance 848 s, collar ties 54, shots 27, other level changes 56, snap-downs 13, takedowns 28, body locks 9. Detections are candidates; the ones used were checked against the camera images (`tools/motion/harmony4d/segments.json`).
- Imported as takes (`data/motion/takes/`): `harmony4d.train-grappling-001` (601 frames), `harmony4d.test-grappling2-043` (326), `harmony4d.train-grappling-055` (67), with full attribution.

## Findings

### 1. Real techniques take far longer than our phases

| Phase | Real (checked segment) | Core of the motion | Our phase | Our phase is |
|---|---|---|---|---|
| Double-leg penetration (hips 0.46 m down to a knee) | 1.0 s | 0.7 s | 16–22 ticks, 0.27–0.37 s | 2.7–3.8× faster |
| Snap-down (opponent's head pulled 0.64 m down) | 1.4 s | 0.8 s | 10–16 ticks, 0.17–0.27 s | 5.2–8.4× faster |
| Body lock (0.69 m apart to chest to chest) | 1.0 s | 0.8 s | 12–20 ticks, 0.20–0.33 s | 3.0–5.0× faster |

Over all 83 real shots and level changes the core of the drop (10–90%) takes 0.40 s at the median, 0.20 s at the quickest tenth; hips drop 0.34 m at the median (0.21–0.55 m), peaking at 1.1 m/s (3.0 m/s at most). The 2018 World Championships throw (Semenov–Coon, single camera) agrees: its flight takes about 0.75 s, against 0.27–0.43 s for our throw flights.

A recorded take is stretched over its phase, so today it would play 3–8 times too fast. Tried with the snap-down: the snapped wrestler's physical body was driven at that speed and thrown upside down. The takes are therefore imported but **not assigned** (`data/motion/index.json` is empty) until the phases have real durations.

### 2. Stance: taller and more upright

| | Real | Keyed (level 0.75, posture 0.85) |
|---|---|---|
| Hip height, share of stature | 0.46–0.48 | 0.40 |
| Trunk lean | 16–17° | 33° |
| Distance between pelvises at the start of a shot | 1.15 m | 0.80 m |

Our stance is about 10 cm lower at the hips and leans twice as far forward. Recreational athletes stand taller than elite ones, so the truth for elite wrestlers is probably between the two; elite reference motion is needed to settle it. Shots also start from further away than our clips assume.

### 3. Shots end where ours do

From a higher start, the real double-leg penetration ends at hips 0.22 of stature, the same as the keyed clip's knee-down end (0.22). Its trunk stays more upright on the way down (17–36° against 33–48°): the real shooter drops the hips under the shoulders rather than bending forward. Peak hip speed 0.74 m/s over a 0.46 m drop.

### 4. Body lock: the executor straightens up

Closing a body lock, the real executor's trunk comes up from 39° to 3° as the chests meet (hips stay at 0.46–0.49 of stature). The keyed clip keeps the trunk at 33° throughout.

### 5. Snap-down: the snapper steps back

The real snapper keeps their hips up (0.45–0.50) and steps back as they pull, the bodies separating from 0.89 m to 1.21 m. The keyed clip closes in (0.80 m to 0.57 m). The opponent ends bent over at hips 0.37 of stature, close to the keyed 0.32.

## What would make real motion usable

1. Phase durations from real timing: snap about 0.7–1.0 s, body lock about 0.8–1.0 s, double-leg penetration about 0.6–1.0 s (or the core 0.4 s for the fastest shooters). Phase durations are simulation content (they set counter windows and how the AI's attacks play out), so this is a game-design change, to be balanced with `Wrestling.Sim balance`.
2. Then assign the checked segments in `segments.json` with `motion-assign`, and render them to confirm.
3. Stance, hand fighting and ties are posed procedurally, not by technique clips, so recorded motion cannot reach them yet; a recorded neutral-motion library (stance, circling, hand fighting, collar ties) would need its own playback in the viewer.
