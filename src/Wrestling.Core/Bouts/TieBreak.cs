using Wrestling.Core.Rules;

namespace Wrestling.Core.Bouts
{
    /// <summary>The criterion that decided a tied bout.</summary>
    public enum TieBreakCriterion
    {
        HighestValueHold,
        MostHighestValueHolds,
        FewestCautions,
        LastTechnicalPoint,
        OnlyTechnicalPoint,
        FirstPoint,
    }

    /// <summary>
    /// Tie-break for bouts level on points (Art. 29), applied in order:
    /// 1. the hold of the highest value; 2. the most holds of that value; 3. the fewest cautions;
    /// 4. the last technical point. Criteria 1 and 2 compare every point award by its value
    /// (the rulebook resolves "passivity point vs technical point" at criterion 4, so passivity
    /// points must count as 1-point actions earlier). For criterion 4 the rulebook's 1–1 cases
    /// are applied to every level score:
    /// both wrestlers with technical points → the last technical scorer wins;
    /// only one with technical points → that wrestler wins;
    /// no technical points at all → the first scorer wins.
    /// </summary>
    public static class TieBreak
    {
        public static Corner? Decide(BoutScoreSheet sheet) => Decide(sheet, out _);

        public static Corner? Decide(BoutScoreSheet sheet, out TieBreakCriterion? criterion)
        {
            criterion = null;
            if (sheet.Points(Corner.Red) != sheet.Points(Corner.Blue))
            {
                throw new System.InvalidOperationException("Tie-break applies only to bouts level on points.");
            }

            HoldSummary red = Summarize(sheet, Corner.Red);
            HoldSummary blue = Summarize(sheet, Corner.Blue);

            // 1. The action/hold of the highest value.
            if (red.HighestValue != blue.HighestValue)
            {
                criterion = TieBreakCriterion.HighestValueHold;
                return red.HighestValue > blue.HighestValue ? Corner.Red : Corner.Blue;
            }

            // 2. The most actions/holds of the highest value.
            if (red.HighestValueCount != blue.HighestValueCount)
            {
                criterion = TieBreakCriterion.MostHighestValueHolds;
                return red.HighestValueCount > blue.HighestValueCount ? Corner.Red : Corner.Blue;
            }

            // 3. The fewest cautions.
            int redCautions = sheet.Cautions(Corner.Red);
            int blueCautions = sheet.Cautions(Corner.Blue);
            if (redCautions != blueCautions)
            {
                criterion = TieBreakCriterion.FewestCautions;
                return redCautions < blueCautions ? Corner.Red : Corner.Blue;
            }

            // 4. The last technical point(s), with the 1–1 rules generalised.
            PointsEntry? lastTechnical = null;
            PointsEntry? first = null;
            bool redTechnical = false;
            bool blueTechnical = false;
            foreach (PointsEntry entry in sheet.ActivePoints())
            {
                first ??= entry;
                if (entry.IsTechnical)
                {
                    lastTechnical = entry;
                    if (entry.Wrestler == Corner.Red)
                    {
                        redTechnical = true;
                    }
                    else
                    {
                        blueTechnical = true;
                    }
                }
            }

            if (redTechnical && blueTechnical)
            {
                criterion = TieBreakCriterion.LastTechnicalPoint;
                return lastTechnical!.Wrestler;
            }

            if (redTechnical || blueTechnical)
            {
                criterion = TieBreakCriterion.OnlyTechnicalPoint;
                return redTechnical ? Corner.Red : Corner.Blue;
            }

            if (first != null)
            {
                criterion = TieBreakCriterion.FirstPoint;
                return first.Wrestler;
            }

            return null;
        }

        private static HoldSummary Summarize(BoutScoreSheet sheet, Corner corner)
        {
            int highest = 0;
            int count = 0;
            foreach (PointsEntry entry in sheet.ActivePoints())
            {
                if (entry.Wrestler != corner)
                {
                    continue;
                }

                if (entry.Points > highest)
                {
                    highest = entry.Points;
                    count = 1;
                }
                else if (entry.Points == highest)
                {
                    count++;
                }
            }

            return new HoldSummary(highest, count);
        }

        private readonly struct HoldSummary
        {
            public HoldSummary(int highestValue, int highestValueCount)
            {
                HighestValue = highestValue;
                HighestValueCount = highestValueCount;
            }

            public int HighestValue { get; }

            public int HighestValueCount { get; }
        }
    }
}
