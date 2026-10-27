using Wrestling.Core.Bouts;
using Wrestling.Core.Competition;
using Wrestling.Core.Rules;
using static Wrestling.Core.Tests.Competition.EliminationBracketTests;

namespace Wrestling.Core.Tests.Competition;

public class DualMeetTests
{
    private static readonly int[] Categories = { 57, 61, 65, 70, 74, 79, 86, 92, 97, 125 };

    [Fact]
    public void MoreBoutsWins()
    {
        DualMeet meet = Meet(homeWins: 6);
        Assert.Equal(MeetSide.Home, meet.Winner());
        Assert.Equal("IRI", meet.WinningTeam());
        Assert.Equal(6, meet.Totals(MeetSide.Home).BoutsWon);
    }

    [Fact]
    public void FiveFive_DecidedByClassificationPoints()
    {
        var meet = new DualMeet("IRI", "USA", Categories);
        for (int i = 0; i < 10; i++)
        {
            bool home = i < 5;
            // The away team wins one of its bouts by fall (5 points instead of 3).
            BoutResult result = !home && i == 9
                ? BoutResult.Win(Corner.Blue, VictoryType.Fall, new BoutScoreSheet(), Rules.Current)
                : PointsWin(home ? Corner.Red : Corner.Blue);
            meet.Record(Categories[i], result);
        }

        Assert.Equal(MeetSide.Away, meet.Winner());
    }

    [Fact]
    public void FullyLevelMeet_DecidedByLastBout()
    {
        var meet = new DualMeet("IRI", "USA", new[] { 57, 61 });
        meet.Record(61, PointsWin(Corner.Red));
        meet.Record(57, PointsWin(Corner.Blue)); // wrestled last
        Assert.Equal(MeetSide.Away, meet.Winner());
    }

    [Fact]
    public void Forfeit_GivesTheBoutToTheOtherTeam()
    {
        var meet = new DualMeet("IRI", "USA", new[] { 57 });
        meet.RecordForfeit(57, MeetSide.Away, Rules.Current);
        Assert.Equal("VFO", meet.ResultFor(57)!.Code);
        Assert.Equal(MeetSide.Home, meet.Winner());
    }

    [Fact]
    public void RejectsUnknownOrRepeatedCategory()
    {
        var meet = new DualMeet("IRI", "USA", new[] { 57 });
        Assert.Throws<ArgumentException>(() => meet.Record(58, PointsWin(Corner.Red)));
        meet.Record(57, PointsWin(Corner.Red));
        Assert.Throws<InvalidOperationException>(() => meet.Record(57, PointsWin(Corner.Red)));
        Assert.Throws<ArgumentException>(() => new DualMeet("IRI", "IRI", Categories));
    }

    [Fact]
    public void TeamGroup_EveryTeamMeetsEveryOther()
    {
        var group = new TeamGroup(Teams("A", "B", "C", "D"), Categories);
        Assert.Equal(6, group.Meets.Count);
        Assert.Equal(3, group.Rounds);
    }

    [Fact]
    public void TeamGroup_TwoTeamsLevel_DirectMeetDecides()
    {
        // A and B finish on 2 wins each, C and D on 1. A has more classification points (two falls),
        // but B beat A, and D beat C, so the direct meets decide both ties.
        var group = new TeamGroup(Teams("A", "B", "C", "D"), new[] { 57 });
        Win(group, "B", "A");
        Win(group, "A", "C", VictoryType.Fall);
        Win(group, "A", "D", VictoryType.Fall);
        Win(group, "B", "D");
        Win(group, "C", "B");
        Win(group, "D", "C");

        Assert.Equal(new[] { "B", "A", "D", "C" }, group.Ranking());
    }

    [Fact]
    public void TeamGroup_ThreeWayTie_UsesClassificationPoints()
    {
        // One win each. Classification points: B 5 + 1 = 6, C 1 + 3 = 4, A 0 + 3 = 3.
        var group = new TeamGroup(Teams("A", "B", "C"), new[] { 57 });
        Win(group, "B", "A", VictoryType.Fall);
        Win(group, "A", "C");
        Win(group, "C", "B");

        Assert.Equal(new[] { "B", "C", "A" }, group.Ranking());
    }

    [Fact]
    public void TeamGroup_FullyLevel_LowestDrawNumberFirst()
    {
        var group = new TeamGroup(Teams("A", "B", "C"), new[] { 57 });
        Win(group, "B", "A");
        Win(group, "A", "C");
        Win(group, "C", "B");

        Assert.Equal(new[] { "A", "B", "C" }, group.Ranking());
    }

    private static DualMeet Meet(int homeWins)
    {
        var meet = new DualMeet("IRI", "USA", Categories);
        for (int i = 0; i < Categories.Length; i++)
        {
            meet.Record(Categories[i], PointsWin(i < homeWins ? Corner.Red : Corner.Blue));
        }

        return meet;
    }

    private static List<TeamEntry> Teams(params string[] names) => names.Select((n, i) => new TeamEntry(n, i + 1)).ToList();

    private static void Win(TeamGroup group, string winner, string loser, VictoryType type = VictoryType.Points)
    {
        DualMeet meet = group.MeetBetween(winner, loser);
        Corner corner = meet.HomeTeam == winner ? Corner.Red : Corner.Blue;
        foreach (int weight in meet.WeightCategoriesKg)
        {
            meet.Record(weight, type == VictoryType.Points ? PointsWin(corner) : BoutResult.Win(corner, type, new BoutScoreSheet(), Rules.Current));
        }
    }
}
