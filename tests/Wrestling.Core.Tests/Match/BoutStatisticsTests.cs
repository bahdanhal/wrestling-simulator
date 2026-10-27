using Wrestling.Core.AI;
using Wrestling.Core.Bouts;
using Wrestling.Core.Competition;
using Wrestling.Core.Match;
using Wrestling.Core.Rules;
using Wrestling.Core.Techniques;
using Wrestling.Data.Engine;
using Wrestling.Tests.Shared;
using static Wrestling.Core.Tests.Competition.EliminationBracketTests;

namespace Wrestling.Core.Tests.Match;

public class BoutStatisticsTests
{
    private static readonly EngineTuning Tuning = TuningLoader.FromFile(Path.Combine(AppContext.BaseDirectory, "data", "engine", "tuning.json"));
    private static readonly TechniqueCatalog Catalog = TestTechniques.EngineCatalog();
    private static readonly string[] Techniques =
    {
        "technique.all.hip_toss_overhook", "technique.all.hip_block", "technique.fs.double_leg_blast", "technique.all.gut_wrench", "technique.all.stand_up",
    };

    private static BoutEngine FinishedBout(ulong seed)
    {
        var engine = new BoutEngine(new BoutSetup
        {
            Rules = Rules.Current,
            Tuning = Tuning,
            Catalog = Catalog,
            Style = WrestlingStyle.Freestyle,
            Red = TestWrestlers.WithRepertoire(TestWrestlers.Create("red"), 60, Techniques),
            Blue = TestWrestlers.WithRepertoire(TestWrestlers.Create("blue"), 60, Techniques),
            RedController = new AiController(Tactics.Default, seed),
            BlueController = new AiController(Tactics.Default, seed + 1),
            Seed = seed,
        });
        engine.RunToEnd();
        return engine;
    }

    [Fact]
    public void StatisticsAgreeWithTheEventLogAndSheet()
    {
        for (ulong seed = 1; seed <= 10; seed++)
        {
            BoutEngine engine = FinishedBout(seed);
            BoutStatistics stats = BoutStatistics.From(engine);

            foreach (Corner corner in new[] { Corner.Red, Corner.Blue })
            {
                WrestlerBoutStatistics side = stats.For(corner);
                Assert.Equal(engine.Sheet.Points(corner), side.Points);
                Assert.Equal(side.Points, side.PointsByReason.Values.Sum());
                Assert.Equal(engine.Events.Count(e => e.Type == BoutEventType.TechniqueStarted && e.Wrestler == corner), side.Attempts);
                Assert.Equal(side.Attempts, side.AttemptsByFamily.Values.Sum());
                Assert.InRange(side.Successes, 0, side.Attempts);
                Assert.InRange(side.SuccessRate, 0.0, 1.0);
            }

            Assert.Equal(stats.WrestlingSeconds, engine.Clock.ElapsedTicks / BoutClock.TicksPerSecond);
            // Each position is rounded down to whole seconds separately: up to one second lost per position.
            Assert.InRange(stats.PositionSeconds.Values.Sum(), stats.WrestlingSeconds - stats.PositionSeconds.Count, stats.WrestlingSeconds);
            Assert.Same(engine.Result, stats.Result);
        }
    }

    [Fact]
    public void RequiresAFinishedBout()
    {
        var engine = new BoutEngine(new BoutSetup
        {
            Rules = Rules.Current,
            Tuning = Tuning,
            Catalog = Catalog,
            Style = WrestlingStyle.Freestyle,
            Red = TestWrestlers.WithRepertoire(TestWrestlers.Create("red"), 60, Techniques),
            Blue = TestWrestlers.WithRepertoire(TestWrestlers.Create("blue"), 60, Techniques),
            RedController = new AiController(Tactics.Default, 1),
            BlueController = new AiController(Tactics.Default, 2),
            Seed = 1,
        });
        Assert.Throws<InvalidOperationException>(() => BoutStatistics.From(engine));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(21)]
    public void CategoryRunner_PlaysEveryFormatToTheEnd(int entrants)
    {
        ICompetitionFormat format = CompetitionSystems.Create(Entrants(entrants), Rules.Current);
        int bouts = 0;
        IReadOnlyList<Placement> standings = CategoryRunner.Run(format, bout =>
        {
            bouts++;
            return PointsWin(bouts % 3 == 0 ? Corner.Blue : Corner.Red);
        });

        Assert.True(format.IsComplete);
        Assert.Equal(entrants, standings.Count);
        Assert.Equal(1, standings[0].Rank);
        Assert.Equal(format.Bouts.Count(b => b.Result != null && b.Top != null && b.Bottom != null && b.Result.Code != "VFO"), bouts);
    }
}
