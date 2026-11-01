using System.Numerics;
using Wrestling.Core.Numerics;

namespace Wrestling.Core.Tests.Numerics;

public class Fix64Tests
{
    private static readonly BigInteger OneBig = BigInteger.One << Fix64.FractionalBits;

    [Fact]
    public void FromInt_RoundTrips()
    {
        Assert.Equal(5, Fix64.FromInt(5).FloorToInt());
        Assert.Equal(-7, Fix64.FromInt(-7).FloorToInt());
        Assert.Equal(1L << 32, Fix64.One.Raw);
    }

    [Fact]
    public void Parse_ReadsInvariantDecimals()
    {
        Assert.Equal(Fix64.Half, Fix64.Parse("0.5"));
        Assert.Equal(Fix64.FromInt(-2) - Fix64.FromRatio(1, 4), Fix64.Parse("-2.25"));
        Assert.Equal(429496730L, Fix64.Parse("0.1").Raw); // 0.1 * 2^32 = 429496729.6, rounded
        Assert.Equal("3.5", Fix64.Parse("3.5").ToString());
    }

    [Fact]
    public void Parse_RejectsOutOfRange()
    {
        Assert.Throws<OverflowException>(() => Fix64.Parse("3000000000"));
    }

    [Theory]
    [InlineData("0.5", "0.5", "0.25")]
    [InlineData("-1.5", "2", "-3")]
    [InlineData("-1.5", "-2", "3")]
    [InlineData("1000", "0.001", "1")]
    public void Multiply_KnownValues(string a, string b, string expected)
    {
        Fix64 product = Fix64.Parse(a) * Fix64.Parse(b);
        Assert.Equal(Fix64.Parse(expected).ToDecimal(), decimal.Round(product.ToDecimal(), 6));
    }

    [Fact]
    public void Multiply_RoundsHalfAwayFromZero()
    {
        Assert.Equal(Fix64.Epsilon, Fix64.Epsilon * Fix64.Half);
        Assert.Equal(-Fix64.Epsilon, -Fix64.Epsilon * Fix64.Half);
    }

    [Fact]
    public void Multiply_FractionalFastPathMatchesExactArithmeticAtBoundaries()
    {
        long[] values = { 0, 1, -1, 1L << 31, -(1L << 31), uint.MaxValue, -(long)uint.MaxValue, 1L << 32, -(1L << 32) };
        foreach (long a in values)
        {
            foreach (long b in values)
            {
                long expected = (long)RoundedDivide(new BigInteger(a) * b, OneBig);
                Assert.Equal(expected, (Fix64.FromRaw(a) * Fix64.FromRaw(b)).Raw);
            }
        }
    }

    [Fact]
    public void Multiply_ThrowsOnOverflow()
    {
        Fix64 big = Fix64.FromInt(100_000);
        Assert.Throws<OverflowException>(() => big * big);
    }

    [Theory]
    [InlineData(7, 2, "3.5")]
    [InlineData(-7, 2, "-3.5")]
    [InlineData(1, -4, "-0.25")]
    public void Divide_KnownValues(int a, int b, string expected)
    {
        Assert.Equal(Fix64.Parse(expected), Fix64.FromInt(a) / Fix64.FromInt(b));
    }

    [Fact]
    public void Divide_RoundsToNearest()
    {
        Assert.Equal(1431655765L, (Fix64.One / Fix64.FromInt(3)).Raw);   // 2^32 / 3 = 1431655765.33
        Assert.Equal(2863311531L, (Fix64.FromInt(2) / Fix64.FromInt(3)).Raw); // 2863311530.67
        Assert.Equal(-1431655765L, (-Fix64.One / Fix64.FromInt(3)).Raw);
    }

    [Fact]
    public void Divide_ThrowsOnZeroAndOverflow()
    {
        Assert.Throws<DivideByZeroException>(() => Fix64.One / Fix64.Zero);
        Assert.Throws<OverflowException>(() => Fix64.FromInt(1_000_000) / Fix64.Epsilon);
    }

