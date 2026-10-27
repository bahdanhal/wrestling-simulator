using System;
using System.Collections.Generic;
using System.Linq;
using Wrestling.Core.Match;
using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;
using Wrestling.Core.Wrestlers;

namespace Wrestling.Core.Techniques
{
    public enum TechniqueRole
    {
        /// <summary>Started from neutral or par terre to score.</summary>
        Attack,

        /// <summary>Started inside an opponent's technique phase to turn it around.</summary>
        Counter,

        /// <summary>Started inside an opponent's technique phase to stop it.</summary>
        Defence,
    }

    /// <summary>Position a technique can be started from (design section 5.5).</summary>
    public enum StartPosition
    {
        NeutralApart,
        NeutralTied,
        ParTerreTop,
        ParTerreBottom,
        Scramble,

        /// <summary>Inside the opponent's technique (counters and defences only).</summary>
        OpponentTechnique,
    }

    public enum RelativePosition
    {
        Front,
        Side,
        Behind,
    }

    /// <summary>Graded results of a phase (design section 5.7).</summary>
    public enum PhaseOutcome
    {
        Clean,
        Partial,
        Stalled,
        Blocked,
        FailedIntoScramble,
        Countered,
    }

    /// <summary>Landing and control states the scoring evaluator receives (design section 5.10).</summary>
    public enum EndState
    {
        /// <summary>Thrown or turned straight into danger position.</summary>
        DangerDirect,

        /// <summary>Danger position reached after landing.</summary>
        DangerDelayed,

        /// <summary>Landed on the side (flank).</summary>
        Flank,

        /// <summary>Landed chest down.</summary>
        Stomach,

        /// <summary>Landed on the chest or outstretched arms after a complete lift.</summary>
        ChestOrArms,

        /// <summary>Got behind and controlled on three of seven contact points.</summary>
        ControlBehind,

        /// <summary>Opponent pushed into the protection area.</summary>
        PushedOut,

        /// <summary>Contested position with no control.</summary>
        Scramble,

        /// <summary>Back to neutral standing with no score.</summary>
        Neutral,
    }

    /// <summary>Gesture families for Play mode (design section 7.1).</summary>
    public enum GestureFamily
    {
        StraightSwipeDown,
        Arc,
        ScoopOver,
        SidewaysFlick,
        SwipeAround,
        PullDown,
        Circle,
        UpwardPull,
        SwipeAway,
        SwipeAgainst,
    }

    public enum GripSideRequirement
    {
        Any,

        /// <summary>The grip must be on the side the technique is executed to.</summary>
        Same,

        Opposite,
    }

    public sealed class GripRequirement
    {
        public GripType Type { get; init; }

        public GripSideRequirement Side { get; init; }
    }

    public sealed class GripBonus
    {
        public GripType Type { get; init; }

        /// <summary>Added to the attack score when this grip is held (0–1 scale).</summary>
        public Fix64 Bonus { get; init; }
    }

    public sealed class StartRequirements
    {
        public IReadOnlyList<StartPosition> Positions { get; init; } = Array.Empty<StartPosition>();

        public IReadOnlyList<GripRequirement> RequiredGrips { get; init; } = Array.Empty<GripRequirement>();

        public IReadOnlyList<GripBonus> OptionalGrips { get; init; } = Array.Empty<GripBonus>();

        public IReadOnlyList<RelativePosition> RelativePositions { get; init; } = Array.Empty<RelativePosition>();

        /// <summary>Own level range (0 flat, 1 upright) needed to start.</summary>
        public Fix64 MinimumOwnLevel { get; init; }

        public Fix64 MaximumOwnLevel { get; init; } = Fix64.One;
    }

    /// <summary>An attribute's share of a phase score, in whole percent (exact, unlike decimal fractions in binary fixed-point).</summary>
    public sealed class AttributeWeight
    {
        public WrestlerAttribute Attribute { get; init; }

        public int Percent { get; init; }
    }

    /// <summary>A window, in ticks from the start of a phase, in which a counter or defence can begin.</summary>
    public sealed class CounterWindow
    {
        public string Technique { get; init; } = string.Empty;

        public int FromTick { get; init; }

        public int ToTick { get; init; }
    }

    public sealed class PhaseDefinition
    {
        public string Id { get; init; } = string.Empty;

        public int MinimumTicks { get; init; }

        public int MaximumTicks { get; init; }

