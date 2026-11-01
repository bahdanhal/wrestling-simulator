# Reviewing movement against its source

Realism is assessed against camera footage and wrestling mechanics. The physics lab and the
reconstructed joint tracks help locate problems; neither supplies a realism score. Keep the
recording, joint reconstruction and rendered body visible separately so errors in one cannot be
hidden by agreement with another.

Reviewed [comparison images and native-speed video](../images/realism-review/README.md) are kept
with source provenance in the repository.

## Native-time review

`Wrestling.Editor.RecordedMotionReview` renders consecutive original samples from an imported
take. It places aligned source joints next to the character rig at a common scale, without
physics, and records the source, actor mapping, body profiles, frame range and native frame rate.
It does not assign the take to a technique or decide a bout outcome.

For the camera-checked Harmony4D snap-down:

```bash
/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath unity/WrestlingSimulator -executeMethod Wrestling.Editor.RecordedMotionReview.RenderFromCommandLine -reviewTake harmony4d.test-grappling2-043 -reviewFrom 176 -reviewTo 204 -reviewExecutor 1 -reviewOutput OUT/rig -logFile OUT/rig.log
```

This writes a contact sheet, every source sample as a PNG, a native-rate video when `ffmpeg` is
available, and a manifest. The source has 29 samples at 20 fps: the endpoints are 1.4 seconds
apart; the encoded video holds the final sample for one additional frame interval.

The character panel uses measured per-side leg lengths to reach the recorded ankle trajectories,
retaining the pair's common scale and the source pelvis path. It freezes the source pelvis frame
before solving the knees so the solve cannot move its own hip anchors. The manifest records the
source, reachable and actual rig ankle positions. Targets outside the athlete's reach remain
explicit residuals; preserving the source pelvis can make them impossible for a shorter-legged
body. This does not establish planted feet or contact preservation. Pass
`-reviewRawRig` to inspect the earlier rotation-only mapping. Leg and arm mapping can also be
controlled independently with `-reviewRetargetLegs` and `-reviewRetargetArms`.

Recorded arms solve from the actual rig's shoulder origins after trunk and clavicle posing.
The upper arms and forearms keep their measured lengths. Reachable wrists stay on the source
targets; unreachable targets remain residuals, without shifting either wrestler's root. The
manifest retains source, reachable and actual wrist coordinates. Palm contact locations and
finger shapes are still unknown; fitting a wrist cannot establish a grip.

Fetch the matching camera images using the original frame numbers in the downloaded pose NPZ
(see `tools/motion/harmony4d/README.md` for fetching those poses):

```bash
python tools/motion/harmony4d/harmony_images.py test/03_grappling2.zip 043_grappling2 OUT/poses-test/03_grappling2/043_grappling2.npz --from-index 176 --to-index 204 --camera cam01 --take data/motion/takes/harmony4d.test-grappling2-043.json --output OUT/camera
python tools/motion/harmony4d/harmony_review.py --camera OUT/camera --review OUT/rig --output OUT/comparison
```

The downloader requests only bounded ZIP byte ranges. Its manifest maps each pose index to the
exact archive image member; the comparison tool checks source identity, frame coverage and
timing before combining them. Photos are resized with padding, without cropping or reconstructing
their contents. The camera and model views differ; the comparison is not a pixel overlay.

