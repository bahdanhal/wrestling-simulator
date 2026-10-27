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
        Assert.Equal(0x9E6E33915945D915UL, DeterminismProbe.Run(1_000));
    }

    [Fact]
    public void RejectsNonPositiveIterations()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DeterminismProbe.Run(0));
    }
}
