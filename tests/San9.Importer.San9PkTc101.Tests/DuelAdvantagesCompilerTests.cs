using San9.Core.Domain;
using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class DuelAdvantagesCompilerTests
{
    [Theory]
    [InlineData(3, DuelAdvantages.DominantParry | DuelAdvantages.DominantAttack)]
    [InlineData(9, DuelAdvantages.DominantParry)]
    [InlineData(4, DuelAdvantages.DominantDoubleStrike)]
    [InlineData(0, DuelAdvantages.None)]
    [InlineData(36, DuelAdvantages.None)]
    public void BiographyIdentityMapsToIndependentDuelSemantics(int biography, DuelAdvantages expected) =>
        Assert.Equal(expected, OfficerContentCompiler.DuelTraits(biography));
}
