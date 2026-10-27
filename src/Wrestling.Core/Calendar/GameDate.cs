using System;
using System.Globalization;

namespace Wrestling.Core.Calendar
{
    public enum Weekday
    {
        Monday,
        Tuesday,
        Wednesday,
        Thursday,
        Friday,
        Saturday,
        Sunday,
    }

    /// <summary>
    /// A calendar date in the proleptic Gregorian calendar, stored as days since 1970-01-01.
    /// The simulation never reads the system clock; all time comes from game dates.
    /// </summary>
    public readonly struct GameDate : IEquatable<GameDate>, IComparable<GameDate>
    {
        private GameDate(int dayNumber)
        {
            DayNumber = dayNumber;
        }

        /// <summary>Days since 1970-01-01 (which is day 0, a Thursday).</summary>
        public int DayNumber { get; }

        public int Year => ToCivil(DayNumber).Year;

        public int Month => ToCivil(DayNumber).Month;

        public int Day => ToCivil(DayNumber).Day;

        public Weekday Weekday => (Weekday)Mod(DayNumber + 3, 7);

        public static GameDate FromDayNumber(int dayNumber) => new GameDate(dayNumber);

        public static GameDate Create(int year, int month, int day)
        {
            if (month < 1 || month > 12)
            {
                throw new ArgumentOutOfRangeException(nameof(month));
            }

            if (day < 1 || day > DaysInMonth(year, month))
            {
                throw new ArgumentOutOfRangeException(nameof(day));
            }

            return new GameDate(FromCivil(year, month, day));
        }

        /// <summary>Parses an ISO date such as "2008-03-14".</summary>
        public static GameDate Parse(string text)
        {
            if (text == null || text.Length != 10 || text[4] != '-' || text[7] != '-')
            {
                throw new FormatException($"'{text}' is not a yyyy-MM-dd date.");
            }

            int year = int.Parse(text.Substring(0, 4), NumberStyles.None, CultureInfo.InvariantCulture);
            int month = int.Parse(text.Substring(5, 2), NumberStyles.None, CultureInfo.InvariantCulture);
            int day = int.Parse(text.Substring(8, 2), NumberStyles.None, CultureInfo.InvariantCulture);
            return Create(year, month, day);
        }

        public static bool IsLeapYear(int year) => (year % 4 == 0 && year % 100 != 0) || year % 400 == 0;

        public static int DaysInMonth(int year, int month) => month switch
        {
            2 => IsLeapYear(year) ? 29 : 28,
            4 or 6 or 9 or 11 => 30,
            _ => 31,
        };

        public static int operator -(GameDate a, GameDate b) => a.DayNumber - b.DayNumber;

        public static bool operator ==(GameDate a, GameDate b) => a.DayNumber == b.DayNumber;

        public static bool operator !=(GameDate a, GameDate b) => a.DayNumber != b.DayNumber;

        public static bool operator <(GameDate a, GameDate b) => a.DayNumber < b.DayNumber;

        public static bool operator >(GameDate a, GameDate b) => a.DayNumber > b.DayNumber;

        public static bool operator <=(GameDate a, GameDate b) => a.DayNumber <= b.DayNumber;

        public static bool operator >=(GameDate a, GameDate b) => a.DayNumber >= b.DayNumber;

        public GameDate AddDays(int days) => new GameDate(checked(DayNumber + days));

        public GameDate AddWeeks(int weeks) => AddDays(checked(weeks * 7));

        /// <summary>The Monday on or before this date; career weeks start on Mondays.</summary>
        public GameDate StartOfWeek() => AddDays(-(int)Weekday);

        /// <summary>Age reached during <paramref name="year"/>, as UWW age categories use (Art. 7).</summary>
        public int AgeInYear(int year) => year - Year;

        /// <summary>Exact age in whole years on <paramref name="onDate"/>.</summary>
        public int AgeOn(GameDate onDate)
        {
            int age = onDate.Year - Year;
            if (onDate.Month < Month || (onDate.Month == Month && onDate.Day < Day))
            {
                age--;
            }

            return age;
        }

        public bool Equals(GameDate other) => DayNumber == other.DayNumber;

        public override bool Equals(object? obj) => obj is GameDate other && Equals(other);

        public override int GetHashCode() => DayNumber;

        public int CompareTo(GameDate other) => DayNumber.CompareTo(other.DayNumber);

        public override string ToString()
        {
            (int y, int m, int d) = ToCivil(DayNumber);
            return string.Format(CultureInfo.InvariantCulture, "{0:0000}-{1:00}-{2:00}", y, m, d);
        }

        private static int Mod(int value, int divisor) => ((value % divisor) + divisor) % divisor;

        // Civil-from-days and days-from-civil algorithms (Howard Hinnant), exact for all int ranges used here.
        private static int FromCivil(int year, int month, int day)
        {
            int y = month <= 2 ? year - 1 : year;
            int era = (y >= 0 ? y : y - 399) / 400;
            int yearOfEra = y - (era * 400);
            int dayOfYear = ((153 * (month + (month > 2 ? -3 : 9))) + 2) / 5 + day - 1;
            int dayOfEra = (yearOfEra * 365) + (yearOfEra / 4) - (yearOfEra / 100) + dayOfYear;
            return (era * 146097) + dayOfEra - 719468;
        }

        private static (int Year, int Month, int Day) ToCivil(int dayNumber)
        {
            int z = dayNumber + 719468;
            int era = (z >= 0 ? z : z - 146096) / 146097;
            int dayOfEra = z - (era * 146097);
            int yearOfEra = (dayOfEra - (dayOfEra / 1460) + (dayOfEra / 36524) - (dayOfEra / 146096)) / 365;
            int y = yearOfEra + (era * 400);
            int dayOfYear = dayOfEra - ((365 * yearOfEra) + (yearOfEra / 4) - (yearOfEra / 100));
            int mp = ((5 * dayOfYear) + 2) / 153;
            int d = dayOfYear - (((153 * mp) + 2) / 5) + 1;
            int m = mp + (mp < 10 ? 3 : -9);
            return (m <= 2 ? y + 1 : y, m, d);
        }
    }
}
