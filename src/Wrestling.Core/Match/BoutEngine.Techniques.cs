using System.Collections.Generic;
using System.Linq;
using Wrestling.Core.Bouts;
using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;
using Wrestling.Core.Techniques;
using Wrestling.Core.Wrestlers;

namespace Wrestling.Core.Match
{
    public sealed partial class BoutEngine
    {
        private static readonly Fix64 ShotDistanceFactor = Fix64.FromInt(2);
        private static readonly Fix64 PartialQualityFactor = Fix64.FromRatio(3, 4);
        private static readonly Fix64 DangerTorsoRotation = Fix64.FromInt(130);
        private static readonly Fix64 CounterOutcomeShare = Fix64.FromRatio(1, 200);

        private void StartRequestedTechniques()
        {
            // When both want to attack on the same tick, the quicker wrestler goes first.
            WrestlerRuntime red = Wrestler(Corner.Red);
            WrestlerRuntime blue = Wrestler(Corner.Blue);
            bool redWants = red.Intent.Technique != null && !red.TechniqueIntentUsed;
            bool blueWants = blue.Intent.Technique != null && !blue.TechniqueIntentUsed;
            if (!redWants && !blueWants)
            {
                return;
            }

            Corner first = Corner.Red;
            if (redWants && blueWants)
            {
                Fix64 redSpeed = red.Score((WrestlerAttribute.Speed, 50), (WrestlerAttribute.Explosiveness, 50));
                Fix64 blueSpeed = blue.Score((WrestlerAttribute.Speed, 50), (WrestlerAttribute.Explosiveness, 50));
                first = _random.Chance(FixMath.Sigmoid((redSpeed - blueSpeed) * _rates.InverseResolutionScale)) ? Corner.Red : Corner.Blue;
            }
            else if (blueWants)
            {
                first = Corner.Blue;
            }

            foreach (Corner corner in new[] { first, first.Opponent() })
            {
                WrestlerRuntime self = Wrestler(corner);
                if (ActiveTechnique != null || self.Intent.Technique == null || self.TechniqueIntentUsed)
                {
                    continue;
                }

                self.TechniqueIntentUsed = true;
                if (Catalog.Contains(self.Intent.Technique))
                {
                    TechniqueDefinition technique = Catalog.Get(self.Intent.Technique);
                    if (CanStart(corner, technique, self.Intent.TechniqueSide))
                    {
                        Begin(corner, technique, self.Intent.TechniqueSide, self.Intent.ExecutionQuality);
                    }
                }
            }
        }

        private StartPosition CurrentStartPosition(Corner corner) => Position switch
        {
            BoutPosition.ParTerre => Top == corner ? StartPosition.ParTerreTop : StartPosition.ParTerreBottom,
            BoutPosition.Scramble => StartPosition.Scramble,
            _ => Grips.All.Count > 0 ? StartPosition.NeutralTied : StartPosition.NeutralApart,
        };

        private RelativePosition CurrentRelativePosition(Corner corner) =>
            Position == BoutPosition.ParTerre && Top == corner ? RelativePosition.Behind : RelativePosition.Front;

        private bool CanStart(Corner corner, TechniqueDefinition technique, GripSide side)
        {
            WrestlerRuntime self = Wrestler(corner);
            if (self.Known(technique.Id) == null || !technique.IsAllowed(Style, Age))
            {
                return false;
            }

            if (technique.Role == TechniqueRole.Attack && Tick < self.AttackReadyTick)
            {
                return false;
            }

            StartPosition position = CurrentStartPosition(corner);
            if (!technique.Start.Positions.Contains(position))
            {
                return false;
            }

            if (position == StartPosition.NeutralApart)
            {
                Fix64 reach = Tuning.ContactDistance * ShotDistanceFactor;
                if (FixVector2.DistanceSquared(self.Body.Position, Wrestler(corner.Opponent()).Body.Position) > reach * reach)
                {
                    return false;
                }
            }

            foreach (GripRequirement requirement in technique.Start.RequiredGrips)
            {
                Grip? grip = Grips.Find(corner, requirement.Type);
                if (grip == null
                    || (requirement.Side == GripSideRequirement.Same && grip.Side != side)
                    || (requirement.Side == GripSideRequirement.Opposite && grip.Side == side))
                {
                    return false;
                }
            }

            if (technique.Start.RelativePositions.Count > 0 && !technique.Start.RelativePositions.Contains(CurrentRelativePosition(corner)))
            {
                return false;
            }

            if (self.Body.Level < technique.Start.MinimumOwnLevel || self.Body.Level > technique.Start.MaximumOwnLevel)
            {
                return false;
            }

            return self.Burst >= technique.Phases[0].BurstCost;
        }

