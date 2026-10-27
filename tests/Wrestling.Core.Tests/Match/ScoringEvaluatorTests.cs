using Wrestling.Core.Bouts;
using Wrestling.Core.Match;
using Wrestling.Core.Rules;
using static Wrestling.Core.Rules.Corner;

namespace Wrestling.Core.Tests.Match;

/// <summary>Article 38 values (UWW rules, January 2026), one situation per test case.</summary>
public class ScoringEvaluatorTests
{
    [Fact]
    public void GrandAmplitudeFromStandingIntoDirectDanger_Is5()
    {
        AssertSingle(Evaluate(new ActionFacts { Executor = Red, Completed = true, OpponentLifted = true, GrandAmplitude = true, Landing = Landing.DirectDanger }),
            Red, 5, ScoringReason.GrandAmplitudeToDirectDanger);
    }

    [Fact]
    public void GrandAmplitudeFromParTerreIntoDirectDanger_Is5()
    {
        AssertSingle(Evaluate(new ActionFacts { Executor = Blue, OpponentGroundedAtStart = true, Completed = true, OpponentLifted = true, GrandAmplitude = true, Landing = Landing.DirectDanger }),
            Blue, 5, ScoringReason.GrandAmplitudeToDirectDanger);
    }

    [Theory]
    [InlineData(Landing.Stomach)]
    [InlineData(Landing.Flank)]
    [InlineData(Landing.DelayedDanger)]
    public void GrandAmplitudeWithoutDirectDanger_Is4(Landing landing)
    {
        AssertSingle(Evaluate(new ActionFacts { Executor = Red, Completed = true, OpponentLifted = true, GrandAmplitude = true, Landing = landing }),
            Red, 4, ScoringReason.GrandAmplitude);
    }

    [Fact]
    public void ShortAmplitudeLiftIntoDanger_Is4_EvenFromTheKnees()
    {
        AssertSingle(Evaluate(new ActionFacts { Executor = Red, OpponentGroundedAtStart = true, Completed = true, OpponentLifted = true, Landing = Landing.DelayedDanger }),
            Red, 4, ScoringReason.LiftToDanger);
    }

    [Fact]
    public void CompleteLiftLandingOnChestOrArms_Is4()
    {
        AssertSingle(Evaluate(new ActionFacts { Executor = Red, Completed = true, OpponentLifted = true, Landing = Landing.ChestOrArms }),
            Red, 4, ScoringReason.LiftToChestOrArms);
    }

    [Fact]
    public void StandingHoldIntoDangerInOneContinuousAction_Is4()
    {
        AssertSingle(Evaluate(new ActionFacts { Executor = Red, Completed = true, ContinuousToDanger = true, Landing = Landing.DelayedDanger }),
            Red, 4, ScoringReason.StandingHoldToDanger);
        AssertSingle(Evaluate(new ActionFacts { Executor = Red, Completed = true, Landing = Landing.DirectDanger }),
            Red, 4, ScoringReason.StandingHoldToDanger);
    }

    [Fact]
    public void InstantaneousFallFromStandingWithControl_Is4()
    {
        AssertSingle(Evaluate(new ActionFacts { Executor = Blue, Completed = true, InstantaneousFall = true }),
            Blue, 4, ScoringReason.InstantaneousFall);
    }

    [Theory]
    [InlineData(Landing.Stomach)]
    [InlineData(Landing.Flank)]
    public void ThrowToStomachOrFlank_Is2(Landing landing)
    {
        AssertSingle(Evaluate(new ActionFacts { Executor = Red, Completed = true, Landing = landing }), Red, 2, ScoringReason.Throw);
    }

    [Fact]
    public void Takedown_Is2()
    {
        AssertSingle(Evaluate(new ActionFacts { Executor = Red, Completed = true, TakedownBehind = true }), Red, 2, ScoringReason.Takedown);
    }

    [Fact]
    public void SameHoldIsWorthMoreStandingThanInParTerre()
    {
        ActionScore standing = Evaluate(new ActionFacts { Executor = Red, Completed = true, ContinuousToDanger = true, Landing = Landing.DelayedDanger });
        ActionScore parTerre = Evaluate(new ActionFacts { Executor = Red, OpponentGroundedAtStart = true, Completed = true, ContinuousToDanger = true, Landing = Landing.DelayedDanger });
        Assert.Equal(4, standing.Awards.Single().Points);
        Assert.Equal(2, parTerre.Awards.Single().Points);
    }

    [Fact]
    public void ConsecutiveGutWrenchesEachScore2()
    {
        ActionScore score = Evaluate(new ActionFacts { Executor = Red, OpponentGroundedAtStart = true, Completed = true, Exposures = 3 });
        Assert.Equal(3, score.Awards.Count);
        Assert.All(score.Awards, a => Assert.Equal((Red, 2, ScoringReason.Exposure), (a.To, a.Points, a.Reason)));
    }

