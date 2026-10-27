using System;

namespace Wrestling.Core.Rules
{
    /// <summary>Classification points for the winner and loser of a bout.</summary>
    public sealed class ClassificationPoints
    {
        public int Winner { get; init; }

        public int Loser { get; init; }

        internal void Validate(ValidationContext context, string path)
        {
            context.RequireRange(Winner, 0, 10, path + ".winner");
            context.RequireRange(Loser, 0, 10, path + ".loser");
            if (Loser > Winner)
            {
                context.Error(path + ": loser points must not exceed winner points.");
            }
        }
    }

    /// <summary>Classification points per result type (Art. 41).</summary>
    public sealed class ClassificationPointsTable
    {
        public ClassificationPoints Fall { get; init; } = new ClassificationPoints();

        public ClassificationPoints Injury { get; init; } = new ClassificationPoints();

        public ClassificationPoints ThreeCautions { get; init; } = new ClassificationPoints();

        public ClassificationPoints Forfeit { get; init; } = new ClassificationPoints();

        public ClassificationPoints Disqualification { get; init; } = new ClassificationPoints();

        public ClassificationPoints TechnicalSuperiority { get; init; } = new ClassificationPoints();

        public ClassificationPoints TechnicalSuperiorityLoserScored { get; init; } = new ClassificationPoints();

        public ClassificationPoints Points { get; init; } = new ClassificationPoints();

        public ClassificationPoints PointsLoserScored { get; init; } = new ClassificationPoints();

        /// <summary>Both wrestlers disqualified, injured or forfeited; <see cref="ClassificationPoints.Winner"/> applies to both.</summary>
        public ClassificationPoints DoubleLoss { get; init; } = new ClassificationPoints();

        /// <summary>Points for a bout with a winner. Whether the loser scored only matters for superiority and points wins.</summary>
        public ClassificationPoints For(VictoryType type, bool loserScoredTechnicalPoints) => type switch
        {
            VictoryType.Fall => Fall,
            VictoryType.Injury => Injury,
            VictoryType.ThreeCautions => ThreeCautions,
            VictoryType.Forfeit => Forfeit,
            VictoryType.Disqualification => Disqualification,
            VictoryType.TechnicalSuperiority => loserScoredTechnicalPoints ? TechnicalSuperiorityLoserScored : TechnicalSuperiority,
            VictoryType.Points => loserScoredTechnicalPoints ? PointsLoserScored : Points,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };

        internal void Validate(ValidationContext context)
        {
            const string path = "classificationPoints.";
            Fall.Validate(context, path + "fall");
            Injury.Validate(context, path + "injury");
            ThreeCautions.Validate(context, path + "threeCautions");
            Forfeit.Validate(context, path + "forfeit");
            Disqualification.Validate(context, path + "disqualification");
            TechnicalSuperiority.Validate(context, path + "technicalSuperiority");
            TechnicalSuperiorityLoserScored.Validate(context, path + "technicalSuperiorityLoserScored");
            Points.Validate(context, path + "points");
            PointsLoserScored.Validate(context, path + "pointsLoserScored");
            DoubleLoss.Validate(context, path + "doubleLoss");
        }
    }

    /// <summary>Official UWW result codes, as shown on result sheets.</summary>
    public static class ResultCodes
    {
        public static string For(VictoryType type, bool loserScoredTechnicalPoints) => type switch
        {
            VictoryType.Fall => "VFA",
            VictoryType.Injury => "VIN",
            VictoryType.ThreeCautions => "VCA",
            VictoryType.Forfeit => "VFO",
            VictoryType.Disqualification => "DSQ",
            VictoryType.TechnicalSuperiority => loserScoredTechnicalPoints ? "VSU1" : "VSU",
            VictoryType.Points => loserScoredTechnicalPoints ? "VPO1" : "VPO",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };

        public static string For(DoubleLossType type) => type switch
        {
            DoubleLossType.DoubleDisqualification => "2DSQ",
            DoubleLossType.DoubleInjury => "2VIN",
            DoubleLossType.DoubleForfeit => "2VFO",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }
}
