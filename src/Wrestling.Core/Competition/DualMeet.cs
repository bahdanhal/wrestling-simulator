using System;
using System.Collections.Generic;
using System.Linq;
using Wrestling.Core.Bouts;
using Wrestling.Core.Rules;

namespace Wrestling.Core.Competition
{
    /// <summary>Which side of a dual meet: the home team wrestles in red.</summary>
    public enum MeetSide
    {
        Home,
        Away,
    }

    /// <summary>Totals of one team in one dual meet or across several, for Art. 33 criteria.</summary>
    public sealed class TeamMeetTotals
    {
        public int BoutsWon { get; internal set; }

        public int ClassificationPoints { get; internal set; }

        public int FallWins { get; internal set; }

        public int SuperiorityWins { get; internal set; }

        public int TechnicalPointsScored { get; internal set; }

        public int TechnicalPointsConceded { get; internal set; }

        internal void Add(TeamMeetTotals other)
        {
            BoutsWon += other.BoutsWon;
            ClassificationPoints += other.ClassificationPoints;
            FallWins += other.FallWins;
            SuperiorityWins += other.SuperiorityWins;
            TechnicalPointsScored += other.TechnicalPointsScored;
            TechnicalPointsConceded += other.TechnicalPointsConceded;
        }
    }

    /// <summary>
    /// A team match (Art. 33): one bout per weight category. The team winning more bouts wins.
    /// On equal bouts: total classification points, most falls, most superiority wins, most
    /// technical points, fewest conceded, then the outcome of the last bout wrestled.
    /// </summary>
    public sealed class DualMeet
    {
        private readonly Dictionary<int, BoutResult> _results = new Dictionary<int, BoutResult>();
        private readonly List<int> _completionOrder = new List<int>();

        public DualMeet(string homeTeam, string awayTeam, IReadOnlyList<int> weightCategoriesKg)
        {
            if (string.IsNullOrWhiteSpace(homeTeam) || string.IsNullOrWhiteSpace(awayTeam))
            {
                throw new ArgumentException("Team names must not be empty.");
            }

            if (homeTeam == awayTeam)
            {
                throw new ArgumentException("A team cannot meet itself.");
            }

            if (weightCategoriesKg == null || weightCategoriesKg.Count == 0 || weightCategoriesKg.Distinct().Count() != weightCategoriesKg.Count)
            {
                throw new ArgumentException("A dual meet needs one or more distinct weight categories.", nameof(weightCategoriesKg));
            }

            HomeTeam = homeTeam;
            AwayTeam = awayTeam;
            WeightCategoriesKg = weightCategoriesKg;
        }

        public string HomeTeam { get; }

        public string AwayTeam { get; }

        public IReadOnlyList<int> WeightCategoriesKg { get; }

        public bool IsComplete => _results.Count == WeightCategoriesKg.Count;

        public BoutResult? ResultFor(int weightKg) => _results.TryGetValue(weightKg, out BoutResult? result) ? result : null;

        public string TeamOn(MeetSide side) => side == MeetSide.Home ? HomeTeam : AwayTeam;

        /// <summary>Records a category's bout; red is the home team's wrestler. Categories may be wrestled in any order.</summary>
        public void Record(int weightKg, BoutResult result)
        {
            if (!WeightCategoriesKg.Contains(weightKg))
            {
                throw new ArgumentException($"{weightKg} kg is not part of this meet.", nameof(weightKg));
            }

            if (_results.ContainsKey(weightKg))
            {
                throw new InvalidOperationException($"{weightKg} kg is already decided.");
            }

            _results[weightKg] = result ?? throw new ArgumentNullException(nameof(result));
            _completionOrder.Add(weightKg);
        }

        /// <summary>A team without a wrestler in a category loses it by forfeit (5:0).</summary>
        public void RecordForfeit(int weightKg, MeetSide absent, Ruleset rules)
        {
            Corner winner = absent == MeetSide.Home ? Corner.Blue : Corner.Red;
            Record(weightKg, BoutResult.Win(winner, VictoryType.Forfeit, new BoutScoreSheet(), rules));
        }

        public TeamMeetTotals Totals(MeetSide side)
        {
            Corner corner = side == MeetSide.Home ? Corner.Red : Corner.Blue;
            var totals = new TeamMeetTotals();
            foreach (BoutResult result in _results.Values)
            {
                totals.ClassificationPoints += result.ClassificationPoints(corner);
                totals.TechnicalPointsScored += result.TechnicalPoints(corner);
                totals.TechnicalPointsConceded += result.TechnicalPoints(corner.Opponent());
                if (result.Winner == corner)
                {
                    totals.BoutsWon++;
                    if (result.VictoryType == VictoryType.Fall)
                    {
                        totals.FallWins++;
                    }
                    else if (result.VictoryType == VictoryType.TechnicalSuperiority)
                    {
                        totals.SuperiorityWins++;
                    }
                }
            }

            return totals;
        }

