using System.Collections.Generic;
using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;

namespace Wrestling.Core.Match
{
    /// <summary>
    /// Every balancing number of the bout engine, loaded from <c>data/engine/tuning.json</c>.
    /// Rates are per second; attribute-based values are scaled by attributes on the 1–100 scale.
    /// </summary>
    public sealed class EngineTuning
    {
        public int SchemaVersion { get; init; }

        /// <summary>Decision interval in ticks for Wrestling IQ 100 (fastest) and 1 (slowest).</summary>
        public int FastestDecisionTicks { get; init; }

        public int SlowestDecisionTicks { get; init; }

        /// <summary>Distance between centres of mass at which wrestlers are in contact (metres).</summary>
        public Fix64 ContactDistance { get; init; }

        /// <summary>Walking speed toward the opponent at Speed 100 (m/s).</summary>
        public Fix64 ApproachSpeed { get; init; }

        /// <summary>How fast a tied pair moves under a full, unopposed push (m/s).</summary>
        public Fix64 PushSpeed { get; init; }

        public Fix64 LevelChangePerSecond { get; init; }

        /// <summary>Standing level each style returns to when not changing level.</summary>
        public StyleValues<Fix64> NeutralLevel { get; init; } = new StyleValues<Fix64>();

        public Fix64 BalanceRecoveryPerSecond { get; init; }

        /// <summary>Balance lost per second per unit of push the wrestler cannot resist.</summary>
        public Fix64 PushBalanceLoss { get; init; }

        public Fix64 GripGainPerSecond { get; init; }

        public Fix64 GripStripPerSecond { get; init; }

        public Fix64 GripStrengthDecayPerSecond { get; init; }

        /// <summary>Stamina pool = base + Aerobic Endurance.</summary>
        public Fix64 StaminaBase { get; init; }

        /// <summary>Burst pool = base + Anaerobic Capacity.</summary>
        public Fix64 BurstBase { get; init; }

        public Fix64 StaminaDrainPerSecond { get; init; }

        public Fix64 StaminaRecoveryPerSecond { get; init; }

        public Fix64 BurstRecoveryPerSecond { get; init; }

        /// <summary>Share of missing stamina recovered during the break between periods.</summary>
        public Fix64 BreakRecoveryFraction { get; init; }

        /// <summary>Physical performance multiplier at zero stamina.</summary>
        public Fix64 FatigueFloor { get; init; }

        /// <summary>Attribute points that move the sigmoid by one unit when resolving a contest.</summary>
        public Fix64 ResolutionScale { get; init; }

        /// <summary>Attribute points added to every defence against an attack: a set, alert defender's readiness.</summary>
        public Fix64 DefenceReadiness { get; init; }

        /// <summary>Attack score bonus (attribute points) for a fully off-balance defender.</summary>
        public Fix64 OpeningWeight { get; init; }

        /// <summary>Attack score bonus (attribute points) per unit of optional grip bonus.</summary>
        public Fix64 GripBonusWeight { get; init; }

        public Fix64 FailedAttackBalanceLoss { get; init; }

        /// <summary>Time after a technique ends before the same wrestler can start another attack (re-setting stance and grips).</summary>
        public Fix64 AttackRecoverySeconds { get; init; }

        /// <summary>How far a completed technique carries the pair along the executor's facing (metres).</summary>
        public Fix64 TechniqueTravel { get; init; }

        public Fix64 ScrambleSeconds { get; init; }

        /// <summary>Time the clock stays stopped for a stoppage before the restart.</summary>
        public Fix64 StoppageSeconds { get; init; }

        public Fix64 ParTerreStalemateSeconds { get; init; }

        public Fix64 DangerEscapePerSecond { get; init; }

        /// <summary>Rate, at even strength, at which the top wrestler forces both shoulders to the mat.</summary>
        public Fix64 PinPerSecond { get; init; }

        /// <summary>Rate, at even strength, at which the bottom wrestler lifts a pinned shoulder back off the mat.</summary>
        public Fix64 ShoulderBreakPerSecond { get; init; }

        /// <summary>Share of scrambles that end with both wrestlers back on their feet.</summary>
        public Fix64 ScrambleNeutralShare { get; init; }

        public Fix64 DangerMaximumSeconds { get; init; }

        /// <summary>Activity measures decay with this half-life (seconds).</summary>
        public Fix64 ActivityHalfLifeSeconds { get; init; }

        public Fix64 ActivityPerAttack { get; init; }

        public Fix64 ActivityPerPushSecond { get; init; }

        /// <summary>Freestyle: a wrestler below this activity, and below half the opponent's, is reported inactive.</summary>
        public Fix64 InactivityThreshold { get; init; }

        public Fix64 InactivityReportCooldownSeconds { get; init; }

