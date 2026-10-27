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

## Known rule gaps

Rules from the rulebook that the competition code does not handle yet:

- **Double disqualification or double forfeit in a semi-final** (Art. 14): the two quarter-final losers should wrestle a replacement semi-final, and the repechage groups change with its result. Currently the final side stays vacant.
- **Medal reassignment after doping disqualification** (Art. 14): an administrative step after the event; not modelled.

## Next systems

In dependency order:

1. Match engine foundations: mat geometry, body state, grips, bout clock
2. Technique content model and loader (phases, counter windows, end states)
3. Scoring evaluator and officials
4. Event scheduling (sessions, mats, 20-minute rest, two-day categories)
5. Wrestler creation (backgrounds, point-buy), ageing and training