    [Fact]
    public void TakedownFollowedByExposure_ScoresBoth()
    {
        ActionScore score = Evaluate(new ActionFacts { Executor = Red, Completed = true, TakedownBehind = true, Exposures = 1 });
        Assert.Equal(new[] { 2, 2 }, score.Awards.Select(a => a.Points));
    }

    [Fact]
    public void Reversal_Is1()
    {
        AssertSingle(Evaluate(new ActionFacts { Executor = Blue, Completed = true, Reversal = true }), Blue, 1, ScoringReason.Reversal);
    }

    [Fact]
    public void BlockedAndHeldInDanger_OpponentScores2()
    {
        AssertSingle(Evaluate(new ActionFacts { Executor = Red, ExecutorBlockedInDanger = true }), Blue, 2, ScoringReason.BlockedInDanger);
    }

    [Fact]
    public void RiskRule_FailedAttemptUnderneathGivesNothingAndRestartsStanding()
    {
        ActionScore score = Evaluate(new ActionFacts { Executor = Red, FailedAndEndedUnderneath = true });
        Assert.Empty(score.Awards);
        Assert.Equal(RestartPosition.StandingCentre, score.Restart);
    }

    [Fact]
    public void AttackerSteppingOutFirstWithoutCompleting_GivesOpponent1()
    {
        ActionScore score = Evaluate(new ActionFacts { Executor = Red, ExecutorSteppedOutFirst = true });
        AssertSingle(score, Blue, 1, ScoringReason.FailedAttackStepOut);
        Assert.Equal(PointSource.StepOut, score.Awards[0].Source);
    }

    [Fact]
    public void LiftedAndControlledButNotCompleted_NoPoints()
    {
        ActionScore score = Evaluate(new ActionFacts { Executor = Red, ExecutorSteppedOutFirst = true, LiftedAndControlledWithoutCompleting = true });
        Assert.Empty(score.Awards);
        Assert.Equal(RestartPosition.StandingCentre, score.Restart);
    }

    [Fact]
    public void FailedAttemptUnderneathInProtectionArea_CostsOnePoint()
    {
        AssertSingle(Evaluate(new ActionFacts { Executor = Red, FailedAndEndedUnderneath = true, EndedInProtectionArea = true }), Blue, 1, ScoringReason.FailedAttackStepOut);
    }

    [Fact]
    public void FailedAttemptOnBackInProtectionArea_CostsTwoPoints()
    {
        AssertSingle(Evaluate(new ActionFacts { Executor = Red, EndedInProtectionArea = true, ExecutorOnBackUnderControl = true }), Blue, 2, ScoringReason.FailedAttackOnBackInProtectionArea);
    }

    [Fact]
    public void CompletedHoldEndingInProtectionArea_ScoresAndRestartsInCentre()
    {
        ActionScore score = Evaluate(new ActionFacts { Executor = Red, Completed = true, Landing = Landing.Flank, EndedInProtectionArea = true });
        AssertSingle(score, Red, 2, ScoringReason.Throw);
        Assert.Equal(RestartPosition.StandingCentre, score.Restart);
    }

    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 0)] // deliberate push-out without action: verbal warning only
    [InlineData(true, true, 1)]  // ...unless the defender left on the knees
    public void StepOut(bool deliberate, bool grounded, int points)
    {
        ActionScore score = ScoringEvaluator.StepOut(Red, deliberate, grounded, Rules.Current);
        Assert.Equal(points, score.Awards.Sum(a => a.Points));
        Assert.All(score.Awards, a => Assert.Equal(Blue, a.To));
        Assert.Equal(RestartPosition.StandingCentre, score.Restart);
    }

    [Fact]
    public void RejectsImpossibleFacts()
    {
        Assert.Throws<ArgumentException>(() => Evaluate(new ActionFacts { Executor = Red, Completed = true, GrandAmplitude = true, Landing = Landing.DirectDanger }));
        Assert.Throws<ArgumentException>(() => Evaluate(new ActionFacts { Executor = Red, Completed = true }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Evaluate(new ActionFacts { Executor = Red, Completed = true, Exposures = -1 }));
    }

    private static ActionScore Evaluate(ActionFacts facts) => ScoringEvaluator.Evaluate(facts, Rules.Current);

    private static void AssertSingle(ActionScore score, Corner to, int points, ScoringReason reason)
    {
        PointAward award = Assert.Single(score.Awards);
        Assert.Equal((to, points, reason), (award.To, award.Points, award.Reason));
    }
}
