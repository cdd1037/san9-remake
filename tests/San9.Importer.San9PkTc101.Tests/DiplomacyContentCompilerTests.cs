using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class DiplomacyContentCompilerTests
{
    private static San9ScenarioForceRecord Force(int id, uint flags) =>
        new(id, 0, 0, flags, (ushort)id, (ushort)id, 0, 0, 0, 0, ushort.MaxValue, 0, 0, 0, new byte[50]);

    [Fact]
    public void PreservesAllRolesAndDurationWithoutRelyingOnConsecutiveIdentities()
    {
        var alliance = DiplomacyContentCompiler.Alliance(36,
            [Force(17, 16), Force(5, 64), Force(7, 48), Force(2, 0)], new HashSet<int> { 17, 5, 7, 2 });
        Assert.Equal(5, alliance!.TargetForceId);
        Assert.Equal(7, alliance.LeaderForceId);
        Assert.Equal(new[] { 7, 17 }, alliance.MemberForceIds);
        Assert.Equal(36, alliance.RemainingXun);
    }

    [Fact]
    public void RejectsDroppedMembersOrContradictorySourceState()
    {
        San9ScenarioForceRecord[] forces = [Force(0, 16), Force(5, 64), Force(7, 48)];
        Assert.Throws<InvalidDataException>(() => DiplomacyContentCompiler.Alliance(36, forces, new HashSet<int> { 5, 7 }));
        Assert.Throws<InvalidDataException>(() => DiplomacyContentCompiler.Alliance(0, forces, new HashSet<int> { 0, 5, 7 }));
        Assert.Throws<InvalidDataException>(() => DiplomacyContentCompiler.Alliance(36, [Force(0, 0)], new HashSet<int> { 0 }));
        Assert.Null(DiplomacyContentCompiler.Alliance(0, [Force(0, 0)], new HashSet<int> { 0 }));
    }
}
