using System;
using System.Collections.Generic;
using Wrestling.Core.Bouts;
using Wrestling.Core.Rules;

namespace Wrestling.Core.Match
{
    /// <summary>How the opponent came down at the end of a throw or lift.</summary>
    public enum Landing
    {
        /// <summary>No throw or lift (takedowns, turns, reversals).</summary>
        None,
        Stomach,
        Flank,

        /// <summary>On the chest or one or two outstretched arms after rotating.</summary>
        ChestOrArms,

        /// <summary>Straight into danger position.</summary>
        DirectDanger,

        /// <summary>Into danger position after landing, not immediately.</summary>
        DelayedDanger,
    }

    /// <summary>Why points were awarded, for commentary and statistics.</summary>
    public enum ScoringReason
    {
        GrandAmplitudeToDirectDanger,
        GrandAmplitude,
        LiftToDanger,
        LiftToChestOrArms,
        StandingHoldToDanger,
        InstantaneousFall,
        Throw,
        Takedown,
        Exposure,
        Reversal,
        BlockedInDanger,
        StepOut,
        FailedAttackStepOut,
        FailedAttackOnBackInProtectionArea,
    }

    public enum RestartPosition
    {
        /// <summary>Wrestling continues without a stop.</summary>
        Continue,
        StandingCentre,
        ParTerre,
    }

    /// <summary>
    /// What physically happened in one action, from the point of view of the executing wrestler
    /// (the one whose hold it is; for a counter, the original defender). The engine fills this in
    /// from body and contact state; <see cref="ScoringEvaluator"/> turns it into points.
    /// </summary>
    public sealed class ActionFacts
    {
        public Corner Executor { get; init; }

        /// <summary>The opponent had at least one knee down when the action began: par terre valuation (Art. 34).</summary>
        public bool OpponentGroundedAtStart { get; init; }

        /// <summary>The hold was completed in a continuous action.</summary>
        public bool Completed { get; init; }

        /// <summary>The opponent lost all contact with the mat.</summary>
        public bool OpponentLifted { get; init; }

        /// <summary>A broad sweeping curve in the air with control (Art. 37). Implies <see cref="OpponentLifted"/>.</summary>
        public bool GrandAmplitude { get; init; }

        public Landing Landing { get; init; }

        /// <summary>From standing, brought into danger in one continuous, dynamic action (including when the opponent keeps one hand down).</summary>
        public bool ContinuousToDanger { get; init; }

        /// <summary>The opponent fell instantly from standing and the executor showed control.</summary>
        public bool InstantaneousFall { get; init; }

        /// <summary>Got behind and controlled the opponent on three of seven contact points.</summary>
        public bool TakedownBehind { get; init; }

        /// <summary>Separate exposures (back below 90°, rolls), for example consecutive gut wrenches.</summary>
        public int Exposures { get; init; }

        /// <summary>The executor was underneath in par terre and passed behind.</summary>
        public bool Reversal { get; init; }

        /// <summary>The executor's hold was blocked and the executor held in danger position by the opponent.</summary>
        public bool ExecutorBlockedInDanger { get; init; }

        /// <summary>The executor stepped into the protection area first during the hold.</summary>
        public bool ExecutorSteppedOutFirst { get; init; }

        /// <summary>The executor lifted and controlled the opponent but could not complete the hold.</summary>
        public bool LiftedAndControlledWithoutCompleting { get; init; }

        /// <summary>A failed attempt ended with the executor underneath, the opponent having done nothing (risk rule).</summary>
        public bool FailedAndEndedUnderneath { get; init; }

        /// <summary>The failed attempt ended in the protection area.</summary>
        public bool EndedInProtectionArea { get; init; }

        /// <summary>The failed attempt ended with the executor on the back under the opponent's control.</summary>
        public bool ExecutorOnBackUnderControl { get; init; }
    }

    public sealed class PointAward
    {
        public PointAward(Corner to, int points, ScoringReason reason, PointSource source)
        {
            To = to;
            Points = points;
            Reason = reason;
            Source = source;
        }

        public Corner To { get; }

        public int Points { get; }

        public ScoringReason Reason { get; }

        public PointSource Source { get; }

        public override string ToString() => $"{Points} to {To} ({Reason})";
    }

    public sealed class ActionScore
    {
        public ActionScore(IReadOnlyList<PointAward> awards, RestartPosition restart)
        {
            Awards = awards;
            Restart = restart;
        }

        public IReadOnlyList<PointAward> Awards { get; }

        public RestartPosition Restart { get; }
    }

    /// <summary>
    /// Values actions by Art. 34, 37, 38 and 45. The value depends only on the attacked wrestler's
    /// position, completed holds score from their end state, and a failed attempt only costs points
    /// in the step-out cases the rulebook lists.
    /// </summary>
    public static class ScoringEvaluator
    {
        public static ActionScore Evaluate(ActionFacts facts, Ruleset rules)
        {
            if (facts == null)
            {
                throw new ArgumentNullException(nameof(facts));
            }

            if (facts.Exposures < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(facts), "Exposures cannot be negative.");
            }

            if (facts.GrandAmplitude && !facts.OpponentLifted)
            {
                throw new ArgumentException("A grand amplitude throw requires the opponent to leave the mat.", nameof(facts));
            }

            TechnicalPointValues values = rules.TechnicalPoints;
            Corner executor = facts.Executor;
            Corner opponent = executor.Opponent();
            var awards = new List<PointAward>();

