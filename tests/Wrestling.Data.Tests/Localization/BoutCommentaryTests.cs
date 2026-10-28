using Wrestling.Core.AI;
using Wrestling.Core.Bouts;
using Wrestling.Core.Match;
using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;
using Wrestling.Core.Wrestlers;
using Wrestling.Data.Localization;

namespace Wrestling.Data.Tests.Localization;

public class BoutCommentaryTests
{
    private static readonly GameContent Content = GameContent.Load(TestContent.PathOf(string.Empty));

    [Fact]
    public void EveryVictoryTypeAndTieBreakHasText()
    {
        foreach (VictoryType type in Enum.GetValues<VictoryType>())
        {
            Assert.True(Content.English.Contains("result.victory." + type), type.ToString());
        }

        foreach (TieBreakCriterion criterion in Enum.GetValues<TieBreakCriterion>())
        {
            Assert.True(Content.English.Contains("result.tiebreak." + criterion), criterion.ToString());
        }
    }

    [Fact]
    public void DescribesEveryNotableEventOfARealBout()
    {
        var random = new Pcg32(9, 1);
        Wrestler red = WrestlerGenerator.Generate("red", WrestlingStyle.Freestyle, 60, 12, Content.Techniques, Content.Names, random);
        Wrestler blue = WrestlerGenerator.Generate("blue", WrestlingStyle.Freestyle, 60, 12, Content.Techniques, Content.Names, random);
        var engine = new BoutEngine(new BoutSetup
        {
            Rules = Content.Rules,
            Tuning = Content.Tuning,
            Catalog = Content.Techniques,
            Style = WrestlingStyle.Freestyle,
            Red = red,
            Blue = blue,
            RedController = new AiController(Tactics.Default, 18),
            BlueController = new AiController(Tactics.Default, 19),
            Seed = 9,
        });
        BoutResult result = engine.RunToEnd();

        List<string> lines = engine.Events.Where(BoutCommentary.IsNotable).Select(e => BoutCommentary.Describe(e, Content, red, blue)).ToList();
        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.False(string.IsNullOrWhiteSpace(line)));
        Assert.StartsWith(Content.English.Get("result.victory." + result.VictoryType), BoutCommentary.ResultLine(result, Content));
    }

    [Fact]
    public void ClockShowsElapsedBoutTime()
    {
        var clock = new BoutClock(TestContent.LoadCurrentRuleset().BoutDuration.For(AgeCategory.Senior));
        var time = new BoutTime(2, 75 * BoutClock.TicksPerSecond);
        Assert.Equal("P2 4:15", BoutCommentary.Clock(time, clock));
    }
}
