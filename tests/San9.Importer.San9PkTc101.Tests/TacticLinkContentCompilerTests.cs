using System.Buffers.Binary;
using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class TacticLinkContentCompilerTests
{
    private static San9ScenarioPersonRecord Person(ushort biography) => new(
        0, 6000, 0, 0, 0, 0, 0, biography, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        [], 0, 0, [], 0);

    private static byte[] Executable()
    {
        var exe = new byte[0x20f940 + 58 * 12];
        void Write(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(exe.AsSpan(offset, 4), value);
        for (int i = 0; i < 58; i++)
        {
            Write(0x20f940 + i * 12, 1000 + i);
            Write(0x20f944 + i * 12, 2000 + i);
            Write(0x20f948 + i * 12, 20);
        }
        for (int i = 0; i < 3; i++)
        {
            Write(0x20f928 + i * 8, 3000 + i);
            Write(0x20f92c + i * 8, 4000 + i);
        }
        Write(0x20f940, 36); Write(0x20f944, 2); Write(0x20f948, 45);
        Write(0x20f928, 2); Write(0x20f92c, 36);
        return exe;
    }

    [Fact]
    public void BiographyPairsExpandToRetainedIdentitiesAndMergeSiblingExclusions()
    {
        var people = new[] { Person(2), Person(36), Person(2), Person(9) };
        var all = TacticLinkContentCompiler.Compile(Executable(), people, [0, 1, 2, 3], true);
        Assert.Equal(new[] { (0, 1), (1, 2) }, all.Pairs.Select(p => (p.FirstId, p.SecondId)));
        Assert.All(all.Pairs, p => { Assert.Equal(45, p.Bonus); Assert.True(p.SuppressesSiblingBonus); });
        var filtered = TacticLinkContentCompiler.Compile(Executable(), people, [1, 2, 3], false);
        Assert.Equal(1, Assert.Single(filtered.Pairs).FirstId);
        Assert.False(filtered.RelationshipsEnabled);
        Assert.Equal(20, filtered.KinshipBonus);
    }

    [Fact]
    public void InvalidSourceRelationshipAndTruncatedTablesAreRejected()
    {
        var exe = Executable();
        BinaryPrimitives.WriteInt32LittleEndian(exe.AsSpan(0x20f948, 4), -1);
        Assert.Throws<InvalidDataException>(() => TacticLinkContentCompiler.Compile(exe, [], [], true));
        Assert.Throws<InvalidDataException>(() => TacticLinkContentCompiler.Compile([], [], [], true));
    }
}
