using Wrestling.Core.Bouts;
using Wrestling.Core.Rules;
using static Wrestling.Core.Rules.Corner;

namespace Wrestling.Core.Tests.Bouts;

public class BoutResultTests
{
    [Fact]
    public void PointsWin_LoserScored_Is3_1()
    {
        var sheet = new SheetBuilder().Hold(Red, 2).Hold(Blue, 1).Hold(Red, 4).Sheet;
        BoutResult result = BoutResult.AtEndOfRegularTime(sheet, Rules.Current);

        Assert.Equal(Red, result.Winner);
        Assert.Equal(VictoryType.Points, result.VictoryType);
        Assert.Equal("VPO1", result.Code);
        Assert.Equal(3, result.RedClassificationPoints);
        Assert.Equal(1, result.BlueClassificationPoints);
        Assert.Equal(6, result.RedPoints);
        Assert.Equal(1, result.BluePoints);
        Assert.Null(result.DecidedBy);
    }

    [Fact]
    public void PointsWin_LoserScoreless_Is3_0()
    {
        var sheet = new SheetBuilder().StepOut(Blue).Sheet;
        BoutResult result = BoutResult.AtEndOfRegularTime(sheet, Rules.Current);

        Assert.Equal(Blue, result.Winner);
        Assert.Equal("VPO", result.Code);
        Assert.Equal(0, result.RedClassificationPoints);
        Assert.Equal(3, result.BlueClassificationPoints);
    }

    [Fact]
    public void LevelScore_IsDecidedByTieBreak_AndCountsAsLoserScored()
    {
        var sheet = new SheetBuilder().Passivity(Red).Passivity(Blue).Sheet;
        BoutResult result = BoutResult.AtEndOfRegularTime(sheet, Rules.Current);

        Assert.Equal(Red, result.Winner);
        Assert.Equal(TieBreakCriterion.FirstPoint, result.DecidedBy);
        Assert.Equal("VPO1", result.Code);
    }

    [Fact]
    public void ScorelessEnd_IsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => BoutResult.AtEndOfRegularTime(new BoutScoreSheet(), Rules.Current));
    }

    [Theory]
    [InlineData(WrestlingStyle.GrecoRoman, 8, true)]
    [InlineData(WrestlingStyle.GrecoRoman, 7, false)]
    [InlineData(WrestlingStyle.Freestyle, 9, false)]
    [InlineData(WrestlingStyle.Freestyle, 10, true)]
    [InlineData(WrestlingStyle.Womens, 10, true)]
    public void TechnicalSuperiority_Thresholds(WrestlingStyle style, int lead, bool ends)
    {
        var builder = new SheetBuilder();
        for (int i = 0; i < lead; i++)
        {
            builder.Hold(Red, 1);
        }

        Corner? superior = BoutEndConditions.TechnicalSuperiority(builder.Sheet, Rules.Current, style);
        Assert.Equal(ends ? Red : (Corner?)null, superior);
    }

    [Fact]
    public void TechnicalSuperiority_ClassificationDependsOnLoserScoring()
    {
        var shutout = new SheetBuilder().Hold(Blue, 4).Hold(Blue, 4).Sheet;
        BoutResult clean = BoutResult.Win(Blue, VictoryType.TechnicalSuperiority, shutout, Rules.Current);
        Assert.Equal("VSU", clean.Code);
        Assert.Equal(4, clean.BlueClassificationPoints);
        Assert.Equal(0, clean.RedClassificationPoints);

        var scored = new SheetBuilder().StepOut(Red).Hold(Blue, 5).Hold(Blue, 4).Sheet;
        BoutResult withPoints = BoutResult.Win(Blue, VictoryType.TechnicalSuperiority, scored, Rules.Current);
        Assert.Equal("VSU1", withPoints.Code);
        Assert.Equal(1, withPoints.RedClassificationPoints);
    }

    [Fact]
    public void Fall_Is5_0_EvenWhenLoserScored()
    {
        var sheet = new SheetBuilder().Hold(Red, 4).Hold(Blue, 2).Sheet;
        BoutResult result = BoutResult.Win(Blue, VictoryType.Fall, sheet, Rules.Current);

        Assert.Equal("VFA", result.Code);
        Assert.Equal(5, result.BlueClassificationPoints);
        Assert.Equal(0, result.RedClassificationPoints);
        Assert.Equal(4, result.RedPoints);
    }

    [Fact]
    public void ThreeCautions_EndTheBout()
    {
        var sheet = new SheetBuilder().Caution(Red).Caution(Red).Sheet;
        Assert.Null(BoutEndConditions.LostByCautions(sheet, Rules.Current));

        sheet.GiveCaution(Red, CautionReason.FleeingMat, new BoutTime(2, 50));
        Assert.Equal(Red, BoutEndConditions.LostByCautions(sheet, Rules.Current));

        BoutResult result = BoutResult.Win(Blue, VictoryType.ThreeCautions, sheet, Rules.Current);
        Assert.Equal("VCA", result.Code);
        Assert.Equal(5, result.BlueClassificationPoints);
    }

    [Theory]
    [InlineData(DoubleLossType.DoubleDisqualification, "2DSQ")]
    [InlineData(DoubleLossType.DoubleInjury, "2VIN")]
    [InlineData(DoubleLossType.DoubleForfeit, "2VFO")]
    public void DoubleLoss_GivesNoWinnerAndZeroPoints(DoubleLossType type, string code)
    {
        BoutResult result = BoutResult.DoubleLossResult(type, new BoutScoreSheet(), Rules.Current);
        Assert.Null(result.Winner);
        Assert.Equal(code, result.Code);
        Assert.Equal(0, result.RedClassificationPoints);
        Assert.Equal(0, result.BlueClassificationPoints);
    }

    [Fact]
    public void Win_RejectsPointsType()
    {
        Assert.Throws<ArgumentException>(() => BoutResult.Win(Red, VictoryType.Points, new BoutScoreSheet(), Rules.Current));
    }

    [Fact]
    public void ScoreSheet_TracksPassivityCallsAcrossBothWrestlers()
    {
        var sheet = new BoutScoreSheet();
        Assert.Equal(1, sheet.CallPassivity(Blue, new BoutTime(1, 10)).CallNumber);
        Assert.Equal(2, sheet.CallPassivity(Red, new BoutTime(1, 20)).CallNumber);
        Assert.Equal(1, sheet.PassivityCallsAgainst(Blue));
        Assert.Equal(2, sheet.PassivityCalls);
        Assert.Equal(1, Rules.Current.Passivity.GrecoRoman.PointsForCall(sheet.PassivityCalls));
    }

    [Fact]
    public void ScoreSheet_RejectsOutOfOrderEntries()
    {
        var sheet = new BoutScoreSheet();
        sheet.AwardPoints(Red, 2, PointSource.Hold, new BoutTime(2, 0));
        Assert.Throws<ArgumentException>(() => sheet.AwardPoints(Blue, 1, PointSource.StepOut, new BoutTime(1, 500)));
        Assert.Throws<ArgumentOutOfRangeException>(() => sheet.AwardPoints(Blue, 0, PointSource.StepOut, new BoutTime(2, 1)));
    }
}
