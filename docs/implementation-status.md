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
| Unity bout viewer (Watch mode): rulebook-sized mat and platform, procedural figures sized from each wrestler's body and posed from the engine state and grips, broadcast camera, scoreboard and event feed; scene generated from code, stills renderer, data packaged into player builds | Unity `Wrestling.Presentation`, `Wrestling.Editor` | 7.1, 11, 12 |

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
6. Presentation: technique motion. The figures follow body state, grips and ground positions, but techniques do not yet animate their phases (entry, lift, throw arc, landing); the next step is per-phase poses driven by the technique data, then character models and motion-matched animation (design 7)
7. Play mode input (gestures and push direction) and Coach mode controls in the viewer

## Calibration snapshot

`balance --bouts 300` per style (generated wrestlers at level 55 ± 12 with the full style repertoire, default tactics), engine tuning as committed:

| Style | Points wins | Technical superiority | Falls | Points per bout | Attempts per bout | Attack success |
|---|---|---|---|---|---|---|
| Freestyle | 72% | 22% | 6% | 11.4 | 27 | 9% |
| Greco-Roman | 70% | 25% | 4% | 9.4 | 20 | 9% |
| Women's | 75% | 21% | 4% | 11.6 | 27 | 9% |

Real reference values still have to be compiled (open item 3). Known tendencies to check against them: attack success is low while counters score a large share of points, and takedowns from scrambles remain common.
