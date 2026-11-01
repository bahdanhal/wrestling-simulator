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