        /// <summary>Attributes that decide the phase; percentages sum to exactly 100.</summary>
        public IReadOnlyList<AttributeWeight> Attributes { get; init; } = Array.Empty<AttributeWeight>();

        /// <summary>Steepness k of the logistic success curve for this phase.</summary>
        public Fix64 Steepness { get; init; } = Fix64.One;

        public Fix64 StaminaCost { get; init; }

        public Fix64 BurstCost { get; init; }

        public IReadOnlyList<PhaseOutcome> Outcomes { get; init; } = Array.Empty<PhaseOutcome>();

        public IReadOnlyList<CounterWindow> CounterWindows { get; init; } = Array.Empty<CounterWindow>();

        /// <summary>Possible end states; only the final phase has them.</summary>
        public IReadOnlyList<EndState> EndStates { get; init; } = Array.Empty<EndState>();

        /// <summary>Animation segment for this phase.</summary>
        public string Animation { get; init; } = string.Empty;
    }

    public sealed class GestureDefinition
    {
        public GestureFamily Family { get; init; }
    }

    /// <summary>An illegal outcome a technique can produce (for example a reverse waist hold thrown top-to-bottom).</summary>
    public sealed class FoulRisk
    {
        public FoulKind Kind { get; init; }

        /// <summary>Base probability per attempt before Discipline and fatigue adjust it.</summary>
        public Fix64 BaseProbability { get; init; }
    }

    public enum FoulKind
    {
        IllegalHold,
        LegFoul,
        Brutality,
    }

    /// <summary>
    /// A technique as content (design section 6.2): where it can start, its phases, counter windows,
    /// end states, gesture and animation. The engine runs techniques only from these definitions.
    /// </summary>
    public sealed class TechniqueDefinition
    {
        public int SchemaVersion { get; init; }

        public string Id { get; init; } = string.Empty;

        public string NameKey { get; init; } = string.Empty;

        public TechniqueRole Role { get; init; }

        /// <summary>Family for attacks (which rating applies); null for counters and defences.</summary>
        public TechniqueFamily? Family { get; init; }

        public IReadOnlyList<WrestlingStyle> Styles { get; init; } = Array.Empty<WrestlingStyle>();

        /// <summary>Age categories where the technique is illegal (for example double nelsons for U15 and U17).</summary>
        public IReadOnlyList<AgeCategory> ForbiddenForAges { get; init; } = Array.Empty<AgeCategory>();

        public StartRequirements Start { get; init; } = new StartRequirements();

        public IReadOnlyList<PhaseDefinition> Phases { get; init; } = Array.Empty<PhaseDefinition>();

        public GestureDefinition Gesture { get; init; } = new GestureDefinition();

        public IReadOnlyList<FoulRisk> Fouls { get; init; } = Array.Empty<FoulRisk>();

        public bool IsAllowed(WrestlingStyle style, AgeCategory age) => Styles.Contains(style) && !ForbiddenForAges.Contains(age);

        /// <summary>Problems within this definition alone; cross-references are checked by <see cref="TechniqueCatalog"/>.</summary>
        public IReadOnlyList<string> Validate()
        {
            var context = new ValidationContext();
            string path = string.IsNullOrEmpty(Id) ? "technique" : Id;
            context.RequireRange(SchemaVersion, 1, 1, path + ".schemaVersion");
            if (string.IsNullOrWhiteSpace(Id) || !Id.StartsWith("technique.", StringComparison.Ordinal))
            {
                context.Error(path + ": id must start with 'technique.'.");
            }

            context.RequireNotEmpty(NameKey, path + ".nameKey");
            if (Styles.Count == 0 || Styles.Distinct().Count() != Styles.Count)
            {
                context.Error(path + ".styles must list one or more distinct styles.");
            }

            ValidateRole(context, path);
            ValidateStart(context, path);
            ValidatePhases(context, path);
            foreach (FoulRisk foul in Fouls)
            {
                if (foul.BaseProbability <= Fix64.Zero || foul.BaseProbability > Fix64.One)
                {
                    context.Error(path + ".fouls[].baseProbability must be in (0, 1].");
                }
            }

            return context.Errors;
        }

