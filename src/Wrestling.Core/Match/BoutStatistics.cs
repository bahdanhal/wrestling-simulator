using System;
using System.Collections.Generic;
using System.Linq;
using Wrestling.Core.Bouts;
using Wrestling.Core.Rules;
using Wrestling.Core.Techniques;
using Wrestling.Core.Wrestlers;

namespace Wrestling.Core.Match
{
    /// <summary>One wrestler's numbers from a bout (design section 5.14).</summary>
    public sealed class WrestlerBoutStatistics
    {
        internal WrestlerBoutStatistics(Corner corner)
        {
            Corner = corner;
        }

        public Corner Corner { get; }

        public int Points { get; internal set; }

        public int Attempts { get; internal set; }

        /// <summary>Attacks completed with a scoring or controlling end state.</summary>
        public int Successes { get; internal set; }

        public int CountersStarted { get; internal set; }

        public int TimesCountered { get; internal set; }

        public int StepOutsConceded { get; internal set; }

        public int PassivityCallsAgainst { get; internal set; }

        public int Cautions { get; internal set; }

        public int TimesInDanger { get; internal set; }

        public Dictionary<TechniqueFamily, int> AttemptsByFamily { get; } = new Dictionary<TechniqueFamily, int>();

        public Dictionary<TechniqueFamily, int> SuccessesByFamily { get; } = new Dictionary<TechniqueFamily, int>();

        /// <summary>Points by how they were scored (scoring reasons and non-technical sources).</summary>
        public Dictionary<string, int> PointsByReason { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Successful attacks as a share of attempts, 0–1.</summary>
        public double SuccessRate => Attempts == 0 ? 0 : (double)Successes / Attempts;
    }

    /// <summary>
    /// Post-bout statistics derived from the event log and score sheet, for the result screen,
    /// commentary summaries and balancing. Uses doubles only for display ratios, never in the simulation.
    /// </summary>
    public sealed class BoutStatistics
    {
        private BoutStatistics(BoutResult result, WrestlerBoutStatistics red, WrestlerBoutStatistics blue, IReadOnlyDictionary<BoutPosition, int> positionSeconds, int wrestlingSeconds)
        {
            Result = result;
            Red = red;
            Blue = blue;
            PositionSeconds = positionSeconds;
            WrestlingSeconds = wrestlingSeconds;
        }

        public BoutResult Result { get; }

        public WrestlerBoutStatistics Red { get; }

        public WrestlerBoutStatistics Blue { get; }

        /// <summary>Wrestling seconds spent in each position.</summary>
        public IReadOnlyDictionary<BoutPosition, int> PositionSeconds { get; }

        public int WrestlingSeconds { get; }

        public WrestlerBoutStatistics For(Corner corner) => corner == Corner.Red ? Red : Blue;

        public static BoutStatistics From(BoutEngine engine)
        {
            if (engine == null)
            {
                throw new ArgumentNullException(nameof(engine));
            }

            if (!engine.IsFinished)
            {
                throw new InvalidOperationException("Statistics are available once the bout is finished.");
            }

            var red = new WrestlerBoutStatistics(Corner.Red);
            var blue = new WrestlerBoutStatistics(Corner.Blue);
            WrestlerBoutStatistics Of(Corner corner) => corner == Corner.Red ? red : blue;
            var attackInProgress = new Dictionary<Corner, TechniqueDefinition>();

            foreach (BoutEvent e in engine.Events)
            {
                if (e.Wrestler == null)
                {
                    continue;
                }

                Corner corner = e.Wrestler.Value;
                WrestlerBoutStatistics stats = Of(corner);
                switch (e.Type)
                {
                    case BoutEventType.TechniqueStarted:
                        TechniqueDefinition started = engine.Catalog.Get(e.TechniqueId!);
                        attackInProgress[corner] = started;
                        stats.Attempts++;
                        Increment(stats.AttemptsByFamily, started.Family);
                        break;
                    case BoutEventType.CounterStarted:
                        stats.CountersStarted++;
                        Of(corner.Opponent()).TimesCountered++;
                        break;
                    case BoutEventType.TechniqueCompleted:
                        if (attackInProgress.TryGetValue(corner, out TechniqueDefinition? completed) && completed.Id == e.TechniqueId && IsSuccess(e.Detail))
                        {
                            stats.Successes++;
                            Increment(stats.SuccessesByFamily, completed.Family);
                        }

                        attackInProgress.Remove(corner);
                        break;
                    case BoutEventType.TechniqueFailed:
                        attackInProgress.Remove(corner);
                        break;
                    case BoutEventType.PointsAwarded:
                        stats.PointsByReason[e.Detail] = stats.PointsByReason.TryGetValue(e.Detail, out int points) ? points + e.Points : e.Points;
                        break;
                    case BoutEventType.StepOut:
                        stats.StepOutsConceded++;
                        break;
                    case BoutEventType.DangerPosition:
                        stats.TimesInDanger++;
                        break;
                }
            }

            foreach (Corner corner in new[] { Corner.Red, Corner.Blue })
            {
                WrestlerBoutStatistics stats = Of(corner);
                stats.Points = engine.Sheet.Points(corner);
                stats.Cautions = engine.Sheet.Cautions(corner);
                stats.PassivityCallsAgainst = engine.Sheet.PassivityCallsAgainst(corner);
            }

            var positions = new Dictionary<BoutPosition, int>();
            foreach (BoutPosition position in (BoutPosition[])Enum.GetValues(typeof(BoutPosition)))
            {
                positions[position] = engine.PositionTicks(position) / BoutClock.TicksPerSecond;
            }

            return new BoutStatistics(engine.Result!, red, blue, positions, engine.Clock.ElapsedTicks / BoutClock.TicksPerSecond);
        }

        private static bool IsSuccess(string endState) =>
            Enum.TryParse(endState, out EndState state) && state != EndState.Neutral && state != EndState.Scramble;

        private static void Increment(Dictionary<TechniqueFamily, int> counts, TechniqueFamily? family)
        {
            if (family.HasValue)
            {
                counts[family.Value] = counts.TryGetValue(family.Value, out int count) ? count + 1 : 1;
            }
        }
    }
}
