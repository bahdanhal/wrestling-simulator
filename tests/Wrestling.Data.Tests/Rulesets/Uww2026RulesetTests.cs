using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;

namespace Wrestling.Data.Tests.Rulesets;

/// <summary>Checks the shipped ruleset against the UWW International Wrestling Rules, January 2026.</summary>
public class Uww2026RulesetTests
{
    private readonly Ruleset _rules = TestContent.LoadCurrentRuleset();

    [Fact]
    public void LoadsWithoutValidationErrors()
    {
        Assert.Equal("uww-2026-01", _rules.Id);
        Assert.Empty(_rules.Validate());
    }

    [Fact]
    public void Mat_Article4()
    {
        Assert.Equal(Fix64.Parse("4.5"), _rules.Mat.WrestlingAreaRadius);
        Assert.Equal(Fix64.Parse("3.5"), _rules.Mat.CentralSurfaceRadius);
        Assert.Equal(Fix64.FromInt(6), _rules.Mat.OuterRadius);
        Assert.Equal(Fix64.One, _rules.Mat.CentralCircleDiameter);
    }

    [Theory]
    [InlineData(AgeCategory.U15, 120)]
    [InlineData(AgeCategory.U17, 120)]
    [InlineData(AgeCategory.Veterans, 120)]
    [InlineData(AgeCategory.U20, 180)]
    [InlineData(AgeCategory.U23, 180)]
    [InlineData(AgeCategory.Senior, 180)]
    public void BoutDuration_Article22(AgeCategory age, int periodSeconds)
    {
        BoutDuration duration = _rules.BoutDuration.For(age);
        Assert.Equal(2, duration.Periods);
        Assert.Equal(periodSeconds, duration.PeriodSeconds);
        Assert.Equal(30, duration.BreakSeconds);
    }

    [Theory]
    [InlineData(AgeCategory.U15, 14, false, true)]
    [InlineData(AgeCategory.U15, 13, false, false)]
    [InlineData(AgeCategory.U15, 13, true, true)]
    [InlineData(AgeCategory.U15, 16, true, false)]
    [InlineData(AgeCategory.U17, 15, true, true)]
    [InlineData(AgeCategory.U20, 17, true, true)]
    [InlineData(AgeCategory.U20, 21, false, false)]
    [InlineData(AgeCategory.U23, 18, true, true)]
    [InlineData(AgeCategory.Senior, 18, false, true)] // U20 wrestlers may enter senior events...
    [InlineData(AgeCategory.Senior, 17, true, false)] // ...except those aged 17 that year.
    [InlineData(AgeCategory.Senior, 40, false, true)]
    [InlineData(AgeCategory.Veterans, 35, false, true)]
    [InlineData(AgeCategory.Veterans, 34, false, false)]
    public void AgeEligibility_Article7(AgeCategory age, int ageInYear, bool authorization, bool eligible)
    {
        Assert.Equal(eligible, _rules.AgeEligibility.For(age).IsEligible(ageInYear, authorization));
    }

    [Theory]
    [InlineData(WrestlingStyle.Freestyle, new[] { 57, 61, 65, 70, 74, 79, 86, 92, 97, 125 })]
    [InlineData(WrestlingStyle.GrecoRoman, new[] { 55, 60, 63, 67, 72, 77, 82, 87, 97, 130 })]
    [InlineData(WrestlingStyle.Womens, new[] { 50, 53, 55, 57, 59, 62, 65, 68, 72, 76 })]
    public void SeniorWeights_Article7(WrestlingStyle style, int[] limits)
    {
        Assert.Equal(limits, _rules.WeightCategories.For(AgeCategory.Senior, style).LimitsKg);
        Assert.Equal(limits, _rules.WeightCategories.For(AgeCategory.U20, style).LimitsKg);
    }

    [Theory]
    [InlineData(WrestlingStyle.Freestyle, new[] { 57, 65, 74, 86, 97, 125 })]
    [InlineData(WrestlingStyle.GrecoRoman, new[] { 60, 67, 77, 87, 97, 130 })]
    [InlineData(WrestlingStyle.Womens, new[] { 50, 53, 57, 62, 68, 76 })]
    public void OlympicWeights_Article7(WrestlingStyle style, int[] limits)
    {
        Assert.Equal(limits, _rules.WeightCategories.Olympic.For(style).LimitsKg);
    }

    [Fact]
    public void YouthWeights_Article7()
    {
        WeightCategoryList u15Womens = _rules.WeightCategories.For(AgeCategory.U15, WrestlingStyle.Womens);
        Assert.Equal("29-33 kg", u15Womens.Categories[0].Label);
        Assert.Equal("66 kg", u15Womens.Categories[^1].Label);

        WeightCategoryList u17Greco = _rules.WeightCategories.For(AgeCategory.U17, WrestlingStyle.GrecoRoman);
        Assert.Equal("41-45 kg", u17Greco.Categories[0].Label);
        Assert.Equal(110, u17Greco.Categories[^1].LimitKg);
    }

    [Fact]
    public void Heavyweight_RequiresMinimumBodyWeight()
    {
        WeightCategory heavy = _rules.WeightCategories.For(AgeCategory.Senior, WrestlingStyle.Freestyle).Find(125);
        Assert.False(heavy.Accepts(Fix64.FromInt(97), Fix64.Zero));
        Assert.True(heavy.Accepts(Fix64.Parse("97.1"), Fix64.Zero));
        Assert.False(heavy.Accepts(Fix64.Parse("125.1"), Fix64.Zero));

        WeightCategory womensHeavy = _rules.WeightCategories.For(AgeCategory.Senior, WrestlingStyle.Womens).Find(76);
        Assert.False(womensHeavy.Accepts(Fix64.FromInt(72), Fix64.Zero));
    }

