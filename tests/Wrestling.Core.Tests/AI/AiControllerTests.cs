using Wrestling.Core.AI;
using Wrestling.Core.Bouts;
using Wrestling.Core.Match;
using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;
using Wrestling.Core.Techniques;
using Wrestling.Core.Tests.Match;
using Wrestling.Core.Wrestlers;
using Wrestling.Data.Engine;
using Wrestling.Tests.Shared;

namespace Wrestling.Core.Tests.AI;

public class AiControllerTests
{
    private static readonly EngineTuning Tuning = TuningLoader.FromFile(Path.Combine(AppContext.BaseDirectory, "data", "engine", "tuning.json"));
    private static readonly TechniqueCatalog Catalog = TestTechniques.EngineCatalog();

    private static readonly string[] Techniques =
    {
        "technique.all.hip_toss_overhook", "technique.all.hip_block", "technique.fs.double_leg_blast", "technique.all.gut_wrench", "technique.all.stand_up",
    };

    private static Wrestler Fighter(string id, int level = 50, Action<AttributeSet>? adjust = null) =>
        TestWrestlers.WithRepertoire(TestWrestlers.Create(id, attributeValue: level, adjust: adjust), 60, Techniques);

    private static BoutEngine Bout(ulong seed, IBoutController red, IBoutController blue, Wrestler? redWrestler = null, Wrestler? blueWrestler = null) => new(new BoutSetup
    {
        Rules = Rules.Current,
        Tuning = Tuning,
        Catalog = Catalog,
        Style = WrestlingStyle.Freestyle,
        Red = redWrestler ?? Fighter("red"),
        Blue = blueWrestler ?? Fighter("blue"),
        RedController = red,
        BlueController = blue,
        Seed = seed,
    });

    [Fact]
    public void SameSeeds_GiveIdenticalBouts()
    {
        BoutEngine a = Bout(5, new AiController(Tactics.Default, 1), new AiController(Tactics.Default, 2));
        BoutEngine b = Bout(5, new AiController(Tactics.Default, 1), new AiController(Tactics.Default, 2));
        a.RunToEnd();
        b.RunToEnd();
        Assert.Equal(a.Events.Select(e => e.ToString()), b.Events.Select(e => e.ToString()));
    }

    [Fact]
    public void AiBeatsAnIdleOpponent()
    {
        int wins = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            BoutEngine engine = Bout(seed, new AiController(Tactics.Default, seed), ScriptedController.Idle());
            if (engine.RunToEnd().Winner == Corner.Red)
            {
                wins++;
            }
        }

        Assert.True(wins >= 19, $"AI won {wins} of 20 against an idle opponent.");
    }

    [Fact]
    public void HighPaceAttacksMoreThanLowPace()
    {
        int Attempts(Pace pace)
        {
            int total = 0;
            for (ulong seed = 1; seed <= 15; seed++)
            {
                var tactics = new Tactics { Pace = pace };
                BoutEngine engine = Bout(seed, new AiController(tactics, seed), new AiController(Tactics.Default, seed + 100));
                engine.RunToEnd();
                total += engine.Events.Count(e => e.Type == BoutEventType.TechniqueStarted && e.Wrestler == Corner.Red);
            }

            return total;
        }

        Assert.True(Attempts(Pace.High) > Attempts(Pace.Low));
    }

    [Fact]
    public void BoutsHaveARealisticNumberOfAttempts()
    {
        int attempts = 0;
        int bouts = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            BoutEngine engine = Bout(seed, new AiController(Tactics.Default, seed), new AiController(Tactics.Default, seed + 50));
            engine.RunToEnd();
            if (engine.Result!.VictoryType == VictoryType.Points)
            {
                attempts += engine.Events.Count(e => e.Type == BoutEventType.TechniqueStarted);
                bouts++;
            }
        }

        // Two wrestlers over six minutes: between one attempt a minute and one every few seconds.
        Assert.True(bouts > 0);
        Assert.InRange(attempts / bouts, 12, 80);
    }

    [Fact]
    public void NearTheEdge_PushesBackTowardTheCentre()
    {
        BoutEngine engine = Bout(1, new AiController(Tactics.Default, 1), ScriptedController.Idle());
        WrestlerRuntime red = engine.Wrestler(Corner.Red);
        WrestlerRuntime blue = engine.Wrestler(Corner.Blue);
        red.Body.Position = new FixVector2(Fix64.FromRatio(40, 10), Fix64.Zero);
        blue.Body.Position = new FixVector2(Fix64.FromRatio(48, 10), Fix64.Zero);
        engine.Clock.Start();

        Intent intent = new AiController(Tactics.Default, 1).Decide(engine, Corner.Red);
        Assert.True(intent.PushDirection.X < Fix64.Zero || intent.PushDirection.LengthSquared < Fix64.FromRatio(1, 100), $"Expected a push back toward the centre, got {intent.PushDirection}.");
    }

    [Fact]
    public void ChoosesParTerreWhenTheTopGameIsStronger()
    {
        BoutEngine strongTop = Bout(1,
            new AiController(Tactics.Default, 1), new AiController(Tactics.Default, 2),
            Fighter("red", 50, a => { a[WrestlerAttribute.ParTerreTurns] = 85; a[WrestlerAttribute.ParTerreBottomDefence] = 75; }),
            Fighter("blue", 50, a => a[WrestlerAttribute.ParTerreBottomDefence] = 40));
        Assert.True(new AiController(Tactics.Default, 1).ChoosesParTerre(strongTop, Corner.Red));
        Assert.False(new AiController(Tactics.Default, 1).ChoosesParTerre(strongTop, Corner.Blue));
    }

    [Fact]
    public void CounterWrestlerUsesCounters()
    {
        var counterTactics = new Tactics { Focus = AttackFocus.Counters, Pace = Pace.Low };
        int counters = 0;
        for (ulong seed = 1; seed <= 15; seed++)
        {
            BoutEngine engine = Bout(seed,
                new AiController(new Tactics { Pace = Pace.High }, seed),
                new AiController(counterTactics, seed + 7),
                blueWrestler: Fighter("blue", 50, a => { a[WrestlerAttribute.CounterWrestling] = 85; a[WrestlerAttribute.WrestlingIq] = 80; }));
            engine.RunToEnd();
            counters += engine.Events.Count(e => e.Type == BoutEventType.CounterStarted && e.Wrestler == Corner.Blue);
        }

        Assert.True(counters > 0);
    }

    [Fact]
    public void StoppedBoutGivesIdleIntent()
    {
        BoutEngine engine = Bout(1, new AiController(Tactics.Default, 1), new AiController(Tactics.Default, 2));
        Assert.Same(Intent.Idle, new AiController(Tactics.Default, 1).Decide(engine, Corner.Red));
    }
}