        private bool CanCounter(Corner corner, string techniqueId)
        {
            WrestlerRuntime self = Wrestler(corner);
            if (self.Known(techniqueId) == null || !Catalog.Contains(techniqueId))
            {
                return false;
            }

            TechniqueDefinition counter = Catalog.Get(techniqueId);
            return counter.IsAllowed(Style, Age) && self.Burst >= counter.Phases[0].BurstCost;
        }

        private void Begin(Corner corner, TechniqueDefinition technique, GripSide side, Fix64 requestedQuality)
        {
            WrestlerRuntime self = Wrestler(corner);
            WrestlerRuntime opponent = Wrestler(corner.Opponent());
            (Fix64 floor, Fix64 cap) = self.QualityRange(technique);
            Fix64 quality = Fix64.Clamp(requestedQuality, floor, cap);

            Fix64 gripBonus = Fix64.Zero;
            foreach (GripBonus bonus in technique.Start.OptionalGrips)
            {
                if (Grips.Has(corner, bonus.Type))
                {
                    gripBonus += bonus.Bonus;
                }
            }

            bool executorWasBottom = Position == BoutPosition.ParTerre && Top != corner;
            ActiveTechnique = new TechniqueExecution(corner, technique, side, quality, gripBonus, opponent.Body.IsGrounded, executorWasBottom, Tick);
            StartPhase(ActiveTechnique, 0);
            self.LastAttackTick = Tick;
            self.Activity += Tuning.ActivityPerAttack;
            Emit(technique.Role == TechniqueRole.Attack ? BoutEventType.TechniqueStarted : BoutEventType.CounterStarted, corner, technique.Id, 0, side.ToString());
        }

        private void StartPhase(TechniqueExecution execution, int index)
        {
            PhaseDefinition phase = execution.Definition.Phases[index];
            execution.PhaseIndex = index;
            execution.PhaseTick = 0;
            execution.PhaseLength = _random.NextInt(phase.MinimumTicks, phase.MaximumTicks + 1);
        }

        private void AdvanceTechnique()
        {
            TechniqueExecution execution = ActiveTechnique!;
            execution.PhaseTick++;

            // The defender may start a counter or defence while a window is open.
            Corner defender = execution.Executor.Opponent();
            WrestlerRuntime defending = Wrestler(defender);
            string? counter = defending.Intent.Counter;
            if (counter != null && !defending.CounterIntentUsed)
            {
                CounterWindow? open = execution.Phase.CounterWindows.FirstOrDefault(w => w.Technique == counter && execution.PhaseTick >= w.FromTick && execution.PhaseTick <= w.ToTick);
                if (open != null && CanCounter(defender, counter))
                {
                    defending.CounterIntentUsed = true;
                    StartCounter(execution, open);
                    return;
                }
            }

            if (execution.PhaseTick >= execution.PhaseLength)
            {
                ResolvePhase(execution);
            }
        }

        private void StartCounter(TechniqueExecution interrupted, CounterWindow window)
        {
            TechniqueDefinition counter = Catalog.Get(window.Technique);
            WrestlerRuntime attacker = Wrestler(interrupted.Executor);
            attacker.Body.Balance -= Tuning.FailedAttackBalanceLoss;
            attacker.AttackReadyTick = Tick + _rates.AttackRecoveryTicks;
            Emit(BoutEventType.TechniqueFailed, interrupted.Executor, interrupted.Definition.Id, 0, PhaseOutcome.Countered.ToString());
            Corner defender = interrupted.Executor.Opponent();
            ActiveTechnique = null;
            Begin(defender, counter, interrupted.Side == GripSide.Left ? GripSide.Right : GripSide.Left, Wrestler(defender).Intent.ExecutionQuality);
            ActiveTechnique!.EntryAnimation = window.Animation;
        }

