using System;
using System.Collections.Generic;

namespace Wrestling.Core.Competition
{
    /// <summary>One pairing in a round-robin schedule.</summary>
    public readonly struct RoundRobinPairing<T>
    {
        public RoundRobinPairing(int round, T home, T away)
        {
            Round = round;
            Home = home;
            Away = away;
        }

        /// <summary>1-based round number.</summary>
        public int Round { get; }

        /// <summary>Listed first (red corner for individual bouts).</summary>
        public T Home { get; }

        public T Away { get; }
    }

    /// <summary>
    /// Circle-method round robin: every pair meets exactly once, nobody appears twice in a round,
    /// and home/away alternates between rounds. Odd fields get one resting participant per round.
    /// </summary>
    public static class RoundRobin
    {
        public static IReadOnlyList<RoundRobinPairing<T>> Schedule<T>(IReadOnlyList<T> participants)
            where T : class
        {
            if (participants == null)
            {
                throw new ArgumentNullException(nameof(participants));
            }

            if (participants.Count < 2)
            {
                throw new ArgumentException("A round robin needs at least two participants.", nameof(participants));
            }

            var circle = new List<T?>(participants);
            if (circle.Count % 2 == 1)
            {
                circle.Add(null);
            }

            int n = circle.Count;
            var pairings = new List<RoundRobinPairing<T>>();
            for (int round = 0; round < n - 1; round++)
            {
                for (int i = 0; i < n / 2; i++)
                {
                    T? a = circle[i];
                    T? b = circle[n - 1 - i];
                    if (a == null || b == null)
                    {
                        continue;
                    }

                    bool swap = (round + i) % 2 == 1;
                    pairings.Add(new RoundRobinPairing<T>(round + 1, swap ? b : a, swap ? a : b));
                }

                // Keep the first position fixed and rotate the others.
                T? last = circle[n - 1];
                circle.RemoveAt(n - 1);
                circle.Insert(1, last);
            }

            return pairings;
        }

        /// <summary>Number of rounds for <paramref name="participants"/> entries.</summary>
        public static int RoundCount(int participants) => participants % 2 == 0 ? participants - 1 : participants;
    }
}
