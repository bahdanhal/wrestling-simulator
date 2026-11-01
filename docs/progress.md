# Progress toward the full product

Updated 1 November 2026. Baseline commit: `6055771`.

**Approximately 20% complete**, as an engineering estimate of the full product described in
[the roadmap](roadmap.md), not a measured score or a count of equally sized checklist items.
The deterministic bout/rules engine and tournament foundations are substantially built.
Realistic presentation, source motion coverage, career/world systems, player controls,
full UI, audio and release work account for most of the remaining effort.

Working tests and better lab measurements do not establish realistic wrestling. Completion of
movement work requires comparison with original camera recordings, with capture limitations
and unverified contacts stated explicitly. Expert acceptance is a separate requirement.

## Next substantial milestone

The requested next 20 percentage points is a target for substantial implementation, not a
promise that any particular batch of changes raises this estimate to 40%. Prioritize wrestling
movement and appearance before expanding unrelated game systems:

- Calibrate passive anatomical constraints in the physical body, starting with knee flexion.
- Search recorded motion as a coherent pair, using source-time pose and velocity, explicit
  engine-state compatibility and honest contact metadata.
- Improve retargeting and transitions while preserving limb lengths and exposing unreachable
  source targets; review both sexes, body proportions and mirrored sides.
- Track actual motion coverage and timing evidence separately from unreviewed segment detections.
- Render the resulting motion and compare it with source footage before marking visual
  improvements complete.

Record completed work and evidence in [implementation-status.md](implementation-status.md).
Reassess the estimate after this milestone; do not increment it for tool counts, test counts
or optimising a model against itself. Club capture, athlete consent and coach review require
real external participation and cannot be replaced by generated evidence.
