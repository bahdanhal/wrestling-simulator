using System;
using System.Collections.Generic;
using System.Linq;
using Wrestling.Core.Bouts;

namespace Wrestling.Core.Competition
{
    /// <summary>Common surface of every weight-category format.</summary>
    public interface ICompetitionFormat
    {
        IReadOnlyList<BracketBout> Bouts { get; }

        IEnumerable<Entrant> Entrants { get; }

        bool IsComplete { get; }

        EntrantStatus StatusOf(string entrantId);

        IEnumerable<BracketBout> ReadyBouts();

        void RecordResult(int boutId, BoutResult result);

        void Withdraw(string entrantId, EntrantStatus status);

        IReadOnlyList<Placement> Standings();
    }

    public enum CompetitionSystem
    {
        /// <summary>A single entrant at a Continental or World Championship: the category is cancelled.</summary>
        Cancelled,

        /// <summary>2–5 wrestlers: round robin.</summary>
        Nordic,

        /// <summary>6–7 wrestlers: two Nordic pools, semi-finals, final and one bronze bout.</summary>
        TwoPools,

        /// <summary>8 or more: direct elimination with repechage.</summary>
        Elimination,
    }

    /// <summary>Chooses the competition system from the number of entrants (Art. 8).</summary>
    public static class CompetitionSystems
    {
        public static CompetitionSystem ForEntrantCount(int count)
        {
            if (count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "A category needs at least one entrant.");
            }

            return count switch
            {
                1 => CompetitionSystem.Cancelled,
                <= 5 => CompetitionSystem.Nordic,
                <= 7 => CompetitionSystem.TwoPools,
                _ => CompetitionSystem.Elimination,
            };
        }

        public static ICompetitionFormat Create(IReadOnlyList<Entrant> entrants, Rules.Ruleset rules) => ForEntrantCount(entrants.Count) switch
        {
            CompetitionSystem.Nordic => NordicGroup.Create(entrants, rules),
            CompetitionSystem.TwoPools => TwoPoolTournament.Create(entrants, rules),
            CompetitionSystem.Elimination => EliminationBracket.Create(entrants, rules),
            _ => throw new InvalidOperationException("A category with a single entrant is cancelled and has no competition."),
        };
    }

    internal static class Seeding
    {
        public static void Validate(IReadOnlyList<Entrant> entrants, int maximumSeeds)
        {
            List<int> seeds = entrants.Where(e => e.Seed.HasValue).Select(e => e.Seed!.Value).OrderBy(s => s).ToList();
            for (int i = 0; i < seeds.Count; i++)
            {
                if (seeds[i] != i + 1)
                {
                    throw new ArgumentException("Seeds must be numbered 1..n without gaps or duplicates.", nameof(entrants));
                }
            }

            if (seeds.Count > maximumSeeds)
            {
                throw new ArgumentException($"At most {maximumSeeds} seeds are possible here.", nameof(entrants));
            }
        }
    }

    /// <summary>Builds a standings list: explicit places first, then the rest by criteria, unranked last.</summary>
    internal sealed class StandingsBuilder
    {
        private readonly BoutLedger _ledger;
        private readonly List<Placement> _placements = new List<Placement>();
        private readonly HashSet<string> _placed = new HashSet<string>();

        public StandingsBuilder(BoutLedger ledger)
        {
            _ledger = ledger;
        }

        public int NextRank => _placements.Count + 1;

        public void Place(string? entrantId, int rank)
        {
            if (entrantId != null && _ledger.StatusOf(entrantId) != EntrantStatus.Unranked && _placed.Add(entrantId))
            {
                _placements.Add(new Placement(entrantId, rank));
            }
        }

        public bool IsPlaced(string entrantId) => _placed.Contains(entrantId);

        /// <summary>Ranks the given entrants consecutively from <paramref name="firstRank"/> by the Art. 8 criteria.</summary>
        public void PlaceByCriteria(IEnumerable<string> entrantIds, int firstRank)
        {
            List<string> candidates = entrantIds.Where(id => !_placed.Contains(id) && _ledger.StatusOf(id) != EntrantStatus.Unranked).ToList();
            int rank = firstRank;
            foreach (string id in StandingCriteria.Order(candidates, _ledger.Bouts, _ledger.Entrants))
            {
                Place(id, rank++);
            }
        }

        public void PlaceRemainingByCriteria(int firstRank) => PlaceByCriteria(_ledger.Entrants.Keys, firstRank);

        public IReadOnlyList<Placement> Build()
        {
            foreach (Entrant entrant in _ledger.Entrants.Values.Where(e => _ledger.StatusOf(e.Id) == EntrantStatus.Unranked).OrderBy(e => e.DrawNumber))
            {
                _placements.Add(new Placement(entrant.Id, null));
            }

            foreach (Entrant entrant in _ledger.Entrants.Values.Where(e => !_placed.Contains(e.Id) && _ledger.StatusOf(e.Id) != EntrantStatus.Unranked))
            {
                throw new InvalidOperationException($"Entrant '{entrant.Id}' was not placed.");
            }

            return _placements;
        }
    }
}