    [Fact]
    public void AddAndSubtract_ThrowOnOverflow()
    {
        Assert.Throws<OverflowException>(() => Fix64.MaxValue + Fix64.Epsilon);
        Assert.Throws<OverflowException>(() => Fix64.MinValue - Fix64.Epsilon);
        Assert.Throws<OverflowException>(() => -Fix64.MinValue);
    }

    [Theory]
    [InlineData("2.5", "2", "3")]
    [InlineData("-2.5", "-3", "-2")]
    [InlineData("4", "4", "4")]
    [InlineData("-4", "-4", "-4")]
    public void FloorAndCeiling(string value, string floor, string ceiling)
    {
        Fix64 x = Fix64.Parse(value);
        Assert.Equal(Fix64.Parse(floor), Fix64.Floor(x));
        Assert.Equal(Fix64.Parse(ceiling), Fix64.Ceiling(x));
    }

    [Fact]
    public void MinMaxClampAbs()
    {
        Fix64 a = Fix64.FromInt(-3);
        Fix64 b = Fix64.FromInt(2);
        Assert.Equal(a, Fix64.Min(a, b));
        Assert.Equal(b, Fix64.Max(a, b));
        Assert.Equal(Fix64.FromInt(3), Fix64.Abs(a));
        Assert.Equal(b, Fix64.Clamp(Fix64.FromInt(9), a, b));
        Assert.Equal(a, Fix64.Clamp(Fix64.FromInt(-9), a, b));
        Assert.Throws<ArgumentException>(() => Fix64.Clamp(Fix64.Zero, b, a));
    }

    /// <summary>Compares multiply and divide against exact BigInteger arithmetic with the same rounding rule.</summary>
    [Fact]
    public void MultiplyAndDivide_MatchExactReference()
    {
        var random = new Random(20261027);
        for (int i = 0; i < 20_000; i++)
        {
            long a = RandomRaw(random);
            long b = RandomRaw(random);

            BigInteger exactProduct = RoundedDivide(new BigInteger(a) * b, OneBig);
            if (exactProduct >= long.MinValue && exactProduct <= long.MaxValue)
            {
                Assert.Equal((long)exactProduct, (Fix64.FromRaw(a) * Fix64.FromRaw(b)).Raw);
            }
            else
            {
                Assert.Throws<OverflowException>(() => Fix64.FromRaw(a) * Fix64.FromRaw(b));
            }

            if (b == 0)
            {
                continue;
            }

            BigInteger exactQuotient = RoundedDivide(new BigInteger(a) * OneBig, b);
            if (exactQuotient >= long.MinValue && exactQuotient <= long.MaxValue)
            {
                Assert.Equal((long)exactQuotient, (Fix64.FromRaw(a) / Fix64.FromRaw(b)).Raw);
            }
            else
            {
                Assert.Throws<OverflowException>(() => Fix64.FromRaw(a) / Fix64.FromRaw(b));
            }
        }
    }

    /// <summary>Raw values spread across magnitudes, from tiny fractions to near the range limit.</summary>
    private static long RandomRaw(Random random)
    {
        int bits = random.Next(1, 63);
        long magnitude = random.NextInt64(0, long.MaxValue) >> (63 - bits);
        return random.Next(2) == 0 ? magnitude : -magnitude;
    }

    private static BigInteger RoundedDivide(BigInteger numerator, BigInteger denominator)
    {
        bool negative = (numerator.Sign < 0) != (denominator.Sign < 0);
        BigInteger n = BigInteger.Abs(numerator);
        BigInteger d = BigInteger.Abs(denominator);
        BigInteger quotient = BigInteger.DivRem(n, d, out BigInteger remainder);
        if (remainder * 2 >= d)
        {
            quotient += 1;
        }

        return negative ? -quotient : quotient;
    }
}
