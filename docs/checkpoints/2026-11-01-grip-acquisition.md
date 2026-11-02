# Grip acquisition checkpoint — 1 November 2026

Continues [the motion/contact checkpoint](2026-11-01-motion-contacts.md), resume item 1: inspect
whether engine grips become physical holds through the recorded review entries. Starting commit
`7ea382e`.

## Added

- `Wrestling.Editor.RecordedAcquisitionChecks` (see `README.md`, *Recorded motion*). It searches
  160 seeds per style (Freestyle, Greco-Roman, Women's) for each entry in
  `tools/motion/harmony4d/reviews/entry-review-index.json`, up to two bouts per style and executor
  corner. It plays each bout twice: once with the review entries and once with the keyed clips.
  For each hand the engine has in a grip it records the held share of grip frames, the time from
  the grip starting to a hold, tear-offs while the engine still grips, the physical palm-to-hold
  gap, and how far an unheld physical palm is from a surface the grip allows. The window is the
  recorded phase plus 30 ticks of exit.
- Regression guard: per clip and role, recorded playback may hold no more than 10 percentage
  points less than the keyed clips in the same bouts.
- Small tool-facing additions: `BoutViewer.UseMotion` (swap motion library between bouts),
  `BoutViewer.RecordedMirrored`, and `HandGrips.HeldParts` made public so tools use the same
  allowed-surface rule as the game.

## Findings (report: `docs/research/acquisition/2026-11-01-entry-acquisition.txt`)

- 10 of 20 found cases play recorded motion. The double-leg penetration entry is rejected in all
  but one case because its compatibility list is empty and the engine usually still has a grip
  when it starts. The one played case has no grips to measure. Snap-down cases were found only in
  Greco-Roman within the search range.
- Held share of engine-grip frames, recorded versus keyed: body lock executor 84% versus 75%,
  opponent 72% versus 55%; snap executor 93% versus 96%, opponent 100% versus 86%. Recorded entries
  are not worse at keeping holds. Most holds are made before the recorded window and carried in.
- In both modes the physical palm sits about 9–15 cm from what it holds (the lab's figure across
  whole bouts: 9.9 cm). The post-physics pass hides this on the shown bones. Physical arms read the
  retargeted pose (targets are captured after retargeting), so this is not a wiring bug. Muscle
  activation lag and strength limits keep the physical hand short of its hold.
- Holds tear when a **keyed counter exit separates the pair while the engine keeps the grip.**
  Traced case: Freestyle seed 117, mirrored body lock countered by `hip_block`. Within 14 ticks of
  the exit the executor's shoulder-to-held-point distance grows from 0.5 m to over 1 m, past
  any arm, while the engine still reports the body lock. The same happens with keyed clips only
  (mirrored body lock: six tear-offs keyed, two recorded).
- After a tear, recorded mode leaves the unheld physical palm farther from an allowed surface
  (about 19–66 cm) than keyed mode (about 5–31 cm). Keyed mode re-aims the arm at the socket.
  Recorded mode keeps the source arm plus a filtered correction.

## Tried and rejected (deterministic lab, 3 bouts × 60 s per style; identical on re-run)

| Change | Pelvis err mean | Lost % | Overpowered % | Grips held % | Palm gap (physics) |
|---|---|---|---|---|---|
| Baseline | 6.2 cm | 6.1 | 27.6 | 87 | 9.94 cm |
| Hand hold spring 2500→10000 N/m, damper 60→110 | 7.2 cm | 6.0 | 34.4 | 83 | 7.78 cm |
| Keep holding hands within arm reach by drawing keyed poses together | 8.4 cm | 8.8 | 38.2 | 83 | 9.87 cm |

Both were reverted. A stiffer spring pulls harder against arm drives and balance. Drawing target
poses together fights the authored spacing without the arms or legs acting to close it. Differences
come from one sample of bouts each, so chaotic divergence between runs is part of them. They are
still large enough, and in the wrong direction, to reject both changes.

## Resume here

1. Counter exits that separate the pair (hip block, likely others) should either release the
   engine grip or move the bodies as a connected pair. Decide this in the technique data and
   engine (who keeps the lock through a hip block), not with a presentation spacing patch.
2. After a tear during recorded playback, re-aim the arm at the nearest allowed surface, as the
   keyed path does, instead of keeping the source arm.
3. Penetration entry: review whether the source window supports entries from a tie (collar tie or
   underhook released during penetration). Otherwise document that it only plays from open space.
4. Closing the 10 cm physical palm gap needs arm-drive work: grip-aware arm strength, or a hand
   that acts as a constraint up to grip strength. Measure with this check and the lab together.
5. Earlier resume items 2–5 from the motion/contact checkpoint remain open. Production motion
   index is still empty (0 of 74). No completion re-rating.

## Verification

- `dotnet build -c Release`: no warnings or errors. `dotnet test`: 414 passed.
- `RecordedAcquisitionChecks`: passes. `RecordedBoutChecks`: passes. The lab is back at the
  baseline above after both reverts.

## Follow-up: hip block against chest-to-chest attacks (resume item 1)

**Cause.** The keyed `anim.hip_block.block` clip puts the attacker facing away from the blocker
(heading 0), which matches turn-in throws (hip toss, headlock, arm and underhook throws) where
the attacker has turned their back. Body lock, front suplex, salto and lateral drop are chest to
chest. Against those the clip spun the attacker 180° while the engine (correctly) kept their lock,
so the locked hands ended up behind them. In Freestyle seed 117 the shoulder-to-hold distance grew
past 1 m and both holds tore at tick 2691–2692.

**Change.**
- Counter windows have an optional `animation` (every window states it; null keeps the counter's
  own clip). A counter started from that window plays the clip for its first phase
  (`TechniqueExecution.Animation`/`EntryAnimation`). The contest is unchanged; a test shows identical
  bouts with and without it.
- New clip `anim.hip_block.block_chest`: the pair stays chest to chest while the blocker sinks the
  hips back and down (level 0.62 to 0.48, slight forward lean). It is used by the windows of
  body lock (lock), front suplex (lift), salto (lift) and lateral drop (drop).
- The content loader requires a window's clip to be in the counter's own animation file, feasible
  at the counter's first-phase minimum, and present. Strict content JSON now also accepts null for
  nullable-annotated reference properties (it already did for nullable value types).
- Content test: every hip-block window's counter clip starts with the attacker facing the blocker
  within 30° of how the attack's clip leaves them at the end of the window.

**Measured.** In the same acquisition cases, body-lock holds went from 84% to 97% (attacker,
recorded) and 72% to 91% (defender, recorded); keyed 75% to 80% and 55% to 57%. Seed 117 now keeps
shoulder-to-hold at 0.25–0.49 m and both holds stay on through the block. The physics lab is
identical to the baseline (its bouts contain no hip block against a lift). Render:
[`fs-seed117-body-lock-hip-block.png`](../images/realism-review/hip-block/fs-seed117-body-lock-hip-block.png).
The pair stays connected chest to chest, but the blocker's hip drop is barely visible.

**Found, not fixed (pre-existing; the previous commit renders the same):**
- During the recorded lock the attacker's grips physically lift the blocker's hips about 0.2 m
  above their target. Through the block the blocker's pelvis stays 0.3–0.6 m from its target, so
  the hip drop barely shows.
- After the counter the neutral placement jumps about 0.5 m from where the bodies are, and both
  pelvises are 0.5–0.75 m from their targets for the next half second. (Before: 0.44–0.62 m; with
  holds now kept, the locked pair resists the jump slightly more.)
- The same facing check across all counters finds other mismatches: `step_around` against arm drag,
  duck-under, slide-by and two-on-one (57–110°), and `counter_lift` against front suplex (180°, the
  same bug as hip block). `counter_throw_landing` differs by 90–180°, but mid-flight facing may not be
  the right comparison for a landing counter.

## Follow-up: counter-lift against the front suplex

**Cause.** `counter_lift` is used only against the front suplex's lift phase, which is chest to
chest. Its clip started with the attacker's back to the counter-lifter (a rear lift), 180° off.

**Change.** `anim.counter_lift.lift` is now a chest-to-chest counter-suplex built on the front
suplex's own mechanics: the defender, already sagging under the lift, drops a little more,
takes the chest (load from 0.35), and arches the attacker back over (lateral −200° from 0.35 to
0.8, same forearm grips on Spine2/Spine as the suplex), landing supine behind. The defender's
first drop is limited so it passes the free-fall check at the phase's 16-tick minimum. The facing
test now covers six standing counters (hip block, counter-lift, re-arch, sprawl, whizzer, front
headlock spin, within 35°). It names `step_around` and `counter_throw_landing` as not yet covered.

**Rendered** (Greco-Roman, viewer bouts found with the viewer's own wrestler generation):
- Seed 87 tick 8011, counter-lift scores 5 (grand amplitude to danger). New:
  [`gr-seed87-counter-lift-new.png`](../images/realism-review/counter-lift/gr-seed87-counter-lift-new.png);
  old clip at the same moment: [`gr-seed87-counter-lift-old.png`](../images/realism-review/counter-lift/gr-seed87-counter-lift-old.png).
  With the old clip the attacker is flung away upright (pelvis 0.69–0.79 m from target, the
  counter-lifter's 0.6 m). With the new clip the pair stays chest to chest and goes to the mat
  together with the attacker underneath (at most 0.43 and 0.31 m).
- Seed 26 tick 3385, blocked counter-lift: entry chest to chest, no spin
  ([`gr-seed26-counter-lift-blocked-new.png`](../images/realism-review/counter-lift/gr-seed26-counter-lift-blocked-new.png)).
- The physics lab is unchanged (its bouts contain no counter-lift).

**Not fixed.** Neither clip shows a real arched throw: the attacker's hips stay near 0.71 m
through the throw window, then both bodies fall. The phase lasts 16–26 ticks (0.27–0.43 s), so the
throw must turn the body 200° in about 0.12–0.2 s. A lift and arch takes far longer (the front
suplex's own lift plus arch is 32–52 ticks). The phase timing is engine data that changes the
contest, so it needs a deliberate decision and a balance run, not a presentation patch.

## Follow-up: counter-lift timing, with balance run

**Change.** `technique.all.counter_lift` phase `lift`: 16–26 → 28–42 ticks (0.47–0.70 s). One phase
holds both the lift and the arch. The front suplex's own lift plus arch is 32–52 ticks, and the
counter starts from a position already loaded, so slightly shorter is consistent. The clip is
retimed for it: load key at 0.4, throw from 0.4 to 0.85.

**Balance** ([report](../research/balance/2026-11-01-counter-lift-timing.md)): across 2,000 bouts
per style (`wrestling-sim balance`), victory types, points per bout, attempts and attack success
change by 0.5 points or less. Across 1,000 viewer-style bouts per style, counter-lift starts and
outcomes stay within seed-to-seed noise (Greco-Roman: 239 → 240 starts, completed 164 → 156,
blocked 74 → 83). The contest itself does not depend on phase length; only clock time and
random-draw order change. Side finding: in Greco-Roman the front suplex is countered in 565 of 777
attempts (73%). That is worth a balance look of its own.

**Throw still not shown.** Render (seed 87, same scoring counter-lift):
[`gr-seed87-counter-lift-28-42-ticks.png`](../images/realism-review/counter-lift/gr-seed87-counter-lift-28-42-ticks.png).
Here the counter-lifter sinks to the knees while the attacker stays up. With the 16-tick phase the
pair went down together with the attacker underneath. Neither shows a lift.
- Diagnosis: the working front suplex arch (seed 63) closes the pelvis gap from 0.78 to 0.05 m while
  the thrown hips rise from 0.73 to 1.25 m. In the counter-lift the gap stays 0.68–0.76 m against a
  0.31 m target for most of the phase. It closes only at the end of the turn, and the attacker's hips
  never pass 0.78 m. The counter starts the moment the front suplex is released (the counter-lifter
  was the thrown body a tick earlier, pelvis 0.2 m off target), so the pair begins apart and loose.
- Tried and removed: a presentation-only `turnFrom` (hold and draw in from `from`, turn from
  `turnFrom`), so a one-phase throw loads before it turns like the suplex's separate lift phase.
  The draw-in did not close the gap (still 0.72–0.76 m) and the attacker's hips peaked at 0.88 m.
  It was reverted rather than kept unproven.
- Next: find why the throw's grips and squeeze cannot draw in a chest-to-chest opponent here
  (grip forces, the released body's state, or the thrower's own balance limit), using the per-step
  `PhysicalBody.Trace` on this seed. Splitting the counter-lift into lift and arch phases like the
  suplex is the engine-side alternative, but it adds a contest roll and needs its own balance run.

## Follow-up: why the counter-lift cannot draw the attacker in (trace)

Traced with the new `-clipTrace` clip option: the throw's per-step forces, plus which segments of
the two bodies touch. Runs: Greco-Roman seed 87 (`-clipFrom 7975`, counter-lift turn starts at tick
8011) and the front suplex at seed 63 (`-clipFrom 9660`).

**What holds the attacker off is body contact, not strength.**
- With contact, the attacker's centre of mass stays 0.46–0.53 m from the counter-lifter's chest
  (the fulcrum) for the whole turn. The throw's legs are capped by how loaded the body is (0.24–0.38
  here), and the trunk turns 15° of the planned 200°.
- Same seed with `-clipNoContact`: the attacker is drawn onto the chest at once (0.07–0.14 m,
  loaded 1.0), the lift uses up to 2.9 kN of the 4.1 kN available, and the trunk turns 81°. The
  bodies pass through each other, so this is a diagnosis, not a fix.
- Touching segments every frame with contact: heads, both pairs of upper arms, and the chests
  (gap 0–1 cm). The pair is already chest-to-chest. Seating the attacker's waist on the chest, which
  the throw's squeeze asks for, needs the counter-lifter to get under them (a level change, head to
  one side), and the clip has the counter-lifter level with the attacker when the turn starts
  (executor level 0.56 against the attacker's 0.62).
- Renders, same three moments (turn start, +0.27 s, +0.53 s):
  [with contact](../images/realism-review/counter-lift/gr-seed87-counter-lift-contact.png) (jammed,
  attacker's hips back, no lift) and
  [without contact](../images/realism-review/counter-lift/gr-seed87-counter-lift-no-contact.png)
  (lifted and thrown over, through the other body).

**Correction to the earlier comparison.** The front suplex at seed 63 is not drawn in either: its
centre of mass is 0.55 m off the fulcrum when the turn starts and 0.45 m at the end, and its trunk
turns 18° of 200°. It reads as a suplex because its separate lift phase raises the opponent first
and the carry lifts them further (hips 0.73 → 1.18 m) while the thrower falls back under them. The
"gap closes 0.78 → 0.05 m" figure earlier was the pelvis's distance to its animated target, not to
the thrower.

**Tried and reverted: draw-in during the turn.** The carry's target keeps the offset from the
fulcrum the body had when the turn started, rotated. Blending it to the seated offset over the first
45° of the plan (so the carry pulls the body in rather than holding it out) brought the attacker to
0.39 m, but they fell down and away instead of rising. In the physics lab (3 bouts × 60 s per
style) the mean throw turn dropped from 40° to 22° (Greco-Roman's one throw 110° → 7°), grips held
87 → 84%. Reverted; the lab after the revert matches the baseline exactly.

**Next.** The fix is choreography first: a counter-lift clip where the counter-lifter changes level
under the attacker, with the head to one side and hips in, before the turn starts. That should also
apply to the other chest-to-chest throws (front suplex, salto, body lock). Separately, every
physical throw turns far less than planned (lab mean 40° of about 200°), which needs its own look.

## Follow-up: counter-lift clip with a level change (tried, not kept)

Goal: get the counter-lifter under the attacker before the turn, as the contact trace suggested.

**Keyed poses overlap.** Measured on the reference body with `ClipFeasibility.Place` (sampled every
0.05 of the phase), the two keyed trunks cross in both chest-to-chest clips: the closest distance
between the spine lines is 0, so the torsos overlap by their full depth (0.22 m). Standing lean is
at least 24° even at full posture (`ClipKinematics.StandingLean`), so two wrestlers at posture
0.85 lean about 34° into each other. Both `anim.front_suplex.lift` (all of it) and
`anim.counter_lift.lift` do this. The physics resolves it by holding the pelvises about 0.7 m
apart, which is why neither throw starts with the opponent on the fulcrum.

**Trial clip** (seed 87 and seed 26 rendered):
- Lift over 0–0.5 of the phase, the suplex's arch over 0.5–1 (throw 0.5–0.9).
- Level-change key at 0.3: counter-lifter level 0.53, pitch −12 (free-fall limit), heads to
  opposite sides (bank 10° each); attacker more upright (pitch −30).
- Loaded key at 0.5: counter-lifter level 0.7, pitch −35; attacker pitch −18. The keyed trunks
  just touch (−0.01 m), pelvises 0.29 m apart, the attacker's hips 0.21 m above the counter-lifter's.

**Result.**
- Seed 87 (scores): heads no longer touch, and the attacker rises to 0.91 m instead of 0.78 m.
  But they stay on their feet 0.48 m off the chest; the turn reaches about 0° and both fall
  ([render](../images/realism-review/counter-lift/gr-seed87-counter-lift-level-change-trial.png)).
- The attacker's balance is friction-limited the whole time: their feet stay planted, so the
  pelvis cannot follow its target in. The engine also releases all grips when the counter
  starts (`grips 0/0` through the phase), so nothing holds the attacker until the turn begins.
- Seed 26 (blocked): the stronger lift throws the attacker flat (hips 0.14 m) and they stay down
  while the engine has them standing. With the committed clip they dip to 0.35 m and are back up
  within a second
  ([render](../images/realism-review/counter-lift/gr-seed26-counter-lift-level-change-trial-blocked.png)).
  The outcome is decided at the end of the phase, after the turn has played, so a one-phase
  counter-lift cannot avoid throwing blocked attempts. That is about a third of Greco-Roman
  counter-lifts, so the trial clip was not kept.
- Also tried and reverted: a presentation-only `turnFrom` on throw specs (holds and squeeze from
  `from`, turn from `turnFrom`) with the trial clip. The attacker ended further off the chest
  (0.57 m against 0.48 m), so it did not justify a schema field on every clip.

**Conclusion.** The counter-lift needs two engine phases, as the front suplex has: a lift phase
whose contest decides whether the attacker comes off the mat (blocked: no arch plays), then an
arch phase. With the arch only playing after a won lift, the clip can lift hard without throwing
blocked attempts. That changes the contest (one more roll and window), so it needs a balance run.
The overlap of keyed chest-to-chest poses should be fixed in the same pass, for the front suplex
too, and could become a content check once both clips pass it.