        /// <summary>Greco-Roman: wrestling seconds between passivity assessments.</summary>
        public Fix64 GrecoPassivityCheckSeconds { get; init; }

        /// <summary>Greco-Roman: activity gap that makes one wrestler clearly more active.</summary>
        public Fix64 GrecoActivityDifference { get; init; }

        /// <summary>A push-out with no attack by the pusher within this time is deliberate (no point).</summary>
        public Fix64 DeliberatePushLookbackSeconds { get; init; }

        public IReadOnlyList<string> Validate()
        {
            var context = new ValidationContext();
            context.RequireRange(SchemaVersion, 1, 1, "schemaVersion");
            context.RequireRange(FastestDecisionTicks, 1, 120, "fastestDecisionTicks");
            context.RequireRange(SlowestDecisionTicks, FastestDecisionTicks, 240, "slowestDecisionTicks");
            Positive(context, ContactDistance, "contactDistance");
            Positive(context, ApproachSpeed, "approachSpeed");
            Positive(context, PushSpeed, "pushSpeed");
            Positive(context, LevelChangePerSecond, "levelChangePerSecond");
            Fraction(context, NeutralLevel.GrecoRoman, "neutralLevel.grecoRoman");
            Fraction(context, NeutralLevel.Freestyle, "neutralLevel.freestyle");
            Fraction(context, NeutralLevel.Womens, "neutralLevel.womens");
            Positive(context, BalanceRecoveryPerSecond, "balanceRecoveryPerSecond");
            Positive(context, PushBalanceLoss, "pushBalanceLoss");
            Positive(context, GripGainPerSecond, "gripGainPerSecond");
            Positive(context, GripStripPerSecond, "gripStripPerSecond");
            Positive(context, GripStrengthDecayPerSecond, "gripStrengthDecayPerSecond");
            Positive(context, StaminaBase, "staminaBase");
            Positive(context, BurstBase, "burstBase");
            Positive(context, StaminaDrainPerSecond, "staminaDrainPerSecond");
            Positive(context, StaminaRecoveryPerSecond, "staminaRecoveryPerSecond");
            Positive(context, BurstRecoveryPerSecond, "burstRecoveryPerSecond");
            Fraction(context, BreakRecoveryFraction, "breakRecoveryFraction");
            Fraction(context, FatigueFloor, "fatigueFloor");
            Positive(context, ResolutionScale, "resolutionScale");
            Positive(context, OpeningWeight, "openingWeight");
            Positive(context, DefenceReadiness, "defenceReadiness");
            Positive(context, GripBonusWeight, "gripBonusWeight");
            Fraction(context, FailedAttackBalanceLoss, "failedAttackBalanceLoss");
            Positive(context, AttackRecoverySeconds, "attackRecoverySeconds");
            Positive(context, TechniqueTravel, "techniqueTravel");
            Positive(context, ScrambleSeconds, "scrambleSeconds");
            Positive(context, StoppageSeconds, "stoppageSeconds");
            Positive(context, ParTerreStalemateSeconds, "parTerreStalemateSeconds");
            Positive(context, DangerEscapePerSecond, "dangerEscapePerSecond");
            Positive(context, PinPerSecond, "pinPerSecond");
            Positive(context, ShoulderBreakPerSecond, "shoulderBreakPerSecond");
            Fraction(context, ScrambleNeutralShare, "scrambleNeutralShare");
            Positive(context, DangerMaximumSeconds, "dangerMaximumSeconds");
            Positive(context, ActivityHalfLifeSeconds, "activityHalfLifeSeconds");
            Positive(context, ActivityPerAttack, "activityPerAttack");
            Positive(context, ActivityPerPushSecond, "activityPerPushSecond");
            Positive(context, InactivityThreshold, "inactivityThreshold");
            Positive(context, InactivityReportCooldownSeconds, "inactivityReportCooldownSeconds");
            Positive(context, GrecoPassivityCheckSeconds, "grecoPassivityCheckSeconds");
            Positive(context, GrecoActivityDifference, "grecoActivityDifference");
            Positive(context, DeliberatePushLookbackSeconds, "deliberatePushLookbackSeconds");
            return context.Errors;
        }

        public EngineTuning EnsureValid()
        {
            IReadOnlyList<string> errors = Validate();
            if (errors.Count > 0)
            {
                throw new RulesValidationException("Engine tuning", errors);
            }

            return this;
        }

        private static void Positive(ValidationContext context, Fix64 value, string path) => context.RequirePositive(value, path);

        private static void Fraction(ValidationContext context, Fix64 value, string path)
        {
            if (value <= Fix64.Zero || value > Fix64.One)
            {
                context.Error($"{path} must be in (0, 1].");
            }
        }
    }
}
