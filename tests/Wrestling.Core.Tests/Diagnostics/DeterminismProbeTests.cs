using Wrestling.Core.Diagnostics;

namespace Wrestling.Core.Tests.Diagnostics;

public class DeterminismProbeTests
{
    [Fact]
    public void ProducesReferenceHash()
    {
        Assert.Equal(DeterminismProbe.ReferenceHash, DeterminismProbe.Run());
        Assert.True(DeterminismProbe.MatchesReference());
    }

    /// <summary>A second, shorter reference catches changes that the long run might mask.</summary>
    [Fact]
    public void ProducesReferenceHashForShortRun()
    {
        Assert.Equal(0x7DEE54D6AEE5DB1EUL, DeterminismProbe.Run(1_000));
    }

    [Fact]
    public void RejectsNonPositiveIterations()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DeterminismProbe.Run(0));
    }
}
