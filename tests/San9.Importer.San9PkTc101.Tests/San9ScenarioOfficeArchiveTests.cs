using System.Text;
using San9.Importer.San9PkTc101;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class San9ScenarioOfficeArchiveTests
{
    [Fact]
    public void ReadKnownOffices_MapsFieldsAndAssignabilityBoundary()
    {
        using MemoryStream stream = BuildScenarioArchive();
        San9ScenarioSection section = San9ScenarioArchive.ReadKnownLayout(stream)
            .GetRequiredSection("shak");
        WriteOfficeRecord(
            stream,
            section.DataOffset,
            San9ScenarioOfficeArchive.NoOfficeId - 1,
            commandCapacity: 15_000,
            serializedId: 75,
            opaqueField06: 75,
            opaqueField08: 75,
            rankGroupCode: 8,
            salary: 4,
            abilityMask: 0x000C,
            abilityBonus: 0);
        WriteOfficeRecord(
            stream,
            section.DataOffset,
            San9ScenarioOfficeArchive.NoOfficeId,
            commandCapacity: 0,
            serializedId: 76,
            opaqueField06: 76,
            opaqueField08: 76,
            rankGroupCode: 9,
            salary: 0,
            abilityMask: 0x0010,
            abilityBonus: 7);

        stream.Position = 0;
        IReadOnlyList<San9ScenarioOfficeRecord> records =
            San9ScenarioOfficeArchive.ReadKnownRecords(stream);

        San9ScenarioOfficeRecord assignable = records[75];
        Assert.True(assignable.IsAssignable);
        Assert.Equal((uint)15_000, assignable.CommandCapacity);
        Assert.Equal((ushort)75, assignable.SerializedId);
        Assert.Equal((ushort)75, assignable.OpaqueField06);
        Assert.Equal((ushort)75, assignable.OpaqueField08);
        Assert.Equal(8, assignable.RankGroupId);
        Assert.Equal((ushort)4, assignable.Salary);
        Assert.Equal((ushort)0x000C, assignable.AbilityMask);
        Assert.False(assignable.HasLeadershipBonus);
        Assert.False(assignable.HasStrengthBonus);
        Assert.True(assignable.HasIntelligenceBonus);
        Assert.True(assignable.HasPoliticsBonus);
        Assert.Equal((ushort)0, assignable.AbilityBonus);

        San9ScenarioOfficeRecord noOffice = records[76];
        Assert.False(noOffice.IsAssignable);
        Assert.Null(noOffice.RankGroupId);
        Assert.Equal((ushort)0x0010, noOffice.AbilityMask);
        Assert.Equal((ushort)7, noOffice.AbilityBonus);
        Assert.Equal(76, noOffice.Id);
    }

    [Fact]
    public void ReadKnownOffices_RejectsAbilityIndexesOutsideTheFourKnownAbilities()
    {
        using MemoryStream stream = BuildScenarioArchive();
        San9ScenarioOfficeRecord record =
            San9ScenarioOfficeArchive.ReadKnownRecords(stream)[0];

        Assert.Throws<ArgumentOutOfRangeException>(() => record.HasAbilityBonus(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => record.HasAbilityBonus(4));
    }

    [Fact]
    public void ReadKnownOffices_RejectsUnexpectedSectionSize()
    {
        using MemoryStream stream = BuildScenarioArchive();
        San9ScenarioSection section = San9ScenarioArchive.ReadKnownLayout(stream)
            .GetRequiredSection("shak");
        stream.Position = section.DataOffset - 4;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(San9ScenarioOfficeArchive.RecordCount * San9ScenarioOfficeArchive.RecordSize - 2);
        }

        stream.Position = 0;
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioOfficeArchive.ReadKnownRecords(stream));

        Assert.Contains("FourCC", exception.Message);
    }

    private static void WriteOfficeRecord(
        Stream stream,
        long sectionDataOffset,
        int index,
        uint commandCapacity,
        ushort serializedId,
        ushort opaqueField06,
        ushort opaqueField08,
        ushort rankGroupCode,
        ushort salary,
        ushort abilityMask,
        ushort abilityBonus)
    {
        stream.Position = sectionDataOffset + index * San9ScenarioOfficeArchive.RecordSize;
        using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(commandCapacity);
        writer.Write(serializedId);
        writer.Write(opaqueField06);
        writer.Write(opaqueField08);
        writer.Write(rankGroupCode);
        writer.Write(salary);
        writer.Write(abilityMask);
        writer.Write(abilityBonus);
    }

    private static MemoryStream BuildScenarioArchive()
    {
        MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(0);
            writer.Write(Encoding.ASCII.GetBytes("S9SC"));
            foreach (San9ScenarioSectionDefinition section in San9ScenarioArchive.ExpectedSections)
            {
                writer.Write(Encoding.ASCII.GetBytes(section.Id));
                writer.Write(section.Size);
                writer.Write(new byte[section.Size]);
                if ((section.Size & 1) != 0)
                {
                    writer.Write((byte)0);
                }
            }

            stream.Position = 4;
            writer.Write(checked((int)stream.Length - 8));
        }

        stream.Position = 0;
        return stream;
    }
}
