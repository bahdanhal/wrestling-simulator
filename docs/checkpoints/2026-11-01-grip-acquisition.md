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
