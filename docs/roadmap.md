# Roadmap: from keyed clips to real wrestling

31 October 2026. The goal is that bouts **look like real wrestling**, judged against real athletes rather than against our own animation. This sets the order of the work after the biomechanics pass (`production-grade-biomechanics-and-visual-plan.md`) and the first real-motion import (`research/real-motion-comparison.md`). Game systems that do not depend on it run in parallel (last section).

## Where we are

- The simulation decides every outcome; the viewer shows it with hand-keyed clips (80 phases), a procedural stance and hand fight, and physically simulated bodies that follow them.
- Clips are now physically feasible (`ClipFeasibility`), but they are still our guess at wrestling, and the physics lab only measures how well the bodies follow that guess.
- Real two-person 3D motion exists in the repo for the first time: three checked Harmony4D takes (a double-leg penetration, a snap-down, a body lock), plus tools to find and convert more (`tools/motion/harmony4d/`).
- **The main blocker is timing.** Real movements take 3–8 times longer than our technique phases (snap-down 1.4 s against 0.17–0.27 s). Squeezed into a phase, real motion throws the bodies about, so none of it is in play yet.
- Real stances are about 10 cm taller at the hips and half as bent as ours; shots start from 1.15 m apart, not 0.80 m (recreational athletes, so elite figures will sit in between).

## Decisions needed first

| Decision | Options | Recommendation |
|---|---|---|
| Phase timing | Keep the engine's short phases (and only ever show compressed motion), or give phases real durations | Real durations. It changes simulation timing, so it comes with a rebalance (step 1). |
| Harmony4D licence | Rely on the MIT tags, or get written confirmation | Ask the authors to confirm commercial use of the 3D keypoints before shipping anything built on them. |
| Elite and Olympic-specific motion | Harmony4D only (recreational, no big throws, no par terre); coaching footage with permission; our own multi-camera session with a club; a commercial mocap studio day | Own multi-camera session with a club (step 4): it covers what no open source has, and the pipeline is mostly built. |

## Step 1. Real timing (1–2 weeks)

Give technique phases the durations real wrestlers take, then let real motion play.

1. Measure real durations per technique family: Harmony4D for shots, snap-downs, ties, locks and takedowns; the 2018 Worlds footage for throw timing (flight about 0.75 s).
2. Retime the phases in `data/techniques/` (minimum and maximum ticks, counter windows scaled with them), keeping the clips feasible (`ClipFeasibility` runs on load).
3. Rebalance with `Wrestling.Sim balance` and, if needed, `data/engine/tuning.json` so win types, points and attack success stay where they were or move towards real statistics.
4. Assign the three checked takes (`tools/motion/harmony4d/segments.json`), render them and run the physics lab.

**Done when** every phase with a take plays it at no more than 1.3× real speed, balance stays within its current spread, and the lab shows no regression (lost-motion frames ≤ 8%).

## Step 2. Real neutral wrestling (2–4 weeks)

Most of a bout is stance, circling, hand fighting and ties, and all of it is procedural today; recorded motion can only reach technique phases.

1. A neutral-motion library from Harmony4D: about 14 minutes of stance and 54 collar ties, more after step 4. Segments indexed by what the engine knows: distance, ties held, levels, facing, who is pressing.
2. A neutral playback in the viewer that picks the segment matching the engine's state each moment and blends between segments (the first, simple form of motion matching).
3. Recalibrate the procedural fallback (`ClipKinematics` stance height and lean) to the measured stances, for moments no segment covers.

**Done when** standing neutral plays recorded motion for most of its time and the procedural stance matches real hip height and lean within 3 cm and 5°.

## Step 3. Realism benchmark (1 week, then ongoing)

Replace "how well the bodies follow our clips" as the acceptance gate with "how close the bout looks to real wrestling".

1. Hold out real takes (the Harmony4D test split) as references.
2. Measure the viewer's shown motion against them in matching situations: hip height over time, trunk lean, distance between the bodies, hand-to-opponent contacts, movement timing and speeds.
3. Report it from the physics lab next to the existing figures; set targets per measure.
4. Expert review: a wrestler or coach scores stills and clips side by side with real footage, at each milestone.

**Done when** the benchmark runs in the lab and has targets agreed with an expert reviewer.

## Step 4. Elite and Olympic-specific capture (4–8 weeks, needs a partner club)

What open data cannot give: elite stances and speed, Greco-Roman throws, lifts, par terre turns and counters.

1. A shot list from the catalogue: every phase of the 51 techniques, both sides, the counters, several repetitions.
2. A multi-camera session with a club: 8–12 synchronised cameras round the mat, calibrated; written consent and licence for the footage and the motion.
3. A multi-view reconstruction tool: 2D pose per camera (RTMPose, already used in the Commons pilot) triangulated across cameras, the same approach as Harmony4D, written into `tools/motion/`.
4. Takes for every phase, checked by eye and assigned; the keyed clip stays as a fallback only where a phase has no take.

**Done when** every technique phase used in Greco-Roman and freestyle plays recorded elite motion.

## Step 5. Motion matching and physics on real motion (3–6 weeks)

1. Full motion matching: a database of all takes with pose, velocity and contact features, searched every frame so any phase flows into the next from the current pose (design section 7).
2. Revisit physics features that failed on keyed clips because their stances only balance by leaning on the opponent: contact-only balance (`ContactSolver` driving it) and the footwork controller. Real motion is balanced, so they should now work.
3. Investigate the lost-frames rise after the loaded-wrist change (6.2% → 8.2%, mostly women's bouts) and the worst sunk foot (−7 cm).
4. Physics imitation training (`tools/imitation`) on the real takes, on GPU, for throws the articulated bodies cannot follow by tracking alone.

**Done when** contact feasibility is above 90%, lost-motion frames are under 5%, and phase transitions show no visible pops.

## Step 6. Close-up finish (ongoing, lower priority)

Faces (validate the effort expressions against reference photos), hands on the mat and in grips, skin, hair and sweat, judged in the broadcast stills and close-ups at each milestone.

## In parallel: game systems

These do not wait for the motion work (detail in `implementation-status.md`, "Next systems"):

1. Calibration against real competition statistics (the retimed phases from step 1 feed into it).
2. Officials model: observation noise, wrong calls, challenge decisions.
3. Engine performance: under 5 ms per bout on a mid-range phone.
4. Event scheduling.
5. Wrestler creation, ageing and training.
6. Play mode gestures and Coach mode controls.
