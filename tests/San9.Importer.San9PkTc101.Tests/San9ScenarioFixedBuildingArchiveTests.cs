using System.Text;
using San9.Importer.San9PkTc101;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class San9ScenarioFixedBuildingArchiveTests
{
    [Fact]
    public void ReadKnownFixedBuildings_UsesRecordOrderAndPreservesAllFields()
    {
        using MemoryStream stream = BuildScenarioArchive();
        for (int index = 0; index < San9ScenarioFixedBuildingArchive.RecordCount; index++)
        {
            stream.Position = 0x13E + index * San9ScenarioFixedBuildingArchive.RecordSize;
            using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);
            for (int field = 0; field < 11; field++)
            {
                writer.Write((ushort)(0x1000 + index * 0x10 + field));
            }

            writer.Write(new byte[]
            {
                (byte)(index + 1),
                (byte)(index + 2),
                (byte)(index + 3),
                (byte)(index + 4),
                (byte)(index + 5),
                (byte)(index + 6),
            });
        }

        stream.Position = 0;
        IReadOnlyList<San9ScenarioFixedBuildingRecord> records =
            San9ScenarioFixedBuildingArchive.ReadKnownRecords(stream);

        Assert.Equal(San9ScenarioFixedBuildingArchive.RecordCount, records.Count);
        Assert.Equal(0, records[0].Id);
        Assert.Equal(0x1000, records[0].SerializedId);
        Assert.Equal(0x1001, records[0].OpaqueField02);
        Assert.Equal(0x1002, records[0].OpaqueField04);
        Assert.Equal(0x1003, records[0].ConstructionCost);
        Assert.Equal(0x1004, records[0].MaxDurability);
        Assert.Equal(0x1005, records[0].OpaqueField0A);
        Assert.Equal(0x1006, records[0].ShapeLength);
        Assert.Equal(0x1007, records[0].ShapeWidth);
        Assert.Equal(0x1006, records[0].FootprintWidth);
        Assert.Equal(0x1007, records[0].FootprintHeight);
        Assert.Equal((long)0x1006 * 0x1007, records[0].FootprintArea);
        Assert.Equal(0x1008, records[0].AppearanceId);
        Assert.Equal(0x1009, records[0].RecoveryPerTurn);
        Assert.Equal(0x100A, records[0].TacticCode);
        Assert.Null(records[0].TacticId);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, records[0].OpaqueTail);
        Assert.Equal(16, records[16].Id);
        Assert.Equal(0x1100, records[16].SerializedId);
        Assert.Equal(new byte[] { 17, 18, 19, 20, 21, 22 }, records[16].OpaqueTail);
    }

    [Fact]
    public void ReadKnownFixedBuildings_ResolvesValidTacticCode()
    {
        using MemoryStream stream = BuildScenarioArchive();
        stream.Position = 0x13E + 16 * San9ScenarioFixedBuildingArchive.RecordSize + 0x14;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((ushort)20);
        }

        stream.Position = 0;
        San9ScenarioFixedBuildingRecord record =
            San9ScenarioFixedBuildingArchive.ReadKnownRecords(stream)[16];

        Assert.Equal((ushort)20, record.TacticCode);
        Assert.Equal(20, record.TacticId);
    }

    [Fact]
    public void ReadKnownFixedBuildings_RejectsUnexpectedSectionSize()
    {
        using MemoryStream stream = BuildScenarioArchive();
        stream.Position = 0x136 + 4;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(475);
        }

        stream.Position = 0;
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioFixedBuildingArchive.ReadKnownRecords(stream));

        Assert.Contains("fbld", exception.Message);
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
