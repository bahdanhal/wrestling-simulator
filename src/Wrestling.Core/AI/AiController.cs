using System.Collections.Generic;
using System.Linq;
using Wrestling.Core.Match;
using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;
using Wrestling.Core.Techniques;
using Wrestling.Core.Wrestlers;

namespace Wrestling.Core.AI
{
    /// <summary>
    /// Utility AI for one wrestler (design section 5.13). Every decision it weighs the expected
    /// points of each technique it could start (estimated with the engine's own formulas),
    /// counter risk, fatigue, the score and the clock, its personality and the coach's tactics.
    /// Mistakes come from attributes — low Wrestling IQ misjudges, low composure executes worse —
    /// never from deliberate handicaps. Deterministic for a given seed.
    /// </summary>
    public sealed class AiController : IBoutController
    {
        private static readonly Fix64 Hundred = Fix64.FromInt(100);
        private static readonly Fix64 EdgeRadius = Fix64.FromRatio(32, 10);
        private static readonly Fix64 OpponentEdgeRadius = Fix64.FromRatio(30, 10);
        private static readonly Fix64 TiredStamina = Fix64.FromRatio(35, 100);
        private static readonly Fix64 QualityNoise = Fix64.FromRatio(1, 10);
        private static readonly Fix64 LateSeconds = Fix64.FromInt(45);

        private readonly Pcg32 _random;
        private List<RepertoireEntry>? _byProficiency;

        public AiController(Tactics tactics, ulong seed)
        {
            Tactics = tactics ?? Tactics.Default;
            _random = new Pcg32(seed, 0xA1A1UL);
        }

        /// <summary>The current plan; the coach can replace it at any time.</summary>
        public Tactics Tactics { get; set; }

        public Intent Decide(IBoutView view, Corner self)
        {
            if (view.IsStopped || view.IsFinished)
            {
                return Intent.Idle;
            }

            WrestlerRuntime me = view.Wrestler(self);
            WrestlerRuntime opponent = view.Wrestler(self.Opponent());
            TechniqueExecution? active = view.ActiveTechnique;

            if (active != null && active.Executor != self)
            {
                return Defend(view, self, me);
            }

            return view.Position switch
            {
                BoutPosition.ParTerre when view.Top == self => ParTerreTop(view, self, me, opponent),
                BoutPosition.ParTerre => ParTerreBottom(view, self, me),
                BoutPosition.Neutral => Neutral(view, self, me, opponent),
                _ => Intent.Idle,
            };
        }

        /// <summary>Take par terre after a passivity call when the top game is stronger than the opponent's bottom defence.</summary>
        public bool ChoosesParTerre(IBoutView view, Corner self)
        {
            WrestlerRuntime me = view.Wrestler(self);
            WrestlerRuntime opponent = view.Wrestler(self.Opponent());
            Fix64 top = Fix64.Max(me.Effective(WrestlerAttribute.ParTerreTurns), me.Effective(WrestlerAttribute.ParTerreLifts));
            Fix64 bottom = opponent.Effective(WrestlerAttribute.ParTerreBottomDefence);
            return top + (Tactics.Focus == AttackFocus.ParTerre ? Fix64.FromInt(10) : Fix64.Zero) >= bottom;
        }

        private Intent Neutral(IBoutView view, Corner self, WrestlerRuntime me, WrestlerRuntime opponent)
        {
            FixVector2 towardOpponent = opponent.Body.Position - me.Body.Position;
            FixVector2 direction = towardOpponent.Normalized();

            // Near the edge, lean back toward the centre instead of stepping out.
            if (me.Body.Position.LengthSquared > EdgeRadius * EdgeRadius)
            {
                direction = (direction - me.Body.Position.Normalized()).Normalized();
            }

            Fix64 effort = Effort(me);
            if (Tactics.Pressure == PressureStyle.Forward && opponent.Body.Position.LengthSquared > OpponentEdgeRadius * OpponentEdgeRadius)
            {
                effort = Fix64.Min(Fix64.One, effort + Fix64.FromRatio(2, 10));
            }
            else if (Tactics.Pressure == PressureStyle.Defensive)
            {
                effort = effort * Fix64.FromRatio(6, 10);
            }

            (TechniqueDefinition Technique, GripSide Side, Fix64 Value)? attack = ChooseAttack(view, self, me, opponent);
            GripType? gripGoal = WantedGrip(view, me);
            bool strip = view.Grips.Of(opponent.Corner).Any() && _random.Chance(me.Effective(WrestlerAttribute.HandFighting) / Hundred);
            Fix64 level = view.Tuning.NeutralLevel.For(view.Style);
            if (view.Style.UsesFreestyleRules() && me.Wrestler.Attributes[WrestlerAttribute.LegAttacks] > 60)
            {
                level -= Fix64.FromRatio(5, 100);
            }

            return new Intent
            {
                PushDirection = direction,
                PushIntensity = effort,
                GripGoal = gripGoal,
                GripGoalSide = gripGoal.HasValue && GripRules.HandsUsed(gripGoal.Value) == 1 ? DominantSide(me) : GripSide.Right,
                StripGrips = strip,
                TargetLevel = level,
                Technique = attack?.Technique.Id,
                TechniqueSide = attack?.Side ?? GripSide.Right,
                ExecutionQuality = attack.HasValue ? Quality(view, me, attack.Value.Technique) : Fix64.Half,
            };
        }

