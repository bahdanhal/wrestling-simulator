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
    /// <item>Everyone who lost to a finalist enters that finalist's repechage group, earliest round first; each group's last bout is for bronze.</item>
    /// </list>
    /// </summary>
    public sealed class EliminationBracket
    {
        public const int MinimumEntrants = 8;

        private readonly Ruleset _rules;
        private readonly Dictionary<string, Entrant> _entrants;
        private readonly Dictionary<string, EntrantStatus> _status;
        private readonly List<BracketBout> _bouts = new List<BracketBout>();
        private readonly int _mainSize;
        private int _nextBoutId = 1;
        private BracketBout? _topSemiFinal;
        private BracketBout? _bottomSemiFinal;
        private bool _repechageCreated;

        private EliminationBracket(Ruleset rules, IReadOnlyList<Entrant> entrants, int mainSize)
        {
            _rules = rules;
            _mainSize = mainSize;
            _entrants = entrants.ToDictionary(e => e.Id);
            _status = entrants.ToDictionary(e => e.Id, _ => EntrantStatus.Active);
        }

        public IReadOnlyList<BracketBout> Bouts => _bouts;

        public IEnumerable<Entrant> Entrants => _entrants.Values;

        /// <summary>Number of lines in the main draw (8, 16, 32, ...).</summary>
        public int MainDrawSize => _mainSize;

        public BracketBout Final { get; private set; } = null!;

        /// <summary>Bronze bouts, available once both semi-finals are decided (top half first).</summary>
        public IReadOnlyList<BracketBout> BronzeBouts => _bouts.Where(b => b.Stage == BracketStage.Repechage && b.Name == "Bronze").ToList();

        public bool IsComplete => _repechageCreated && _bouts.All(b => b.IsDecided);

        public EntrantStatus StatusOf(string entrantId) => _status[entrantId];

        /// <summary>Bouts that can be wrestled now, in schedule order.</summary>
        public IEnumerable<BracketBout> ReadyBouts() => _bouts.Where(b => b.IsReadyToWrestle);

        public BracketBout Bout(int id) => _bouts.FirstOrDefault(b => b.Id == id) ?? throw new ArgumentException($"No bout #{id}.", nameof(id));

        public static EliminationBracket Create(IReadOnlyList<Entrant> entrants, Ruleset rules)
        {
            if (entrants == null)
            {
                throw new ArgumentNullException(nameof(entrants));
            }

            if (rules == null)
            {
                throw new ArgumentNullException(nameof(rules));
            }

            ValidateEntrants(entrants);
            int mainSize = BracketMath.LargestPowerOfTwoAtMost(entrants.Count);
            var bracket = new EliminationBracket(rules, entrants, mainSize);
            bracket.Build(entrants);
            bracket.Advance();
            return bracket;
        }

        /// <summary>Records a wrestled bout. The result's red corner is the top participant.</summary>
        public void RecordResult(int boutId, BoutResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            BracketBout bout = Bout(boutId);
            if (!bout.IsReadyToWrestle)
            {
                throw new InvalidOperationException($"Bout #{boutId} is not ready to be wrestled.");
            }

            ApplyResult(bout, result);
            Advance();
        }

        /// <summary>
        /// Removes an entrant from further bouts, for example after failing the second-day weigh-in
        /// (unranked) or a medical withdrawal (injured, still ranked). Pending opponents advance.
        /// </summary>
        public void Withdraw(string entrantId, EntrantStatus status)
        {
            if (!_entrants.ContainsKey(entrantId))
            {
                throw new ArgumentException($"Unknown entrant '{entrantId}'.", nameof(entrantId));
            }

            if (status == EntrantStatus.Active)
            {
                throw new ArgumentException("Withdrawal status must be Injured or Unranked.", nameof(status));
            }

            _status[entrantId] = status;
            Advance();
        }

        /// <summary>Final placings: 1, 2, 3, 3, 5, 5, then from 7th by classification points and Art. 8 criteria; unranked entrants last without rank.</summary>
        public IReadOnlyList<Placement> Standings()
        {
            if (!IsComplete)
            {
                throw new InvalidOperationException("Standings are available once every bout is decided.");
            }

            var placements = new List<Placement>();
            var placed = new HashSet<string>();

            void Place(string? id, int rank)
            {
                if (id != null && _status[id] != EntrantStatus.Unranked && placed.Add(id))
                {
                    placements.Add(new Placement(id, rank));
                }
            }

            Place(Final.Winner, 1);
            Place(Final.Loser, 2);
            foreach (BracketBout bronze in BronzeBouts)
            {
                Place(bronze.Winner, 3);
            }

            foreach (BracketBout bronze in BronzeBouts)
            {
                Place(bronze.Loser, 5);
            }

            List<string> rest = _entrants.Keys
                .Where(id => !placed.Contains(id) && _status[id] != EntrantStatus.Unranked)
                .ToList();
            IReadOnlyList<string> ordered = StandingCriteria.Order(rest, _bouts, _entrants);
            int rank = 7;
            foreach (string id in ordered)
            {
                placements.Add(new Placement(id, rank++));
            }

            foreach (string id in _entrants.Keys.Where(id => _status[id] == EntrantStatus.Unranked).OrderBy(id => _entrants[id].DrawNumber))
            {
                placements.Add(new Placement(id, null));
            }

            return placements;
        }

        private static void ValidateEntrants(IReadOnlyList<Entrant> entrants)
        {
            if (entrants.Count < MinimumEntrants)
            {
                throw new ArgumentException($"Elimination brackets need at least {MinimumEntrants} entrants; use the Nordic or pool system below that.", nameof(entrants));
            }

            if (entrants.Select(e => e.Id).Distinct().Count() != entrants.Count)
            {
                throw new ArgumentException("Entrant ids must be unique.", nameof(entrants));
            }

            if (entrants.Select(e => e.DrawNumber).Distinct().Count() != entrants.Count)
            {
                throw new ArgumentException("Draw numbers must be unique.", nameof(entrants));
            }

            List<int> seeds = entrants.Where(e => e.Seed.HasValue).Select(e => e.Seed!.Value).OrderBy(s => s).ToList();
            for (int i = 0; i < seeds.Count; i++)
            {
                if (seeds[i] != i + 1)
                {
                    throw new ArgumentException("Seeds must be numbered 1..n without gaps or duplicates.", nameof(entrants));
                }
            }

            if (seeds.Count > BracketMath.LargestPowerOfTwoAtMost(entrants.Count))
            {
                throw new ArgumentException("There cannot be more seeds than main-draw lines.", nameof(entrants));
            }
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
                    BracketBout bout = AddBout(BracketStage.Qualification, 1, "Qualification",
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
                    BracketBout bout = AddBout(BracketStage.Main, round, name, current[2 * i], current[(2 * i) + 1]);
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

        private BracketBout AddBout(BracketStage stage, int round, string name, SlotSource top, SlotSource bottom)
        {
            var bout = new BracketBout(_nextBoutId++, stage, round, name, top, bottom);
            _bouts.Add(bout);
            return bout;
        }

        /// <summary>Resolves participants, settles walkovers and creates repechage until nothing changes.</summary>
        private void Advance()
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (BracketBout bout in _bouts)
                {
                    changed |= Resolve(bout, top: true);
                    changed |= Resolve(bout, top: false);
                    changed |= SettleWithoutWrestling(bout);
                }

                if (!_repechageCreated && _topSemiFinal!.IsDecided && _bottomSemiFinal!.IsDecided)
                {
                    CreateRepechage();
                    changed = true;
                }
            }
        }

        private bool Resolve(BracketBout bout, bool top)
        {
            if (bout.IsResolved(top))
            {
                return false;
            }

            SlotSource source = top ? bout.TopSource : bout.BottomSource;
            string? id = source.EntrantId;
            bool vacant = false;
            if (source.WinnerOfBoutId.HasValue)
            {
                BracketBout feeder = Bout(source.WinnerOfBoutId.Value);
                if (!feeder.IsDecided)
                {
                    return false;
                }

                id = feeder.Winner;
                vacant = id == null;
            }

            if (top)
            {
                bout.Top = id;
                bout.TopVacant = vacant;
            }
            else
            {
                bout.Bottom = id;
                bout.BottomVacant = vacant;
            }

            return true;
        }

        /// <summary>A bout where a side is vacant or withdrawn is decided without wrestling.</summary>
        private bool SettleWithoutWrestling(BracketBout bout)
        {
            if (bout.IsDecided || !bout.IsResolved(true) || !bout.IsResolved(false))
            {
                return false;
            }

            bool topAvailable = bout.Top != null && _status[bout.Top] == EntrantStatus.Active;
            bool bottomAvailable = bout.Bottom != null && _status[bout.Bottom] == EntrantStatus.Active;
            if (topAvailable && bottomAvailable)
            {
                return false;
            }

            bout.IsDecided = true;
            if (!topAvailable && !bottomAvailable)
            {
                return true;
            }

            string winner = topAvailable ? bout.Top! : bout.Bottom!;
            string? absent = topAvailable ? bout.Bottom : bout.Top;
            bout.Winner = winner;
            bout.Loser = absent;
            if (absent != null)
            {
                // A wrestler who cannot appear loses by injury (if injured) or forfeit; the score sheet is empty.
                VictoryType type = _status[absent] == EntrantStatus.Injured ? VictoryType.Injury : VictoryType.Forfeit;
                bout.Result = BoutResult.Win(topAvailable ? Corner.Red : Corner.Blue, type, new BoutScoreSheet(), _rules);
            }

            return true;
        }

        private void ApplyResult(BracketBout bout, BoutResult result)
        {
            bout.IsDecided = true;
            bout.Result = result;
            if (result.Winner == null)
            {
                MarkDoubleLoss(bout, result.DoubleLoss!.Value);
                return;
            }

            bout.Winner = result.Winner == Corner.Red ? bout.Top : bout.Bottom;
            bout.Loser = result.Winner == Corner.Red ? bout.Bottom : bout.Top;
            switch (result.VictoryType)
            {
                case VictoryType.Injury:
                    _status[bout.Loser!] = EntrantStatus.Injured;
                    break;
                case VictoryType.Forfeit:
                case VictoryType.Disqualification:
                    _status[bout.Loser!] = EntrantStatus.Unranked;
                    break;
            }
        }

        private void MarkDoubleLoss(BracketBout bout, DoubleLossType type)
        {
            EntrantStatus status = type == DoubleLossType.DoubleInjury ? EntrantStatus.Injured : EntrantStatus.Unranked;
            _status[bout.Top!] = status;
            _status[bout.Bottom!] = status;
        }

        private void CreateRepechage()
        {
            _repechageCreated = true;
            CreateRepechageGroup(_topSemiFinal!.Winner);
            CreateRepechageGroup(_bottomSemiFinal!.Winner);
        }

        /// <summary>Wrestlers beaten by the finalist, earliest round first, wrestle a ladder ending in a bronze bout.</summary>
        private void CreateRepechageGroup(string? finalist)
        {
            if (finalist == null)
            {
                return;
            }

            List<string> beaten = _bouts
                .Where(b => b.Stage != BracketStage.Repechage && b != Final && b.IsDecided && b.Winner == finalist && b.Loser != null)
                .OrderBy(b => b.Stage == BracketStage.Qualification ? 0 : b.Round)
                .Select(b => b.Loser!)
                .Where(id => _status[id] == EntrantStatus.Active)
                .ToList();

            if (beaten.Count == 0)
            {
                return;
            }

            if (beaten.Count == 1)
            {
                // Only the semi-final loser remains: the bronze is won without a bout.
                BracketBout single = AddBout(BracketStage.Repechage, 1, "Bronze", SlotSource.Entrant(beaten[0]), SlotSource.Entrant(beaten[0]));
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
                BracketBout bout = AddBout(BracketStage.Repechage, i, last ? "Bronze" : "Repechage", holder, SlotSource.Entrant(beaten[i]));
                holder = SlotSource.WinnerOf(bout.Id);
            }
        }
    }
}
