using System.Numerics;
using Wrestling.Core.Numerics;

namespace Wrestling.Core.Tests.Numerics;

public class GeometryTests
{
    [Theory]
    [InlineData("0", "0")]
    [InlineData("1", "1")]
    [InlineData("4", "2")]
    [InlineData("2.25", "1.5")]
    [InlineData("20.25", "4.5")]
    public void Sqrt_ExactSquares(string value, string root)
    {
        Assert.Equal(Fix64.Parse(root), Fix64.Sqrt(Fix64.Parse(value)));
    }

    [Fact]
    public void Sqrt_MatchesExactReferenceRounding()
    {
        var random = new Random(77);
        for (int i = 0; i < 20_000; i++)
        {
            long raw = random.NextInt64(0, long.MaxValue) >> random.Next(0, 63);
            BigInteger n = new BigInteger(raw) << 32;
            BigInteger root = IntegerSqrt(n);
            if (n - (root * root) > root)
            {
                root += 1;
            }

            Assert.Equal((long)root, Fix64.Sqrt(Fix64.FromRaw(raw)).Raw);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => Fix64.Sqrt(-Fix64.One));
    }

    [Fact]
    public void Vector_LengthAndNormalization()
    {
        var v = new FixVector2(Fix64.FromInt(3), Fix64.FromInt(4));
        Assert.Equal(Fix64.FromInt(25), v.LengthSquared);
        Assert.Equal(Fix64.FromInt(5), v.Length);
        FixVector2 unit = v.Normalized();
        Assert.Equal(Fix64.Parse("0.6"), Round(unit.X));
        Assert.Equal(Fix64.Parse("0.8"), Round(unit.Y));
        Assert.Equal(FixVector2.Zero, FixVector2.Zero.Normalized());
    }

    [Fact]
    public void Vector_RotationByUnitVector()
    {
        FixVector2 quarterTurn = FixVector2.UnitY; // 90° counter-clockwise
        Assert.Equal(FixVector2.UnitY, FixVector2.UnitX.Rotate(quarterTurn));
        Assert.Equal(FixVector2.UnitY, FixVector2.UnitX.Perpendicular());
        Assert.Equal(-FixVector2.UnitX, FixVector2.UnitX.Rotate(quarterTurn).Rotate(quarterTurn));
    }

    [Fact]
    public void Vector_DotCrossClamp()
    {
        Assert.Equal(Fix64.Zero, FixVector2.Dot(FixVector2.UnitX, FixVector2.UnitY));
        Assert.Equal(Fix64.One, FixVector2.Cross(FixVector2.UnitX, FixVector2.UnitY));
        var v = new FixVector2(Fix64.FromInt(6), Fix64.FromInt(8));
        Assert.Equal(Fix64.FromInt(2), Round(v.ClampLength(Fix64.FromInt(2)).Length));
        Assert.Equal(v, v.ClampLength(Fix64.FromInt(20)));
    }

    private static Fix64 Round(Fix64 value) => Fix64.FromDecimal(decimal.Round(value.ToDecimal(), 6));

    private static BigInteger IntegerSqrt(BigInteger n)
    {
        if (n.IsZero)
        {
            return 0;
        }

        BigInteger x = (BigInteger)Math.Sqrt((double)n);
        while (x * x > n)
        {
            x--;
        }

        while ((x + 1) * (x + 1) <= n)
        {
            x++;
        }

        return x;
    }
}