        /// <summary>Resolves the end of a phase with the sigmoid contest of design section 5.7.</summary>
        private void ResolvePhase(TechniqueExecution execution)
        {
            PhaseDefinition phase = execution.Phase;
            WrestlerRuntime self = Wrestler(execution.Executor);
            WrestlerRuntime opponent = Wrestler(execution.Executor.Opponent());
            self.Stamina = Fix64.Max(Fix64.Zero, self.Stamina - phase.StaminaCost);
            self.Burst = Fix64.Max(Fix64.Zero, self.Burst - phase.BurstCost);

            Fix64 attack = ContestFormulas.AttackScore(self, phase, execution.Quality, execution.GripBonus, opponent.Body.Balance, Tuning);
            Fix64 defence = ContestFormulas.DefenceScore(opponent, execution.Definition, Tuning);
            Fix64 chance = ContestFormulas.PhaseChance(phase, attack, defence, Tuning);
            Fix64 roll = _random.NextFix01();

            if (roll < chance)
            {
                Fix64 margin = (chance - roll) / chance;
                if (execution.IsLastPhase)
                {
                    Complete(execution, PickEndState(phase.EndStates, margin));
                }
                else
                {
                    Emit(BoutEventType.PhaseSucceeded, execution.Executor, execution.Definition.Id, 0, phase.Id);
                    StartPhase(execution, execution.PhaseIndex + 1);
                }

                return;
            }

            List<PhaseOutcome> failures = phase.Outcomes.Where(o => o != PhaseOutcome.Clean).ToList();
            PhaseOutcome outcome = failures.Count == 0 ? PhaseOutcome.Blocked : failures[_random.NextInt(0, failures.Count)];
            Fail(execution, outcome);
        }

        /// <summary>End states are listed best first; a bigger success margin gives a better one.</summary>
        private static EndState PickEndState(IReadOnlyList<EndState> endStates, Fix64 margin)
        {
            int index = ((Fix64.One - margin) * Fix64.FromInt(endStates.Count)).FloorToInt();
            return endStates[System.Math.Max(0, System.Math.Min(endStates.Count - 1, index))];
        }

        private void Fail(TechniqueExecution execution, PhaseOutcome outcome)
        {
            WrestlerRuntime self = Wrestler(execution.Executor);
            Corner defender = execution.Executor.Opponent();
            switch (outcome)
            {
                case PhaseOutcome.Partial:
                    execution.Quality *= PartialQualityFactor;
                    if (execution.IsLastPhase)
                    {
                        IReadOnlyList<EndState> endStates = execution.Phase.EndStates;
                        Complete(execution, endStates[endStates.Count - 1]);
                    }
                    else
                    {
                        StartPhase(execution, execution.PhaseIndex + 1);
                    }

                    return;
                case PhaseOutcome.Countered:
                    // Turning a failed attack into a counter takes counter-wrestling skill.
                    Fix64 counterChance = Wrestler(defender).Effective(WrestlerAttribute.CounterWrestling) * CounterOutcomeShare;
                    CounterWindow? counter = !_random.Chance(counterChance) ? null : execution.Phase.CounterWindows
                        .Where(w => CanCounter(defender, w.Technique))
                        .OrderByDescending(w => Wrestler(defender).Known(w.Technique)!.Proficiency)
                        .ThenBy(w => w.Technique, System.StringComparer.Ordinal)
                        .FirstOrDefault();
                    if (counter != null)
                    {
                        StartCounter(execution, counter);
                        return;
                    }

                    outcome = PhaseOutcome.Blocked;
                    break;
            }

            ActiveTechnique = null;
            self.AttackReadyTick = Tick + _rates.AttackRecoveryTicks;
            Emit(BoutEventType.TechniqueFailed, execution.Executor, execution.Definition.Id, 0, outcome.ToString());
            switch (outcome)
            {
                case PhaseOutcome.Stalled:
                    self.Body.Balance -= Tuning.FailedAttackBalanceLoss / Fix64.FromInt(2);
                    break;
                case PhaseOutcome.FailedIntoScramble:
                    self.Body.Balance -= Tuning.FailedAttackBalanceLoss;
                    BeginScramble(execution.Executor, fromFailure: true);
                    break;
                default:
                    self.Body.Balance -= Tuning.FailedAttackBalanceLoss;
                    Grips.ReleaseAll(execution.Executor);
                    break;
            }

            // A failed attack that leaves the attacker stepping out costs a point (Art. 38).
            if (Position == BoutPosition.Neutral && FootInProtectionArea(self))
            {
                ApplyScore(ScoringEvaluator.Evaluate(new ActionFacts { Executor = execution.Executor, ExecutorSteppedOutFirst = true }, Rules));
            }
        }