            if (facts.ExecutorBlockedInDanger)
            {
                // Blocked and held in danger: the opponent scores (Art. 34, 38 "blocks the opponent ... in a position of danger").
                awards.Add(new PointAward(opponent, values.BlockAttackerInDanger, ScoringReason.BlockedInDanger, PointSource.Hold));
                return new ActionScore(awards, RestartPosition.Continue);
            }

            if (!facts.Completed)
            {
                return EvaluateFailedAttempt(facts, values, opponent, awards);
            }

            AddHoldValue(facts, values, executor, awards);
            for (int i = 0; i < facts.Exposures; i++)
            {
                awards.Add(new PointAward(executor, values.Exposure, ScoringReason.Exposure, PointSource.Hold));
            }

            if (awards.Count == 0)
            {
                throw new ArgumentException("A completed action must produce a scoring situation (landing, takedown, exposure, reversal or fall).", nameof(facts));
            }

            return new ActionScore(awards, facts.EndedInProtectionArea ? RestartPosition.StandingCentre : RestartPosition.Continue);
        }

        /// <summary>
        /// A wrestler stepped into the protection area with one entire foot without a hold being
        /// executed (Art. 38, 45). Deliberately pushing out with no meaningful action earns nothing,
        /// unless the defender was on the knees.
        /// </summary>
        public static ActionScore StepOut(Corner steppedOut, bool deliberatePushWithoutAction, bool steppedOutWhileGrounded, Ruleset rules)
        {
            var awards = new List<PointAward>();
            if (!deliberatePushWithoutAction || steppedOutWhileGrounded)
            {
                awards.Add(new PointAward(steppedOut.Opponent(), rules.TechnicalPoints.StepOut, ScoringReason.StepOut, PointSource.StepOut));
            }

            return new ActionScore(awards, RestartPosition.StandingCentre);
        }

        private static void AddHoldValue(ActionFacts facts, TechnicalPointValues values, Corner executor, List<PointAward> awards)
        {
            void Add(int points, ScoringReason reason) => awards.Add(new PointAward(executor, points, reason, PointSource.Hold));

            bool danger = facts.Landing == Landing.DirectDanger || facts.Landing == Landing.DelayedDanger;

            if (facts.Reversal)
            {
                Add(values.Reversal, ScoringReason.Reversal);
            }
            else if (facts.GrandAmplitude && facts.Landing == Landing.DirectDanger)
            {
                // From standing, or a complete lift from par terre: both 5 (Art. 38).
                Add(facts.OpponentGroundedAtStart ? values.ParTerreGrandAmplitudeToDirectDanger : values.GrandAmplitudeToDirectDanger, ScoringReason.GrandAmplitudeToDirectDanger);
            }
            else if (facts.GrandAmplitude)
            {
                Add(values.GrandAmplitudeWithoutDirectDanger, ScoringReason.GrandAmplitude);
            }
            else if (facts.OpponentLifted && danger)
            {
                Add(values.ShortAmplitudeLiftToDanger, ScoringReason.LiftToDanger);
            }
            else if (facts.OpponentLifted && facts.Landing == Landing.ChestOrArms)
            {
                Add(values.CompleteLiftLandingOnChestOrArms, ScoringReason.LiftToChestOrArms);
            }
            else if (facts.InstantaneousFall && !facts.OpponentGroundedAtStart)
            {
                Add(values.InstantaneousFallFromStanding, ScoringReason.InstantaneousFall);
            }
            else if (!facts.OpponentGroundedAtStart && (facts.ContinuousToDanger || facts.Landing == Landing.DirectDanger))
            {
                Add(values.StandingHoldToDanger, ScoringReason.StandingHoldToDanger);
            }
            else if (danger)
            {
                // A hold that turns the opponent's back below 90°, standing or par terre: 2 (Art. 38).
                Add(values.Exposure, ScoringReason.Exposure);
            }
            else if (facts.Landing == Landing.Stomach || facts.Landing == Landing.Flank || facts.Landing == Landing.ChestOrArms)
            {
                Add(values.ThrowToStomachOrFlank, ScoringReason.Throw);
            }
            else if (facts.TakedownBehind)
            {
                Add(values.Takedown, ScoringReason.Takedown);
            }
        }

        private static ActionScore EvaluateFailedAttempt(ActionFacts facts, TechnicalPointValues values, Corner opponent, List<PointAward> awards)
        {
            if (facts.EndedInProtectionArea && facts.ExecutorOnBackUnderControl)
            {
                // Landed on the back in the protection area under control: 2 to the opponent (Art. 45).
                awards.Add(new PointAward(opponent, values.Exposure, ScoringReason.FailedAttackOnBackInProtectionArea, PointSource.Hold));
                return new ActionScore(awards, RestartPosition.StandingCentre);
            }

            if (facts.ExecutorSteppedOutFirst || (facts.EndedInProtectionArea && facts.FailedAndEndedUnderneath))
            {
                if (facts.LiftedAndControlledWithoutCompleting)
                {
                    // Lifted and controlled but not completed: stop, no points (Art. 38).
                    return new ActionScore(awards, RestartPosition.StandingCentre);
                }

                awards.Add(new PointAward(opponent, values.StepOut, ScoringReason.FailedAttackStepOut, PointSource.StepOut));
                return new ActionScore(awards, RestartPosition.StandingCentre);
            }

            // Risk rule: a failed attempt that ends underneath without the opponent's own action gives nothing; restart standing (Art. 34).
            return new ActionScore(awards, facts.FailedAndEndedUnderneath ? RestartPosition.StandingCentre : RestartPosition.Continue);
        }
    }
}
