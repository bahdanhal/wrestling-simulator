using System;
using System.Collections.Generic;
using System.Linq;
using Wrestling.Core.Bouts;
using Wrestling.Core.Rules;

namespace Wrestling.Core.Competition
{
    /// <summary>
    /// Shared bookkeeping for every competition format: bouts, entrant status, results,
    /// withdrawals and bouts decided without wrestling (walkovers).
    /// </summary>
    internal sealed class BoutLedger
    {
        private readonly Ruleset _rules;
        private readonly List<BracketBout> _bouts = new List<BracketBout>();
        private readonly Dictionary<string, EntrantStatus> _status;
        private int _nextBoutId = 1;

        public BoutLedger(Ruleset rules, IReadOnlyList<Entrant> entrants)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            if (entrants == null)
            {
                throw new ArgumentNullException(nameof(entrants));
            }

            if (entrants.Select(e => e.Id).Distinct().Count() != entrants.Count)
            {
                throw new ArgumentException("Entrant ids must be unique.", nameof(entrants));
            }

            if (entrants.Select(e => e.DrawNumber).Distinct().Count() != entrants.Count)
            {
                throw new ArgumentException("Draw numbers must be unique.", nameof(entrants));
            }

            Entrants = entrants.ToDictionary(e => e.Id);
            _status = entrants.ToDictionary(e => e.Id, _ => EntrantStatus.Active);
        }

        public Ruleset Rules => _rules;

        public IReadOnlyDictionary<string, Entrant> Entrants { get; }

        public IReadOnlyList<BracketBout> Bouts => _bouts;

        public EntrantStatus StatusOf(string entrantId) => _status[entrantId];

        public BracketBout Bout(int id) => _bouts.FirstOrDefault(b => b.Id == id) ?? throw new ArgumentException($"No bout #{id}.", nameof(id));

        public BracketBout AddBout(BracketStage stage, int round, string name, SlotSource top, SlotSource bottom)
        {
            var bout = new BracketBout(_nextBoutId++, stage, round, name, top, bottom);
            _bouts.Add(bout);
            return bout;
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

            bout.IsDecided = true;
            bout.Result = result;
            if (result.Winner == null)
            {
                EntrantStatus status = result.DoubleLoss == DoubleLossType.DoubleInjury ? EntrantStatus.Injured : EntrantStatus.Unranked;
                _status[bout.Top!] = status;
                _status[bout.Bottom!] = status;
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

        public void Withdraw(string entrantId, EntrantStatus status)
        {
            if (!Entrants.ContainsKey(entrantId))
            {
                throw new ArgumentException($"Unknown entrant '{entrantId}'.", nameof(entrantId));
            }

            if (status == EntrantStatus.Active)
            {
                throw new ArgumentException("Withdrawal status must be Injured or Unranked.", nameof(status));
            }

            _status[entrantId] = status;
        }

        /// <summary>Fills known participants and settles walkovers until nothing changes. Returns true if anything changed.</summary>
        public bool Advance()
        {
            bool any = false;
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

                any |= changed;
            }

            return any;
        }

        private bool Resolve(BracketBout bout, bool top)
        {
            if (bout.IsResolved(top))
            {
                return false;
            }

            SlotSource source = top ? bout.TopSource : bout.BottomSource;
            string? id = source.EntrantId;
            bool vacant = source.IsVacant;
            if (source.FromBoutId.HasValue)
            {
                BracketBout feeder = Bout(source.FromBoutId.Value);
                if (!feeder.IsDecided)
                {
                    return false;
                }

                id = source.TakesLoser ? feeder.Loser : feeder.Winner;
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

            string? absent = topAvailable ? bout.Bottom : bout.Top;
            bout.Winner = topAvailable ? bout.Top : bout.Bottom;
            bout.Loser = absent;
            if (absent != null)
            {
                // A wrestler who cannot appear loses by injury (if injured) or forfeit, with an empty score sheet.
                VictoryType type = _status[absent] == EntrantStatus.Injured ? VictoryType.Injury : VictoryType.Forfeit;
                bout.Result = BoutResult.Win(topAvailable ? Corner.Red : Corner.Blue, type, new BoutScoreSheet(), _rules);
            }

            return true;
        }
    }
}
