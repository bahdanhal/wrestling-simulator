using System;

namespace Wrestling.Core.Competition
{
    internal static class BracketMath
    {
        public static int LargestPowerOfTwoAtMost(int value)
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            int power = 1;
            while (power * 2 <= value)
            {
                power *= 2;
            }

            return power;
        }

        /// <summary>
        /// Line ranks for a bracket of <paramref name="size"/> lines (a power of two): element i is the
        /// seed rank placed on line i. Seeds 1 and 2 are in opposite halves, 1–4 in different quarters,
        /// and so on, keeping the strongest as far apart as possible. The weakest ranks alternate
        /// between halves, which gives the top-bottom, bottom-top order for qualification bouts.
        /// </summary>
        public static int[] LineRanks(int size)
        {
            if (size < 2 || (size & (size - 1)) != 0)
            {
                throw new ArgumentException("Bracket size must be a power of two of at least 2.", nameof(size));
            }

            int[] ranks = { 1, 2 };
            while (ranks.Length < size)
            {
                int next = ranks.Length * 2;
                var expanded = new int[next];
                for (int i = 0; i < ranks.Length; i++)
                {
                    expanded[2 * i] = ranks[i];
                    expanded[(2 * i) + 1] = next + 1 - ranks[i];
                }

                ranks = expanded;
            }

            return ranks;
        }

        /// <summary>Name of a main-draw round from the number of bouts in it.</summary>
        public static string MainRoundName(int boutsInRound) => boutsInRound switch
        {
            1 => "Final",
            2 => "1/2 Final",
            4 => "1/4 Final",
            _ => $"1/{boutsInRound} Final",
        };
    }
}
