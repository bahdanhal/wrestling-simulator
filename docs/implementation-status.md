# Implementation status

What is built, against [game-design.md](game-design.md). Updated with every change that completes or adds a system.

## Done

| Area | Code | Design section |
|---|---|---|
| Deterministic fixed-point math (Q32.32) and PCG32 RNG | `Wrestling.Core.Numerics` | 5.1, 14.3 |
| Cross-platform determinism probe (.NET, Unity editor, device runner) | `Wrestling.Core.Diagnostics`, Unity `Wrestling.Diagnostics` | 14.3 |
| Typed ruleset model with full validation | `Wrestling.Core.Rules` | 3 |
| UWW January 2026 ruleset content | `data/rulesets/uww-2026-01.json` | 3, 13.3 |
| Strict JSON content loading | `Wrestling.Data.Json`, `Wrestling.Data.Rulesets` | 13.1 |
| Bout score sheet (points, cautions, passivity, warnings, revocation) | `Wrestling.Core.Bouts.BoutScoreSheet` | 3.5–3.8 |
| Tie-break criteria (Art. 29) | `Wrestling.Core.Bouts.TieBreak` | 3.11 |
| Bout results, UWW result codes, classification points | `Wrestling.Core.Bouts.BoutResult` | 3.11 |
| Competition system by entrant count | `Wrestling.Core.Competition.CompetitionSystems` | 10 |
| Elimination with qualification, seeding, repechage, standings | `EliminationBracket` | 10.1, 10.4 |
| Nordic round robin | `NordicGroup` | 10.3 |
| Two pools with semi-finals (6–7 wrestlers) | `TwoPoolTournament` | 10.3 |
| Team ranking points | `TeamStandings`, ruleset `teamRanking` | 10.5 |
| Withdrawals, injuries, forfeits and double losses in every format | `BoutLedger` | 10.2, 10.7 |
| Shared round-robin scheduler | `RoundRobin` | 10.3, 10.6 |
| Team dual meets and team groups (Art. 33) | `DualMeet`, `TeamGroup` | 10.6 |
| Weigh-in: tolerance by event and day, category eligibility, moving up | `WeighIn` | 8.5, 10.2 |
| Deterministic game calendar (dates, weeks, ages) | `Wrestling.Core.Calendar.GameDate` | 8.1 |
| Wrestler model: identity, body, 34 attributes, hidden traits, repertoire, condition, validation | `Wrestling.Core.Wrestlers` | 4.1–4.4 |
| Derived archetype label | `ArchetypeClassifier` | 4.6 |
| Roster packs: strict JSON import/export | `Wrestling.Data.Rosters` | 13.7 |
| Fixed-point square root and 2D vectors (in the determinism probe) | `Fix64.Sqrt`, `FixVector2` | 5.1, 5.2 |
| Mat zones from the ruleset geometry | `MatGeometry.ZoneAt` | 3.4, 5.2 |
| Bout clock at 60 ticks per second, periods, break, scoreboard time | `Wrestling.Core.Match.BoutClock` | 5.1 |
| Body state (stance, level, posture, balance, ground, danger position, feet) | `BodyState` | 5.3 |
| Grip model with hand use and Greco-Roman legality | `GripSet` | 5.4 |
| Technique content model, catalogue cross-checks and loader | `Wrestling.Core.Techniques`, `Wrestling.Data.Techniques` | 6.2, 13.4 |
| Scoring evaluator: Art. 34/37/38/45 values from action facts, step-outs, risk rule | `Wrestling.Core.Match.ScoringEvaluator` | 3.5, 3.6, 5.10 |
| Freestyle activity clock, Greco-Roman passivity, video challenge procedures | `ActivityClockProcedure`, `GrecoPassivityProcedure`, `ChallengeProcedure` | 3.8, 3.12 |
| Engine tuning as data | `EngineTuning`, `data/engine/tuning.json` | 5, 15 |
| Technique catalogue: 51 techniques (standing attacks, leg attacks and trips, par terre turns and lifts, escapes and reversals, defences and counters) with English names | `data/techniques/`, `data/localization/en.json` | 6.3 |
| Localisation tables | `Wrestling.Data.Localization` | 12.5 |
| Bout statistics (attempts and successes by family, points by reason, time per position) | `BoutStatistics` | 5.14 |
| Category runner for any competition format | `CategoryRunner` | 10 |
| Content loader for the whole data folder | `Wrestling.Data.GameContent` | 13 |
| Headless CLI: `bout`, `balance`, `tournament`, `determinism-probe` | `Wrestling.Sim` | 15.2 |
| AI wrestler: expected-value attack choice with the engine's own formulas, pace/focus/pressure/risk tactics, openings, score and clock awareness, counters, par terre top and bottom, edge awareness | `Wrestling.Core.AI` | 5.13 |
| Bout engine tick loop: intents, hand-fight, pressure and balance, techniques with phases and counters, par terre, danger and falls, scrambles, risk rule, step-outs and deliberate push-outs, zone calls, passivity in both styles, stoppages and restarts, fatigue, events | `Wrestling.Core.Match.BoutEngine` | 5.1–5.12 |
| Wrestler generator for fictional opponents, name pools as data | `WrestlerGenerator`, `data/names/` | 4, 9 |
| Shared play-by-play text and result lines (CLI and game) | `Wrestling.Data.Localization.BoutCommentary` | 12.5 |
| Unity bout viewer (Watch mode): rulebook-sized mat and platform, broadcast camera, scoreboard and event feed; scene generated from code, stills renderer, data packaged into player builds | Unity `Wrestling.Presentation`, `Wrestling.Editor` | 7.1, 11, 12 |
| Character models from MakeHuman (CC0) via Blender + MPFB2, built by `tools/characters/build_wrestlers.py`: male and female athletic bases with the Mixamo-named rig (fingers, jaw, eyes), skin textures (7 male, 6 female), eyes, brows, lashes, hair and shoes, and blend shapes for build (heavy, light, muscular, soft) and face (head shape, nose, broken nose, chin, jaw, cheekbones, ears) set per wrestler | `tools/characters`, `Assets/Characters`, `CharacterLibrary`, `WrestlerLook` | 4.2, 7 |
| Physically simulated wrestlers: each body is one PhysX articulation (reduced coordinates, so joints never stretch under load) of 16 segments with hands and feet, de Leva mass shares, always under gravity, stepped at 240 Hz. **Muscles**: every joint is driven to its animated angle by a motor whose torque is capped at that joint's strength for the athlete's size and upper- or lower-body strength (hip and knee 400–450 N·m, shoulder 200, elbow 130 ...), with ~50 ms activation; the joints are built in a wrestling stance so everyday poses sit mid-range. **Balance**: the body as a whole is moved only by support: feet, knees, hands or trunk on the mat (or a carrier the trunk rests on, whose legs then bear the weight), push only, at most 2.6 body weights, sideways within mat friction, turning within what friction and the base reach; off the mat there is none, so bodies fly and land under gravity alone. The engine's balance weakens the push. Legs relax while the hips drop and planted feet are re-aimed from where the hips physically are. Only the wrestler a move is done to goes limp. Display only (the engine decides every outcome) | `PhysicalBody`, `BoutViewer` | 7 |
| Physical grips: every engine grip is a strength-capped force pair between the gripping palm and the opponent's segment under it (made when the palm reaches the body, torn off past 30 cm), so pulls, snaps and carried weight pass between the wrestlers; the holding arm aims at the point it physically holds | `GripLink`, `HandGrips` | 5.4, 7 |
| Physics throws: in the flight phase of 13 throws and lifts the thrown wrestler loses their own balance and is held by the thrower's palms or trapped against their trunk and squeezed onto the fulcrum (hip, chest or shoulders); the thrower carries and turns them with a strength-capped force pair (scaled by the thrower's support) that their own legs must bear; on release the body flies on with its momentum and lands. Specs per phase in `data/animation` (`throw`) | `PhysicalBody`, `BoutViewer`, `ThrowSpec` | 7 |
| Physics lab: plays real bouts live and reports pelvis tracking, bodies losing their motion, standing collapses, overlap, sinking feet, joint errors, balance limits, grips held, throw rotation and cost per frame; renders any stretch of a bout as video with the animation's pelvis targets marked and a per-frame log | `Wrestling.Editor.PhysicsLab` | 7, 15 |
| Recorded motion pipeline: two-person takes (17 joints per actor per frame, with source and licence) imported from BVH (Mixamo, CMU, SMPL-style names) or keypoint JSON, assigned to technique phases, validated against the catalogue; in the viewer a phase with a take plays it aligned to the wrestlers, scaled to their height, mirrored by side, eased in, tracked by the physics bodies; editor recorder writes takes from the game for testing and training references | `Wrestling.Data.Motion`, `wrestling-sim motion-import/motion-assign`, `MotionPlayback`, `MotionRecorder` | 7 |
| Physics imitation training (R&D): two MuJoCo humanoids sized to the recorded people with strength-limited PD muscles, contact with each other and the mat; reference fitted by physics-based IK (3.5 cm mean error); PPO controller for both bodies with DeepMimic-style reward; export of the simulated motion as a take for the game. Verified end to end on CPU (learning shows on a standing reference; a throw needs GPU-scale training on real footage) | `tools/imitation` | 7 |
| Character rendering: skin shader (MakeHuman textures, normal maps where provided, wrap-lighting subsurface approximation for phones) with the singlet drawn per pixel from bind-pose coordinates; rig retargeting from the pose solver with bind-pose-measured proportions and finger curl for grips | `WrestlerSkin.shader`, `CharacterImport`, `CharacterRig`, `WrestlerCharacter` | 7 |
| Move animation: paired keyframe clips for all 80 phases of the 51 techniques as data (position, facing, level, posture, ground state, torso roll, whole-body lift, pitch and bank for both wrestlers), timed by the engine's phase progress, mirrored by side; throws, lifts and carries hold the thrown wrestler on a contact point of the thrower (hip, lower back, chest, shoulders) so the bodies turn over a real pivot and never separate; every technique (counters included) eases in over 12 ticks from the pose and hold on screen; landings come from the engine's end state | `data/animation/`, `Wrestling.Data.Animation`, `Choreographer` | 6.2, 7 |