Source: [Harmony4D](https://huggingface.co/datasets/Jyun-Ting/Harmony4D), Rawal Khirodkar,
Jyun-Ting Song, Jinkun Cao, Zhengyi Luo and Kris Kitani (Carnegie Mellon University), NeurIPS 2024,
MIT. Source attribution and limitations are retained in the review manifests.

## Independent image landmarks

`harmony_camera.py` reads the released COLMAP fisheye intrinsics/extrinsics and metric transform.
It can triangulate manually marked points from multiple original images, recording the exact
image/calibration members, selected calibration timestamps, annotation uncertainty and per-view
reprojection residuals. It does not use the supplied SMPL-derived 2D landmarks as independent
camera measurements.

```bash
python tools/motion/harmony4d/harmony_camera.py fetch test/03_grappling2.zip 043_grappling2 OUT/calibration
python tools/motion/harmony4d/harmony_camera.py triangulate OUT/calibration --frame 177 --landmarks OUT/marks.json --output OUT/points.json
```

Annotations use original image pixels and the archive frame number, not the zero-based take index:

```json
{
  "uncertainty": "Describe visibility, correspondence ambiguity and estimated pixel uncertainty here.",
  "landmarks": {
    "feature_name": { "cam01": [1234, 567], "cam05": [2100, 800] }
  }
}
```

These coordinates illustrate the input format only. They are not observations of a foot or mat.
Use at least two genuinely corresponding visible features and check additional views. A low
reprojection residual does not establish a correct anatomical correspondence or support force.
Calibrations are sparse: the tool uses the released nearest-timestamp convention, reports the
gap, and by default rejects frames outside the calibration range or more than 25 frames from
the selected calibration. An explicit gap override is available when justified by the source.
No accepted camera-derived mat plane or heel/toe contact annotations are shipped yet. Approximate
tape-corner candidates are retained in `tools/motion/harmony4d/reviews/` as review evidence,
explicitly marked as unaccepted.

`harmony_scene_floor.py` can assess a floor candidate from static COLMAP points selected by
visible-image polygons, without a prior floor-plane filter. Fetch calibration with
`harmony_camera.py fetch ... --include-points`, obtain original frame 301 from cameras 01, 05
and 11, and place the reviewed `043-grappling2-floor-masks.json` alongside their image folders.
Image paths in that file are relative to the mask file. For example:

```bash
python tools/motion/harmony4d/harmony_scene_floor.py OUT/calibration OUT/floor-views/043-grappling2-floor-masks.json OUT/floor-diagnostic --up 0 0 1 --threshold 0.02
```

The threshold is an explicit fit parameter in metres, not a realism acceptance limit. The report
retains every selected point, track support, excluded fit IDs and the distinct held-out point
IDs. Image-region membership is not proof that a reconstructed point lies on the mat; held-out
IDs still share the same camera calibration. The reviewed sample left several held-out points
over 2 cm from the fit, so it has not replaced the ankle-based floor or changed any imported take.

## What the reference can establish

The checked snap-down shows the snapper keeping their hips relatively high as the opponent folds
and the distance between pelvises increases. Its core and full segment are different timing
measurements. One recreational performance does not establish an elite timing distribution or
the duration of the following go-behind.

The imported joints are triangulated annotations, not full motion capture of every body part.
Hands are wrists, toes are synthesized, and head orientation is not directly tracked. The floor
was fitted to low ankle samples; an ankle near the drawn floor line does not establish a flat
sole contact. The footage shows stepping and rolling support, which must not be erased by
flattening every low foot.

Before treating a take as production motion, review its entry, core, exit, both sides and several
body proportions. Check support and release against the camera: which foot/heel/toe bears weight,
which hand grips which surface, and when those contacts change. Preserving a wrist coordinate
does not by itself preserve a palm grip on a differently proportioned opponent.

## Procedural geometry checks

`Wrestling.Editor.PoseConstructionChecks.RunFromCommandLine` samples the 74 shipped keyed clips,
checks fixed arm and leg lengths on a reference body, and reports inherited sampled-path
free-fall violations separately. `MorphologySheet.RenderFromCommandLine -morphologyOutput DIR`
renders same-height male/female builds with different leg ratios and spans, with measured geometry
assertions. These establish specific geometry properties and support visual inspection; they do
not validate a technique as realistic wrestling.

`Wrestling.Editor.RecordedRetargetChecks.RunFromCommandLine` checks actual rig ankle and wrist positions
after applying recorded poses, including both sides, male/female models, different limb ratios
and deliberately unreachable targets. This checks the trajectory mapping contract; it does not
test final physics-tracked bodies or infer foot contacts from ankle height.

Within-clip ground construction is continuous across its authored interval. Clip entry/exit,
counter handoffs, actual footstep trajectories and paired contact-preserving retargeting still
need sourced transition motion and contact annotations. A generic easing function cannot supply
a real recovery from the mat.

## Paired search and native playback

`PairedMotionSearch` indexes explicitly supplied source ranges as pairs. Features include both
wrestlers' root-relative poses, velocities per source second, pelvis heights, relative facing
and pair spacing. Horizontal translation/yaw are registered; mirroring swaps anatomical sides.
Pose, velocity and pair weights are configurable, not evidence of realism. Derivatives stay
inside each labelled range. Source frames must not be mutated after indexing.

Queries can require explicit state/contact labels. Null metadata means unknown, not an absent
grip; it cannot satisfy a verified-contact request. A source-time continuity penalty and minimum
remaining duration help reject inconvenient matches. This is source-to-source selection, not
matching from an arbitrary rendered/physical avatar pose or automatic technique acceptance.

`PairedMotionPlayer` plays an explicit range at native FPS, aligns matched switches at the
matched frame, and carries positional velocity into the incoming pair. During a switch its
search query describes the underlying source, not the temporarily blended visible pose.
`RecordedPoseTransition` also replaces recursive recorded-phase entry blending in the viewer.
It retains the outgoing pelvis frame before leg IK; joint-position and velocity offsets decay
in simulation/source seconds. Angular-velocity continuity, recorded-to-procedural exits and
contact constraints remain separate work.

Run `Wrestling.Editor.PairedMotionChecks.RunFromCommandLine` for native time, frame partition,
range bounds, handoff geometry and unknown-contact checks. `RecordedBoutChecks.RunFromCommandLine`
uses explicit in-memory review assignments for the three source windows and compares the live
viewer against an independent engine session. It leaves `data/motion/index.json` unchanged.
Source coverage is recorded in [motion-coverage.md](motion-coverage.md); detections and reviewed
windows remain distinct from production assignments.

## Validation snapshot before the motion/contact checkpoint

At the preceding checkpoint, the Release build and 407 C# tests passed, as did 18 offline Python tests. Unity checked fixed limb
lengths over 35,668 procedural poses and recorded ankle mapping over 15,904 actor-frames across
three source takes, both sexes, two leg proportions and both mirror states. Actual rig ankles
agree with the solver's reachable endpoints within 0.000002 m. Short-legged profiles still have
13,816 unreachable source ankle targets, with residuals up to 0.1609 m; those are exposed rather
than hidden by moving the pelvis. The procedural path diagnostic also retains 26 inherited
free-fall findings. These checks establish software and geometric contracts, not realism or
contact acceptance. Reviewed body sheets and the native-speed camera comparison are linked above.
Recorded-arm checks also match actual rig wrists to reachable endpoints within 0.000001 m;
their deliberately impossible-target cases are reported separately in the log interpretation.
The in-memory live-bout check exercised body-lock entry (FS seed 101, tick 433), penetration
(FS seed 10, tick 1156) and snap-down (GR seed 2, tick 131), with 10, 15 and 46 recorded frames
respectively before counter/phase changes. Entry and exit code paths ran; this is not a check
that their contacts or procedural exits look like the recording.
See [anatomical-knees.md](anatomical-knees.md) for knee geometry, passive-stop evidence and the
remaining ambiguity in inferred signed flexion.

Current verification, seven-take expansion, contact integration and remaining limitations are
recorded in the [1 November motion/contact checkpoint](../checkpoints/2026-11-01-motion-contacts.md).
The measurements above describe the earlier three-take, pelvis-preserving baseline.