    [Fact]
    public void Tolerance_IsAppliedToTheLimit()
    {
        WeightCategory category = _rules.WeightCategories.For(AgeCategory.Senior, WrestlingStyle.Freestyle).Find(65);
        Fix64 tolerance = Fix64.FromInt(_rules.WeighIn.FirstDayToleranceKg.RankingSeries);
        Assert.True(category.Accepts(Fix64.Parse("66.9"), tolerance));
        Assert.False(category.Accepts(Fix64.Parse("67.1"), tolerance));
        Assert.False(category.Accepts(Fix64.Parse("65.1"), Fix64.FromInt(_rules.WeighIn.FirstDayToleranceKg.Championships)));
        Assert.Equal(0, _rules.WeighIn.SecondDayToleranceKg);
    }

    [Fact]
    public void MovingUpOneCategory_SeniorAndU23Only()
    {
        Assert.Equal(new[] { AgeCategory.Senior, AgeCategory.U23 }, _rules.WeightCategories.MayMoveUpOneCategory);
    }

    [Fact]
    public void TechnicalSuperiority_Article43()
    {
        Assert.Equal(8, _rules.TechnicalSuperiority.For(WrestlingStyle.GrecoRoman));
        Assert.Equal(10, _rules.TechnicalSuperiority.For(WrestlingStyle.Freestyle));
        Assert.Equal(10, _rules.TechnicalSuperiority.For(WrestlingStyle.Womens));
    }

    [Fact]
    public void TechnicalPoints_Article38()
    {
        TechnicalPointValues p = _rules.TechnicalPoints;
        Assert.Equal(1, p.StepOut);
        Assert.Equal(1, p.Reversal);
        Assert.Equal(2, p.Takedown);
        Assert.Equal(2, p.Exposure);
        Assert.Equal(4, p.StandingHoldToDanger);
        Assert.Equal(4, p.InstantaneousFallFromStanding);
        Assert.Equal(5, p.GrandAmplitudeToDirectDanger);
        Assert.Equal(5, p.ParTerreGrandAmplitudeToDirectDanger);
    }

    [Fact]
    public void Penalties_DifferByStyle()
    {
        Assert.Equal(2, _rules.Penalties.NegativeWrestling.For(WrestlingStyle.GrecoRoman));
        Assert.Equal(1, _rules.Penalties.NegativeWrestling.For(WrestlingStyle.Freestyle));
        Assert.Equal(2, _rules.Penalties.DefenderIllegalAction.For(WrestlingStyle.GrecoRoman));
        Assert.Equal(1, _rules.Penalties.DefenderIllegalAction.For(WrestlingStyle.Womens));
        Assert.Equal(2, _rules.Penalties.FleeingMatInDanger);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    [InlineData(7, 0)]
    public void GrecoPassivity_PointsByCall_Article46(int call, int points)
    {
        Assert.Equal(points, _rules.Passivity.GrecoRoman.PointsForCall(call));
    }

    [Fact]
    public void ActivityClock_Article46()
    {
        ActivityClockRules clock = _rules.Passivity.ActivityClock;
        Assert.Equal(30, clock.ActivitySeconds);
        Assert.True(clock.VerbalWarningFirst.Senior);
        Assert.False(clock.VerbalWarningFirst.U17);
        Assert.Equal(120, clock.ScorelessDesignationAtSeconds.Senior);
        Assert.Equal(60, clock.ScorelessDesignationAtSeconds.U15);
    }

    [Fact]
    public void Challenge_Article31()
    {
        Assert.Equal(1, _rules.Challenge.PerBout);
        Assert.Equal(1, _rules.Challenge.LostChallengePoints);
        Assert.Equal(10, _rules.Challenge.RequestWindowSeconds);
        Assert.True(_rules.Challenge.SuccessfulChallengeIsKept);
    }

    [Theory]
    [InlineData(VictoryType.Fall, false, 5, 0, "VFA")]
    [InlineData(VictoryType.Injury, false, 5, 0, "VIN")]
    [InlineData(VictoryType.ThreeCautions, false, 5, 0, "VCA")]
    [InlineData(VictoryType.Forfeit, false, 5, 0, "VFO")]
    [InlineData(VictoryType.Disqualification, false, 5, 0, "DSQ")]
    [InlineData(VictoryType.TechnicalSuperiority, false, 4, 0, "VSU")]
    [InlineData(VictoryType.TechnicalSuperiority, true, 4, 1, "VSU1")]
    [InlineData(VictoryType.Points, false, 3, 0, "VPO")]
    [InlineData(VictoryType.Points, true, 3, 1, "VPO1")]
    public void ClassificationPoints_Article41(VictoryType type, bool loserScored, int winner, int loser, string code)
    {
        ClassificationPoints points = _rules.ClassificationPoints.For(type, loserScored);
        Assert.Equal(winner, points.Winner);
        Assert.Equal(loser, points.Loser);
        Assert.Equal(code, ResultCodes.For(type, loserScored));
    }

    [Fact]
    public void Procedure_Articles23_26_29()
    {
        Assert.Equal(3, _rules.Procedure.CautionsForLoss);
        Assert.Equal(20, _rules.Procedure.MinimumRestBetweenBoutsMinutes);
        Assert.Equal(3, _rules.Procedure.CallsBeforeForfeit);
        Assert.Equal(240, _rules.Procedure.BleedingTreatmentSecondsPerBout);
    }
}
