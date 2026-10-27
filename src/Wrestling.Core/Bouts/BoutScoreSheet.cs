using System;
using System.Collections.Generic;
using Wrestling.Core.Rules;

namespace Wrestling.Core.Bouts
{
    /// <summary>
    /// The official record of a bout: points, cautions, passivity calls and warnings in order.
    /// Totals, technical superiority and tie-break decisions (Art. 29) are derived from it.
    /// </summary>
    public sealed class BoutScoreSheet
    {
        private readonly List<ScoreSheetEntry> _entries = new List<ScoreSheetEntry>();
        private int _nextSequence = 1;

        /// <summary>All entries in recording order, including revoked ones.</summary>
        public IReadOnlyList<ScoreSheetEntry> Entries => _entries;

        public PointsEntry AwardPoints(Corner scoredBy, int points, PointSource source, BoutTime time)
        {
            if (points <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(points), "Awarded points must be positive.");
            }

            return Add(new PointsEntry(_nextSequence, time, scoredBy, points, source));
        }

        public CautionEntry GiveCaution(Corner against, CautionReason reason, BoutTime time) =>
            Add(new CautionEntry(_nextSequence, time, against, reason));

        public PassivityEntry CallPassivity(Corner against, BoutTime time) =>
            Add(new PassivityEntry(_nextSequence, time, against, PassivityCalls + 1));

        public WarningEntry Warn(Corner against, WarningReason reason, BoutTime time) =>
            Add(new WarningEntry(_nextSequence, time, against, reason));

        /// <summary>Removes an entry after a successful challenge or a consultation correction.</summary>
        public void Revoke(ScoreSheetEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            if (!_entries.Contains(entry))
            {
                throw new ArgumentException("Entry does not belong to this score sheet.", nameof(entry));
            }

            if (entry.IsRevoked)
            {
                throw new InvalidOperationException("Entry is already revoked.");
            }

            entry.Revoke();
        }

        public int Points(Corner corner)
        {
            int total = 0;
            foreach (PointsEntry entry in ActivePoints())
            {
                if (entry.Wrestler == corner)
                {
                    total += entry.Points;
                }
            }

            return total;
        }

        public int Cautions(Corner corner) => CountActive<CautionEntry>(corner);

        public int Warnings(Corner corner, WarningReason reason)
        {
            int count = 0;
            foreach (ScoreSheetEntry entry in _entries)
            {
                if (!entry.IsRevoked && entry is WarningEntry warning && warning.Wrestler == corner && warning.Reason == reason)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Passivity calls in the bout, both wrestlers together.</summary>
        public int PassivityCalls
        {
            get
            {
                int count = 0;
                foreach (ScoreSheetEntry entry in _entries)
                {
                    if (!entry.IsRevoked && entry is PassivityEntry)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int PassivityCallsAgainst(Corner corner) => CountActive<PassivityEntry>(corner);

        /// <summary>The wrestler ahead on points, or null when level.</summary>
        public Corner? Leader
        {
            get
            {
                int red = Points(Corner.Red);
                int blue = Points(Corner.Blue);
                return red > blue ? Corner.Red : blue > red ? Corner.Blue : (Corner?)null;
            }
        }

        /// <summary>The wrestler leading by at least <paramref name="lead"/> points, or null.</summary>
        public Corner? LeadingBy(int lead)
        {
            int difference = Points(Corner.Red) - Points(Corner.Blue);
            return difference >= lead ? Corner.Red : -difference >= lead ? Corner.Blue : (Corner?)null;
        }

        /// <summary>
        /// Winner on points after regular time: the leader, or for level scores the tie-break
        /// winner (Art. 29). Null only for a bout with no deciding entry at all (0–0, no cautions).
        /// </summary>
        public Corner? DecideOnPoints() => Leader ?? TieBreak.Decide(this);

        internal IEnumerable<PointsEntry> ActivePoints()
        {
            foreach (ScoreSheetEntry entry in _entries)
            {
                if (!entry.IsRevoked && entry is PointsEntry points)
                {
                    yield return points;
                }
            }
        }

        private int CountActive<T>(Corner corner)
            where T : ScoreSheetEntry
        {
            int count = 0;
            foreach (ScoreSheetEntry entry in _entries)
            {
                if (!entry.IsRevoked && entry is T && entry.Wrestler == corner)
                {
                    count++;
                }
            }

            return count;
        }

        private T Add<T>(T entry)
            where T : ScoreSheetEntry
        {
            if (_entries.Count > 0 && entry.Time.CompareTo(_entries[_entries.Count - 1].Time) < 0)
            {
                throw new ArgumentException("Entries must be recorded in time order.", nameof(entry));
            }

            _entries.Add(entry);
            _nextSequence++;
            return entry;
        }
    }
}
