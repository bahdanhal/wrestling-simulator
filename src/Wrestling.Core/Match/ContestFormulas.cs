using System.Linq;
using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;
using Wrestling.Core.Techniques;
using Wrestling.Core.Wrestlers;

namespace Wrestling.Core.Match
{
    /// <summary>
    /// The formulas that decide technique phases (design section 5.7). The engine resolves with
    /// them and the AI estimates with them, so the AI never "knows" a different game.
    /// </summary>
    public static class ContestFormulas
    {
        /// <summary>Attribute points the top wrestler gains from position when the bottom wrestler tries to escape.</summary>
        private static readonly Fix64 TopPositionAdvantage = Fix64.FromInt(25);

        /// <summary>Attack score A: phase attributes × execution quality, plus grip bonus and the opening from the defender's lost balance.</summary>
        public static Fix64 AttackScore(WrestlerRuntime attacker, PhaseDefinition phase, Fix64 quality, Fix64 gripBonus, Fix64 defenderBalance, EngineTuning tuning)
        {
            Fix64 attack = attacker.Score(phase.Attributes);
            return (attack * (Fix64.Half + (Fix64.Half * quality)))
                + (gripBonus * tuning.GripBonusWeight)
                + ((Fix64.One - defenderBalance) * tuning.OpeningWeight);
        }

        /// <summary>Defence score D: the attributes that stop this kind of technique.</summary>
        public static Fix64 DefenceScore(WrestlerRuntime defender, TechniqueDefinition technique, EngineTuning tuning) =>
            technique.Role == TechniqueRole.Attack ? FamilyDefence(defender, technique) + tuning.DefenceReadiness : FamilyDefence(defender, technique);

        private static Fix64 FamilyDefence(WrestlerRuntime defender, TechniqueDefinition technique)
        {
            if (technique.Role != TechniqueRole.Attack && technique.Start.Positions.Contains(StartPosition.ParTerreBottom))
            {
                // Against an escape or reversal from underneath: the top wrestler's control, plus the advantage of being on top.
                return defender.Score((WrestlerAttribute.ParTerreTurns, 40), (WrestlerAttribute.UpperBodyStrength, 30), (WrestlerAttribute.Balance, 30)) + TopPositionAdvantage;
            }

            if (technique.Role != TechniqueRole.Attack || technique.Family == null)
            {
                // Against a counter: staying balanced and reading it.
                return defender.Score((WrestlerAttribute.Balance, 50), (WrestlerAttribute.WrestlingIq, 50));
            }

            return technique.Family.Value switch
            {
                TechniqueFamily.LegAttacks => defender.Score((WrestlerAttribute.LegDefence, 70), (WrestlerAttribute.Balance, 30)),
                TechniqueFamily.TripsAndSweeps => defender.Score((WrestlerAttribute.Balance, 60), (WrestlerAttribute.LegDefence, 40)),
                TechniqueFamily.UpperBodyThrows => defender.Score((WrestlerAttribute.UpperBodyDefence, 70), (WrestlerAttribute.Balance, 30)),
                TechniqueFamily.BodyLockAndLifts => defender.Score((WrestlerAttribute.UpperBodyDefence, 50), (WrestlerAttribute.LowerBodyStrength, 50)),
                TechniqueFamily.GoBehindsAndDrags => defender.Score((WrestlerAttribute.HandFighting, 50), (WrestlerAttribute.Agility, 50)),
                TechniqueFamily.SnapDownsAndFrontHeadlock => defender.Score((WrestlerAttribute.Balance, 50), (WrestlerAttribute.UpperBodyDefence, 50)),
                TechniqueFamily.ParTerreTurns => defender.Score((WrestlerAttribute.ParTerreBottomDefence, 70), (WrestlerAttribute.Flexibility, 30)),
                _ => defender.Score((WrestlerAttribute.ParTerreBottomDefence, 60), (WrestlerAttribute.LowerBodyStrength, 40)),
            };
        }

        /// <summary>Probability that a phase succeeds.</summary>
        public static Fix64 PhaseChance(PhaseDefinition phase, Fix64 attack, Fix64 defence, EngineTuning tuning) =>
            FixMath.Sigmoid(phase.Steepness * (attack - defence) / tuning.ResolutionScale);

        /// <summary>What a completed technique means for scoring, from its end state.</summary>
        public static ActionFacts FactsFor(Corner executor, TechniqueDefinition technique, EndState endState, bool opponentGroundedAtStart, bool executorWasBottom, bool endedInProtectionArea) => new ActionFacts
        {
            Executor = executor,
            OpponentGroundedAtStart = opponentGroundedAtStart,
            Completed = endState != EndState.Neutral && endState != EndState.Scramble && endState != EndState.PushedOut,
            OpponentLifted = technique.Amplitude != Amplitude.None,
            GrandAmplitude = technique.Amplitude == Amplitude.GrandAmplitude,
            Landing = LandingFor(endState),
            ContinuousToDanger = endState == EndState.DangerDirect && !opponentGroundedAtStart,
            TakedownBehind = endState == EndState.ControlBehind && !executorWasBottom,
            Reversal = endState == EndState.ControlBehind && executorWasBottom,
            EndedInProtectionArea = endedInProtectionArea,
        };

        /// <summary>Points a completed technique would earn with the given end state.</summary>
        public static int PointsFor(TechniqueDefinition technique, EndState endState, bool opponentGroundedAtStart, bool executorWasBottom, Ruleset rules)
        {
            if (endState == EndState.PushedOut)
            {
                return rules.TechnicalPoints.StepOut;
            }

            ActionFacts facts = FactsFor(Corner.Red, technique, endState, opponentGroundedAtStart, executorWasBottom, false);
            return facts.Completed ? ScoringEvaluator.Evaluate(facts, rules).Awards.Where(a => a.To == Corner.Red).Sum(a => a.Points) : 0;
        }

        public static Landing LandingFor(EndState endState) => endState switch
        {
            EndState.DangerDirect => Landing.DirectDanger,
            EndState.DangerDelayed => Landing.DelayedDanger,
            EndState.Stomach => Landing.Stomach,
            EndState.Flank => Landing.Flank,
            EndState.ChestOrArms => Landing.ChestOrArms,
            _ => Landing.None,
        };
    }
}
