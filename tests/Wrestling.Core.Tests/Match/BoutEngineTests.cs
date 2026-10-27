using Wrestling.Core.Bouts;
using Wrestling.Core.Match;
using Wrestling.Core.Rules;
using Wrestling.Core.Techniques;
using Wrestling.Core.Wrestlers;
using Wrestling.Data.Engine;
using Wrestling.Tests.Shared;

namespace Wrestling.Core.Tests.Match;

public class BoutEngineTests
{
    private static readonly Lazy<EngineTuning> TuningData = new(() =>
        TuningLoader.FromFile(Path.Combine(AppContext.BaseDirectory, "data", "engine", "tuning.json")));

    private static readonly TechniqueCatalog Catalog = TestTechniques.EngineCatalog();

    private static readonly string[] AllTechniques =
    {
        "technique.all.hip_toss_overhook", "technique.all.hip_block", "technique.fs.double_leg_blast",
        "technique.all.gut_wrench", "technique.all.stand_up",
    };

    private static readonly string[] GrecoTechniques =
    {
        "technique.all.hip_toss_overhook", "technique.all.hip_block", "technique.all.gut_wrench", "technique.all.stand_up",
    };

    private static Wrestler Fighter(string id, WrestlingStyle style, int level = 50, Action<AttributeSet>? adjust = null, int proficiency = 60) =>
        TestWrestlers.WithRepertoire(
            TestWrestlers.Create(id, style: style, attributeValue: level, adjust: adjust),
            proficiency,
            style == WrestlingStyle.GrecoRoman ? GrecoTechniques : AllTechniques);

    private static BoutEngine Engine(
        ulong seed,
        WrestlingStyle style = WrestlingStyle.Freestyle,
        IBoutController? red = null,
        IBoutController? blue = null,
        Wrestler? redWrestler = null,
        Wrestler? blueWrestler = null) =>
        new(new BoutSetup
        {
            Rules = Rules.Current,
            Tuning = TuningData.Value,
            Catalog = Catalog,
            Style = style,
            Red = redWrestler ?? Fighter("red", style),
            Blue = blueWrestler ?? Fighter("blue", style),
            RedController = red ?? new ScriptedController { GripGoal = GripType.Overhook },
            BlueController = blue ?? new ScriptedController { GripGoal = GripType.Overhook },
            Seed = seed,
        });

    [Fact]
    public void TuningFileIsValid()
    {
        Assert.Empty(TuningData.Value.Validate());
    }

    [Fact]
    public void SameSeed_GivesIdenticalBouts()
    {
        BoutEngine a = Engine(42);
        BoutEngine b = Engine(42);
        a.RunToEnd();
        b.RunToEnd();

        Assert.Equal(a.Events.Select(e => e.ToString()), b.Events.Select(e => e.ToString()));
        Assert.Equal(a.Result!.Code, b.Result!.Code);
        Assert.Equal(a.Tick, b.Tick);
    }

    [Fact]
    public void DifferentSeeds_GiveDifferentBouts()
    {
        var signatures = Enumerable.Range(1, 8).Select(seed =>
        {
            BoutEngine engine = Engine((ulong)seed);
            engine.RunToEnd();
            return string.Join("|", engine.Events.Select(e => e.ToString()));
        }).ToList();

        Assert.True(signatures.Distinct().Count() > 1);
    }

    [Theory]
    [InlineData(WrestlingStyle.Freestyle)]
    [InlineData(WrestlingStyle.GrecoRoman)]
    [InlineData(WrestlingStyle.Womens)]
    public void EveryBoutEndsWithAConsistentResult(WrestlingStyle style)
    {
        for (ulong seed = 1; seed <= 25; seed++)
        {
            Wrestler red = Fighter("red", style == WrestlingStyle.Womens ? WrestlingStyle.Womens : style);
            Wrestler blue = Fighter("blue", style == WrestlingStyle.Womens ? WrestlingStyle.Womens : style);
            if (style == WrestlingStyle.Womens)
            {
                red = Woman(red);
                blue = Woman(blue);
            }

            BoutEngine engine = Engine(seed, style, redWrestler: red, blueWrestler: blue);
            BoutResult result = engine.RunToEnd();

            Assert.NotNull(result.Winner);
            Assert.Equal(engine.Sheet.Points(Corner.Red), result.RedPoints);
            Assert.Equal(engine.Sheet.Points(Corner.Blue), result.BluePoints);
            Assert.Equal(BoutEventType.BoutEnded, engine.Events[^1].Type);
            Assert.True(engine.Clock.ElapsedTicks <= 2 * 180 * BoutClock.TicksPerSecond);
        }
    }

    [Fact]
    public void IdleFreestyleBout_IsDecidedByTheActivityClock()
    {
        BoutEngine engine = Engine(7, red: ScriptedController.Idle(), blue: ScriptedController.Idle());
        BoutResult result = engine.RunToEnd();

        Assert.Equal(VictoryType.Points, result.VictoryType);
        Assert.Contains(engine.Sheet.ActivePoints(), p => p.Source == PointSource.ActivityClock);
        Assert.Contains(engine.Events, e => e.Type == BoutEventType.ActivityPeriodStarted);
    }

