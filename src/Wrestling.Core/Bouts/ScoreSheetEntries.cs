using System;
using Wrestling.Core.Rules;

namespace Wrestling.Core.Bouts
{
    /// <summary>Why points were awarded. Determines how the points count in tie-breaks (Art. 29).</summary>
    public enum PointSource
    {
        /// <summary>A hold or action: takedown, exposure, throw, lift, reversal.</summary>
        Hold,

        /// <summary>Opponent stepped into the protection area (push-out).</summary>
        StepOut,

        /// <summary>Greco-Roman passivity call.</summary>
        Passivity,

        /// <summary>Freestyle/Women's activity period expired without the passive wrestler scoring.</summary>
        ActivityClock,

        /// <summary>The opponent lost a challenge.</summary>
        LostChallenge,

        /// <summary>Opponent's infringement (fleeing, illegal action, negative wrestling, refusing position).</summary>
        Infringement,

        /// <summary>Opponent stopped the bout for injury with no blood or visible injury.</summary>
        InjuryStoppage,
    }

    public enum CautionReason
    {
        FleeingHold,
        FleeingMat,
        IllegalHold,
        IllegalAction,
        NegativeWrestling,
        RefusingParTerrePosition,
        LegFoul,
        Stalling,
        FinalSecondsEvasion,
        Brutality,
    }

    public enum WarningReason
    {
        Passivity,
        Zone,
        IllegalHold,
        ParTerrePosition,
        LegFoul,
        Jumping,
        InterruptingBout,
        DeliberatePushOut,
    }

    public static class PointSourceExtensions
    {
        /// <summary>
        /// Technical points come from the scorer's own wrestling: holds and push-outs (Art. 29, 4.1.1).
        /// Passivity, activity-clock, lost-challenge, infringement and injury points are awarded
        /// because of the opponent and are non-technical.
        /// </summary>
        public static bool IsTechnical(this PointSource source) => source == PointSource.Hold || source == PointSource.StepOut;
    }

    /// <summary>Position on the bout clock: period number (1-based) and simulation ticks elapsed in that period.</summary>
    public readonly struct BoutTime : IEquatable<BoutTime>, IComparable<BoutTime>
    {
        public BoutTime(int period, int tickInPeriod)
        {
            if (period < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(period), "Periods start at 1.");
            }

            if (tickInPeriod < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tickInPeriod), "Ticks cannot be negative.");
            }

            Period = period;
            TickInPeriod = tickInPeriod;
        }

        public int Period { get; }

        public int TickInPeriod { get; }

        public int CompareTo(BoutTime other) => Period != other.Period ? Period.CompareTo(other.Period) : TickInPeriod.CompareTo(other.TickInPeriod);

        public bool Equals(BoutTime other) => Period == other.Period && TickInPeriod == other.TickInPeriod;

        public override bool Equals(object? obj) => obj is BoutTime other && Equals(other);

        public override int GetHashCode() => (Period * 397) ^ TickInPeriod;

        public override string ToString() => $"P{Period}+{TickInPeriod}t";
    }

    /// <summary>One line of the mat chairman's score sheet. Entries are immutable; corrections revoke and re-add.</summary>
    public abstract class ScoreSheetEntry
    {
        protected ScoreSheetEntry(int sequence, BoutTime time, Corner wrestler)
        {
            Sequence = sequence;
            Time = time;
            Wrestler = wrestler;
        }

        /// <summary>Order of recording, unique within a sheet. Decides "first" and "last" point.</summary>
        public int Sequence { get; }

        public BoutTime Time { get; }

        /// <summary>The wrestler the entry is recorded for (who scored, or who was cautioned/called/warned).</summary>
        public Corner Wrestler { get; }

        /// <summary>True when a challenge review removed this entry.</summary>
        public bool IsRevoked { get; private set; }

        internal void Revoke() => IsRevoked = true;
    }

    public sealed class PointsEntry : ScoreSheetEntry
    {
        internal PointsEntry(int sequence, BoutTime time, Corner scoredBy, int points, PointSource source)
            : base(sequence, time, scoredBy)
        {
            Points = points;
            Source = source;
        }

        public int Points { get; }

        public PointSource Source { get; }

        public bool IsTechnical => Source.IsTechnical();
    }

    /// <summary>A caution "O" against <see cref="ScoreSheetEntry.Wrestler"/>. Any points to the opponent are a separate <see cref="PointsEntry"/>.</summary>
    public sealed class CautionEntry : ScoreSheetEntry
    {
        internal CautionEntry(int sequence, BoutTime time, Corner against, CautionReason reason)
            : base(sequence, time, against)
        {
            Reason = reason;
        }

        public CautionReason Reason { get; }
    }

    /// <summary>A passivity designation "P" against <see cref="ScoreSheetEntry.Wrestler"/>.</summary>
    public sealed class PassivityEntry : ScoreSheetEntry
    {
        internal PassivityEntry(int sequence, BoutTime time, Corner against, int callNumber)
            : base(sequence, time, against)
        {
            CallNumber = callNumber;
        }

        /// <summary>1-based count of passivity calls in the bout, regardless of wrestler (Greco-Roman counts calls per bout).</summary>
        public int CallNumber { get; }
    }

    /// <summary>A verbal warning "V" against <see cref="ScoreSheetEntry.Wrestler"/>.</summary>
    public sealed class WarningEntry : ScoreSheetEntry
    {
        internal WarningEntry(int sequence, BoutTime time, Corner against, WarningReason reason)
            : base(sequence, time, against)
        {
            Reason = reason;
        }

        public WarningReason Reason { get; }
    }
}
