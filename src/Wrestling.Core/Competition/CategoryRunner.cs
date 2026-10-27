using System;
using System.Collections.Generic;
using System.Linq;
using Wrestling.Core.Bouts;

namespace Wrestling.Core.Competition
{
    /// <summary>
    /// Plays a whole weight category in any format: repeatedly takes the next ready bout and asks
    /// <paramref name="wrestle"/> for its result (the top participant is red), until standings are final.
    /// </summary>
    public static class CategoryRunner
    {
        /// <summary>Bouts are wrestled in schedule order; returns the final standings.</summary>
        public static IReadOnlyList<Placement> Run(ICompetitionFormat format, Func<BracketBout, BoutResult> wrestle)
        {
            if (format == null)
            {
                throw new ArgumentNullException(nameof(format));
            }

            if (wrestle == null)
            {
                throw new ArgumentNullException(nameof(wrestle));
            }

            int safety = format.Bouts.Count * 4 + 64;
            while (!format.IsComplete)
            {
                BracketBout? next = format.ReadyBouts().FirstOrDefault();
                if (next == null)
                {
                    throw new InvalidOperationException("The competition is not complete but no bout is ready.");
                }

                if (--safety < 0)
                {
                    throw new InvalidOperationException("The competition did not finish.");
                }

                format.RecordResult(next.Id, wrestle(next));
            }

            return format.Standings();
        }
    }
}
