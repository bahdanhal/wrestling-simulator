using Wrestling.Core.Bouts;
using Wrestling.Core.Competition;
using Wrestling.Core.Rules;
using static Wrestling.Core.Tests.Competition.EliminationBracketTests;

namespace Wrestling.Core.Tests.Competition;

public class SmallCategoryTests
{
    [Theory]
    [InlineData(1, CompetitionSystem.Cancelled)]
    [InlineData(2, CompetitionSystem.Nordic)]
    [InlineData(5, CompetitionSystem.Nordic)]
    [InlineData(6, CompetitionSystem.TwoPools)]
    [InlineData(7, CompetitionSystem.TwoPools)]
    [InlineData(8, CompetitionSystem.Elimination)]
    [InlineData(40, CompetitionSystem.Elimination)]
    public void SystemDependsOnEntrantCount(int count, CompetitionSystem expected)
    {
        Assert.Equal(expected, CompetitionSystems.ForEntrantCount(count));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 3)]
    [InlineData(4, 6)]
    [InlineData(5, 10)]
    public void Nordic_EveryoneMeetsEveryoneOnce(int count, int bouts)
    {
        NordicGroup group = NordicGroup.Create(Entrants(count), Rules.Current);

        Assert.Equal(bouts, group.Bouts.Count);
        var pairs = group.Bouts.Select(b => string.Join("-", new[] { b.Top, b.Bottom }.OrderBy(x => x))).ToList();
        Assert.Equal(pairs.Count, pairs.Distinct().Count());
        Assert.Equal(count % 2 == 0 ? count - 1 : count, group.Rounds);
    }

    [Fact]
    public void Nordic_NoWrestlerHasTwoBoutsInOneRound()
    {
        NordicGroup group = NordicGroup.Create(Entrants(5), Rules.Current);
        foreach (IGrouping<int, BracketBout> round in group.Bouts.GroupBy(b => b.Round))
        {
            var wrestlers = round.SelectMany(b => new[] { b.Top!, b.Bottom! }).ToList();
            Assert.Equal(wrestlers.Count, wrestlers.Distinct().Count());
        }
    }

    [Fact]
    public void Nordic_RanksByWins()
    {
        NordicGroup group = NordicGroup.Create(Entrants(4), Rules.Current);
        WrestleAllFormat(group);

        Assert.Equal(new[] { "E01", "E02", "E03", "E04" }, group.Ranking());
        Assert.Equal(new int?[] { 1, 2, 3, 4 }, group.Standings().Select(p => p.Rank));
    }

    [Fact]
    public void Nordic_ThreeWayTieUsesCriteria()
    {
        NordicGroup group = NordicGroup.Create(Entrants(3), Rules.Current);
        RecordBetween(group, "E01", "E03", winner: "E01", VictoryType.Fall);
        RecordBetween(group, "E02", "E03", winner: "E03", VictoryType.Points);
        RecordBetween(group, "E01", "E02", winner: "E02", VictoryType.Points);

        // All three have one win, so classification points decide: E01 6 (5 + 1), E02 4, E03 3.
        Assert.Equal(new[] { "E01", "E02", "E03" }, group.Ranking());
    }

    [Fact]
    public void Nordic_TwoWayTieUsesDirectBout()
    {
        NordicGroup group = NordicGroup.Create(Entrants(4), Rules.Current);
        // E01 and E02 both finish with two wins; E02 won their bout, E01 won the others by fall.
        RecordBetween(group, "E01", "E02", winner: "E02", VictoryType.Points);
        RecordBetween(group, "E01", "E03", winner: "E01", VictoryType.Fall);
        RecordBetween(group, "E01", "E04", winner: "E01", VictoryType.Fall);
        RecordBetween(group, "E02", "E03", winner: "E03", VictoryType.Points);
        RecordBetween(group, "E02", "E04", winner: "E02", VictoryType.Points);
        RecordBetween(group, "E03", "E04", winner: "E04", VictoryType.Points);

        IReadOnlyList<string> ranking = group.Ranking();
        Assert.Equal("E02", ranking[0]);
        Assert.Equal("E01", ranking[1]);
    }

    [Fact]
    public void Nordic_WithdrawnWrestlerForfeitsRemainingBoutsAndIsUnranked()
    {
        NordicGroup group = NordicGroup.Create(Entrants(4), Rules.Current);
        group.Withdraw("E04", EntrantStatus.Unranked);
        WrestleAllFormat(group);

        Assert.All(group.Bouts.Where(b => b.Involves("E04")), b => Assert.Equal("VFO", b.Result!.Code));
        IReadOnlyList<Placement> standings = group.Standings();
        Assert.Null(standings.Single(p => p.EntrantId == "E04").Rank);
        Assert.Equal(3, standings.Count(p => p.Rank.HasValue));
    }

    [Theory]
    [InlineData(6, 3, 3)]
    [InlineData(7, 4, 3)]
    public void TwoPools_SplitsEvenlyByDraw(int count, int sizeA, int sizeB)
    {
        TwoPoolTournament tournament = TwoPoolTournament.Create(Entrants(count), Rules.Current);
        Assert.Equal(sizeA, tournament.PoolA.Members.Count);
        Assert.Equal(sizeB, tournament.PoolB.Members.Count);
        Assert.Equal("E01", tournament.PoolA.Members[0]);
        Assert.Equal("E02", tournament.PoolB.Members[0]);
    }

    [Fact]
    public void TwoPools_SeedsFollowArticle12()
    {
        TwoPoolTournament tournament = TwoPoolTournament.Create(Entrants(7, seeds: 7), Rules.Current);
        Assert.Equal(new[] { "S1", "S4", "S5", "S7" }, tournament.PoolA.Members);
        Assert.Equal(new[] { "S2", "S3", "S6" }, tournament.PoolB.Members);
    }

    [Fact]
    public void TwoPools_SemiFinalsCrossPools_AndOneBronze()
    {
        TwoPoolTournament tournament = TwoPoolTournament.Create(Entrants(7), Rules.Current);
        WrestleAllFormat(tournament);

        BracketBout[] semis = tournament.Bouts.Where(b => b.Name == "1/2 Final").ToArray();
        Assert.Equal((tournament.PoolA.Ranking()[0], tournament.PoolB.Ranking()[1]), (semis[0].Top, semis[0].Bottom));
        Assert.Equal((tournament.PoolA.Ranking()[1], tournament.PoolB.Ranking()[0]), (semis[1].Top, semis[1].Bottom));

        IReadOnlyList<Placement> standings = tournament.Standings();
        Assert.Equal(new int?[] { 1, 2, 3, 4, 5, 6, 7 }, standings.Select(p => p.Rank));
        Assert.True(semis[0].Loser == tournament.Bronze!.Winner || semis[1].Loser == tournament.Bronze.Winner);
    }

    [Fact]
    public void CompetitionSystems_CreatesMatchingFormat()
    {
        Assert.IsType<NordicGroup>(CompetitionSystems.Create(Entrants(5), Rules.Current));
        Assert.IsType<TwoPoolTournament>(CompetitionSystems.Create(Entrants(6), Rules.Current));
        Assert.IsType<EliminationBracket>(CompetitionSystems.Create(Entrants(9), Rules.Current));
        Assert.Throws<InvalidOperationException>(() => CompetitionSystems.Create(Entrants(1), Rules.Current));
    }

    [Fact]
    public void TeamPoints_Article32()
    {
        TeamRankingRules team = Rules.Current.TeamRanking;
        Assert.Equal(new[] { 25, 20, 15, 12, 10, 9, 8, 6, 4, 2 }, Enumerable.Range(1, 10).Select(team.PointsFor));
        Assert.Equal(0, team.PointsFor(11));
    }

    [Fact]
    public void TeamStandings_OnlyBestWrestlerPerCountryScores_AndTiesUseMostWins()
    {
        // Category 1: TUR first and second, UKR and GER third. Category 2: GER first, UKR second.
        var category1 = new List<Placement> { new("tur1", 1), new("tur2", 2), new("ukr1", 3), new("ger1", 3) };
        var category2 = new List<Placement> { new("ger2", 1), new("ukr2", 2), new("tur3", 3) };
        string TeamOf(string id) => id[..3].ToUpperInvariant();

        IReadOnlyList<TeamScore> scores = TeamStandings.Compute(new[] { category1, category2 }, TeamOf, Rules.Current.TeamRanking);

        Assert.Equal(40, scores.Single(s => s.Team == "TUR").Points); // 25 + 15; tur2 does not score
        Assert.Equal(40, scores.Single(s => s.Team == "GER").Points); // 15 + 25
        Assert.Equal(35, scores.Single(s => s.Team == "UKR").Points);

        // TUR and GER are level on points and on places (one 1st, one 3rd each), so the order falls back to the team code.
        Assert.Equal(new[] { "GER", "TUR", "UKR" }, scores.Select(s => s.Team));
    }

    private static void WrestleAllFormat(ICompetitionFormat format)
    {
        while (format.ReadyBouts().Any())
        {
            BracketBout bout = format.ReadyBouts().First();
            int top = format.Entrants.Single(e => e.Id == bout.Top).DrawNumber;
            int bottom = format.Entrants.Single(e => e.Id == bout.Bottom).DrawNumber;
            format.RecordResult(bout.Id, PointsWin(top < bottom ? Corner.Red : Corner.Blue));
        }
    }

    private static void RecordBetween(ICompetitionFormat format, string a, string b, string winner, VictoryType type)
    {
        BracketBout bout = format.Bouts.Single(x => x.Involves(a) && x.Involves(b));
        Corner corner = bout.Top == winner ? Corner.Red : Corner.Blue;
        BoutResult result = type == VictoryType.Points
            ? PointsWin(corner)
            : BoutResult.Win(corner, type, new BoutScoreSheet(), Rules.Current);
        format.RecordResult(bout.Id, result);
    }
}
