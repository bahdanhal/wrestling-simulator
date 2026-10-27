using System;
using System.Collections.Generic;
using System.Linq;
using Wrestling.Core.Bouts;
using Wrestling.Core.Rules;

namespace Wrestling.Core.Competition
{
    /// <summary>
    /// Categories of 6 or 7 wrestlers (Art. 8, 12): two Nordic pools, semi-finals A1–B2 and A2–B1,
    /// a final, and one bronze bout between the semi-final losers.
    /// Pool placement: seeds 1, 4, 5, 7 to pool A and 2, 3, 6 to pool B; the others alternate
    /// A, B, A, ... in draw-number order, pool A taking the extra wrestler when the count is odd.
    /// Places from 5th: the pools' third-placed wrestlers by criteria, then the fourth-placed.
    /// </summary>
    public sealed class TwoPoolTournament : ICompetitionFormat
    {
        public const int MinimumEntrants = 6;
        public const int MaximumEntrants = 7;

        private static readonly int[] SeedsInPoolA = { 1, 4, 5, 7 };

        private readonly BoutLedger _ledger;
        private bool _knockoutCreated;

        private TwoPoolTournament(BoutLedger ledger, IReadOnlyList<Entrant> entrants)
        {
            _ledger = ledger;
            (List<string> a, List<string> b) = SplitIntoPools(entrants);
            PoolA = NordicGroup.CreatePool(ledger, a, "Pool A");
            PoolB = NordicGroup.CreatePool(ledger, b, "Pool B");
        }

        public NordicGroup PoolA { get; }

        public NordicGroup PoolB { get; }

        public BracketBout? Final { get; private set; }

        public BracketBout? Bronze { get; private set; }

        public IReadOnlyList<BracketBout> Bouts => _ledger.Bouts;

        public IEnumerable<Entrant> Entrants => _ledger.Entrants.Values;

        public bool IsComplete => _knockoutCreated && Bouts.All(b => b.IsDecided);

        public static TwoPoolTournament Create(IReadOnlyList<Entrant> entrants, Ruleset rules)
        {
            if (entrants == null)
            {
                throw new ArgumentNullException(nameof(entrants));
            }

            if (entrants.Count < MinimumEntrants || entrants.Count > MaximumEntrants)
            {
                throw new ArgumentException($"The two-pool system takes {MinimumEntrants}–{MaximumEntrants} wrestlers.", nameof(entrants));
            }

            Seeding.Validate(entrants, entrants.Count);
            var ledger = new BoutLedger(rules, entrants);
            var tournament = new TwoPoolTournament(ledger, entrants);
            tournament.Advance();
            return tournament;
        }

        public EntrantStatus StatusOf(string entrantId) => _ledger.StatusOf(entrantId);

        public IEnumerable<BracketBout> ReadyBouts() => Bouts.Where(b => b.IsReadyToWrestle);

        public void RecordResult(int boutId, BoutResult result)
        {
            _ledger.RecordResult(boutId, result);
            Advance();
        }

        public void Withdraw(string entrantId, EntrantStatus status)
        {
            _ledger.Withdraw(entrantId, status);
            Advance();
        }

        public IReadOnlyList<Placement> Standings()
        {
            if (!IsComplete)
            {
                throw new InvalidOperationException("Standings are available once every bout is decided.");
            }

            var standings = new StandingsBuilder(_ledger);
            standings.Place(Final!.Winner, 1);
            standings.Place(Final.Loser, 2);
            standings.Place(Bronze!.Winner, 3);
            standings.Place(Bronze.Loser, 4);

            IReadOnlyList<string> a = PoolA.Ranking();
            IReadOnlyList<string> b = PoolB.Ranking();
            for (int position = 0; position < Math.Max(a.Count, b.Count); position++)
            {
                var atPosition = new List<string>();
                if (position < a.Count)
                {
                    atPosition.Add(a[position]);
                }

                if (position < b.Count)
                {
                    atPosition.Add(b[position]);
                }

                standings.PlaceByCriteria(atPosition, standings.NextRank);
            }

            return standings.Build();
        }

        internal static (List<string> A, List<string> B) SplitIntoPools(IReadOnlyList<Entrant> entrants)
        {
            int sizeA = (entrants.Count + 1) / 2;
            int sizeB = entrants.Count / 2;
            var a = new List<string>();
            var b = new List<string>();

            foreach (Entrant seeded in entrants.Where(e => e.Seed.HasValue).OrderBy(e => e.Seed))
            {
                (SeedsInPoolA.Contains(seeded.Seed!.Value) ? a : b).Add(seeded.Id);
            }

            bool nextIsA = true;
            foreach (Entrant entrant in entrants.Where(e => !e.Seed.HasValue).OrderBy(e => e.DrawNumber))
            {
                bool toA = (nextIsA && a.Count < sizeA) || b.Count >= sizeB;
                (toA ? a : b).Add(entrant.Id);
                nextIsA = !toA;
            }

            return (a, b);
        }

        private void Advance()
        {
            _ledger.Advance();
            if (_knockoutCreated || !PoolA.IsComplete || !PoolB.IsComplete)
            {
                return;
            }

            _knockoutCreated = true;
            IReadOnlyList<string> a = PoolA.Ranking();
            IReadOnlyList<string> b = PoolB.Ranking();
            BracketBout first = _ledger.AddBout(BracketStage.Main, 1, "1/2 Final", Slot(a, 0), Slot(b, 1));
            BracketBout second = _ledger.AddBout(BracketStage.Main, 1, "1/2 Final", Slot(a, 1), Slot(b, 0));
            Final = _ledger.AddBout(BracketStage.Main, 2, "Final", SlotSource.WinnerOf(first.Id), SlotSource.WinnerOf(second.Id));
            Bronze = _ledger.AddBout(BracketStage.Main, 2, "Bronze", SlotSource.LoserOf(first.Id), SlotSource.LoserOf(second.Id));
            _ledger.Advance();
        }

        private static SlotSource Slot(IReadOnlyList<string> ranking, int position) =>
            position < ranking.Count ? SlotSource.Entrant(ranking[position]) : SlotSource.Vacant();
    }
}
