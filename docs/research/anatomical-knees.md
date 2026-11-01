# Knee model and its evidence

1 November 2026. This is a scoped mechanical model, not validation of human wrestling motion.

The physical knees now have one flexion coordinate with passive stops. Their construction stance
is already bent: its measured hip–knee–ankle angle offsets the joint zero and both limits. The
anchor X axes are calibrated from the actual limb plane. Reset, muscle targets and diagnostics
use that coordinate, retaining the athlete's strength-capped torque.

The approximate −5° to 150° envelope is informed by
[Kono et al., 2024](https://pmc.ncbi.nlm.nih.gov/articles/PMC10850127/), an in-vivo squatting study
of 20 knees in 10 Japanese male volunteers. Its observations are not individual limits for
wrestlers, women or every build. The study also describes axial rotation and translation that
our fixed hinge does not reproduce. Elbows remain unrestricted spherical joints.

## Animation targets must use the same joint frame

The rig calibrates the hinge axis in both actual leg bones in the same stance. Posing then rolls
the thigh and shin about their lengths to align those axes with the bend plane, while keeping
hip, knee and ankle positions and restoring the foot's world rotation. Near straight extension,
endpoint noise cannot determine that plane. The preceding axis is transported through this
singularity; a restart clears that history. The extension sweep caught a 14.59° jump before
this continuity fix.

The plane's sign is selected near the construction-stance reference rather than rotating the thigh 180°
to turn every backward target into positive flexion. This is an inference: joint positions alone
do not measure femur axial rotation or determine signed flexion near extension. In the current
recorded-pose checks, 318 requested coordinates fall outside the modeled signed envelope, by up
to 109.83°. That large result is a limitation of the inferred roll and model, **not evidence of
impossible knees in the source recordings**. It must be resolved with pose/rotation evidence
before treating physical tracking of these takes as accepted.

## Verification and limits

- `KneeJointChecks.RunFromCommandLine` checks actual left/right anchors, construction-angle
  offsets, and both passive stops under 30 N·m of torque for 2.5 seconds per direction.
- `KneeTargetChecks.RunFromCommandLine` compares independently reconstructed physical anchor
  axes against the posed rig over 15,904 actor-frames: three real takes, both sexes, two body
  profiles and both mirror states. Maximum axis mismatch is 0.0286° and endpoint error 0.000002 m.
  Both knees also sweep through −4° to +4° without the former roll jump. Plane comparisons are
  omitted near collinearity; parent/child axis and endpoint checks still run.
- Live sprawl/recovery footage was inspected for gross deformation and tracking errors. It
  remains a crouch-like keyed approximation, not a camera-validated sprawl.

Neither torque-stop tests nor endpoint agreement establish realistic balance, footwork, a
correct changing anatomical axis, or agreement with a coach. Hip constraints, tibial rotation,
elbow/forearm coordinates, contacts and source-based technique replacement remain open.
