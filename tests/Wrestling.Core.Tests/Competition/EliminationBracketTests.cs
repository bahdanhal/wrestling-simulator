using Wrestling.Core.Bouts;
using Wrestling.Core.Competition;
using Wrestling.Core.Rules;

namespace Wrestling.Core.Tests.Competition;

public class EliminationBracketTests
{
    [Fact]
    public void LineRanks_KeepSeedsApart()
    {
        Assert.Equal(new[] { 1, 8, 4, 5, 2, 7, 3, 6 }, BracketMath.LineRanks(8));
        int[] sixteen = BracketMath.LineRanks(16);
        Assert.Equal(1, sixteen[0]);
        Assert.Contains(2, sixteen[8..]);
        Assert.Equal(16, sixteen.Distinct().Count());
    }

    [Fact]
    public void EightEntrants_NoQualification_PairedTopToBottomByDraw()
    {
        EliminationBracket bracket = Create(8);
        List<BracketBout> first = bracket.Bouts.Where(b => b.Stage == BracketStage.Main && b.Round == 1).ToList();

        Assert.Equal(8, bracket.MainDrawSize);
        Assert.DoesNotContain(bracket.Bouts, b => b.Stage == BracketStage.Qualification);
        Assert.Equal(4, first.Count);
        Assert.All(first, b => Assert.Equal("1/4 Final", b.Name));
        Assert.Equal(("E01", "E02"), (first[0].Top, first[0].Bottom));
        Assert.Equal(("E07", "E08"), (first[3].Top, first[3].Bottom));
        Assert.Equal("Final", bracket.Final.Name);
    }

    [Fact]
    public void TwentyEntrants_FourQualificationBoutsSplitBetweenHalves()
    {
        EliminationBracket bracket = Create(20);
        List<BracketBout> qualification = bracket.Bouts.Where(b => b.Stage == BracketStage.Qualification).ToList();

        Assert.Equal(16, bracket.MainDrawSize);
        Assert.Equal(4, qualification.Count);
        Assert.Equal(4 + 15, bracket.Bouts.Count);

        // Each half of the main draw receives two qualification winners.
        List<BracketBout> firstRound = bracket.Bouts.Where(b => b.Stage == BracketStage.Main && b.Round == 1).ToList();
        int QualificationFeedsIn(IEnumerable<BracketBout> bouts) => bouts.Count(b => b.Top == null || b.Bottom == null);
        Assert.Equal(2, QualificationFeedsIn(firstRound.Take(4)));
        Assert.Equal(2, QualificationFeedsIn(firstRound.Skip(4)));
    }

    [Fact]
    public void Seeds_ArePlacedInDifferentQuartersAndSkipQualification()
    {
        List<Entrant> entrants = Entrants(20, seeds: 8);
        EliminationBracket bracket = EliminationBracket.Create(entrants, Rules.Current);
        List<BracketBout> firstRound = bracket.Bouts.Where(b => b.Stage == BracketStage.Main && b.Round == 1).ToList();

        int QuarterOf(string id) => firstRound.FindIndex(b => b.Involves(id)) / 2;
        Assert.Equal(4, new[] { "S1", "S2", "S3", "S4" }.Select(QuarterOf).Distinct().Count());
        Assert.DoesNotContain(bracket.Bouts.Where(b => b.Stage == BracketStage.Qualification), b => b.Top!.StartsWith('S') || b.Bottom!.StartsWith('S'));
        Assert.True(firstRound.FindIndex(b => b.Involves("S1")) < 4);
        Assert.True(firstRound.FindIndex(b => b.Involves("S2")) >= 4);
    }

    [Fact]
    public void Seeds_EnterQualificationLowestFirstWhenUnavoidable()
    {
        // 31 entrants: 16-line main draw with 15 qualification bouts; only seed 1 goes straight through.
        EliminationBracket bracket = EliminationBracket.Create(Entrants(31, seeds: 8), Rules.Current);
        List<BracketBout> qualification = bracket.Bouts.Where(b => b.Stage == BracketStage.Qualification).ToList();

        Assert.Equal(15, qualification.Count);
        Assert.DoesNotContain(qualification, b => b.Involves("S1"));
        foreach (string seed in new[] { "S2", "S3", "S8" })
        {
            Assert.Contains(qualification, b => b.Top == seed);
        }
    }

    [Fact]
    public void FullTournament_RepechageAndStandings()
    {
        EliminationBracket bracket = Create(20);
        WrestleAll(bracket);

        Assert.True(bracket.IsComplete);
        Assert.Equal(2, bracket.BronzeBouts.Count);

        IReadOnlyList<Placement> standings = bracket.Standings();
        Assert.Equal(20, standings.Count);
        Assert.Equal(new int?[] { 1, 2, 3, 3, 5, 5, 7, 8 }, standings.Take(8).Select(p => p.Rank));
        Assert.Equal(standings.Count, standings.Select(p => p.EntrantId).Distinct().Count());

        // The lowest draw number wins every bout, so E01 is champion.
        Assert.Equal("E01", standings[0].EntrantId);
    }

