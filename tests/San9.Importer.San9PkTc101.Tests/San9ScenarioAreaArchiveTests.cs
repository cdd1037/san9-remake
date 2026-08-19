using System.Text;
using San9.Importer.San9PkTc101;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class San9ScenarioAreaArchiveTests
{
    [Fact]
    public void ReadKnownAreaRecords_UsesRecordOrderAsIdAndPreservesAllSlots()
    {
        using MemoryStream stream = BuildScenarioArchive();
        for (int index = 0; index < San9ScenarioAreaArchive.RecordCount; index++)
        {
            stream.Position = 0x6E + index * San9ScenarioAreaArchive.RecordSize;
            using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);
            writer.Write((ushort)(1000 + index));
            writer.Write((ushort)(0x2000 + index));
            writer.Write((ushort)(0x4000 + index));
        }

        stream.Position = 0;
        IReadOnlyList<San9ScenarioAreaRecord> records =
            San9ScenarioAreaArchive.ReadKnownRecords(stream);

        Assert.Equal(San9ScenarioAreaArchive.RecordCount, records.Count);
        Assert.Equal(0, records[0].Id);
        Assert.Equal(1000, records[0].SerializedId);
        Assert.Equal(0x2000, records[0].OpaqueField02);
        Assert.Equal(0x4000, records[0].OpaqueField04);
        Assert.Equal(14, records[14].Id);
        Assert.Equal(1014, records[14].SerializedId);
    }

    [Fact]
    public void ReadKnownAreaRecords_RejectsUnexpectedSectionSize()
    {
        using MemoryStream stream = BuildScenarioArchive();
        stream.Position = 0x6A;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(84);
        }

        stream.Position = 0;
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioAreaArchive.ReadKnownRecords(stream));

        Assert.Contains("FourCC", exception.Message, StringComparison.OrdinalIgnoreCase);
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