## Known rule gaps

Rules from the rulebook that the competition code does not handle yet:

- **Double disqualification or double forfeit in a semi-final** (Art. 14): the two quarter-final losers should wrestle a replacement semi-final, and the repechage groups change with its result. Currently the final side stays vacant.
- **Medal reassignment after doping disqualification** (Art. 14): an administrative step after the event; not modelled.

## Next systems

In dependency order:

1. Calibration against real statistics (design 15.1, open item 3): compile results from recent senior Worlds and Olympics, then tune `data/engine/tuning.json` with a headless balancing command in `Wrestling.Sim`
2. Officials model: observation noise, wrong calls, challenge decisions (the engine currently officiates exactly)
3. Engine performance: about 11–12 ms per AI bout with the full catalogue on an Apple Silicon Mac (Release). The design budget is under 5 ms on a mid-range phone; remaining cost is mostly `Fix64` multiplication, so the next step is a fast path for small operands and fewer per-tick recomputations.
4. Event scheduling (sessions, mats, 20-minute rest, two-day categories)
5. Wrestler creation (backgrounds, point-buy), ageing and training
6. Presentation polish: at the moment of loading, a thrower's hips are not always below the opponent's (the headlock throw loads level); physics tuning: some keyed clips move a body faster than an athlete can (the sprawl drops the hips 0.6 m in two frames and still lifts the body about 20 cm), headlock and hip throws reach only part of their rotation within the thrower's strength, and a thrown wrestler can slide away after release; balance forces act on the pelvis and the whole body rather than through the contacts (a contact-force solver is the next step); lying and par terre poses still twist the arms in places; skins without normal maps look smooth up close; clips are hand-keyed, so motion capture per phase (design 7) will replace them under the same clip ids, with motion matching between phases
7. Play mode input (gestures and push direction) and Coach mode controls in the viewer

## Calibration snapshot

`balance --bouts 300` per style (generated wrestlers at level 55 ± 12 with the full style repertoire, default tactics), engine tuning as committed:

| Style | Points wins | Technical superiority | Falls | Points per bout | Attempts per bout | Attack success |
|---|---|---|---|---|---|---|
| Freestyle | 72% | 22% | 6% | 11.4 | 27 | 9% |
| Greco-Roman | 70% | 25% | 4% | 9.4 | 20 | 9% |
| Women's | 75% | 21% | 4% | 11.6 | 27 | 9% |

Real reference values still have to be compiled (open item 3). Known tendencies to check against them: attack success is low while counters score a large share of points, and takedowns from scrambles remain common.