        private void ValidateRole(ValidationContext context, string path)
        {
            if (Role == TechniqueRole.Attack)
            {
                if (Family == null)
                {
                    context.Error(path + ": attacks need a family.");
                }
                else
                {
                    foreach (WrestlingStyle style in Styles.Where(s => !WrestlerAttributes.IsLegal(Family.Value, s)))
                    {
                        context.Error($"{path}: family {Family} is illegal in {style}.");
                    }
                }

                if (Start.Positions.Contains(StartPosition.OpponentTechnique))
                {
                    context.Error(path + ": attacks cannot start inside the opponent's technique.");
                }
            }
            else
            {
                if (Family != null)
                {
                    context.Error(path + ": counters and defences have no family.");
                }

                if (!Start.Positions.Contains(StartPosition.OpponentTechnique))
                {
                    context.Error(path + ": counters and defences must be able to start inside the opponent's technique.");
                }
            }
        }

        private void ValidateStart(ValidationContext context, string path)
        {
            if (Start.Positions.Count == 0)
            {
                context.Error(path + ".start.positions must not be empty.");
            }

            foreach (GripRequirement grip in Start.RequiredGrips)
            {
                foreach (WrestlingStyle style in Styles.Where(s => !GripRules.IsLegal(grip.Type, s)))
                {
                    context.Error($"{path}: required grip {grip.Type} is illegal in {style}.");
                }
            }

            foreach (GripBonus bonus in Start.OptionalGrips)
            {
                if (bonus.Bonus <= Fix64.Zero || bonus.Bonus > Fix64.One)
                {
                    context.Error(path + ".start.optionalGrips[].bonus must be in (0, 1].");
                }
            }

            if (Start.MinimumOwnLevel < Fix64.Zero || Start.MaximumOwnLevel > Fix64.One || Start.MinimumOwnLevel > Start.MaximumOwnLevel)
            {
                context.Error(path + ".start: level range must be within 0..1 with minimum <= maximum.");
            }
        }

        private void ValidatePhases(ValidationContext context, string path)
        {
            if (Phases.Count == 0)
            {
                context.Error(path + ".phases must not be empty.");
                return;
            }

            if (Phases.Select(p => p.Id).Distinct().Count() != Phases.Count)
            {
                context.Error(path + ".phases ids must be unique.");
            }

            for (int i = 0; i < Phases.Count; i++)
            {
                PhaseDefinition phase = Phases[i];
                string phasePath = $"{path}.phases[{phase.Id}]";
                bool last = i == Phases.Count - 1;
                context.RequireNotEmpty(phase.Id, phasePath + ".id");
                context.RequireRange(phase.MinimumTicks, 1, 600, phasePath + ".minimumTicks");
                context.RequireRange(phase.MaximumTicks, phase.MinimumTicks, 600, phasePath + ".maximumTicks");
                context.RequireNotEmpty(phase.Animation, phasePath + ".animation");

                if (phase.Attributes.Count == 0)
                {
                    context.Error(phasePath + ".attributes must not be empty.");
                }

                int total = 0;
                foreach (AttributeWeight weight in phase.Attributes)
                {
                    context.RequireRange(weight.Percent, 1, 100, phasePath + ".attributes[].percent");
                    total += weight.Percent;
                }

                if (phase.Attributes.Count > 0 && total != 100)
                {
                    context.Error($"{phasePath}.attributes percentages sum to {total}; expected 100.");
                }

                if (phase.Attributes.Select(a => a.Attribute).Distinct().Count() != phase.Attributes.Count)
                {
                    context.Error(phasePath + ".attributes lists an attribute twice.");
                }

                if (phase.Steepness <= Fix64.Zero)
                {
                    context.Error(phasePath + ".steepness must be positive.");
                }

                if (phase.StaminaCost < Fix64.Zero || phase.BurstCost < Fix64.Zero)
                {
                    context.Error(phasePath + ": costs cannot be negative.");
                }

                if (phase.Outcomes.Count == 0 || !phase.Outcomes.Contains(PhaseOutcome.Clean))
                {
                    context.Error(phasePath + ".outcomes must include clean.");
                }

                foreach (CounterWindow window in phase.CounterWindows)
                {
                    context.RequireNotEmpty(window.Technique, phasePath + ".counterWindows[].technique");
                    if (window.FromTick < 0 || window.ToTick < window.FromTick || window.ToTick > phase.MaximumTicks)
                    {
                        context.Error($"{phasePath}.counterWindows[{window.Technique}] must lie within the phase.");
                    }
                }

                if (last && phase.EndStates.Count == 0)
                {
                    context.Error(phasePath + ": the final phase needs end states.");
                }

                if (!last && phase.EndStates.Count > 0)
                {
                    context.Error(phasePath + ": only the final phase has end states.");
                }
            }
        }
    }
}
