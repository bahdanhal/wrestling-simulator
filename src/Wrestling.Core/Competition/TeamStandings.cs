using System;
using System.Collections.Generic;
using System.Linq;
using Wrestling.Core.Rules;

namespace Wrestling.Core.Competition
{
    /// <summary>A country's team total at an individual competition.</summary>
    public sealed class TeamScore
    {
        internal TeamScore(string team, int points, IReadOnlyList<int> placesCount)
        {
            Team = team;
            Points = points;
            PlacesCount = placesCount;
        }

        public string Team { get; }

        public int Points { get; }

        /// <summary>Element i is the number of categories where the team's best wrestler placed i+1.</summary>
        public IReadOnlyList<int> PlacesCount { get; }

        public override string ToString() => $"{Team}: {Points}";
    }

    /// <summary>
    /// Team ranking during individual competitions (Art. 32): in each category only a country's
    /// best-placed wrestler scores, using the ruleset's points per rank. Ties are broken by the
    /// most first places, then second places, and so on.
    /// </summary>
    public static class TeamStandings
    {
        public static IReadOnlyList<TeamScore> Compute(IEnumerable<IReadOnlyList<Placement>> categoryStandings, Func<string, string> teamOf, TeamRankingRules rules)
        {
            if (categoryStandings == null)
            {
                throw new ArgumentNullException(nameof(categoryStandings));
            }

            if (teamOf == null)
            {
                throw new ArgumentNullException(nameof(teamOf));
            }

            var points = new Dictionary<string, int>();
            var places = new Dictionary<string, int[]>();
            int deepest = 0;
            var bestPlaces = new List<(string Team, int Rank)>();

            foreach (IReadOnlyList<Placement> standings in categoryStandings)
            {
                foreach (IGrouping<string, Placement> team in standings.Where(p => p.Rank.HasValue).GroupBy(p => teamOf(p.EntrantId)))
                {
                    int best = team.Min(p => p.Rank!.Value);
                    bestPlaces.Add((team.Key, best));
                    deepest = Math.Max(deepest, best);
                }

                foreach (string team in standings.Select(p => teamOf(p.EntrantId)))
                {
                    if (!points.ContainsKey(team))
                    {
                        points[team] = 0;
                    }
                }
            }

            foreach (string team in points.Keys)
            {
                places[team] = new int[deepest];
            }

            foreach ((string team, int rank) in bestPlaces)
            {
                points[team] += rules.PointsFor(rank);
                places[team][rank - 1]++;
            }

            return points.Keys
                .Select(team => new TeamScore(team, points[team], places[team]))
                .OrderByDescending(s => s.Points)
                .ThenBy(s => s, PlacesComparer.Instance)
                .ThenBy(s => s.Team, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Orders teams with more first places first, then more second places, and so on.</summary>
        private sealed class PlacesComparer : IComparer<TeamScore>
        {
            public static readonly PlacesComparer Instance = new PlacesComparer();

            public int Compare(TeamScore? x, TeamScore? y)
            {
                if (x == null || y == null)
                {
                    return 0;
                }

                for (int i = 0; i < Math.Max(x.PlacesCount.Count, y.PlacesCount.Count); i++)
                {
                    int a = i < x.PlacesCount.Count ? x.PlacesCount[i] : 0;
                    int b = i < y.PlacesCount.Count ? y.PlacesCount[i] : 0;
                    if (a != b)
                    {
                        return b.CompareTo(a);
                    }
                }

                return 0;
            }
        }
    }
}
