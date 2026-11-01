# Harmony4D source and phase coverage

The 3 camera-reviewed segment records are reviewed source windows; their clip labels are provenance annotations, not accepted phase matches. Candidate detections are automated and are not camera-validated.

## Local imported takes

| Take | Source | Licence | FPS | Frames | Endpoint span (s) | Hold-last span (s) |
|---|---|---:|---:|---:|---:|---:|
| harmony4d.test-grappling2-030 | Harmony4D test/03_grappling2.zip, sequence 030_grappling2 | MIT | 20 | 301 | 15.00 | 15.05 |
| harmony4d.test-grappling2-032 | Harmony4D test/03_grappling2.zip, sequence 032_grappling2 | MIT | 20 | 57 | 2.80 | 2.85 |
| harmony4d.test-grappling2-038 | Harmony4D test/03_grappling2.zip, sequence 038_grappling2 | MIT | 20 | 295 | 14.70 | 14.75 |
| harmony4d.test-grappling2-040 | Harmony4D test/03_grappling2.zip, sequence 040_grappling2 | MIT | 20 | 518 | 25.85 | 25.90 |
| harmony4d.test-grappling2-043 | Harmony4D test/03_grappling2.zip, sequence 043_grappling2 | MIT | 20 | 326 | 16.25 | 16.30 |
| harmony4d.train-grappling-001 | Harmony4D train/02_grappling.zip, sequence 001_grappling | MIT | 20 | 601 | 30.00 | 30.05 |
| harmony4d.train-grappling-055 | Harmony4D train/02_grappling.zip, sequence 055_grappling | MIT | 20 | 67 | 3.30 | 3.35 |

Phase coverage: 74 total; 3 have a camera-reviewed source window labelled with the clip; 0 have an accepted motion-index assignment.
Candidate cue events supplied: 0 (broad detections; not validated).

## Per-phase coverage

