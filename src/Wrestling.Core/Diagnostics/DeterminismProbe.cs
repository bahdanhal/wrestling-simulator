using System;
using Wrestling.Core.Numerics;

namespace Wrestling.Core.Diagnostics
{
    /// <summary>
    /// Runs a fixed workload of random numbers and fixed-point arithmetic and returns a hash.
    /// Every platform (desktop, iOS, Android, CI) must produce the same hash for the same
    /// iteration count; a mismatch means simulation results would differ between devices.
    /// </summary>
    public static class DeterminismProbe
    {
        public const int DefaultIterations = 100_000;

        /// <summary>
        /// The hash every platform must produce for <see cref="DefaultIterations"/>. Changing
        /// Fix64 or Pcg32 behaviour changes it and breaks saved replays, so update it only on purpose.
        /// </summary>
        public const ulong ReferenceHash = 0x79A75970E2EE5FB9UL;

        /// <summary>True when this platform reproduces <see cref="ReferenceHash"/>.</summary>
        public static bool MatchesReference() => Run(DefaultIterations) == ReferenceHash;

        private const ulong Seed = 0x5EED_2026_0127UL;
        private const ulong Stream = 1UL;
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        public static ulong Run(int iterations = DefaultIterations)
        {
            if (iterations <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(iterations), "Iterations must be positive.");
            }

            var random = new Pcg32(Seed, Stream);
            ulong hash = FnvOffsetBasis;
            Fix64 accumulator = Fix64.One;

            for (int i = 0; i < iterations; i++)
            {
                // Operands in [-1000, 1000) with full fractional precision.
                Fix64 a = Fix64.FromInt(random.NextInt(-1000, 1000)) + random.NextFix01();
                Fix64 b = Fix64.FromInt(random.NextInt(-1000, 1000)) + random.NextFix01();
                if (b == Fix64.Zero)
                {
                    b = Fix64.One;
                }

                Fix64 product = a * b;
                Fix64 quotient = a / b;
                Fix64 sum = a + b;

                // Keep the accumulator bounded so the workload never overflows.
                accumulator = Fix64.Clamp(accumulator * Fix64.FromRatio(999, 1000) + quotient, Fix64.FromInt(-1_000_000), Fix64.FromInt(1_000_000));

                hash = Mix(hash, product.Raw);
                hash = Mix(hash, quotient.Raw);
                hash = Mix(hash, sum.Raw);
                hash = Mix(hash, accumulator.Raw);
            }

            return hash;
        }

        private static ulong Mix(ulong hash, long value)
        {
            ulong bits = unchecked((ulong)value);
            for (int shift = 0; shift < 64; shift += 8)
            {
                hash ^= (bits >> shift) & 0xFFUL;
                hash = unchecked(hash * FnvPrime);
            }

            return hash;
        }
    }
}
