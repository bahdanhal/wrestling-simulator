using Wrestling.Core.Match;
using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;

namespace Wrestling.Core.Tests.Match;

public class BoutClockTests
{
    private static BoutClock SeniorClock() => new BoutClock(Rules.Current.BoutDuration.Senior);

    [Fact]
    public void SeniorBout_TwoPeriodsOfThreeMinutesWithBreak()
    {
        BoutClock clock = SeniorClock();
        Assert.Equal(360, clock.ScoreboardSecondsRemaining);

        clock.Start();
        int ticks = RunUntilPeriodEnds(clock);
        Assert.Equal(180 * BoutClock.TicksPerSecond, ticks);
        Assert.Equal(ClockPhase.Break, clock.Phase);
        Assert.Equal(180, clock.ScoreboardSecondsRemaining);
        Assert.Throws<InvalidOperationException>(() => clock.Start());

        for (int i = 0; i < 30 * BoutClock.TicksPerSecond; i++)
        {
            clock.Tick();
        }

        clock.Start();
        Assert.Equal(2, clock.Period);
        RunUntilPeriodEnds(clock);
        Assert.Equal(ClockPhase.Finished, clock.Phase);
        Assert.Equal(0, clock.ScoreboardSecondsRemaining);
        Assert.Throws<InvalidOperationException>(() => clock.Start());
    }

    [Fact]
    public void StoppedClock_DoesNotAdvance()
    {
        BoutClock clock = SeniorClock();
        clock.Start();
        clock.Tick();
        clock.Stop();
        for (int i = 0; i < 100; i++)
        {
            clock.Tick();
        }

        Assert.Equal(1, clock.TickInPeriod);
        clock.Start();
        clock.Tick();
        Assert.Equal(2, clock.ElapsedTicks);
        Assert.Equal(new Wrestling.Core.Bouts.BoutTime(1, 2), clock.Now);
    }

    [Fact]
    public void YouthBoutsAreShorter()
    {
        var clock = new BoutClock(Rules.Current.BoutDuration.U17);
        Assert.Equal(240, clock.ScoreboardSecondsRemaining);
        clock.Start();
        Assert.Equal(120 * BoutClock.TicksPerSecond, RunUntilPeriodEnds(clock));
    }

    [Theory]
    [InlineData("0", "0", MatZone.CentralSurface)]
    [InlineData("3.5", "0", MatZone.CentralSurface)]
    [InlineData("3.6", "0", MatZone.PassivityZone)]
    [InlineData("0", "-4.5", MatZone.PassivityZone)]
    [InlineData("3.2", "3.2", MatZone.ProtectionArea)] // 4.53 m from the centre
    [InlineData("6", "0", MatZone.ProtectionArea)]
    [InlineData("6.01", "0", MatZone.OffMat)]
    public void MatZones(string x, string y, MatZone expected)
    {
        Assert.Equal(expected, Rules.Current.Mat.ZoneAt(new FixVector2(Fix64.Parse(x), Fix64.Parse(y))));
    }

    private static int RunUntilPeriodEnds(BoutClock clock)
    {
        int ticks = 0;
        while (!clock.Tick())
        {
            ticks++;
        }

        return ticks + 1;
    }
}