        /// <summary>The winning side once every category is decided; null only if every criterion is level (all bouts double losses).</summary>
        public MeetSide? Winner()
        {
            if (!IsComplete)
            {
                throw new InvalidOperationException("The meet is not complete.");
            }

            TeamMeetTotals home = Totals(MeetSide.Home);
            TeamMeetTotals away = Totals(MeetSide.Away);
            int comparison = Compare(home.BoutsWon, away.BoutsWon);
            if (comparison == 0)
            {
                comparison = CompareCriteria(home, away);
            }

            if (comparison == 0)
            {
                // Outcome of the last bout competed.
                for (int i = _completionOrder.Count - 1; i >= 0 && comparison == 0; i--)
                {
                    Corner? last = _results[_completionOrder[i]].Winner;
                    comparison = last == Corner.Red ? 1 : last == Corner.Blue ? -1 : 0;
                }
            }

            return comparison > 0 ? MeetSide.Home : comparison < 0 ? MeetSide.Away : (MeetSide?)null;
        }

        public string? WinningTeam()
        {
            MeetSide? side = Winner();
            return side.HasValue ? TeamOn(side.Value) : null;
        }

        /// <summary>Positive when <paramref name="a"/> ranks higher: classification points, falls, superiority wins, points scored, fewest conceded.</summary>
        internal static int CompareCriteria(TeamMeetTotals a, TeamMeetTotals b)
        {
            int c = Compare(a.ClassificationPoints, b.ClassificationPoints);
            if (c == 0)
            {
                c = Compare(a.FallWins, b.FallWins);
            }

            if (c == 0)
            {
                c = Compare(a.SuperiorityWins, b.SuperiorityWins);
            }

            if (c == 0)
            {
                c = Compare(a.TechnicalPointsScored, b.TechnicalPointsScored);
            }

            if (c == 0)
            {
                c = Compare(b.TechnicalPointsConceded, a.TechnicalPointsConceded);
            }

            return c;
        }

        private static int Compare(int a, int b) => a.CompareTo(b);
    }

    /// <summary>A team entered in a team competition with its draw number.</summary>
    public sealed class TeamEntry
    {
        public TeamEntry(string team, int drawNumber)
        {
            if (string.IsNullOrWhiteSpace(team))
            {
                throw new ArgumentException("Team must not be empty.", nameof(team));
            }

            Team = team;
            DrawNumber = drawNumber;
        }

        public string Team { get; }

        public int DrawNumber { get; }
    }

    /// <summary>
    /// Teams meeting each other once (Art. 33). A win is 1 team point. Two teams level on team
    /// points are separated by their meet; three or more by total classification points, falls,
    /// superiority wins, technical points scored, fewest conceded, lowest draw number.
    /// </summary>
    public sealed class TeamGroup
    {
        private readonly List<TeamEntry> _teams;
        private readonly List<DualMeet> _meets = new List<DualMeet>();

        public TeamGroup(IReadOnlyList<TeamEntry> teams, IReadOnlyList<int> weightCategoriesKg)
        {
            if (teams == null || teams.Count < 2)
            {
                throw new ArgumentException("A team group needs at least two teams.", nameof(teams));
            }

            if (teams.Select(t => t.Team).Distinct().Count() != teams.Count || teams.Select(t => t.DrawNumber).Distinct().Count() != teams.Count)
            {
                throw new ArgumentException("Teams and draw numbers must be unique.", nameof(teams));
            }

            _teams = teams.OrderBy(t => t.DrawNumber).ToList();
            foreach (RoundRobinPairing<TeamEntry> pairing in RoundRobin.Schedule(_teams))
            {
                _meets.Add(new DualMeet(pairing.Home.Team, pairing.Away.Team, weightCategoriesKg));
                Rounds = Math.Max(Rounds, pairing.Round);
            }
        }

        public IReadOnlyList<DualMeet> Meets => _meets;

        public int Rounds { get; }

        public bool IsComplete => _meets.All(m => m.IsComplete);

        public DualMeet MeetBetween(string a, string b) =>
            _meets.FirstOrDefault(m => (m.HomeTeam == a && m.AwayTeam == b) || (m.HomeTeam == b && m.AwayTeam == a))
            ?? throw new ArgumentException($"{a} and {b} do not meet in this group.");

        public int TeamPoints(string team) => _meets.Count(m => m.IsComplete && m.WinningTeam() == team);

        public TeamMeetTotals Totals(string team)
        {
            var totals = new TeamMeetTotals();
            foreach (DualMeet meet in _meets.Where(m => m.HomeTeam == team || m.AwayTeam == team))
            {
                totals.Add(meet.Totals(meet.HomeTeam == team ? MeetSide.Home : MeetSide.Away));
            }

            return totals;
        }

        public IReadOnlyList<string> Ranking()
        {
            if (!IsComplete)
            {
                throw new InvalidOperationException("The group is not complete.");
            }

            var order = new List<string>();
            foreach (IGrouping<int, TeamEntry> level in _teams.GroupBy(t => TeamPoints(t.Team)).OrderByDescending(g => g.Key))
            {
                List<TeamEntry> tied = level.ToList();
                if (tied.Count == 2)
                {
                    string? winner = MeetBetween(tied[0].Team, tied[1].Team).WinningTeam();
                    if (winner != null)
                    {
                        order.Add(winner);
                        order.Add(winner == tied[0].Team ? tied[1].Team : tied[0].Team);
                        continue;
                    }
                }

                tied.Sort((x, y) =>
                {
                    int c = DualMeet.CompareCriteria(Totals(y.Team), Totals(x.Team));
                    return c != 0 ? c : x.DrawNumber.CompareTo(y.DrawNumber);
                });
                order.AddRange(tied.Select(t => t.Team));
            }

            return order;
        }
    }
}