        private void Complete(TechniqueExecution execution, EndState endState)
        {
            ActiveTechnique = null;
            Corner executor = execution.Executor;
            Corner defender = executor.Opponent();
            WrestlerRuntime self = Wrestler(executor);
            WrestlerRuntime opponent = Wrestler(defender);
            self.AttackReadyTick = Tick + _rates.AttackRecoveryTicks;
            Emit(BoutEventType.TechniqueCompleted, executor, execution.Definition.Id, 0, endState.ToString());

            if (!execution.OpponentGroundedAtStart && Position == BoutPosition.Neutral)
            {
                // The action carries both wrestlers along the executor's direction.
                FixVector2 travel = self.Body.Facing * Tuning.TechniqueTravel;
                self.Body.Position += travel;
                opponent.Body.Position += travel;
            }

            bool inProtectionArea = _zones.ZoneAt(opponent.Body.Position) >= MatZone.ProtectionArea;
            if (endState == EndState.PushedOut)
            {
                ApplyScore(ScoringEvaluator.StepOut(defender, deliberatePushWithoutAction: false, opponent.Body.IsGrounded, Rules));
                Emit(BoutEventType.StepOut, defender, null, 0, "pushed out");
                Stop(RestartPosition.StandingCentre, executor, "step out");
                return;
            }

            ActionFacts facts = ContestFormulas.FactsFor(executor, execution.Definition, endState, execution.OpponentGroundedAtStart, execution.ExecutorWasBottom, inProtectionArea);

            if (facts.Completed)
            {
                ActionScore score = ScoringEvaluator.Evaluate(facts, Rules);
                ApplyScore(score);
                if (IsFinished)
                {
                    return;
                }

                if (score.Restart == RestartPosition.StandingCentre)
                {
                    Stop(RestartPosition.StandingCentre, executor, "action ended in the protection area");
                    return;
                }
            }

            switch (endState)
            {
                case EndState.DangerDirect:
                case EndState.DangerDelayed:
                    EnterDanger(executor);
                    break;
                case EndState.ControlBehind:
                case EndState.Stomach:
                case EndState.Flank:
                case EndState.ChestOrArms:
                    EnterParTerre(executor);
                    break;
                case EndState.Scramble:
                    BeginScramble(executor, fromFailure: false);
                    break;
                default:
                    if (Position == BoutPosition.ParTerre && execution.ExecutorWasBottom)
                    {
                        // The bottom wrestler stood up: wrestling continues standing.
                        Position = BoutPosition.Neutral;
                        Top = null;
                        Grips.Clear();
                        StandUp(self);
                        StandUp(opponent);
                    }

                    break;
            }
        }

        private void EnterParTerre(Corner top)
        {
            Position = BoutPosition.ParTerre;
            Top = top;
            _parTerreTicks = 0;
            Grips.Clear();
            WrestlerRuntime bottom = Wrestler(top.Opponent());
            bottom.Body.Ground = GroundState.Prone;
            bottom.Body.TorsoRotationDegrees = Fix64.Zero;
            bottom.Body.Level = Fix64.FromRatio(2, 10);
            WrestlerRuntime upper = Wrestler(top);
            upper.Body.Ground = GroundState.BothKnees;
            upper.Body.Level = Fix64.FromRatio(4, 10);
        }

        private void EnterDanger(Corner top)
        {
            Position = BoutPosition.Danger;
            Top = top;
            _dangerTicks = 0;
            _shoulderTicks = 0;
            WrestlerRuntime bottom = Wrestler(top.Opponent());
            bottom.Body.Ground = GroundState.Supine;
            bottom.Body.TorsoRotationDegrees = DangerTorsoRotation;
            bottom.Body.Level = Fix64.Zero;
            Wrestler(top).Body.Ground = GroundState.BothKnees;
            Emit(BoutEventType.DangerPosition, top.Opponent(), null, 0, string.Empty);
        }

        private void StandUp(WrestlerRuntime wrestler)
        {
            wrestler.Body.Ground = GroundState.Standing;
            wrestler.Body.TorsoRotationDegrees = Fix64.Zero;
            wrestler.Body.Level = Tuning.NeutralLevel.For(Style);
        }

        private void ApplyScore(ActionScore score)
        {
            foreach (PointAward award in score.Awards)
            {
                Sheet.AwardPoints(award.To, award.Points, award.Source, Clock.Now);
                _parTerreTicks = 0;
                Emit(BoutEventType.PointsAwarded, award.To, null, award.Points, award.Reason.ToString());
            }

            CheckEndConditions();
        }
    }
}
