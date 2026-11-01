# Motion/contact checkpoint — 1 November 2026

Saved at the user's requested end-of-period wrap. Starting commit was `7df96a8`.

## Implemented in this checkpoint

- Recorded playback no longer suspends engine hand grips. Recorded arms approach the actual
  physical opponent's allowed grip surfaces and retain held-point orientation. Corrections are
  filtered in the holder's chest frame; unreachable targets remain visible as errors.
- Grip acquisition now chooses a body **boundary**, including when a palm begins inside a
  capsule, sphere or foot box. The separate volume-distance query used for support is unchanged.
- A paired transition coordinator covers recorded entries, procedural exits and interruptions,
  carries positional velocity, preserves the outgoing pelvis frame and handles resets/time gaps.
  Subsequent IK/physics may change the final displayed path; angular continuity remains open.
- An exact two-leg reach solver finds the smallest vertical pelvis correction while preserving
  both ankle targets and horizontal pair placement. Live playback permits at most 0.35 m; if no
  common correction exists it preserves the root and exposes the remaining errors. Raw-source
  review can still preserve the original pelvis trajectory.
- Contact sidecars load with the motion library. They distinguish wrist/palm/sole, evidence
  origin, uncertainty, frame intervals and anatomical versus mat anchors. Runtime constraints
  support mirrored/remapped actors, activation/release, modeled body surfaces and engine-grip
  priority. No observed-contact sidecars have been fabricated or accepted.
- Source-use policies can restrict engine grips and select mirroring from the actual working
  grip hand rather than a technique's independent direction choice.
- Four additional attributed Harmony4D takes (test grappling2 030, 032, 038, 040) bring the
  library to seven takes, 2,165 paired frames, about 107.9 seconds between take endpoints. Sampled
  cam01 images and review metadata document standing/tie/transition candidates, not full coverage.

## Production versus review

**Production motion index remains empty: zero of 74 phases assigned.** The new takes are source
material; recorded neutral wrestling is not connected to the bout engine yet. Total product
completion has not been re-rated to 40%.

`tools/motion/harmony4d/reviews/entry-review-index.json` is an explicit **review-only** assignment
file for three entries. The snap experiment uses actor 1's left-hand work, follows the engine's
collar-tie hand, and selects frames 188–204: a 0.8-second faster lowering portion after the tie-up.
The full camera-reviewed window remains 176–204. This tail selection is not a formal 10–90%
interval or an elite timing distribution. Fresh inspection of the stored take gives about
0.58 m opponent head lowering across 176–204; earlier report wording of 0.64 m should not be used
as a precise measurement of this take's mapped head joint.

Review with:

```bash
Unity -batchmode -projectPath unity/WrestlingSimulator -executeMethod Wrestling.Editor.PhysicsLab.ClipFromCommandLine -clipStyle gr -clipSeed 2 -clipFrom 100 -clipTicks 180 -clipNoJump -clipMotionIndex tools/motion/harmony4d/reviews/entry-review-index.json -clipOutput OUT/snap.mp4 -logFile OUT/snap.log
```

Saved camera samples, source provenance and the snap preview are in
[`docs/images/realism-review/contact-checkpoint`](../images/realism-review/contact-checkpoint/README.md).
That preview precedes the final change from future posed sockets to current physical acquisition
surfaces; it is review evidence, not final acceptance footage.

## Verification

- Release build: no warnings/errors; 414 C# tests and 18 Python tests pass.
- Paired transition checks pass for multiple frame rates, exits, interruptions, duplicate
  samples, frozen pelvis frames, resets and settling.
- Exact ankle projection checks and authored contact-geometry fixtures pass. Those fixtures
  are software tests, not observations of human contacts.
- The earlier live-bout integration run exercised all three review entries with engine grips
  active and matched independent simulation ticks, scores, position states and active phases.
- Final physical boundary checks pass for interior/exterior spheres, capsules, rotated boxes,
  allowed grip parts and the unchanged support-volume query.
- Final retarget checks pass over 34,640 source actor-frames across seven takes, both sexes,
  two morphologies and both mirror states. Actual rig ankles agree with reachable endpoints
  within 0.000002 m; wrists within 0.000001 m. Raw pelvis-preserving mode exposes 29,334
  unreachable ankle targets, up to 0.3343 m. The bounded pelvis-fit path is also checked on
  these source poses; feasibility is reported rather than assumed. Wrist tests include
  deliberately impossible targets, so their largest residual is not a source-quality metric.
- Final live-bout checks pass after the physical acquisition-target fix: body lock 10 recorded
  frames, penetration 15 and snap 46, with engine grips active throughout each sampled path.
  Independent simulation tick/score/state/phase comparisons match. This test uses the full
  176–204 snap window, not the shorter review-video window, and does not certify physical holds.
- The expanded knee-target sweep was not rerun for this checkpoint. Existing knee calibration
  evidence remains historical; new source takes still need anatomical and contact acceptance.

## Resume here

1. Review the latest physical acquisition behavior through snap, penetration and body-lock
   entries on both sides and body variants. Active engine grips do not establish stable physical
   holds; the saved preview still showed acquisition gaps. Inspect actual held palms and releases.
2. Resolve source floor/sole conventions before enabling entries by default. The source floor
   is ankle-estimated, toes are inferred, and the imported penetration example has a mapped knee
   below that floor near frame 146. Collision palms/soles are model proxies, not measured skin.
3. Validate the selected source subranges, grip compatibility and working-side metadata; then
   adopt individual production entries only when their interaction is reviewable. Do not fill
   the production index merely to improve a coverage count.
4. Curate continuous neutral movement from 038/040 and connect selection to engine movement,
   spacing and contact states. Existing source-to-source search alone is insufficient.
5. Finish contact evidence (including heel/toe and palm orientation), anatomical roll ambiguity,
   angular transition behavior and coach review. See the roadmap and contact-coordinate document.

All subagents completed their work; no background capture or import job is intended to continue.
