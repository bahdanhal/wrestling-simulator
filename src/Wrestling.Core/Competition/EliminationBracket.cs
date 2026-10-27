using System;
using System.Collections.Generic;
using System.Linq;
using Wrestling.Core.Bouts;
using Wrestling.Core.Rules;

namespace Wrestling.Core.Competition
{
    /// <summary>
    /// Direct elimination with qualification rounds and two repechage groups (Art. 8, 12, 14),
    /// for weight categories of 8 or more wrestlers.
    /// <list type="bullet">
    /// <item>The main draw has the largest power of two not above the entry count; the rest wrestle qualification bouts.</item>
    /// <item>Seeds are placed as far apart as possible. Qualification lines are the weakest-ranked lines, so they alternate top-bottom, bottom-top, and seeds enter qualification only when unavoidable, lowest seed first.</item>
    /// <item>Non-seeded wrestlers fill the remaining positions top to bottom in draw-number order.</item>
    /// <item>Everyone who lost to a finalist enters that finalist's repechage group, earliest round first; each group's last bout is for bronze against the semi-final loser of the same half.</item>
    /// </list>
    /// </summary>
    public sealed class EliminationBracket : ICompetitionFormat
    {
        public const int MinimumEntrants = 8;

        private readonly BoutLedger _ledger;
        private readonly int _mainSize;
        private BracketBout? _topSemiFinal;
        private BracketBout? _bottomSemiFinal;
        private bool _repechageCreated;

        private EliminationBracket(BoutLedger ledger, int mainSize)
        {
            _ledger = ledger;
            _mainSize = mainSize;
        }

        public IReadOnlyList<BracketBout> Bouts => _ledger.Bouts;

        public IEnumerable<Entrant> Entrants => _ledger.Entrants.Values;

        /// <summary>Number of lines in the main draw (8, 16, 32, ...).</summary>
        public int MainDrawSize => _mainSize;

        public BracketBout Final { get; private set; } = null!;

        /// <summary>Bronze bouts, available once both semi-finals are decided (top half first).</summary>
        public IReadOnlyList<BracketBout> BronzeBouts => Bouts.Where(b => b.Stage == BracketStage.Repechage && b.Name == "Bronze").ToList();

        public bool IsComplete => _repechageCreated && Bouts.All(b => b.IsDecided);

        public EntrantStatus StatusOf(string entrantId) => _ledger.StatusOf(entrantId);

        public IEnumerable<BracketBout> ReadyBouts() => Bouts.Where(b => b.IsReadyToWrestle);

        public BracketBout Bout(int id) => _ledger.Bout(id);

        public static EliminationBracket Create(IReadOnlyList<Entrant> entrants, Ruleset rules)
        {
            var ledger = new BoutLedger(rules, entrants);
            ValidateEntrants(entrants);
            var bracket = new EliminationBracket(ledger, BracketMath.LargestPowerOfTwoAtMost(entrants.Count));
            bracket.Build(entrants);
            bracket.Advance();
            return bracket;
        }

        public void RecordResult(int boutId, BoutResult result)
        {
            _ledger.RecordResult(boutId, result);
            Advance();
        }

        /// <summary>
        /// Removes an entrant from further bouts, for example after failing the second-day weigh-in
        /// (unranked) or a medical withdrawal (injured, still ranked). Pending opponents advance.
        /// </summary>
        public void Withdraw(string entrantId, EntrantStatus status)
        {
            _ledger.Withdraw(entrantId, status);
            Advance();
        }

        /// <summary>Final placings: 1, 2, 3, 3, 5, 5, then from 7th by Art. 8 criteria; unranked entrants last without rank.</summary>
        public IReadOnlyList<Placement> Standings()
        {
            if (!IsComplete)
            {
                throw new InvalidOperationException("Standings are available once every bout is decided.");
            }

            var standings = new StandingsBuilder(_ledger);
            standings.Place(Final.Winner, 1);
            standings.Place(Final.Loser, 2);
            foreach (BracketBout bronze in BronzeBouts)
            {
                standings.Place(bronze.Winner, 3);
            }

            foreach (BracketBout bronze in BronzeBouts)
            {
                standings.Place(bronze.Loser, 5);
            }

            standings.PlaceRemainingByCriteria(firstRank: 7);
            return standings.Build();
        }

        private static void ValidateEntrants(IReadOnlyList<Entrant> entrants)
        {
            if (entrants.Count < MinimumEntrants)
            {
                throw new ArgumentException($"Elimination brackets need at least {MinimumEntrants} entrants; use the Nordic or pool system below that.", nameof(entrants));
            }

            Seeding.Validate(entrants, BracketMath.LargestPowerOfTwoAtMost(entrants.Count));
        }

