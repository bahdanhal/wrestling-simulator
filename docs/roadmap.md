# Roadmap: everything the best version needs

31 October 2026. Everything still needed for the full product, by area. Not staged: each item is built to its final quality. Target for presentation: bouts that look like real wrestling, judged against real athletes, not against our own clips. Built so far: `implementation-status.md`. Harmony4D's MIT licence is confirmed.

## Real motion data

1. **All of Harmony4D:** every useful segment in the 127 grappling and MMA sequences (stance, circling, hand fighting, collar ties, pummelling, shots, snap-downs, body locks, takedowns, scrambles, clinches), checked against the camera images and imported as takes with attribution.
2. **Elite Olympic capture with a partner club:** every phase of all 51 techniques plus counters and transitions, both sides, several repetitions, in all three styles (Greco-Roman, freestyle, women's), including par terre and big throws; neutral wrestling at elite level; referee restarts, handshakes, walk-ons, celebrations, ceremonies. Written consent and licence from every athlete (design open item 7).
3. **Capture rig:** 8–12 synchronised, calibrated cameras round a mat.
4. **Multi-view reconstruction tool:** whole-body 2D keypoints per camera (body, hands, feet, face), identity tracking through contact, triangulation, floor fit, smoothing. Validated against Harmony4D's images and reference 3D annotations, including camera reprojection checks; triangulated joints do not establish palm contacts, sole contacts or head orientation.
5. **Hands, feet and head from the data:** grip hand shapes, toes and head orientation, rather than placed from the facing.
6. **Timing references for elite throws** from the 2018 Worlds footage (single camera gives timing even without 3D).
7. **Licence records:** attribution for every take; share-alike handling for CC BY-SA sources.

## Simulation matched to reality

8. **Real phase durations** for all 74 technique phases, measured per technique; counter windows scaled with them.
9. **Neutral tempo and spacing:** level-change rates, stance distance (real shots start 1.15 m apart, our clips 0.80 m), movement speeds.
10. **Calibration against real competition statistics:** victory types, points per bout and period, points by type, passivity and cautions, upset rates (design 15.1, open item 3).
11. **Balance tools** reporting against those targets: attribute sensitivity, technique reports, career reports (design 15.2).

## Animation

12. **Recorded motion for every technique phase**, replacing all 74 keyed clips. The keyed clips are removed once every phase is covered.
13. **Recorded neutral wrestling** driven by the engine's state: stance, circling, hand fighting, ties, pressure, edge play, step-outs, restarts, par terre start positions.
14. **Motion matching** over all takes: pose, velocity, contact and engine-state features, paired search for both wrestlers, inertialized transitions, so any phase flows into the next from the current pose.
15. **Retargeting** to each wrestler's own proportions (per segment, not just height), with contacts preserved by IK.
16. **Recorded contacts matched to engine grips**, so hands go where the engine's grips are.
17. **Mirroring** checked for every take and side.

## Physics

18. **Contact-only balance** (`ContactSolver` driving it) and a **footwork controller** on real motion; both failed only because keyed stances lean on the opponent.
    **Anatomical joints:** the current physical knees and elbows are unrestricted spherical joints without passive anatomical stops. Calibrated knee flexion and separate elbow/forearm coordinates need sourced motion-limit profiles and validation under load; simply clamping the current rotation-vector components would constrain the wrong motions.
19. **Throw physics** checked against real throw kinematics (flight about 0.75 s, rotation and carry speeds).
20. **Imitation-learned controllers** (`tools/imitation`, on GPU) for throws the bodies cannot follow by tracking.
21. **Open defects:** visible tracking, grip and stepping errors still need comparison with real footage. The lab baseline before individual limb proportions and continuous ground construction was 3.5% lost-motion frames (6.7% in women's bouts), with −1.3 cm worst active-physics foot penetration. These model measurements diagnose defects; they are not realism targets. The earlier −7 cm reading was an unstepped collider at a restart.
22. **Mobile physics cost** within the frame budget.

## Realism validation

23. **Realism benchmark** in the physics lab: shown motion against held-out real takes (hip height over time, trunk lean, body distances, contacts, timing, speeds), with targets.
24. **Expert review** by a coach or wrestler, side by side with real footage.
25. **Visual regression stills** for every technique.

## Visuals and presentation (design 11)

26. **Characters:** a parametric body system covering real athlete builds per weight class, ears (including cauliflower ears), body hair, hair, shoes, singlets; faces with the effort expressions checked against reference photos; eye gaze.
27. **Close-ups:** hands in grips and on the mat, skin, sweat.
28. **Venues:** club gym, national hall, Ranking Series, continental, world and Olympic arenas, with crowds scaled to the event.
29. **Cameras:** broadcast camera with closer angles for par terre and throws, player camera for Play mode, replay cameras, automatic instant replays, Replay Theatre.
30. **Audio:** mat sounds, breathing, referee whistle and official calls, coaches, crowd, music.
31. **Commentary** templates with variants, localised (design 11.5).
32. **Ceremonies:** medals, World Championship belt, Olympic podium.

## Game systems (design 5–10, 12–14)

33. **Officials model:** observation noise, wrong calls, challenge decisions.
34. **Engine performance:** under 5 ms per bout on a mid-range phone; world week under 2 s; scouting under 3 s.
35. **Event scheduling:** sessions, mats, 20-minute rest, two-day categories; tournament day for the player.
36. **Career:** wrestler creation, training, weight management, injuries, ageing, selection, rankings and seeding, finances, morale and relationships, fame, coach role, federation role.
37. **World simulation:** world database, yearly cycle, background competitions.
38. **Play mode:** gesture controls with assist levels.
39. **Coach mode:** tactics during stoppages and the break.
40. **UI:** every screen in design 12.2, bout HUD, accessibility, localisation (right-to-left, non-Latin names).
41. **Saves, replays, cloud sync**, crash reporting and opt-in telemetry.
42. **Builds** for iOS, Android, macOS and Windows within store limits (Addressables).

## Open items to settle (design 16)

43. LA 2028 qualification system, UWW ranking points and seeding, real statistics, fall confirmation time, launch languages, business model, veterans categories, official bracket charts, the activity period across a period's end.
44. The two remaining rule gaps: a double disqualification in a semi-final, and medal reassignment after doping.
