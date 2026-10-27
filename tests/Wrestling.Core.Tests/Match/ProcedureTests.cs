using Wrestling.Core.Bouts;
using Wrestling.Core.Match;
using Wrestling.Core.Rules;
using static Wrestling.Core.Rules.Corner;

namespace Wrestling.Core.Tests.Match;

public class ProcedureTests
{
    private static (BoutClock Clock, BoutScoreSheet Sheet, ActivityClockProcedure Procedure) Freestyle(AgeCategory age = AgeCategory.Senior)
    {
        var clock = new BoutClock(Rules.Current.BoutDuration.For(age));
        clock.Start();
        return (clock, new BoutScoreSheet(), new ActivityClockProcedure(Rules.Current, age));
    }

    private static void Run(BoutClock clock, int seconds, BoutScoreSheet sheet, ActivityClockProcedure procedure, Corner lessActive = Blue)
    {
        for (int i = 0; i < seconds * BoutClock.TicksPerSecond; i++)
        {
            clock.Tick();
            procedure.Update(sheet, clock, actionInProgress: false, lessActive);
        }
    }

    [Fact]
    public void Senior_WarningThenActivityPeriodThenPoint()
    {
        (BoutClock clock, BoutScoreSheet sheet, ActivityClockProcedure procedure) = Freestyle();
        sheet.AwardPoints(Red, 2, PointSource.Hold, clock.Now); // not scoreless, no forced designation

        Assert.Equal(PassivityDecision.VerbalWarning, procedure.ReportInactivity(Blue, sheet, clock, false));
        Assert.Equal(1, sheet.Warnings(Blue, WarningReason.Passivity));
        Assert.Equal(PassivityDecision.ActivityPeriodStarted, procedure.ReportInactivity(Blue, sheet, clock, false));
        Assert.Equal(Blue, procedure.Passive);
        Assert.Equal(1, sheet.PassivityCallsAgainst(Blue));

        Run(clock, 29, sheet, procedure);
        Assert.Equal(2, sheet.Points(Red));
        Run(clock, 1, sheet, procedure);
        Assert.Equal(3, sheet.Points(Red));
        Assert.Null(procedure.Passive);
    }

    [Fact]
    public void PassiveWrestlerScoring_EndsTheActivityPeriodWithoutAPoint()
    {
        (BoutClock clock, BoutScoreSheet sheet, ActivityClockProcedure procedure) = Freestyle();
        sheet.AwardPoints(Red, 2, PointSource.Hold, clock.Now);
        procedure.ReportInactivity(Blue, sheet, clock, false);
        procedure.ReportInactivity(Blue, sheet, clock, false);

        Run(clock, 10, sheet, procedure);
        sheet.AwardPoints(Blue, 1, PointSource.StepOut, clock.Now);
        Run(clock, 30, sheet, procedure);

        Assert.Equal(2, sheet.Points(Red));
        Assert.Null(procedure.Passive);
    }

    [Fact]
    public void OpponentScoringDoesNotStopThePoint()
    {
        (BoutClock clock, BoutScoreSheet sheet, ActivityClockProcedure procedure) = Freestyle();
        sheet.AwardPoints(Red, 2, PointSource.Hold, clock.Now);
        procedure.ReportInactivity(Blue, sheet, clock, false);
        procedure.ReportInactivity(Blue, sheet, clock, false);
        sheet.AwardPoints(Red, 2, PointSource.Hold, clock.Now);

        Run(clock, 30, sheet, procedure);
        Assert.Equal(5, sheet.Points(Red));
    }

    [Fact]
    public void ExpiryWaitsForTheActionToFinish()
    {
        (BoutClock clock, BoutScoreSheet sheet, ActivityClockProcedure procedure) = Freestyle();
        sheet.AwardPoints(Red, 1, PointSource.StepOut, clock.Now);
        procedure.ReportInactivity(Blue, sheet, clock, false);
        procedure.ReportInactivity(Blue, sheet, clock, false);
        Run(clock, 29, sheet, procedure);

        for (int i = 0; i < 3 * BoutClock.TicksPerSecond; i++)
        {
            clock.Tick();
            Assert.Null(procedure.Update(sheet, clock, actionInProgress: true, Blue));
        }

        // The action ends with the passive wrestler scoring: no activity point.
        sheet.AwardPoints(Blue, 2, PointSource.Hold, clock.Now);
        Assert.Null(procedure.Update(sheet, clock, actionInProgress: false, Blue));
        Assert.Equal(1, sheet.Points(Red));
    }

    [Fact]
    public void ScorelessAfterTwoMinutes_ForcesDesignationOfTheLessActiveWrestler()
    {
        (BoutClock clock, BoutScoreSheet sheet, ActivityClockProcedure procedure) = Freestyle();
        Run(clock, 119, sheet, procedure, lessActive: Red);
        Assert.Null(procedure.Passive);
        Run(clock, 1, sheet, procedure, lessActive: Red);
        Assert.Equal(Red, procedure.Passive);
        Assert.True(procedure.ScorelessDesignationDone);

        Run(clock, 30, sheet, procedure, lessActive: Red);
        Assert.Equal(1, sheet.Points(Blue));
        Assert.Equal(PointSource.ActivityClock, sheet.ActivePoints().Single().Source);
    }

