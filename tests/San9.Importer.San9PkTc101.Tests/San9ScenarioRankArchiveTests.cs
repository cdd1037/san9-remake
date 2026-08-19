using System.Text;
using San9.Importer.San9PkTc101;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class San9ScenarioRankArchiveTests
{
    [Fact]
    public void ReadKnownRanks_UsesRecordOrderAndPreservesAllFields()
    {
        using MemoryStream stream = BuildScenarioArchive();
        San9ScenarioSection section = San9ScenarioArchive.ReadKnownLayout(stream)
            .GetRequiredSection("kans");
        stream.Position = section.DataOffset;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((uint)60_000);
            writer.Write((ushort)0);
            writer.Write((ushort)0x1111);
            writer.Write((ushort)0x2222);
            writer.Write((ushort)5);
            writer.Write((ushort)1_000);
        }

        stream.Position = 0;
        IReadOnlyList<San9ScenarioRankRecord> records =
            San9ScenarioRankArchive.ReadKnownRecords(stream);

        Assert.Equal(San9ScenarioRankArchive.RecordCount, records.Count);
        Assert.Equal(0, records[0].Id);
        Assert.Equal((uint)60_000, records[0].CommandCapacity);
        Assert.Equal((ushort)0, records[0].SerializedId);
        Assert.Equal((ushort)0x1111, records[0].OpaqueField06);
        Assert.Equal((ushort)0x2222, records[0].OpaqueField08);
        Assert.Equal((ushort)5, records[0].AbilityBonus);
        Assert.Equal((ushort)1_000, records[0].PrestigeRequirement);
        Assert.Equal(8, records[8].Id);
    }

    [Fact]
    public void ReadKnownRanks_RejectsUnexpectedSectionSize()
    {
        using MemoryStream stream = BuildScenarioArchive();
        San9ScenarioSection section = San9ScenarioArchive.ReadKnownLayout(stream)
            .GetRequiredSection("kans");
        stream.Position = section.DataOffset - 4;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(San9ScenarioRankArchive.RecordCount * San9ScenarioRankArchive.RecordSize - 2);
        }

        stream.Position = 0;
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioRankArchive.ReadKnownRecords(stream));

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
