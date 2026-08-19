using System.Text;
using San9.Importer.San9PkTc101;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class San9ScenarioSkrkArchiveTests
{
    [Fact]
    public void ReadKnownSkrkRecords_UsesRecordOrderAndPreservesAllWords()
    {
        using MemoryStream stream = BuildScenarioArchive();
        for (int index = 0; index < San9ScenarioSkrkArchive.RecordCount; index++)
        {
            stream.Position = 0xF38 + index * San9ScenarioSkrkArchive.RecordSize;
            using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);
            for (int field = 0; field < 5; field++)
            {
                writer.Write((ushort)(0x3000 + index * 0x10 + field));
            }
        }

        stream.Position = 0;
        IReadOnlyList<San9ScenarioSkrkRecord> records =
            San9ScenarioSkrkArchive.ReadKnownRecords(stream);

        Assert.Equal(San9ScenarioSkrkArchive.RecordCount, records.Count);
        Assert.Equal(0, records[0].Id);
        Assert.Equal(0x3000, records[0].SerializedId);
        Assert.Equal(0x3001, records[0].OpaqueField02);
        Assert.Equal(0x3002, records[0].OpaqueField04);
        Assert.Equal(0x3003, records[0].OpaqueField06);
        Assert.Equal(0x3004, records[0].OpaqueField08);
        Assert.Equal(6, records[6].Id);
        Assert.Equal(0x3060, records[6].SerializedId);
    }

    [Fact]
    public void ReadKnownSkrkRecords_RejectsUnexpectedSectionSize()
    {
        using MemoryStream stream = BuildScenarioArchive();
        stream.Position = 0xF34;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(68);
        }

        stream.Position = 0;
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioSkrkArchive.ReadKnownRecords(stream));

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
