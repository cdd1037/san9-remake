using San9.Core.Domain;
using System.Buffers.Binary;
using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class TreasureContentCompilerTests
{
    private static San9ScenarioPersonRecord Person(ushort biography) => new(
        0, 6000, 0, 0, 0, 0, 0, biography, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        [], 0, 0, [], 0);

    private static byte[] DiplomacyExecutable()
    {
        var exe = new byte[0x2559a8];
        void Write(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(exe.AsSpan(offset, 4), value);
        for (int i = 0; i < 38; i++)
        {
            Write(0x255860 + i * 8, 4);
            Write(0x255864 + i * 8, 99);
        }
        Write(0x255860, 9); Write(0x255864, 36);
        Write(0x255868, 9); Write(0x25586c, 2);
        Write(0x255990, -1);
        Write(0x255998, 79); Write(0x25599c, 80); Write(0x2559a0, 81); Write(0x2559a4, -1);
        return exe;
    }

    [Fact]
    public void DiplomacyTablesMapBiographiesToAllRetainedPeopleWithoutUsingPersonSlots()
    {
        var people = new[] { Person(2), Person(36), Person(2), Person(99) };
        var rules = TreasureContentCompiler.DiplomacyPreferences(DiplomacyExecutable(), people, [2, 1, 0], [4, 9, 79, 80, 81]);
        Assert.Equal(new[] { 0, 1, 2 }, rules[9].FavoredOwnerIds);
        Assert.Empty(rules[4].FavoredOwnerIds);
        Assert.True(rules[9].AutomaticOfferAllowed);
        Assert.All(new[] { 79, 80, 81 }, id => Assert.False(rules[id].AutomaticOfferAllowed));
        var filtered = TreasureContentCompiler.DiplomacyPreferences(DiplomacyExecutable(), people, [1, 2, 3], [4, 9, 79, 80, 81]);
        Assert.Equal(new[] { 1, 2 }, filtered[9].FavoredOwnerIds);
        Assert.Equal(3, Assert.Single(filtered[4].FavoredOwnerIds));
    }

    [Theory]
    [InlineData(0x255990, 0)]
    [InlineData(0x2559a4, 0)]
    [InlineData(0x255860, 500)]
    [InlineData(0x255864, -2)]
    [InlineData(0x25599c, 79)]
    [InlineData(0x255998, 500)]
    public void InvalidDiplomacyTablesAreRejected(int offset, int value)
    {
        var exe = DiplomacyExecutable();
        BinaryPrimitives.WriteInt32LittleEndian(exe.AsSpan(offset, 4), value);
        Assert.Throws<InvalidDataException>(() => TreasureContentCompiler.DiplomacyPreferences(exe, [], [], [4, 9, 79, 80, 81]));
        Assert.Throws<InvalidDataException>(() => TreasureContentCompiler.DiplomacyPreferences([], [], [], [4, 9, 79, 80, 81]));
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0)]
    [InlineData(1, 9, 0, 0, 0)]
    [InlineData(2, 0, 0, 0, 9)]
    [InlineData(3, 0, 0, 9, 0)]
    [InlineData(4, 0, 0, 9, 0)]
    [InlineData(5, 0, 9, 0, 0)]
    [InlineData(6, 0, 0, 9, 0)]
    [InlineData(7, 0, 0, 0, 0)]
    [InlineData(8, 0, 0, 0, 0)]
    [InlineData(9, 0, 9, 0, 0)]
    [InlineData(10, 0, 0, 0, 0)]
    [InlineData(11, 0, 0, 0, 0)]
    [InlineData(12, 0, 0, 0, 0)]
    public void SourceCategoriesBecomeExplicitAbilityBonuses(ushort category, int war, int intelligence, int politics, int leadership)
    {
        var item = new San9ScenarioItemRecord(0, 0, 0, 0, category, 8, 9, ushort.MaxValue, 1, 2, ushort.MaxValue);
        Assert.Equal(new OfficerAbilities(war, intelligence, politics, leadership), TreasureContentCompiler.AbilityBonuses(item));
    }

    [Fact]
    public void InitialStatesKeepBuriedUnownedAndDisabledRecordsAndSourceLocations()
    {
        San9ScenarioItemRecord Item(ushort id, ushort state, ushort holder, ushort region) =>
            new(id, id, id, 0, 0, 10, 0, ushort.MaxValue, holder, state, region);
        var records = new[] { Item(0, 1, ushort.MaxValue, 117), Item(1, 2, 600, 38),
            Item(2, 2, ushort.MaxValue, ushort.MaxValue), Item(100, 0, ushort.MaxValue, 15) };
        var states = TreasureContentCompiler.InitialStates(records);
        Assert.Equal(4, states.Length);
        Assert.Equal(new TreasureState(0, null, TreasureDiscovery.Buried, 117), states[0]);
        Assert.Equal(new TreasureState(1, 600, TreasureDiscovery.Discovered, 38), states[1]);
        Assert.Equal(new TreasureState(2, null, TreasureDiscovery.Discovered, null), states[2]);
        Assert.Equal(new TreasureState(100, null, TreasureDiscovery.Disabled, 15), states[3]);
        Assert.Throws<InvalidDataException>(() => TreasureContentCompiler.InitialStates([Item(100, 1, ushort.MaxValue, 15)]));
    }
}