        private Intent ParTerreTop(IBoutView view, Corner self, WrestlerRuntime me, WrestlerRuntime opponent)
        {
            (TechniqueDefinition Technique, GripSide Side, Fix64 Value)? attack = ChooseAttack(view, self, me, opponent, parTerre: true);
            return new Intent
            {
                PushIntensity = Fix64.Half,
                GripGoal = WantedGrip(view, me),
                GripGoalSide = GripSide.Right,
                TargetLevel = Fix64.FromRatio(4, 10),
                Technique = attack?.Technique.Id,
                TechniqueSide = attack?.Side ?? GripSide.Right,
                ExecutionQuality = attack.HasValue ? Quality(view, me, attack.Value.Technique) : Fix64.Half,
            };
        }

        private Intent ParTerreBottom(IBoutView view, Corner self, WrestlerRuntime me)
        {
            // Defend flat; now and then try to escape or reverse (more often with explosive wrestlers).
            bool tryEscape = _random.Chance(Fix64.FromRatio(25, 1000) * (me.Effective(WrestlerAttribute.Explosiveness) / Hundred) * Fix64.FromInt(2));
            (TechniqueDefinition Technique, GripSide Side) choice = default;
            if (tryEscape)
            {
                IReadOnlyList<(TechniqueDefinition Technique, GripSide Side)> escapes = view.StartableTechniques(self);
                choice = escapes.Count > 0 ? escapes[_random.NextInt(0, escapes.Count)] : default;
                tryEscape = choice.Technique != null;
            }
            return new Intent
            {
                PushIntensity = Fix64.FromRatio(3, 10),
                StripGrips = view.Grips.Of(self.Opponent()).Any(),
                TargetLevel = Fix64.FromRatio(2, 10),
                Technique = tryEscape ? choice.Technique!.Id : null,
                TechniqueSide = choice.Side,
                ExecutionQuality = tryEscape ? Quality(view, me, choice.Technique!) : Fix64.Half,
            };
        }

        /// <summary>Inside the opponent's technique: counter when the read is good and the window is open.</summary>
        private Intent Defend(IBoutView view, Corner self, WrestlerRuntime me)
        {
            IReadOnlyList<TechniqueDefinition> counters = view.OpenCounters(self);
            string? counter = null;
            if (counters.Count > 0)
            {
                TechniqueDefinition best = counters.OrderByDescending(c => me.Known(c.Id)!.Proficiency).ThenBy(c => c.Id, System.StringComparer.Ordinal).First();
                Fix64 read = (me.Effective(WrestlerAttribute.CounterWrestling) + me.Effective(WrestlerAttribute.WrestlingIq)) / Fix64.FromInt(200);
                if (Tactics.Focus == AttackFocus.Counters)
                {
                    read = Fix64.Min(Fix64.One, read + Fix64.FromRatio(2, 10));
                }

                if (_random.Chance(read))
                {
                    counter = best.Id;
                }
            }

            return new Intent
            {
                PushIntensity = Fix64.FromRatio(3, 10),
                TargetLevel = me.Body.Level,
                Counter = counter,
                ExecutionQuality = Fix64.FromInt(me.Wrestler.Attributes[WrestlerAttribute.CounterWrestling]) / Hundred,
            };
        }

