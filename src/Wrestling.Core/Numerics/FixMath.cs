namespace Wrestling.Core.Numerics
{
    /// <summary>Deterministic helper functions on <see cref="Fix64"/>.</summary>
    public static class FixMath
    {
        /// <summary>
        /// Smooth S-curve from 0 to 1 with sigmoid(0) = 0.5: 0.5 + 0.5·x / (1 + |x|).
        /// Algebraic rather than exponential, so it is exact in fixed point on every platform.
        /// </summary>
        public static Fix64 Sigmoid(Fix64 x) => Fix64.Half + (Fix64.Half * (x / (Fix64.One + Fix64.Abs(x))));

        public static Fix64 Lerp(Fix64 from, Fix64 to, Fix64 t) => from + ((to - from) * t);

        /// <summary>Moves <paramref name="value"/> toward <paramref name="target"/> by at most <paramref name="maxStep"/>.</summary>
        public static Fix64 MoveTowards(Fix64 value, Fix64 target, Fix64 maxStep)
        {
            if (value < target)
            {
                return Fix64.Min(value + maxStep, target);
            }

            return Fix64.Max(value - maxStep, target);
        }

        public static Fix64 Percent(int percent) => Fix64.FromRatio(percent, 100);

        /// <summary>A per-second rate expressed per simulation tick.</summary>
        public static Fix64 PerTick(Fix64 perSecond, int ticksPerSecond) => perSecond / Fix64.FromInt(ticksPerSecond);
    }
}
