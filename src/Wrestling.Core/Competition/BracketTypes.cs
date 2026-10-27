using System;
using Wrestling.Core.Bouts;

namespace Wrestling.Core.Competition
{
    /// <summary>A wrestler entered in a weight category, with the draw number and optional seed.</summary>
    public sealed class Entrant
    {
        public Entrant(string id, int drawNumber, int? seed = null)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Entrant id must not be empty.", nameof(id));
            }

            if (drawNumber < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(drawNumber), "Draw numbers start at 1.");
            }

            if (seed.HasValue && seed.Value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(seed), "Seeds start at 1.");
            }

            Id = id;
            DrawNumber = drawNumber;
            Seed = seed;
        }

        public string Id { get; }

        public int DrawNumber { get; }

        public int? Seed { get; }

        public override string ToString() => Seed.HasValue ? $"{Id} (#{DrawNumber}, seed {Seed})" : $"{Id} (#{DrawNumber})";
    }

    public enum EntrantStatus
    {
        /// <summary>Still able to wrestle.</summary>
        Active,

        /// <summary>Out through injury: wrestles no further bouts but is ranked on points so far (Art. 55).</summary>
        Injured,

        /// <summary>Forfeit, failed weigh-in or disqualification: ranked last without rank (Art. 11, 14).</summary>
        Unranked,
    }

    public enum BracketStage
    {
        Qualification,
        Main,
        Repechage,
    }

    /// <summary>Where a bout participant comes from.</summary>
    internal sealed class SlotSource
    {
        private SlotSource(string? entrantId, int? winnerOfBoutId)
        {
            EntrantId = entrantId;
            WinnerOfBoutId = winnerOfBoutId;
        }

        public string? EntrantId { get; }

        public int? WinnerOfBoutId { get; }

        public static SlotSource Entrant(string id) => new SlotSource(id, null);

        public static SlotSource WinnerOf(int boutId) => new SlotSource(null, boutId);
    }

    /// <summary>One bout in a bracket. The top participant wrestles in red, the bottom in blue.</summary>
    public sealed class BracketBout
    {
        internal BracketBout(int id, BracketStage stage, int round, string name, SlotSource top, SlotSource bottom)
        {
            Id = id;
            Stage = stage;
            Round = round;
            Name = name;
            TopSource = top;
            BottomSource = bottom;
        }

        public int Id { get; }

        public BracketStage Stage { get; }

        /// <summary>Order within the stage, starting at 1.</summary>
        public int Round { get; }

        /// <summary>Display name, for example "1/8 Final", "Repechage" or "Bronze".</summary>
        public string Name { get; }

        /// <summary>Red corner, once known.</summary>
        public string? Top { get; internal set; }

        /// <summary>Blue corner, once known.</summary>
        public string? Bottom { get; internal set; }

        /// <summary>True when the top position will never be filled (its feeder bout had no winner).</summary>
        public bool TopVacant { get; internal set; }

        public bool BottomVacant { get; internal set; }

        public bool IsDecided { get; internal set; }

        /// <summary>Null when decided without a winner (double loss) or not yet decided.</summary>
        public string? Winner { get; internal set; }

        public string? Loser { get; internal set; }

        /// <summary>The bout result; null when the bout never took place because neither side was available.</summary>
        public BoutResult? Result { get; internal set; }

        /// <summary>True when both participants are known and the bout still has to be wrestled.</summary>
        public bool IsReadyToWrestle => !IsDecided && Top != null && Bottom != null;

        internal SlotSource TopSource { get; }

        internal SlotSource BottomSource { get; }

        internal bool IsResolved(bool top) => top ? Top != null || TopVacant : Bottom != null || BottomVacant;

        public bool Involves(string entrantId) => Top == entrantId || Bottom == entrantId;

        public override string ToString() => $"#{Id} {Name}: {Top ?? (TopVacant ? "-" : "?")} vs {Bottom ?? (BottomVacant ? "-" : "?")}";
    }

    /// <summary>A final position in the weight category. <see cref="Rank"/> is null for unranked entrants.</summary>
    public sealed class Placement
    {
        public Placement(string entrantId, int? rank)
        {
            EntrantId = entrantId;
            Rank = rank;
        }

        public string EntrantId { get; }

        public int? Rank { get; }

        public override string ToString() => $"{Rank?.ToString() ?? "-"}: {EntrantId}";
    }
}