        /// <summary>
        /// Picks the startable technique with the best expected value and decides whether to go now.
        /// The attack rate depends on pace, aggression, fatigue, openings and the score situation.
        /// </summary>
        private (TechniqueDefinition, GripSide, Fix64)? ChooseAttack(IBoutView view, Corner self, WrestlerRuntime me, WrestlerRuntime opponent, bool parTerre = false)
        {
            // Decide whether to go now before weighing every option: most decisions are not attacks.
            // In par terre the top wrestler gets a couple of real attempts before the referee stands them up.
            Fix64 rate = parTerre ? Fix64.FromRatio(3, 100) : AttackRate(view, self, me, opponent);
            if (!_random.Chance(rate))
            {
                return null;
            }

            IReadOnlyList<(TechniqueDefinition Technique, GripSide Side)> options = view.StartableTechniques(self);
            if (options.Count == 0)
            {
                return null;
            }

            (TechniqueDefinition, GripSide, Fix64)? best = null;
            foreach ((TechniqueDefinition technique, GripSide side) in options)
            {
                if (technique.Role != TechniqueRole.Attack)
                {
                    continue;
                }

                Fix64 value = ExpectedValue(view, me, opponent, technique) * FocusWeight(technique);
                if (best == null || value > best.Value.Item3)
                {
                    best = (technique, side, value);
                }
            }

            return best == null || best.Value.Item3 <= Fix64.Zero ? null : best;
        }

        /// <summary>Estimated points for starting the technique: chance of every phase succeeding × points of the best end state, less counter risk.</summary>
        private Fix64 ExpectedValue(IBoutView view, WrestlerRuntime me, WrestlerRuntime opponent, TechniqueDefinition technique)
        {
            Fix64 quality = Quality(view, me, technique);
            Fix64 chance = Fix64.One;
            int counterWindows = 0;
            foreach (PhaseDefinition phase in technique.Phases)
            {
                Fix64 attack = ContestFormulas.AttackScore(me, phase, quality, Fix64.Zero, opponent.Body.Balance, view.Tuning);
                Fix64 defence = ContestFormulas.DefenceScore(opponent, technique, view.Tuning);
                chance *= ContestFormulas.PhaseChance(phase, attack, defence, view.Tuning);
                counterWindows += phase.CounterWindows.Count(w => opponent.Known(w.Technique) != null);
            }

            // Low Wrestling IQ misjudges the odds.
            Fix64 judgement = me.Effective(WrestlerAttribute.WrestlingIq) / Hundred;
            Fix64 noise = (Fix64.One - judgement) * ((_random.NextFix01() * Fix64.FromInt(2)) - Fix64.One) * Fix64.FromRatio(3, 10);
            chance = Fix64.Clamp(chance + noise, Fix64.Zero, Fix64.One);

            EndState bestEnd = technique.Phases[technique.Phases.Count - 1].EndStates[0];
            bool opponentGrounded = opponent.Body.IsGrounded;
            bool fromBottom = view.Position == BoutPosition.ParTerre && view.Top != me.Corner;
            int points = ContestFormulas.PointsFor(technique, bestEnd, opponentGrounded, fromBottom, view.Rules);
            Fix64 risk = Fix64.FromInt(counterWindows) * (Fix64.One - Tactics.RiskTolerance) * Fix64.FromRatio(3, 10);
            return (chance * Fix64.FromInt(points)) - (risk * (Fix64.One - chance));
        }

        private Fix64 FocusWeight(TechniqueDefinition technique)
        {
            Fix64 preferred = Fix64.FromRatio(15, 10);
            return Tactics.Focus switch
            {
                AttackFocus.Legs when technique.Family == TechniqueFamily.LegAttacks || technique.Family == TechniqueFamily.TripsAndSweeps => preferred,
                AttackFocus.UpperBody when technique.Family == TechniqueFamily.UpperBodyThrows || technique.Family == TechniqueFamily.BodyLockAndLifts => preferred,
                AttackFocus.ParTerre when technique.Family == TechniqueFamily.ParTerreTurns || technique.Family == TechniqueFamily.ParTerreLifts => preferred,
                _ => Fix64.One,
            };
        }