    [Fact]
    public void Repechage_ContainsEveryoneBeatenByEachFinalist_EarliestRoundFirst()
    {
        EliminationBracket bracket = Create(20);
        WrestleAll(bracket);

        foreach (string finalist in new[] { bracket.Final.Top!, bracket.Final.Bottom! })
        {
            List<string> beaten = bracket.Bouts
                .Where(b => b.Stage != BracketStage.Repechage && b != bracket.Final && b.Winner == finalist)
                .Select(b => b.Loser!)
                .ToList();
            List<string> inRepechage = bracket.Bouts
                .Where(b => b.Stage == BracketStage.Repechage)
                .SelectMany(b => new[] { b.Top!, b.Bottom! })
                .Distinct()
                .Where(beaten.Contains)
                .ToList();

            Assert.Equal(beaten.OrderBy(x => x), inRepechage.OrderBy(x => x));
        }

        // Each bronze bout is against the semi-final loser of the same half.
        BracketBout[] semis = bracket.Bouts.Where(b => b.Name == "1/2 Final").ToArray();
        Assert.Equal(semis[0].Loser, bracket.BronzeBouts[0].Bottom);
        Assert.Equal(semis[1].Loser, bracket.BronzeBouts[1].Bottom);
    }

    [Fact]
    public void Withdrawal_GivesOpponentAForfeitWin_AndLeavesWithdrawnUnranked()
    {
        EliminationBracket bracket = Create(8);
        WrestleUntilRepechage(bracket);
        BracketBout repechage = bracket.ReadyBouts().First(b => b.Stage == BracketStage.Repechage);
        string withdrawing = repechage.Bottom!;

        bracket.Withdraw(withdrawing, EntrantStatus.Unranked);

        Assert.True(repechage.IsDecided);
        Assert.Equal(repechage.Top, repechage.Winner);
        Assert.Equal("VFO", repechage.Result!.Code);

        WrestleAll(bracket);
        Placement placement = bracket.Standings().Single(p => p.EntrantId == withdrawing);
        Assert.Null(placement.Rank);
        Assert.Equal(withdrawing, bracket.Standings().Last().EntrantId);
    }

    [Fact]
    public void InjuredLoser_IsRankedButSkipsRepechage()
    {
        EliminationBracket bracket = Create(8);

        // E01 beats E02 by injury in the first round; E01 then wins its way to the final.
        BracketBout first = bracket.ReadyBouts().First();
        bracket.RecordResult(first.Id, BoutResult.Win(Corner.Red, VictoryType.Injury, new BoutScoreSheet(), Rules.Current));
        WrestleAll(bracket);

        Assert.Equal(EntrantStatus.Injured, bracket.StatusOf("E02"));
        Assert.DoesNotContain(bracket.Bouts.Where(b => b.Stage == BracketStage.Repechage), b => b.Involves("E02"));
        Assert.NotNull(bracket.Standings().Single(p => p.EntrantId == "E02").Rank);
    }

    [Fact]
    public void RecordResult_RejectsBoutsThatAreNotReady()
    {
        EliminationBracket bracket = Create(8);
        Assert.Throws<InvalidOperationException>(() => bracket.RecordResult(bracket.Final.Id, PointsWin(Corner.Red)));
        Assert.Throws<InvalidOperationException>(() => bracket.Standings());
    }

    [Theory]
    [InlineData(7)]
    public void RejectsTooFewEntrants(int count)
    {
        Assert.Throws<ArgumentException>(() => Create(count));
    }

    [Fact]
    public void RejectsBrokenSeedsAndDuplicates()
    {
        List<Entrant> gap = Entrants(8);
        gap[0] = new Entrant("X", 99, seed: 2);
        Assert.Throws<ArgumentException>(() => EliminationBracket.Create(gap, Rules.Current));

        List<Entrant> duplicateDraw = Entrants(8);
        duplicateDraw[1] = new Entrant("Y", duplicateDraw[0].DrawNumber);
        Assert.Throws<ArgumentException>(() => EliminationBracket.Create(duplicateDraw, Rules.Current));
    }

    internal static EliminationBracket Create(int count) => EliminationBracket.Create(Entrants(count), Rules.Current);

    /// <summary>Entrants E01.. with draw numbers 1..; seeded entrants are named S1.. and get the highest draw numbers.</summary>
    internal static List<Entrant> Entrants(int count, int seeds = 0)
    {
        var list = new List<Entrant>();
        for (int i = 1; i <= count - seeds; i++)
        {
            list.Add(new Entrant($"E{i:00}", i));
        }

        for (int s = 1; s <= seeds; s++)
        {
            list.Add(new Entrant($"S{s}", count - seeds + s, seed: s));
        }

        return list;
    }

    /// <summary>Wrestles every ready bout; the participant with the lower draw number wins 4–1.</summary>
    internal static void WrestleAll(EliminationBracket bracket)
    {
        while (bracket.ReadyBouts().Any())
        {
            BracketBout bout = bracket.ReadyBouts().First();
            int top = bracket.Entrants.Single(e => e.Id == bout.Top).DrawNumber;
            int bottom = bracket.Entrants.Single(e => e.Id == bout.Bottom).DrawNumber;
            bracket.RecordResult(bout.Id, PointsWin(top < bottom ? Corner.Red : Corner.Blue));
        }
    }

    private static void WrestleUntilRepechage(EliminationBracket bracket)
    {
        while (!bracket.ReadyBouts().Any(b => b.Stage == BracketStage.Repechage))
        {
            BracketBout bout = bracket.ReadyBouts().First(b => b.Stage != BracketStage.Repechage);
            bracket.RecordResult(bout.Id, PointsWin(Corner.Red));
        }
    }

    internal static BoutResult PointsWin(Corner winner)
    {
        var sheet = new BoutScoreSheet();
        sheet.AwardPoints(winner, 4, PointSource.Hold, new BoutTime(1, 0));
        sheet.AwardPoints(winner.Opponent(), 1, PointSource.StepOut, new BoutTime(1, 1));
        return BoutResult.AtEndOfRegularTime(sheet, Rules.Current);
    }
}
