# Real motion against the keyed clips

31 October 2026. Real two-person 3D motion from Harmony4D (multi-camera, both bodies tracked through contact; see `footage-sources.md`) compared with our keyed clips. Tools and how to repeat it: `tools/motion/harmony4d/`. The athletes are recreational, not elite, so stance and speed figures lean upright and slow against international wrestlers.

## Data

- 127 sequences fetched (31 MB of poses): grappling 67 sequences (11.8 minutes), MMA 60 (13.4 minutes). Gross geometry checks give thigh and shin lengths 0.38–0.48 m and standing hips about 0.8 m. Stored confidence values are 1.0; that is not an independent accuracy check. Heights use a floor estimated from low ankles, not a camera-measured mat plane.
- Detected situations: stance 848 s, collar ties 54, shots 27, other level changes 56, snap-downs 13, takedowns 28, body locks 9. Detections are candidates; the ones used were checked against the camera images (`tools/motion/harmony4d/segments.json`).
- Imported as takes (`data/motion/takes/`): `harmony4d.train-grappling-001` (601 frames), `harmony4d.test-grappling2-043` (326), `harmony4d.train-grappling-055` (67), with full attribution.

The [native-time review](motion-review.md) now keeps the original camera, triangulated joints and
character rig visible separately, with exact frame mapping and source limitations. Per-leg IK
preserves reachable recorded ankle trajectories on the rig; it does not establish sole contact,
remove unreachable targets or preserve paired palm grips.

## Findings

### 1. Real techniques take far longer than our phases

| Phase | Real (checked segment) | Core of the motion | Our phase | Our phase is |
|---|---|---|---|---|
| Double-leg penetration (hips 0.46 m down to a knee) | 1.0 s | 0.7 s | 42–60 ticks, 0.70–1.00 s | spans this core and full segment |
| Snap-down (opponent's head pulled 0.64 m down) | 1.4 s | 0.8 s | 42–60 ticks, 0.70–1.00 s | approximately the core duration |
| Body lock (0.69 m apart to chest to chest) | 1.0 s | 0.8 s | 48–60 ticks, 0.80–1.00 s | spans this core and full segment |

Over all 83 real shots and level changes the core of the drop (10–90%) takes 0.40 s at the median, 0.20 s at the quickest tenth; hips drop 0.34 m at the median (0.21–0.55 m), peaking at 1.1 m/s (3.0 m/s at most). The 2018 World Championships throw (Semenov–Coon, single camera) agrees: its flight takes about 0.75 s, against 0.27–0.43 s for our throw flights.

A phase-assigned recording is stretched over its phase; the separate paired player can play native source time. Most phases still need measured durations. Three phases now have provisional ranges informed by these individual checked examples, not elite timing distributions. The snap's full 1.4-second segment would still be compressed. For the retimed double-leg and body-lock phases, existing counter-window ticks were scaled with the minimum phase length (penetration ends at tick 32; lock at tick 48), preserving their opening relative to the fastest phase, subject to integer rounding. This is a deliberate simulation-content change, requiring later competition-statistics calibration. The takes remain **unassigned** (`data/motion/index.json` is empty) pending validation of contact and playback.

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

### 5. Snap-down: the pair circles as separation grows

The source snapper keeps their hips up (roughly 0.45–0.50 of body height), while the pair's horizontal pelvis separation grows from 0.89 m to 1.21 m. This is not a straight backward step. In the initial executor-to-opponent frame, the executor travels to approximately (0.586, −0.345) m and the opponent to (1.023, 0.782) m over source frames 176–204: much of the separation comes from circling. An earlier proposed backward-only keyed edit misread that distance and was removed. The existing keyed path still closes along one line and needs replacement after checking headings and contacts; only the provisional snap timing changed. This single recreational sequence does not validate the go-behind or establish an expert technique model.

## What would make real motion usable

1. Phase durations from real timing: the snap now uses 0.7–1.0 s as a provisional core duration; body lock is about 0.8–1.0 s and double-leg penetration about 0.6–1.0 s (or the core 0.4 s for the fastest shooters) in the checked examples. Phase durations are simulation content (they set counter windows and how the AI's attacks play out), so this is a game-design change, to be balanced with `Wrestling.Sim balance`.
2. Then assign the checked segments in `segments.json` with `motion-assign`, and render them to confirm.
3. Stance, hand fighting and ties are posed procedurally, not by technique clips, so recorded motion cannot reach them yet; a recorded neutral-motion library (stance, circling, hand fighting, collar ties) would need its own playback in the viewer.
