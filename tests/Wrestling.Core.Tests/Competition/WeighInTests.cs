using Wrestling.Core.Competition;
using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;

namespace Wrestling.Core.Tests.Competition;

public class WeighInTests
{
    private static readonly WeightCategoryList SeniorFreestyle = Rules.Current.WeightCategories.For(AgeCategory.Senior, WrestlingStyle.Freestyle);
    private static readonly WeightCategoryList U17Freestyle = Rules.Current.WeightCategories.For(AgeCategory.U17, WrestlingStyle.Freestyle);

    [Theory]
    [InlineData(EventType.OlympicGames, WeighInDay.First, 0)]
    [InlineData(EventType.Championship, WeighInDay.First, 0)]
    [InlineData(EventType.WorldCup, WeighInDay.First, 2)]
    [InlineData(EventType.RankingSeries, WeighInDay.First, 2)]
    [InlineData(EventType.InternationalTournament, WeighInDay.First, 2)]
    [InlineData(EventType.RankingSeries, WeighInDay.Second, 0)]
    public void Tolerance_Article8(EventType type, WeighInDay day, int kg)
    {
        Assert.Equal(Fix64.FromInt(kg), WeighIn.ToleranceKg(Rules.Current, type, day));
    }

    [Theory]
    [InlineData("57", 57)]
    [InlineData("57.1", 61)]
    [InlineData("97", 97)]
    [InlineData("97.05", 125)]
    public void NaturalCategory(string weight, int expected)
    {
        Assert.Equal(expected, WeighIn.NaturalCategory(SeniorFreestyle, Fix64.Parse(weight))!.LimitKg);
    }

    [Theory]
    [InlineData("125.1")]
    public void NaturalCategory_NoneAboveHeaviest(string weight)
    {
        Assert.Null(WeighIn.NaturalCategory(SeniorFreestyle, Fix64.Parse(weight)));
    }

    [Fact]
    public void NaturalCategory_NoneBelowYouthMinimum()
    {
        Assert.Null(WeighIn.NaturalCategory(U17Freestyle, Fix64.Parse("40.9")));
        Assert.Equal(45, WeighIn.NaturalCategory(U17Freestyle, Fix64.FromInt(41))!.LimitKg);
    }

    [Fact]
    public void SeniorMayMoveUpOneCategory_ButNotTwo()
    {
        Fix64 weight = Fix64.Parse("63.5"); // natural category 65
        int[] eligible = WeighIn.EligibleCategories(Rules.Current, SeniorFreestyle, AgeCategory.Senior, weight).Select(c => c.LimitKg).ToArray();
        Assert.Equal(new[] { 65, 70 }, eligible);
    }

    [Fact]
    public void U20MayNotMoveUp()
    {
        int[] eligible = WeighIn.EligibleCategories(Rules.Current, SeniorFreestyle, AgeCategory.U20, Fix64.Parse("63.5")).Select(c => c.LimitKg).ToArray();
        Assert.Equal(new[] { 65 }, eligible);
    }

    [Fact]
    public void MovingUpIntoHeavyweight_RequiresOver97()
    {
        int[] at95 = WeighIn.EligibleCategories(Rules.Current, SeniorFreestyle, AgeCategory.Senior, Fix64.FromInt(95)).Select(c => c.LimitKg).ToArray();
        Assert.Equal(new[] { 97 }, at95);
    }

    [Theory]
    [InlineData("66.9", EventType.RankingSeries, WeighInDay.First, WeighInOutcome.Passed)]
    [InlineData("67.1", EventType.RankingSeries, WeighInDay.First, WeighInOutcome.Overweight)]
    [InlineData("65.1", EventType.Championship, WeighInDay.First, WeighInOutcome.Overweight)]
    [InlineData("65.1", EventType.RankingSeries, WeighInDay.Second, WeighInOutcome.Overweight)]
    [InlineData("65", EventType.Championship, WeighInDay.Second, WeighInOutcome.Passed)]
    [InlineData("57.5", EventType.Championship, WeighInDay.First, WeighInOutcome.Passed)] // senior moving up from 61
    [InlineData("56", EventType.Championship, WeighInDay.First, WeighInOutcome.TooLight)] // two categories up
    public void Check_Registered65(string weight, EventType type, WeighInDay day, WeighInOutcome expected)
    {
        Assert.Equal(expected, WeighIn.Check(Rules.Current, SeniorFreestyle, 65, AgeCategory.Senior, Fix64.Parse(weight), type, day));
    }

    [Fact]
    public void Check_RejectsUnknownCategory()
    {
        Assert.Throws<ArgumentException>(() => WeighIn.Check(Rules.Current, SeniorFreestyle, 66, AgeCategory.Senior, Fix64.FromInt(66), EventType.Championship, WeighInDay.First));
    }
}