| Technique | Phase | Clip | Reviewed source window | Candidate cues* | Assigned | Native window (s) | Engine min–max (s) |
|---|---|---|---:|---:|---:|---:|---:|
| technique.all.arm_bar_turn | bar | anim.arm_bar_turn.bar | no | 0 | no | — | 0.27–0.43 |
| technique.all.arm_bar_turn | turn | anim.arm_bar_turn.turn | no | 0 | no | — | 0.33–0.57 |
| technique.all.arm_drag | drag | anim.arm_drag.drag | no | 0 | no | — | 0.17–0.30 |
| technique.all.arm_drag | behind | anim.arm_drag.behind | no | 0 | no | — | 0.23–0.40 |
| technique.all.arm_throw | entry | anim.arm_throw.entry | no | 0 | no | — | 0.20–0.33 |
| technique.all.arm_throw | flight | anim.arm_throw.flight | no | 0 | no | — | 0.27–0.43 |
| technique.all.body_lock_throw | lock | anim.body_lock_throw.lock | yes | 0 | no | 1.00 | 0.80–1.00 |
| technique.all.body_lock_throw | flight | anim.body_lock_throw.flight | no | 0 | no | — | 0.30–0.50 |
| technique.all.counter_lift | lift | anim.counter_lift.lift | no | 0 | no | — | 0.27–0.43 |
| technique.all.counter_throw_landing | turn | anim.counter_throw_landing.turn | no | 0 | no | — | 0.20–0.33 |
| technique.all.double_nelson_side | turn | anim.double_nelson_side.turn | no | 0 | no | — | 0.40–0.67 |
| technique.all.duck_under | duck | anim.duck_under.duck | no | 0 | no | — | 0.17–0.30 |
| technique.all.duck_under | behind | anim.duck_under.behind | no | 0 | no | — | 0.23–0.40 |
| technique.all.front_headlock_go_behind | spin | anim.front_headlock_go_behind.spin | no | 0 | no | — | 0.27–0.47 |
| technique.all.front_lift | lift | anim.front_lift.lift | no | 0 | no | — | 0.30–0.50 |
| technique.all.front_lift | throw | anim.front_lift.throw | no | 0 | no | — | 0.30–0.50 |
| technique.all.front_suplex | lift | anim.front_suplex.lift | no | 0 | no | — | 0.23–0.37 |
| technique.all.front_suplex | arch | anim.front_suplex.arch | no | 0 | no | — | 0.30–0.50 |
| technique.all.gut_wrench | turn | anim.gut_wrench.turn | no | 0 | no | — | 0.40–0.67 |
| technique.all.half_nelson_turn | turn | anim.half_nelson_turn.turn | no | 0 | no | — | 0.40–0.67 |
| technique.all.headlock_throw | entry | anim.headlock_throw.entry | no | 0 | no | — | 0.20–0.33 |
| technique.all.headlock_throw | flight | anim.headlock_throw.flight | no | 0 | no | — | 0.27–0.43 |
| technique.all.hip_block | block | anim.hip_block.block | no | 0 | no | — | 0.17–0.30 |
| technique.all.hip_toss_overhook | entry | anim.hip_toss_overhook.entry | no | 0 | no | — | 0.20–0.33 |
| technique.all.hip_toss_overhook | flight | anim.hip_toss_overhook.flight | no | 0 | no | — | 0.30–0.47 |
| technique.all.lateral_drop | drop | anim.lateral_drop.drop | no | 0 | no | — | 0.27–0.47 |
| technique.all.par_terre_reversal | reverse | anim.par_terre_reversal.reverse | no | 0 | no | — | 0.30–0.50 |
| technique.all.push_out | drive | anim.push_out.drive | no | 0 | no | — | 0.40–0.67 |
| technique.all.re_arch | arch | anim.re_arch.arch | no | 0 | no | — | 0.23–0.40 |
| technique.all.reverse_body_lift | lift | anim.reverse_body_lift.lift | no | 0 | no | — | 0.30–0.50 |
| technique.all.reverse_body_lift | arch | anim.reverse_body_lift.arch | no | 0 | no | — | 0.33–0.53 |
| technique.all.reverse_gut_wrench | turn | anim.reverse_gut_wrench.turn | no | 0 | no | — | 0.40–0.67 |
| technique.all.salto | lift | anim.salto.lift | no | 0 | no | — | 0.23–0.37 |
| technique.all.salto | bridge | anim.salto.bridge | no | 0 | no | — | 0.33–0.53 |
| technique.all.slide_by | slide | anim.slide_by.slide | no | 0 | no | — | 0.20–0.33 |
| technique.all.slide_by | behind | anim.slide_by.behind | no | 0 | no | — | 0.23–0.40 |
| technique.all.snap_down_go_behind | snap | anim.snap_down_go_behind.snap | yes | 0 | no | 1.40 | 0.70–1.00 |
| technique.all.snap_down_go_behind | behind | anim.snap_down_go_behind.behind | no | 0 | no | — | 0.27–0.47 |
| technique.all.stand_up | rise | anim.stand_up.rise | no | 0 | no | — | 0.30–0.50 |
| technique.all.step_around | step | anim.step_around.step | no | 0 | no | — | 0.17–0.27 |
| technique.all.two_on_one_throw | spin | anim.two_on_one_throw.spin | no | 0 | no | — | 0.27–0.47 |
| technique.all.underhook_throw | entry | anim.underhook_throw.entry | no | 0 | no | — | 0.20–0.33 |
| technique.all.underhook_throw | flight | anim.underhook_throw.flight | no | 0 | no | — | 0.27–0.43 |
| technique.fs.ankle_pick | pick | anim.ankle_pick.pick | no | 0 | no | — | 0.27–0.40 |
| technique.fs.cradle | lock | anim.cradle.lock | no | 0 | no | — | 0.27–0.47 |
| technique.fs.cradle | roll | anim.cradle.roll | no | 0 | no | — | 0.33–0.53 |
| technique.fs.double_leg_blast | penetration | anim.double_leg_blast.penetration | yes | 0 | no | 1.00 | 0.70–1.00 |
| technique.fs.double_leg_blast | finish | anim.double_leg_blast.finish | no | 0 | no | — | 0.27–0.47 |
| technique.fs.double_leg_lift | penetration | anim.double_leg_lift.penetration | no | 0 | no | — | 0.20–0.30 |
| technique.fs.double_leg_lift | lift | anim.double_leg_lift.lift | no | 0 | no | — | 0.30–0.50 |
| technique.fs.fireman_carry | drop | anim.fireman_carry.drop | no | 0 | no | — | 0.27–0.40 |
| technique.fs.fireman_carry | throw | anim.fireman_carry.throw | no | 0 | no | — | 0.30–0.50 |
| technique.fs.foot_sweep | sweep | anim.foot_sweep.sweep | no | 0 | no | — | 0.17–0.27 |
| technique.fs.front_headlock_spin | spin | anim.front_headlock_spin.spin | no | 0 | no | — | 0.23–0.40 |
| technique.fs.granby_roll | roll | anim.granby_roll.roll | no | 0 | no | — | 0.27–0.43 |
| technique.fs.high_crotch | penetration | anim.high_crotch.penetration | no | 0 | no | — | 0.27–0.37 |
| technique.fs.high_crotch | finish | anim.high_crotch.finish | no | 0 | no | — | 0.30–0.50 |
| technique.fs.inside_trip | trip | anim.inside_trip.trip | no | 0 | no | — | 0.23–0.40 |
| technique.fs.knee_pick | pick | anim.knee_pick.pick | no | 0 | no | — | 0.27–0.40 |
| technique.fs.leg_lace | lace | anim.leg_lace.lace | no | 0 | no | — | 0.27–0.47 |
| technique.fs.leg_lace | roll | anim.leg_lace.roll | no | 0 | no | — | 0.33–0.53 |
| technique.fs.leg_turk | turn | anim.leg_turk.turn | no | 0 | no | — | 0.40–0.67 |
| technique.fs.outside_trip | trip | anim.outside_trip.trip | no | 0 | no | — | 0.23–0.40 |
| technique.fs.single_leg_high | penetration | anim.single_leg_high.penetration | no | 0 | no | — | 0.27–0.37 |
| technique.fs.single_leg_high | finish | anim.single_leg_high.finish | no | 0 | no | — | 0.30–0.53 |
| technique.fs.single_leg_low | penetration | anim.single_leg_low.penetration | no | 0 | no | — | 0.27–0.37 |
| technique.fs.single_leg_low | finish | anim.single_leg_low.finish | no | 0 | no | — | 0.30–0.53 |
| technique.fs.sit_out | sit | anim.sit_out.sit | no | 0 | no | — | 0.23–0.40 |
| technique.fs.sprawl | sprawl | anim.sprawl.sprawl | no | 0 | no | — | 0.37–0.50 |
| technique.fs.step_over_turn | turn | anim.step_over_turn.turn | no | 0 | no | — | 0.40–0.67 |
| technique.fs.sweep_single | sweep | anim.sweep_single.sweep | no | 0 | no | — | 0.27–0.40 |
| technique.fs.sweep_single | finish | anim.sweep_single.finish | no | 0 | no | — | 0.30–0.50 |
| technique.fs.switch | switch | anim.switch.switch | no | 0 | no | — | 0.27–0.43 |
| technique.fs.whizzer | whizzer | anim.whizzer.whizzer | no | 0 | no | — | 0.20–0.33 |

*Candidate cue counts are broad event-kind hints and are not evidence that the named phase occurred.

## Reviewed segment provenance audit

| Clip | Take exists | Pose source matches title | Frame range valid |
|---|---:|---:|---:|
| anim.double_leg_blast.penetration | yes | yes | yes |
| anim.snap_down_go_behind.snap | yes | yes | yes |
| anim.body_lock_throw.lock | yes | yes | yes |
