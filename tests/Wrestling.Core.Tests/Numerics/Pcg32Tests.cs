using Wrestling.Core.Numerics;

namespace Wrestling.Core.Tests.Numerics;

public class Pcg32Tests
{
    /// <summary>Reference output of the official pcg32-demo with seed 42 and stream 54.</summary>
    [Fact]
    public void MatchesReferenceImplementation()
    {
        var random = new Pcg32(42, 54);
        uint[] expected = { 0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e };
        foreach (uint value in expected)
        {
            Assert.Equal(value, random.NextUInt());
        }
    }

    [Fact]
    public void SameSeedAndStream_GiveSameSequence()
    {
        var a = new Pcg32(123, 7);
        var b = new Pcg32(123, 7);
        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(a.NextUInt(), b.NextUInt());
        }
    }

    [Fact]
    public void DifferentStreams_GiveDifferentSequences()
    {
        var a = new Pcg32(123, 1);
        var b = new Pcg32(123, 2);
        int equal = 0;
        for (int i = 0; i < 1000; i++)
        {
            if (a.NextUInt() == b.NextUInt())
            {
                equal++;
            }
        }

        Assert.True(equal < 5);
    }

    [Fact]
    public void FromState_ContinuesTheSameSequence()
    {
        var original = new Pcg32(99, 3);
        for (int i = 0; i < 17; i++)
        {
            original.NextUInt();
        }

        Pcg32 restored = Pcg32.FromState(original.State, original.Increment);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(original.NextUInt(), restored.NextUInt());
        }

        Assert.Throws<ArgumentException>(() => Pcg32.FromState(1, 2));
    }

    [Fact]
    public void NextInt_StaysInRangeAndCoversIt()
    {
        var random = new Pcg32(5, 5);
        var counts = new int[10];
        for (int i = 0; i < 100_000; i++)
        {
            int value = random.NextInt(-5, 5);
            Assert.InRange(value, -5, 4);
            counts[value + 5]++;
        }

        // Each bucket expects 10,000; allow a wide margin (far beyond 5 standard deviations).
        Assert.All(counts, count => Assert.InRange(count, 9_400, 10_600));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(3, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.NextUInt(0));
    }

    [Fact]
    public void NextInt_HandlesFullIntRange()
    {
        var random = new Pcg32(1, 1);
        for (int i = 0; i < 1000; i++)
        {
            int value = random.NextInt(int.MinValue, int.MaxValue);
            Assert.InRange(value, int.MinValue, int.MaxValue - 1);
        }
    }

    [Fact]
    public void Chance_RespectsBoundsAndProbability()
    {
        var random = new Pcg32(8, 8);
        Assert.False(random.Chance(Fix64.Zero));
        Assert.True(random.Chance(Fix64.One));

        int hits = 0;
        Fix64 quarter = Fix64.FromRatio(1, 4);
        for (int i = 0; i < 100_000; i++)
        {
            if (random.Chance(quarter))
            {
                hits++;
            }
        }

        Assert.InRange(hits, 24_300, 25_700);
    }

    [Fact]
    public void NextFix01_IsInUnitInterval()
    {
        var random = new Pcg32(2, 2);
        for (int i = 0; i < 10_000; i++)
        {
            Fix64 value = random.NextFix01();
            Assert.True(value >= Fix64.Zero && value < Fix64.One);
        }
    }
}
