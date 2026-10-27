using Wrestling.Core.Calendar;

namespace Wrestling.Core.Tests.Calendar;

public class GameDateTests
{
    [Fact]
    public void Epoch_IsDayZeroAndThursday()
    {
        GameDate epoch = GameDate.Create(1970, 1, 1);
        Assert.Equal(0, epoch.DayNumber);
        Assert.Equal(Weekday.Thursday, epoch.Weekday);
    }

    [Fact]
    public void MatchesSystemCalendarAcrossCenturies()
    {
        // Cross-check every day from 1900 to 2100 against System.DateTime (tests only; the game never uses it).
        var start = new DateTime(1900, 1, 1);
        GameDate date = GameDate.Create(1900, 1, 1);
        for (int i = 0; i < 73_415; i++)
        {
            DateTime expected = start.AddDays(i);
            Assert.Equal((expected.Year, expected.Month, expected.Day), (date.Year, date.Month, date.Day));
            Assert.Equal((int)expected.DayOfWeek, ((int)date.Weekday + 1) % 7);
            date = date.AddDays(1);
        }
    }

    [Theory]
    [InlineData(2024, true)]
    [InlineData(2026, false)]
    [InlineData(1900, false)]
    [InlineData(2000, true)]
    public void LeapYears(int year, bool leap)
    {
        Assert.Equal(leap, GameDate.IsLeapYear(year));
        Assert.Equal(leap ? 29 : 28, GameDate.DaysInMonth(year, 2));
    }

    [Fact]
    public void ParseAndFormatRoundTrip()
    {
        GameDate date = GameDate.Parse("2008-03-14");
        Assert.Equal("2008-03-14", date.ToString());
        Assert.Throws<FormatException>(() => GameDate.Parse("2008-3-14"));
        Assert.Throws<ArgumentOutOfRangeException>(() => GameDate.Parse("2026-02-29"));
    }

    [Fact]
    public void Ages()
    {
        GameDate birth = GameDate.Create(2008, 3, 14);
        Assert.Equal(18, birth.AgeInYear(2026));
        Assert.Equal(17, birth.AgeOn(GameDate.Create(2026, 3, 13)));
        Assert.Equal(18, birth.AgeOn(GameDate.Create(2026, 3, 14)));
    }

    [Fact]
    public void WeeksStartOnMonday()
    {
        GameDate tuesday = GameDate.Create(2026, 10, 27);
        Assert.Equal(Weekday.Tuesday, tuesday.Weekday);
        Assert.Equal(GameDate.Create(2026, 10, 26), tuesday.StartOfWeek());
        Assert.Equal(GameDate.Create(2026, 11, 3), tuesday.AddWeeks(1));
        Assert.Equal(7, tuesday.AddWeeks(1) - tuesday);
    }
}