    [Fact]
    public void Youth_NoVerbalWarning_AndDesignationAtOneMinute()
    {
        (BoutClock clock, BoutScoreSheet sheet, ActivityClockProcedure procedure) = Freestyle(AgeCategory.U17);
        Run(clock, 60, sheet, procedure, lessActive: Blue);
        Assert.Equal(Blue, procedure.Passive);

        (BoutClock clock2, BoutScoreSheet sheet2, ActivityClockProcedure procedure2) = Freestyle(AgeCategory.U15);
        sheet2.AwardPoints(Red, 1, PointSource.StepOut, clock2.Now);
        Assert.Equal(PassivityDecision.ActivityPeriodStarted, procedure2.ReportInactivity(Red, sheet2, clock2, false));
    }

    [Fact]
    public void ActivityPeriodLapsesAtTheEndOfThePeriod()
    {
        (BoutClock clock, BoutScoreSheet sheet, ActivityClockProcedure procedure) = Freestyle();
        sheet.AwardPoints(Red, 1, PointSource.StepOut, clock.Now);
        Run(clock, 165, sheet, procedure);
        procedure.ReportInactivity(Blue, sheet, clock, false);
        procedure.ReportInactivity(Blue, sheet, clock, false);
        Run(clock, 15, sheet, procedure);

        Assert.Equal(ClockPhase.Break, clock.Phase);
        Assert.Null(procedure.Passive);
        Assert.Equal(1, sheet.Points(Red));
    }

    [Fact]
    public void FinalSecondsEvasion_OnlyInsideTheWindow()
    {
        (BoutClock clock, BoutScoreSheet sheet, ActivityClockProcedure procedure) = Freestyle();
        sheet.AwardPoints(Blue, 1, PointSource.StepOut, clock.Now); // not scoreless: no forced designation
        Run(clock, 60, sheet, procedure);
        Assert.False(procedure.PenaliseFinalSecondsEvasion(Blue, sheet, clock));
        Assert.Equal(0, sheet.Cautions(Blue));

        Run(clock, 100, sheet, procedure);
        Assert.True(procedure.PenaliseFinalSecondsEvasion(Blue, sheet, clock));
        Assert.Equal(1, sheet.Cautions(Blue));
        Assert.Equal(1, sheet.Points(Red));
    }

    [Fact]
    public void Greco_FirstTwoCallsScore_ThirdOnlyGivesTheChoice()
    {
        var sheet = new BoutScoreSheet();
        GrecoPassivityCall first = GrecoPassivityProcedure.Call(Blue, sheet, new BoutTime(1, 10), Rules.Current);
        GrecoPassivityCall second = GrecoPassivityProcedure.Call(Red, sheet, new BoutTime(1, 20), Rules.Current);
        GrecoPassivityCall third = GrecoPassivityProcedure.Call(Blue, sheet, new BoutTime(2, 5), Rules.Current);

        Assert.Equal((Red, 1), (first.Point!.Wrestler, first.Point.Points));
        Assert.Equal((Blue, 1), (second.Point!.Wrestler, second.Point.Points));
        Assert.Null(third.Point);
        Assert.True(third.ActiveChoosesPosition);
        Assert.Equal(3, third.Entry.CallNumber);
    }

    [Fact]
    public void Challenge_LostChallengeGivesOpponentAPointAndIsUsedUp()
    {
        var challenge = new ChallengeProcedure(Rules.Current);
        var sheet = new BoutScoreSheet();

        Assert.Equal(ChallengeRefusal.None, challenge.CanRequest(Red, BoutClock.SecondsToTicks(5), false, false, false, false));
        PointsEntry? point = challenge.Resolve(Red, decisionChanged: false, sheet, new BoutTime(1, 100));

        Assert.Equal((Blue, 1, PointSource.LostChallenge), (point!.Wrestler, point.Points, point.Source));
        Assert.Equal(0, challenge.Remaining(Red));
        Assert.Equal(ChallengeRefusal.NoChallengeLeft, challenge.CanRequest(Red, 0, false, false, false, false));
        Assert.Throws<InvalidOperationException>(() => challenge.Resolve(Red, false, sheet, new BoutTime(1, 101)));
    }

    [Fact]
    public void Challenge_SuccessfulChallengeIsKept()
    {
        var challenge = new ChallengeProcedure(Rules.Current);
        Assert.Null(challenge.Resolve(Blue, decisionChanged: true, new BoutScoreSheet(), new BoutTime(1, 1)));
        Assert.Equal(1, challenge.Remaining(Blue));
    }

    [Theory]
    [InlineData(11, false, false, false, false, ChallengeRefusal.OutsideWindow)]
    [InlineData(2, true, false, false, false, ChallengeRefusal.PassivityCannotBeChallenged)]
    [InlineData(2, false, true, false, false, ChallengeRefusal.FallCannotBeChallenged)]
    [InlineData(2, false, true, true, false, ChallengeRefusal.None)]
    [InlineData(2, false, false, false, true, ChallengeRefusal.BoutDecided)]
    public void Challenge_Refusals(int seconds, bool passivity, bool fall, bool illegalBeforeFall, bool decided, ChallengeRefusal expected)
    {
        var challenge = new ChallengeProcedure(Rules.Current);
        Assert.Equal(expected, challenge.CanRequest(Red, BoutClock.SecondsToTicks(seconds), passivity, fall, illegalBeforeFall, decided));
    }
}
