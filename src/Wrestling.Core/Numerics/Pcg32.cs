using System;

namespace Wrestling.Core.Numerics
{
    /// <summary>
    /// PCG32 (PCG-XSH-RR 64/32) random number generator by Melissa O'Neill.
    /// Deterministic on every platform. Each bout, week and generation step uses its own
    /// stream, so systems never consume each other's random numbers.
    /// </summary>
    public sealed class Pcg32
    {
        private const ulong Multiplier = 6364136223846793005UL;

        private ulong _state;

        /// <summary>Seeds the generator. Different <paramref name="stream"/> values give independent sequences for the same seed.</summary>
        public Pcg32(ulong seed, ulong stream)
        {
            Increment = (stream << 1) | 1UL;
            _state = 0;
            NextUInt();
            _state = unchecked(_state + seed);
            NextUInt();
        }

        private Pcg32(ulong state, ulong increment, bool restore)
        {
            _state = state;
            Increment = increment;
        }

        /// <summary>Current internal state, stored in saves together with <see cref="Increment"/>.</summary>
        public ulong State => _state;

        /// <summary>Stream constant (always odd).</summary>
        public ulong Increment { get; }

        /// <summary>Restores a generator from a saved <see cref="State"/> and <see cref="Increment"/>.</summary>
        public static Pcg32 FromState(ulong state, ulong increment)
        {
            if ((increment & 1UL) == 0)
            {
                throw new ArgumentException("PCG32 increment must be odd.", nameof(increment));
            }

            return new Pcg32(state, increment, restore: true);
        }

        /// <summary>Next uniformly distributed 32-bit value.</summary>
        public uint NextUInt()
        {
            ulong old = _state;
            _state = unchecked(old * Multiplier + Increment);
            uint xorShifted = unchecked((uint)(((old >> 18) ^ old) >> 27));
            int rotation = (int)(old >> 59);
            return (xorShifted >> rotation) | (xorShifted << ((-rotation) & 31));
        }

        /// <summary>Uniform value in [0, <paramref name="bound"/>) without modulo bias.</summary>
        public uint NextUInt(uint bound)
        {
            if (bound == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bound), "Bound must be positive.");
            }

            uint threshold = unchecked(0u - bound) % bound;
            while (true)
            {
                uint value = NextUInt();
                if (value >= threshold)
                {
                    return value % bound;
                }
            }
        }

        /// <summary>Uniform integer in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be greater than minInclusive.");
            }

            uint range = (uint)((long)maxExclusive - minInclusive);
            return (int)(minInclusive + (long)NextUInt(range));
        }

        /// <summary>Uniform fixed-point value in [0, 1) using all 32 fractional bits.</summary>
        public Fix64 NextFix01() => Fix64.FromRaw(NextUInt());

        /// <summary>True with the given probability. Values at or below 0 never succeed; values at or above 1 always do.</summary>
        public bool Chance(Fix64 probability)
        {
            if (probability <= Fix64.Zero)
            {
                return false;
            }

            if (probability >= Fix64.One)
            {
                return true;
            }

            return NextFix01() < probability;
        }
    }
}
