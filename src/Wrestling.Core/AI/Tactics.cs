using Wrestling.Core.Numerics;

namespace Wrestling.Core.AI
{
    public enum Pace
    {
        Low,
        Medium,
        High,
    }

    /// <summary>Which kind of attack to prefer (design section 5.13).</summary>
    public enum AttackFocus
    {
        Balanced,
        Legs,
        UpperBody,
        ParTerre,
        Counters,
    }

    public enum PressureStyle
    {
        /// <summary>Drive the opponent back toward the edge.</summary>
        Forward,
        Balanced,

        /// <summary>Hold position, give ground only when needed.</summary>
        Defensive,
    }

    /// <summary>
    /// A bout plan: set before the bout by the coach (or the player in Coach mode) and changed
    /// with live instructions or at the break. Tactics change the AI's weights, never the engine's rules.
    /// </summary>
    public sealed class Tactics
    {
        public static readonly Tactics Default = new Tactics();

        public Pace Pace { get; init; } = Pace.Medium;

        public AttackFocus Focus { get; init; } = AttackFocus.Balanced;

        public PressureStyle Pressure { get; init; } = PressureStyle.Balanced;

        /// <summary>0 safe to 1 reckless: willingness to try low-percentage, high-value attacks.</summary>
        public Fix64 RiskTolerance { get; init; } = Fix64.Half;

        /// <summary>Protect a lead late in the bout instead of attacking.</summary>
        public bool DefendLead { get; init; } = true;
    }
}
