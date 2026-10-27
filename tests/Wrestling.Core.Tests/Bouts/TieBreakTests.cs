using Wrestling.Core.Bouts;
using Wrestling.Core.Rules;
using static Wrestling.Core.Rules.Corner;

namespace Wrestling.Core.Tests.Bouts;

/// <summary>Every example table in Art. 29 of the UWW rules (January 2026), plus edge cases.</summary>
public class TieBreakTests
{
    [Fact]
    public void Example1_LastTechnicalPointWins()
    {
        // Red 1 1 1, Blue 1 1 1; blue scored last.
        var sheet = new SheetBuilder()
            .Hold(Red, 1).Hold(Blue, 1).Hold(Red, 1).Hold(Blue, 1).Hold(Red, 1).Hold(Blue, 1).Sheet;
        AssertWinner(sheet, Blue, TieBreakCriterion.LastTechnicalPoint);
    }

    [Fact]
    public void Example2_HighestValueHoldBeatsLastPoint()
    {
        // Red 1 2, Blue 1 1 1; blue scored last but red has a 2-point hold.
        var sheet = new SheetBuilder()
            .Hold(Red, 1).Hold(Blue, 1).Hold(Red, 2).Hold(Blue, 1).Hold(Blue, 1).Sheet;
        AssertWinner(sheet, Red, TieBreakCriterion.HighestValueHold);
    }

    [Fact]
    public void Example3_FewerCautionsBeatsLastPoint()
    {
        // Red scored last but has two cautions, blue one.
        var sheet = new SheetBuilder()
            .CautionOnly(Red).CautionOnly(Red).CautionOnly(Blue)
            .Hold(Blue, 1).Hold(Blue, 1).Hold(Blue, 1)
            .Hold(Red, 1).Hold(Red, 1).Hold(Red, 1).Sheet;
        AssertWinner(sheet, Blue, TieBreakCriterion.FewestCautions);
    }

    [Fact]
    public void Example4_TwoPointHoldWins()
    {
        // Red 1 1 1 1, Blue 2 + caution.
        var sheet = new SheetBuilder()
            .Hold(Red, 1).Hold(Red, 1).Hold(Blue, 2).CautionOnly(Blue).Hold(Red, 1).Hold(Red, 1).Hold(Blue, 2).Sheet;
        AssertWinner(sheet, Blue, TieBreakCriterion.HighestValueHold);
    }

    [Fact]
    public void Example5_TwoPointHoldBeatsLastPoint()
    {
        // Each one caution; red scored last, blue has the 2-point hold.
        var sheet = new SheetBuilder()
            .CautionOnly(Red).CautionOnly(Blue)
            .Hold(Blue, 1).Hold(Blue, 2).Hold(Red, 1).Hold(Red, 1).Hold(Red, 1).Sheet;
        AssertWinner(sheet, Blue, TieBreakCriterion.HighestValueHold);
    }

    [Fact]
    public void Example6_EqualCautions_LastTechnicalPointWins()
    {
        var sheet = new SheetBuilder()
            .CautionOnly(Red).CautionOnly(Red).CautionOnly(Blue).CautionOnly(Blue)
            .Hold(Red, 1).Hold(Red, 1).Hold(Red, 1).Hold(Blue, 1).Hold(Blue, 1).Hold(Blue, 1).Sheet;
        AssertWinner(sheet, Blue, TieBreakCriterion.LastTechnicalPoint);
    }

    [Fact]
    public void Greco_1_1_BothPassivity_FirstPointWins()
    {
        var sheet = new SheetBuilder().Passivity(Red).Passivity(Blue).Sheet;
        AssertWinner(sheet, Red, TieBreakCriterion.FirstPoint);
    }

    [Fact]
    public void Greco_1_1_FirstScorerHasCaution_Loses()
    {
        var sheet = new SheetBuilder().Passivity(Red).CautionOnly(Red).Passivity(Blue).Sheet;
        AssertWinner(sheet, Blue, TieBreakCriterion.FewestCautions);
    }

    [Fact]
    public void Greco_1_1_BothCautioned_FirstPassivityWins()
    {
        var sheet = new SheetBuilder().Passivity(Red).CautionOnly(Red).Passivity(Blue).CautionOnly(Blue).Sheet;
        AssertWinner(sheet, Red, TieBreakCriterion.FirstPoint);
    }

    [Fact]
    public void Greco_1_1_TechnicalPointBeatsPassivityPoint()
    {
        var sheet = new SheetBuilder().Passivity(Red).StepOut(Blue).Sheet;
        AssertWinner(sheet, Blue, TieBreakCriterion.OnlyTechnicalPoint);
    }

    [Fact]
    public void Greco_1_1_BothTechnical_LastScorerWins()
    {
        var sheet = new SheetBuilder().Hold(Blue, 1).Hold(Red, 1).Sheet;
        AssertWinner(sheet, Red, TieBreakCriterion.LastTechnicalPoint);
    }

    [Fact]
    public void Greco_1_1_LostChallengePointThenTechnical_TechnicalWins()
    {
        var sheet = new SheetBuilder().LostChallengeBy(Blue).StepOut(Blue).Sheet;
        AssertWinner(sheet, Blue, TieBreakCriterion.OnlyTechnicalPoint);
    }

    [Fact]
    public void InfringementPointsCountByValue()
    {
        // Red: 2 points from blue's negative wrestling (GR). Blue: 1 + 1 holds.
        var sheet = new SheetBuilder().Caution(Blue, 2).Hold(Blue, 1).Hold(Blue, 1).Sheet;
        AssertWinner(sheet, Red, TieBreakCriterion.HighestValueHold);
    }

    [Fact]
    public void Greco_1_1_FirstTechnicalScorerWithCaution_Loses()
    {
        // "First point scored by the red wrestler but he has a caution": cautions come before criterion 4.
        var sheet = new SheetBuilder().Hold(Red, 1).Caution(Red).Sheet;
        AssertWinner(sheet, Blue, TieBreakCriterion.FewestCautions);
    }

    [Fact]
    public void ScorelessBoutIsUndecided()
    {
        Assert.Null(TieBreak.Decide(new BoutScoreSheet()));
    }

    [Fact]
    public void ThrowsWhenScoresDiffer()
    {
        var sheet = new SheetBuilder().Hold(Red, 2).Sheet;
        Assert.Throws<InvalidOperationException>(() => TieBreak.Decide(sheet));
    }

    [Fact]
    public void RevokedEntriesAreIgnored()
    {
        var builder = new SheetBuilder().Hold(Red, 1).Hold(Blue, 1);
        PointsEntry wrongCall = builder.Sheet.AwardPoints(Red, 2, PointSource.Hold, new BoutTime(1, 100));
        builder.Sheet.AwardPoints(Blue, 2, PointSource.Hold, new BoutTime(1, 101));
        Assert.Null(builder.Sheet.Leader);

        builder.Sheet.Revoke(wrongCall);
        Assert.Equal(1, builder.Sheet.Points(Red));
        Assert.Equal(Blue, builder.Sheet.Leader);
        Assert.Throws<InvalidOperationException>(() => builder.Sheet.Revoke(wrongCall));
    }

    private static void AssertWinner(BoutScoreSheet sheet, Corner expected, TieBreakCriterion expectedCriterion)
    {
        Assert.Equal(sheet.Points(Red), sheet.Points(Blue));
        Corner? winner = TieBreak.Decide(sheet, out TieBreakCriterion? criterion);
        Assert.Equal(expected, winner);
        Assert.Equal(expectedCriterion, criterion);
    }
}
