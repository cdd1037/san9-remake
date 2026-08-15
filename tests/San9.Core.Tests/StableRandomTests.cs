using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class StableRandomTests
{
    [Fact]
    public void NextUInt64_MatchesProjectOwnedReferenceVector()
    {
        StableRandom random = new(0UL);

        Assert.Equal(0xE220A8397B1DCDAFUL, random.NextUInt64());
        Assert.Equal(0x6E789E6AA1B965F4UL, random.NextUInt64());
        Assert.Equal(0x06C45D188009454FUL, random.NextUInt64());
        Assert.Equal(0xF88BB8A8724C81ECUL, random.NextUInt64());
    }
}
