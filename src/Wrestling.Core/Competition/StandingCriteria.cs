using System.Collections.Generic;
using System.Linq;
using Wrestling.Core.Bouts;
using Wrestling.Core.Rules;

namespace Wrestling.Core.Competition
{
    /// <summary>Totals of one wrestler across all bouts of a competition, for ranking criteria.</summary>
    public sealed class CompetitionRecord
    {
        public CompetitionRecord(string entrantId)
        {
            EntrantId = entrantId;
        }

        public string EntrantId { get; }

        public int Wins { get; internal set; }

        public int ClassificationPoints { get; internal set; }

        public int FallWins { get; internal set; }

        public int SuperiorityWins { get; internal set; }

        public int TechnicalPointsScored { get; internal set; }

        public int TechnicalPointsConceded { get; internal set; }

        /// <summary>Totals over the given bouts (a bout's red corner is its top participant).</summary>
        public static IReadOnlyDictionary<string, CompetitionRecord> Compute(IEnumerable<string> entrantIds, IEnumerable<BracketBout> bouts)
        {
            Dictionary<string, CompetitionRecord> records = entrantIds.ToDictionary(id => id, id => new CompetitionRecord(id));
            foreach (BracketBout bout in bouts)
            {
                if (bout.Result == null)
                {
                    continue;
                }

                Add(records, bout.Top, bout.Result, Corner.Red);
                Add(records, bout.Bottom, bout.Result, Corner.Blue);
            }

            return records;
        }

        private static void Add(Dictionary<string, CompetitionRecord> records, string? id, BoutResult result, Corner corner)
        {
            if (id == null || !records.TryGetValue(id, out CompetitionRecord? record))
            {
                return;
            }

            record.ClassificationPoints += result.ClassificationPoints(corner);
            record.TechnicalPointsScored += result.TechnicalPoints(corner);
            record.TechnicalPointsConceded += result.TechnicalPoints(corner.Opponent());
            if (result.Winner == corner)
            {
                record.Wins++;
                if (result.VictoryType == VictoryType.Fall)
                {
                    record.FallWins++;
                }
                else if (result.VictoryType == VictoryType.TechnicalSuperiority)
                {
                    record.SuperiorityWins++;
                }
            }
        }
    }

    /// <summary>
    /// Ranking from 7th place (Art. 8): classification points, then most falls, most superiority
    /// wins, most technical points scored, fewest conceded, lowest seed, lowest draw number.
    /// </summary>
    public static class StandingCriteria
    {
        public static IReadOnlyList<string> Order(IReadOnlyCollection<string> entrantIds, IEnumerable<BracketBout> bouts, IReadOnlyDictionary<string, Entrant> entrants)
        {
            IReadOnlyDictionary<string, CompetitionRecord> records = CompetitionRecord.Compute(entrantIds, bouts);
            return entrantIds
                .OrderByDescending(id => records[id].ClassificationPoints)
                .ThenByDescending(id => records[id].FallWins)
                .ThenByDescending(id => records[id].SuperiorityWins)
                .ThenByDescending(id => records[id].TechnicalPointsScored)
                .ThenBy(id => records[id].TechnicalPointsConceded)
                .ThenBy(id => entrants[id].Seed ?? int.MaxValue)
                .ThenBy(id => entrants[id].DrawNumber)
                .ToList();
        }
    }
}