        /// <summary>Chance per decision to go for the chosen attack.</summary>
        private Fix64 AttackRate(IBoutView view, Corner self, WrestlerRuntime me, WrestlerRuntime opponent)
        {
            // Per decision (about five a second): medium pace is roughly one attempt every 20 s per wrestler.
            Fix64 rate = Tactics.Pace switch
            {
                Pace.Low => Fix64.FromRatio(5, 1000),
                Pace.High => Fix64.FromRatio(16, 1000),
                _ => Fix64.FromRatio(10, 1000),
            };

            rate *= Fix64.Half + (me.Effective(WrestlerAttribute.Aggression) / Hundred);

            // Openings: an off-balance opponent invites the attack.
            rate *= Fix64.One + ((Fix64.One - opponent.Body.Balance) * Fix64.FromInt(3));

            // Fatigue: a tired wrestler picks moments more carefully.
            if (me.StaminaFraction < TiredStamina)
            {
                rate *= Fix64.Half;
            }

            // Score and clock.
            int lead = view.Sheet.Points(self) - view.Sheet.Points(self.Opponent());
            bool late = view.Clock.Period == view.Clock.Periods
                && Fix64.FromInt(view.Clock.TicksRemainingInPeriod) < LateSeconds * Fix64.FromInt(BoutClock.TicksPerSecond);
            if (late && lead < 0)
            {
                rate *= Fix64.FromInt(2) + (me.Effective(WrestlerAttribute.Determination) / Hundred);
            }
            else if (late && lead > 0 && Tactics.DefendLead)
            {
                rate *= Fix64.Half;
            }

            return Fix64.Min(Fix64.One, rate);
        }

        private Fix64 Effort(WrestlerRuntime me)
        {
            Fix64 effort = Tactics.Pace switch
            {
                Pace.Low => Fix64.FromRatio(35, 100),
                Pace.High => Fix64.FromRatio(8, 10),
                _ => Fix64.FromRatio(55, 100),
            };

            effort *= Fix64.FromRatio(7, 10) + (Fix64.FromRatio(6, 10) * (me.Effective(WrestlerAttribute.WorkRate) / Hundred));
            if (me.StaminaFraction < TiredStamina)
            {
                effort *= Fix64.FromRatio(7, 10);
            }

            return Fix64.Clamp(effort, Fix64.Zero, Fix64.One);
        }

        /// <summary>The grip needed by the wrestler's most proficient technique for this position, if not already held.</summary>
        private GripType? WantedGrip(IBoutView view, WrestlerRuntime me)
        {
            StartPosition position = view.Position == BoutPosition.ParTerre ? StartPosition.ParTerreTop : StartPosition.NeutralTied;
            _byProficiency ??= me.Wrestler.Repertoire.OrderByDescending(r => r.Proficiency).ThenBy(r => r.TechniqueId, System.StringComparer.Ordinal).ToList();
            foreach (RepertoireEntry entry in _byProficiency)
            {
                TechniqueDefinition technique = view.Catalog.Get(entry.TechniqueId);
                if (technique.Role != TechniqueRole.Attack || !technique.IsAllowed(view.Style, view.Age) || !technique.Start.Positions.Contains(position))
                {
                    continue;
                }

                foreach (GripRequirement requirement in technique.Start.RequiredGrips)
                {
                    if (!view.Grips.Has(me.Corner, requirement.Type))
                    {
                        return requirement.Type;
                    }
                }
            }

            return view.Position == BoutPosition.Neutral && !view.Grips.Of(me.Corner).Any() && GripRules.IsLegal(GripType.CollarTie, view.Style) ? GripType.CollarTie : (GripType?)null;
        }

        /// <summary>Execution quality: proficiency and composure, under pressure helped or hurt by clutch.</summary>
        private Fix64 Quality(IBoutView view, WrestlerRuntime me, TechniqueDefinition technique)
        {
            RepertoireEntry? entry = me.Known(technique.Id);
            Fix64 proficiency = entry == null ? Fix64.Zero : Fix64.FromInt(entry.Proficiency) / Hundred;
            Fix64 composure = me.Effective(WrestlerAttribute.Composure) / Hundred;
            bool pressure = view.Clock.Period == view.Clock.Periods && view.Clock.TicksRemainingInPeriod < BoutClock.SecondsToTicks(30);
            if (pressure)
            {
                composure = (composure + (me.Effective(WrestlerAttribute.Clutch) / Hundred)) / Fix64.FromInt(2);
            }

            Fix64 noise = ((_random.NextFix01() * Fix64.FromInt(2)) - Fix64.One) * QualityNoise;
            return Fix64.Clamp((proficiency * Fix64.FromRatio(7, 10)) + (composure * Fix64.FromRatio(3, 10)) + noise, Fix64.Zero, Fix64.One);
        }

        private static GripSide DominantSide(WrestlerRuntime me) => me.Wrestler.Body.DominantSide == Side.Left ? GripSide.Left : GripSide.Right;
    }
}
