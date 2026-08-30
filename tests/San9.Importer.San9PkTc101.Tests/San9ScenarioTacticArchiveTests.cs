using System.Text;
using San9.Importer.San9PkTc101;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class San9ScenarioTacticArchiveTests
{
    [Fact]
    public void ReadKnownTactics_UsesRecordOrderAndPreservesAllSlots()
    {
        using MemoryStream stream = BuildScenarioArchive();
        for (int index = 0; index < San9ScenarioTacticArchive.RecordCount; index++)
        {
            stream.Position = 0x322 + index * San9ScenarioTacticArchive.RecordSize;
            using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);
            for (int field = 0; field < 19; field++)
            {
                writer.Write((ushort)(0x1000 + index * 0x20 + field));
            }
        }

        stream.Position = 0;
        IReadOnlyList<San9ScenarioTacticRecord> records =
            San9ScenarioTacticArchive.ReadKnownRecords(stream);

        Assert.Equal(San9ScenarioTacticArchive.RecordCount, records.Count);
        Assert.Equal(0, records[0].Id);
        Assert.Equal(0x1000, records[0].SerializedId);
        Assert.Equal(0x1001, records[0].OpaqueField02);
        Assert.Equal(0x1002, records[0].OpaqueField04);
        Assert.Equal(0x1003, records[0].OpaqueField06);
        Assert.Equal(0x1004, records[0].SystemCode);
        Assert.Equal(0x1005, records[0].TrainingRequirement);
        Assert.Equal(0x1006, records[0].OpaqueField0C);
        Assert.Equal(0x1007, records[0].OpaqueField0E);
        Assert.Equal(0x1008, records[0].Power);
        Assert.Equal(0x1009, records[0].OpaqueField12);
        Assert.Equal(0x100A, records[0].OpaqueField14);
        Assert.Equal(0x100B, records[0].OpaqueField16);
        Assert.Equal(0x100C, records[0].OpaqueField18);
        Assert.Equal(0x100D, records[0].OpaqueField1A);
        Assert.Equal(0x100E, records[0].MinimumInterval);
        Assert.Equal(0x100F, records[0].OpaqueField1E);
        Assert.Equal(0x1010, records[0].AffectsFacilitiesCode);
        Assert.True(records[0].AffectsFacilities);
        Assert.Equal(0x1011, records[0].CompatibleArrayTypeMaskLow);
        Assert.Equal(0x1012, records[0].CompatibleArrayTypeMaskHigh);
        Assert.Equal(0x10121011u, records[0].CompatibleArrayTypeMask);
        Assert.Equal(40, records[40].Id);
        Assert.Equal(0x1500, records[40].SerializedId);
    }

    [Fact]
    public void ReadKnownTactics_DerivesFacilityFlagWithoutDroppingRawCode()
    {
        using MemoryStream stream = BuildScenarioArchive();
        stream.Position = 0x322 + 23 * San9ScenarioTacticArchive.RecordSize + 0x20;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((ushort)1);
        }

        stream.Position = 0;
        San9ScenarioTacticRecord record = San9ScenarioTacticArchive.ReadKnownRecords(stream)[23];

        Assert.Equal((ushort)1, record.AffectsFacilitiesCode);
        Assert.True(record.AffectsFacilities);
    }

    [Fact]
    public void ReadKnownTactics_CombinesAndQueriesArrayTypeCompatibilityMask()
    {
        using MemoryStream stream = BuildScenarioArchive();
        stream.Position = 0x322 + 0x22;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((ushort)0x8001);
            writer.Write((ushort)0x0410);
        }

        stream.Position = 0;
        San9ScenarioTacticRecord record = San9ScenarioTacticArchive.ReadKnownRecords(stream)[0];

        Assert.Equal(0x04108001u, record.CompatibleArrayTypeMask);
        Assert.True(record.SupportsArrayType(0));
        Assert.True(record.SupportsArrayType(15));
        Assert.True(record.SupportsArrayType(20));
        Assert.True(record.SupportsArrayType(26));
        Assert.False(record.SupportsArrayType(27));
        Assert.False(record.SupportsArrayType(-1));
        Assert.False(record.SupportsArrayType(28));
    }

    [Fact]
    public void ReadKnownTactics_RejectsUnexpectedSectionSize()
    {
        using MemoryStream stream = BuildScenarioArchive();
        stream.Position = 0x31E;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(1556);
        }

        stream.Position = 0;
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioTacticArchive.ReadKnownRecords(stream));

        Assert.Contains("FourCC", exception.Message);
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
