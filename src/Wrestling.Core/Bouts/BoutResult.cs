using System;
using Wrestling.Core.Rules;

namespace Wrestling.Core.Bouts
{
    /// <summary>Final result of a bout with classification points (Art. 29, 41).</summary>
    public sealed class BoutResult
    {
        private BoutResult(
            Corner? winner,
            VictoryType? victoryType,
            DoubleLossType? doubleLoss,
            int redPoints,
            int bluePoints,
            int redClassificationPoints,
            int blueClassificationPoints,
            string code,
            TieBreakCriterion? decidedBy)
        {
            Winner = winner;
            VictoryType = victoryType;
            DoubleLoss = doubleLoss;
            RedPoints = redPoints;
            BluePoints = bluePoints;
            RedClassificationPoints = redClassificationPoints;
            BlueClassificationPoints = blueClassificationPoints;
            Code = code;
            DecidedBy = decidedBy;
        }

        /// <summary>Null only for double losses.</summary>
        public Corner? Winner { get; }

        public Corner? Loser => Winner?.Opponent();

        public VictoryType? VictoryType { get; }

        public DoubleLossType? DoubleLoss { get; }

        public int RedPoints { get; }

        public int BluePoints { get; }

        public int RedClassificationPoints { get; }

        public int BlueClassificationPoints { get; }

        /// <summary>UWW result code, for example "VSU1" or "2DSQ".</summary>
        public string Code { get; }

        /// <summary>For points wins on a level score: the tie-break criterion that decided it.</summary>
        public TieBreakCriterion? DecidedBy { get; }

        public int TechnicalPoints(Corner corner) => corner == Corner.Red ? RedPoints : BluePoints;

        public int ClassificationPoints(Corner corner) => corner == Corner.Red ? RedClassificationPoints : BlueClassificationPoints;

        /// <summary>Result at the end of regular time: the points leader, or the tie-break winner.</summary>
        public static BoutResult AtEndOfRegularTime(BoutScoreSheet sheet, Ruleset rules)
        {
            if (sheet == null)
            {
                throw new ArgumentNullException(nameof(sheet));
            }

            TieBreakCriterion? criterion = null;
            Corner? winner = sheet.Leader;
            if (winner == null)
            {
                winner = TieBreak.Decide(sheet, out criterion);
            }

            if (winner == null)
            {
                // The passivity procedure always produces points before a bout can end level and scoreless.
                throw new InvalidOperationException("A bout cannot end 0–0 without any scoring entry; the passivity procedure must have produced a point.");
            }

            return Create(winner.Value, Rules.VictoryType.Points, sheet, rules, criterion);
        }

        /// <summary>A bout ended early: fall, injury, three cautions, technical superiority, forfeit or disqualification.</summary>
        public static BoutResult Win(Corner winner, VictoryType type, BoutScoreSheet sheet, Ruleset rules)
        {
            if (sheet == null)
            {
                throw new ArgumentNullException(nameof(sheet));
            }

            if (type == Rules.VictoryType.Points)
            {
                throw new ArgumentException("Use AtEndOfRegularTime for points wins.", nameof(type));
            }

            return Create(winner, type, sheet, rules, null);
        }

        public static BoutResult DoubleLossResult(DoubleLossType type, BoutScoreSheet sheet, Ruleset rules)
        {
            if (sheet == null)
            {
                throw new ArgumentNullException(nameof(sheet));
            }

            int points = rules.ClassificationPoints.DoubleLoss.Winner;
            return new BoutResult(null, null, type, sheet.Points(Corner.Red), sheet.Points(Corner.Blue), points, points, ResultCodes.For(type), null);
        }

        private static BoutResult Create(Corner winner, VictoryType type, BoutScoreSheet sheet, Ruleset rules, TieBreakCriterion? criterion)
        {
            int red = sheet.Points(Corner.Red);
            int blue = sheet.Points(Corner.Blue);
            bool loserScored = (winner == Corner.Red ? blue : red) > 0;
            ClassificationPoints points = rules.ClassificationPoints.For(type, loserScored);
            int redClassification = winner == Corner.Red ? points.Winner : points.Loser;
            int blueClassification = winner == Corner.Blue ? points.Winner : points.Loser;
            return new BoutResult(winner, type, null, red, blue, redClassification, blueClassification, ResultCodes.For(type, loserScored), criterion);
        }
    }

    /// <summary>Checks for results that end a bout before regular time.</summary>
    public static class BoutEndConditions
    {
        /// <summary>The wrestler with a technical superiority lead (8 GR, 10 FS/WW), or null. The bout ends once wrestlers return to neutral.</summary>
        public static Corner? TechnicalSuperiority(BoutScoreSheet sheet, Ruleset rules, WrestlingStyle style) =>
            sheet.LeadingBy(rules.TechnicalSuperiority.For(style));

        /// <summary>The wrestler who has lost by reaching the caution limit (3), or null.</summary>
        public static Corner? LostByCautions(BoutScoreSheet sheet, Ruleset rules)
        {
            int limit = rules.Procedure.CautionsForLoss;
            if (sheet.Cautions(Corner.Red) >= limit)
            {
                return Corner.Red;
            }

            return sheet.Cautions(Corner.Blue) >= limit ? Corner.Blue : (Corner?)null;
        }
    }
}
