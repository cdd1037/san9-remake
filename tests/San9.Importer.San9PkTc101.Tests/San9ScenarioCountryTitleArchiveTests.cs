using System.Text;
using San9.Importer.San9PkTc101;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class San9ScenarioCountryTitleArchiveTests
{
    [Fact]
    public void ReadKnownCountryTitles_UsesRecordOrderAndPreservesAllFields()
    {
        using MemoryStream stream = BuildScenarioArchive();
        for (int index = 0; index < San9ScenarioCountryTitleArchive.RecordCount; index++)
        {
            stream.Position = 0xF86 + index * San9ScenarioCountryTitleArchive.RecordSize;
            using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);
            writer.Write((ushort)(0x1000 + index));
            writer.Write((ushort)(0x2000 + index));
            writer.Write((ushort)(0x3000 + index));
            writer.Write((ushort)(0x4000 + index));
            writer.Write((ushort)(0x5000 + index));
            writer.Write((ushort)(0x6000 + index));
        }

        stream.Position = 0;
        IReadOnlyList<San9ScenarioCountryTitleRecord> records =
            San9ScenarioCountryTitleArchive.ReadKnownRecords(stream);

        Assert.Equal(San9ScenarioCountryTitleArchive.RecordCount, records.Count);
        Assert.Equal(0, records[0].Id);
        Assert.Equal(0x1000, records[0].SerializedId);
        Assert.Equal(0x2000, records[0].SerializedIdCopy02);
        Assert.Equal(0x3000, records[0].SerializedIdCopy04);
        Assert.Equal(0x4000, records[0].RulerPersonCode);
        Assert.Equal(0x5000, records[0].Area1Code);
        Assert.Equal(0x6000, records[0].Area2Code);
        Assert.Null(records[0].RulerPersonId);
        Assert.Null(records[0].Area1Id);
        Assert.Null(records[0].Area2Id);
        Assert.Equal(19, records[19].Id);
        Assert.Equal(0x1013, records[19].SerializedId);
        Assert.Equal(5315, records[0].NameMessageId);
        Assert.Equal(5316, records[0].NameReadingMessageId);
        Assert.Equal(5353, records[19].NameMessageId);
        Assert.Equal(5354, records[19].NameReadingMessageId);
    }

    [Fact]
    public void CountryTitleNameMessageIds_RejectInvalidIds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => San9ScenarioCountryTitleArchive.GetNameMessageId(-1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => San9ScenarioCountryTitleArchive.GetNameReadingMessageId(20));
    }

    [Fact]
    public void ReadKnownCountryTitles_ResolvesPersonAndAreaCodesWithoutDroppingRawValues()
    {
        using MemoryStream stream = BuildScenarioArchive();
        stream.Position = 0xF86 + 0x06;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((ushort)326);
            writer.Write((ushort)3);
            writer.Write((ushort)14);
        }

        stream.Position = 0;
        San9ScenarioCountryTitleRecord record = San9ScenarioCountryTitleArchive.ReadKnownRecords(stream)[0];

        Assert.Equal((ushort)326, record.RulerPersonCode);
        Assert.Equal(326, record.RulerPersonId);
        Assert.Equal(3, record.Area1Id);
        Assert.Equal(14, record.Area2Id);
    }

    [Fact]
    public void ReadKnownCountryTitles_RejectsUnexpectedSectionSize()
    {
        using MemoryStream stream = BuildScenarioArchive();
        stream.Position = 0xF82;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(238);
        }

        stream.Position = 0;
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioCountryTitleArchive.ReadKnownRecords(stream));

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
