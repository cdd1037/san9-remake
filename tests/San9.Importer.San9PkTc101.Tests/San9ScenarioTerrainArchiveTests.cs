using System.Text;
using San9.Importer.San9PkTc101;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class San9ScenarioTerrainArchiveTests
{
    [Fact]
    public void ReadKnownTerrainRecords_UsesRecordOrderAsIdAndPreservesAllSlots()
    {
        using MemoryStream stream = BuildScenarioArchive();
        for (int index = 0; index < San9ScenarioTerrainArchive.RecordCount; index++)
        {
            stream.Position = 0xD0 + index * San9ScenarioTerrainArchive.RecordSize;
            using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);
            writer.Write((ushort)(2000 + index));
            writer.Write((ushort)(0x2100 + index));
            writer.Write((ushort)(0x4100 + index));
        }

        stream.Position = 0;
        IReadOnlyList<San9ScenarioTerrainRecord> records =
            San9ScenarioTerrainArchive.ReadKnownRecords(stream);

        Assert.Equal(San9ScenarioTerrainArchive.RecordCount, records.Count);
        Assert.Equal(0, records[0].Id);
        Assert.Equal(2000, records[0].SerializedId);
        Assert.Equal(0x2100, records[0].SerializedIdCopy02);
        Assert.Equal(0x4100, records[0].OpaqueField04);
        Assert.Equal(704, records[0].NameMessageId);
        Assert.Equal(16, records[16].Id);
        Assert.Equal(2016, records[16].SerializedId);
        Assert.Equal(720, records[16].NameMessageId);
    }

    [Fact]
    public void TerrainNameMessageIds_RejectInvalidIds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => San9ScenarioTerrainArchive.GetNameMessageId(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => San9ScenarioTerrainArchive.GetNameMessageId(17));
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
