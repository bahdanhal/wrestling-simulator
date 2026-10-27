using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;
using Wrestling.Core.Techniques;
using Wrestling.Core.Wrestlers;

namespace Wrestling.Core.Match
{
    /// <summary>A wrestler's state during a bout: body, energy, activity and current intent.</summary>
    public sealed class WrestlerRuntime
    {
        private static readonly Fix64 Hundred = Fix64.FromInt(100);
        private static readonly Fix64 Two = Fix64.FromInt(2);
        private static readonly Fix64 Hundredth = Fix64.FromRatio(1, 100);
        private readonly Fix64 _inverseStaminaMax;
        private Fix64 _stamina;
        private Intent _intent = Intent.Idle;
        private Fix64 _fatigueMultiplier = Fix64.One;
        private Fix64 _technicalMultiplier = Fix64.One;
        private static readonly Fix64 QualityFloorBase = Fix64.FromRatio(2, 10);
        private static readonly Fix64 QualityFloorRange = Fix64.FromRatio(4, 10);

        internal WrestlerRuntime(Corner corner, Wrestler wrestler, EngineTuning tuning)
        {
            Corner = corner;
            Wrestler = wrestler;
            StaminaMax = tuning.StaminaBase + Fix64.FromInt(wrestler.Attributes[WrestlerAttribute.AerobicEndurance]);
            BurstMax = tuning.BurstBase + Fix64.FromInt(wrestler.Attributes[WrestlerAttribute.AnaerobicCapacity]);
            FatigueFloor = tuning.FatigueFloor;
            _inverseStaminaMax = Fix64.One / StaminaMax;
            Stamina = StaminaMax;
            Burst = BurstMax;
            LastAttackTick = int.MinValue / 2;
        }

        public Corner Corner { get; }

        public Wrestler Wrestler { get; }

        public BodyState Body { get; } = new BodyState();

        public Fix64 StaminaMax { get; }

        /// <summary>Current stamina; setting it updates the cached fatigue multipliers.</summary>
        public Fix64 Stamina
        {
            get => _stamina;
            internal set
            {
                _stamina = value;
                _fatigueMultiplier = FatigueFloor + ((Fix64.One - FatigueFloor) * (value * _inverseStaminaMax));
                _technicalMultiplier = (Fix64.One + _fatigueMultiplier) * Fix64.Half;
            }
        }

        public Fix64 BurstMax { get; }

        public Fix64 Burst { get; internal set; }

        /// <summary>Recent activity (attacks and forward pressure), decaying over time.</summary>
        public Fix64 Activity { get; internal set; }

        public int LastAttackTick { get; internal set; }

        /// <summary>Engine tick from which the wrestler may start another attack.</summary>
        public int AttackReadyTick { get; internal set; }

        public Intent Intent
        {
            get => _intent;
            internal set
            {
                _intent = value;
                PushUnit = value.PushDirection.Normalized();
            }
        }

        /// <summary>The intent's push direction as a unit vector (zero when not pushing), normalised once per decision.</summary>
        internal FixVector2 PushUnit { get; private set; }

        /// <summary>Stamina as a fraction of the maximum.</summary>
        public Fix64 StaminaFraction => _stamina * _inverseStaminaMax;

        internal int NextDecisionTick { get; set; }

        internal bool TechniqueIntentUsed { get; set; }

        internal bool CounterIntentUsed { get; set; }

        private Fix64 FatigueFloor { get; }

        /// <summary>Physical performance multiplier from stamina: 1 fresh, the tuning floor when exhausted.</summary>
        public Fix64 FatigueMultiplier => _fatigueMultiplier;

        /// <summary>
        /// Attribute value adjusted for fatigue: physical attributes fully, technical ones half as
        /// much (technique survives fatigue better than power), mental ones not at all.
        /// </summary>
        public Fix64 Effective(WrestlerAttribute attribute)
        {
            Fix64 value = Fix64.FromInt(Wrestler.Attributes[attribute]);
            return WrestlerAttributes.GroupOf(attribute) switch
            {
                AttributeGroup.Physical => value * _fatigueMultiplier,
                AttributeGroup.Mental => value,
                _ => value * _technicalMultiplier,
            };
        }

        /// <summary>Weighted score of two attributes given as percentages (0–100 scale). Allocation-free.</summary>
        public Fix64 Score((WrestlerAttribute Attribute, int Percent) first, (WrestlerAttribute Attribute, int Percent) second) =>
            ((Effective(first.Attribute) * Fix64.FromInt(first.Percent)) + (Effective(second.Attribute) * Fix64.FromInt(second.Percent))) * Hundredth;

        /// <summary>Weighted score of three attributes given as percentages (0–100 scale). Allocation-free.</summary>
        public Fix64 Score((WrestlerAttribute Attribute, int Percent) first, (WrestlerAttribute Attribute, int Percent) second, (WrestlerAttribute Attribute, int Percent) third) =>
            ((Effective(first.Attribute) * Fix64.FromInt(first.Percent))
                + (Effective(second.Attribute) * Fix64.FromInt(second.Percent))
                + (Effective(third.Attribute) * Fix64.FromInt(third.Percent))) * Hundredth;

        /// <summary>Weighted score of a technique phase's attributes (0–100 scale).</summary>
        public Fix64 Score(System.Collections.Generic.IReadOnlyList<AttributeWeight> weights)
        {
            Fix64 total = Fix64.Zero;
            for (int i = 0; i < weights.Count; i++)
            {
                total += Effective(weights[i].Attribute) * Fix64.FromInt(weights[i].Percent);
            }

            return total * Hundredth;
        }

        public RepertoireEntry? Known(string techniqueId) => Wrestler.Technique(techniqueId);

        /// <summary>Execution quality limits from proficiency: a novice cannot execute perfectly, an expert rarely badly.</summary>
        public (Fix64 Floor, Fix64 Cap) QualityRange(TechniqueDefinition technique)
        {
            RepertoireEntry? entry = Known(technique.Id);
            Fix64 proficiency = entry == null ? Fix64.Zero : Fix64.FromInt(entry.Proficiency) * Hundredth;
            Fix64 floor = QualityFloorBase + (QualityFloorRange * proficiency);
            Fix64 cap = Fix64.Half + (Fix64.Half * proficiency);
            return (floor, cap);
        }
    }

    /// <summary>A technique being executed (design section 6).</summary>
    public sealed class TechniqueExecution
    {
        internal TechniqueExecution(Corner executor, TechniqueDefinition definition, GripSide side, Fix64 quality, Fix64 gripBonus, bool opponentGroundedAtStart, bool executorWasBottom, int startTick)
        {
            Executor = executor;
            Definition = definition;
            Side = side;
            Quality = quality;
            GripBonus = gripBonus;
            OpponentGroundedAtStart = opponentGroundedAtStart;
            ExecutorWasBottom = executorWasBottom;
            StartTick = startTick;
        }

        public Corner Executor { get; }

        public TechniqueDefinition Definition { get; }

        public GripSide Side { get; }

        public Fix64 Quality { get; internal set; }

        public Fix64 GripBonus { get; }

        public bool OpponentGroundedAtStart { get; }

        public bool ExecutorWasBottom { get; }

        public int StartTick { get; }

        public int PhaseIndex { get; internal set; }

        /// <summary>Ticks elapsed in the current phase.</summary>
        public int PhaseTick { get; internal set; }

        /// <summary>Rolled length of the current phase.</summary>
        public int PhaseLength { get; internal set; }

        public PhaseDefinition Phase => Definition.Phases[PhaseIndex];

        public bool IsLastPhase => PhaseIndex == Definition.Phases.Count - 1;
    }
}