        private void Build(IReadOnlyList<Entrant> entrants)
        {
            int qualificationBouts = entrants.Count - _mainSize;
            int[] lineRanks = BracketMath.LineRanks(_mainSize);

            // A line is a qualification line when its rank is among the weakest.
            bool[] isQualificationLine = lineRanks.Select(rank => rank > _mainSize - qualificationBouts).ToArray();

            // Slots in top-to-bottom order: one per direct line, two per qualification line.
            var slots = new List<(int Line, bool Upper)>();
            for (int line = 0; line < _mainSize; line++)
            {
                slots.Add((line, true));
                if (isQualificationLine[line])
                {
                    slots.Add((line, false));
                }
            }

            var occupant = new Dictionary<(int Line, bool Upper), Entrant>();
            foreach (Entrant seeded in entrants.Where(e => e.Seed.HasValue))
            {
                int line = Array.IndexOf(lineRanks, seeded.Seed!.Value);
                occupant[(line, true)] = seeded;
            }

            using (IEnumerator<Entrant> others = entrants.Where(e => !e.Seed.HasValue).OrderBy(e => e.DrawNumber).GetEnumerator())
            {
                foreach ((int Line, bool Upper) slot in slots)
                {
                    if (!occupant.ContainsKey(slot) && others.MoveNext())
                    {
                        occupant[slot] = others.Current;
                    }
                }
            }

            // Qualification bouts, top to bottom.
            var lineSources = new SlotSource[_mainSize];
            for (int line = 0; line < _mainSize; line++)
            {
                if (isQualificationLine[line])
                {
                    BracketBout bout = _ledger.AddBout(BracketStage.Qualification, 1, "Qualification",
                        SlotSource.Entrant(occupant[(line, true)].Id),
                        SlotSource.Entrant(occupant[(line, false)].Id));
                    lineSources[line] = SlotSource.WinnerOf(bout.Id);
                }
                else
                {
                    lineSources[line] = SlotSource.Entrant(occupant[(line, true)].Id);
                }
            }

            // Main draw rounds down to the final.
            SlotSource[] current = lineSources;
            int round = 1;
            while (current.Length > 1)
            {
                var next = new SlotSource[current.Length / 2];
                string name = BracketMath.MainRoundName(current.Length / 2);
                for (int i = 0; i < next.Length; i++)
                {
                    BracketBout bout = _ledger.AddBout(BracketStage.Main, round, name, current[2 * i], current[(2 * i) + 1]);
                    next[i] = SlotSource.WinnerOf(bout.Id);
                    if (next.Length == 2)
                    {
                        if (i == 0)
                        {
                            _topSemiFinal = bout;
                        }
                        else
                        {
                            _bottomSemiFinal = bout;
                        }
                    }
                    else if (next.Length == 1)
                    {
                        Final = bout;
                    }
                }

                current = next;
                round++;
            }
        }

        private void Advance()
        {
            do
            {
                _ledger.Advance();
                if (!_repechageCreated && _topSemiFinal!.IsDecided && _bottomSemiFinal!.IsDecided)
                {
                    _repechageCreated = true;
                    CreateRepechageGroup(_topSemiFinal.Winner);
                    CreateRepechageGroup(_bottomSemiFinal.Winner);
                    continue;
                }

                break;
            }
            while (true);
        }

        /// <summary>Wrestlers beaten by the finalist, earliest round first, wrestle a ladder ending in a bronze bout.</summary>
        private void CreateRepechageGroup(string? finalist)
        {
            if (finalist == null)
            {
                return;
            }

            List<string> beaten = Bouts
                .Where(b => b.Stage != BracketStage.Repechage && b != Final && b.IsDecided && b.Winner == finalist && b.Loser != null)
                .OrderBy(b => b.Stage == BracketStage.Qualification ? 0 : b.Round)
                .Select(b => b.Loser!)
                .Where(id => _ledger.StatusOf(id) == EntrantStatus.Active)
                .ToList();

            if (beaten.Count == 0)
            {
                return;
            }

            if (beaten.Count == 1)
            {
                // Only one beaten wrestler can continue: the bronze is awarded without a bout.
                BracketBout single = _ledger.AddBout(BracketStage.Repechage, 1, "Bronze", SlotSource.Entrant(beaten[0]), SlotSource.Entrant(beaten[0]));
                single.Top = beaten[0];
                single.BottomVacant = true;
                single.IsDecided = true;
                single.Winner = beaten[0];
                return;
            }

            SlotSource holder = SlotSource.Entrant(beaten[0]);
            for (int i = 1; i < beaten.Count; i++)
            {
                bool last = i == beaten.Count - 1;
                BracketBout bout = _ledger.AddBout(BracketStage.Repechage, i, last ? "Bronze" : "Repechage", holder, SlotSource.Entrant(beaten[i]));
                holder = SlotSource.WinnerOf(bout.Id);
            }
        }
    }
}