    [Fact]
    public void IdleGrecoBout_IsDecidedByPassivityCalls()
    {
        BoutEngine engine = Engine(7, WrestlingStyle.GrecoRoman, ScriptedController.Idle(), ScriptedController.Idle());
        engine.RunToEnd();

        List<BoutEvent> calls = engine.Events.Where(e => e.Type == BoutEventType.PassivityCall).ToList();
        Assert.NotEmpty(calls);
        Assert.Equal(1, calls[0].Points);
        Assert.Contains(engine.Sheet.ActivePoints(), p => p.Source == PointSource.Passivity);
    }

    [Fact]
    public void PushingWithoutAttacking_GivesNoStepOutPoints()
    {
        var pusher = new ScriptedController { Attack = false, Effort = Fix64One };
        var passive = new ScriptedController { Attack = false, Pressure = false, Effort = Wrestling.Core.Numerics.Fix64.Zero };
        BoutEngine engine = Engine(3, red: pusher, blue: passive);
        engine.RunToEnd();

        Assert.Contains(engine.Events, e => e.Type == BoutEventType.StepOut && e.Detail == "deliberate push");
        Assert.DoesNotContain(engine.Sheet.ActivePoints(), p => p.Source == PointSource.StepOut);
        Assert.True(engine.Sheet.Warnings(Corner.Red, WarningReason.DeliberatePushOut) > 0);
    }

    [Fact]
    public void StrongerWrestlerUsuallyWins_AndTechniquesScore()
    {
        int strongerWins = 0;
        int techniquePoints = 0;
        for (ulong seed = 1; seed <= 30; seed++)
        {
            BoutEngine engine = Engine(seed, redWrestler: Fighter("red", WrestlingStyle.Freestyle, 80, proficiency: 85), blueWrestler: Fighter("blue", WrestlingStyle.Freestyle, 35, proficiency: 30));
            BoutResult result = engine.RunToEnd();
            if (result.Winner == Corner.Red)
            {
                strongerWins++;
            }

            techniquePoints += engine.Sheet.ActivePoints().Count(p => p.Source == PointSource.Hold);
        }

        Assert.True(strongerWins >= 25, $"Stronger wrestler won {strongerWins} of 30.");
        Assert.True(techniquePoints > 30);
    }

    [Fact]
    public void DominantParTerreWrestler_CanWinByFallOrSuperiority()
    {
        var types = new HashSet<VictoryType>();
        for (ulong seed = 1; seed <= 30; seed++)
        {
            Wrestler red = Fighter("red", WrestlingStyle.Freestyle, 90, a => { a[WrestlerAttribute.ParTerreTurns] = 90; a[WrestlerAttribute.UpperBodyStrength] = 90; }, 95);
            Wrestler blue = Fighter("blue", WrestlingStyle.Freestyle, 20, proficiency: 10);
            BoutEngine engine = Engine(seed, redWrestler: red, blueWrestler: blue);
            types.Add(engine.RunToEnd().VictoryType!.Value);
        }

        Assert.True(types.Contains(VictoryType.Fall) || types.Contains(VictoryType.TechnicalSuperiority));
    }

    [Fact]
    public void DefenderWithCounterIntent_CountersThrows()
    {
        var thrower = new ScriptedController { GripGoal = GripType.Overhook };
        var counterer = new ScriptedController { GripGoal = GripType.Underhook, Attack = false, Counter = "technique.all.hip_block" };
        int counters = 0;
        for (ulong seed = 1; seed <= 10; seed++)
        {
            BoutEngine engine = Engine(seed, red: thrower, blue: counterer);
            engine.RunToEnd();
            counters += engine.Events.Count(e => e.Type == BoutEventType.CounterStarted && e.Wrestler == Corner.Blue);
        }

        Assert.True(counters > 0);
    }

    [Fact]
    public void GrecoWrestlersNeverStartLegAttacks()
    {
        for (ulong seed = 1; seed <= 10; seed++)
        {
            BoutEngine engine = Engine(seed, WrestlingStyle.GrecoRoman);
            engine.RunToEnd();
            Assert.DoesNotContain(engine.Events, e => e.TechniqueId == "technique.fs.double_leg_blast");
        }
    }

    [Fact]
    public void SetupIsValidated()
    {
        Wrestler unknown = TestWrestlers.WithRepertoire(TestWrestlers.Create("x"), 50, "technique.all.not_in_catalogue");
        Assert.Contains("unknown technique", Assert.Throws<ArgumentException>(() => Engine(1, redWrestler: unknown)).Message);

        Wrestler greco = Fighter("g", WrestlingStyle.GrecoRoman);
        Assert.Contains("does not match", Assert.Throws<ArgumentException>(() => Engine(1, WrestlingStyle.Freestyle, redWrestler: greco)).Message);
    }

    private static readonly Wrestling.Core.Numerics.Fix64 Fix64One = Wrestling.Core.Numerics.Fix64.One;

    private static Wrestler Woman(Wrestler template) => new()
    {
        Id = template.Id,
        Name = template.Name,
        Country = template.Country,
        Gender = Gender.Female,
        Style = WrestlingStyle.Womens,
        BirthDate = template.BirthDate,
        Body = template.Body,
        Attributes = template.Attributes,
        Hidden = template.Hidden,
        Repertoire = template.Repertoire,
        Condition = template.Condition,
    };
}
