using Wrestling.Core.Bouts;
using Wrestling.Core.Rules;

namespace Wrestling.Core.Tests.Bouts;

/// <summary>Writes score sheets in the order events happen, one tick apart.</summary>
internal sealed class SheetBuilder
{
    private int _tick;

    public BoutScoreSheet Sheet { get; } = new BoutScoreSheet();

    public SheetBuilder Hold(Corner corner, int points) => Add(corner, points, PointSource.Hold);

    public SheetBuilder StepOut(Corner corner) => Add(corner, 1, PointSource.StepOut);

    public SheetBuilder Passivity(Corner corner) => Add(corner, 1, PointSource.Passivity);

    public SheetBuilder LostChallengeBy(Corner loser) => Add(loser.Opponent(), 1, PointSource.LostChallenge);

    /// <summary>A caution against <paramref name="against"/> with points to the opponent.</summary>
    public SheetBuilder Caution(Corner against, int pointsToOpponent = 1)
    {
        Sheet.GiveCaution(against, CautionReason.FleeingHold, Next());
        if (pointsToOpponent > 0)
        {
            Sheet.AwardPoints(against.Opponent(), pointsToOpponent, PointSource.Infringement, Next());
        }

        return this;
    }

    /// <summary>A caution without an accompanying point entry (used to reproduce rulebook tables).</summary>
    public SheetBuilder CautionOnly(Corner against)
    {
        Sheet.GiveCaution(against, CautionReason.FleeingHold, Next());
        return this;
    }

    private SheetBuilder Add(Corner corner, int points, PointSource source)
    {
        Sheet.AwardPoints(corner, points, source, Next());
        return this;
    }

    private BoutTime Next() => new BoutTime(1, _tick++);
}
