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
| Bout engine tick loop: intents, hand-fight, pressure and balance, techniques with phases and counters, par terre, danger and falls, scrambles, risk rule, step-outs and deliberate push-outs, zone calls, passivity in both styles, stoppages and restarts, fatigue, events | `Wrestling.Core.Match.BoutEngine` | 5.1–5.12 |

## Known rule gaps

Rules from the rulebook that the competition code does not handle yet:

- **Double disqualification or double forfeit in a semi-final** (Art. 14): the two quarter-final losers should wrestle a replacement semi-final, and the repechage groups change with its result. Currently the final side stays vacant.
- **Medal reassignment after doping disqualification** (Art. 14): an administrative step after the event; not modelled.

## Next systems

In dependency order:

1. AI wrestler controller with tactics (design 5.13)
2. Technique catalogue content (section 6.3) and calibration against real statistics with the headless runner (attack frequency, scores, victory types)
3. Officials model: observation noise, wrong calls, challenge decisions (the engine currently officiates exactly)
4. Engine performance pass (allocation-free hot paths; target under 5 ms per bout on a mid-range phone)
5. Event scheduling (sessions, mats, 20-minute rest, two-day categories)
6. Wrestler creation (backgrounds, point-buy), ageing and training
