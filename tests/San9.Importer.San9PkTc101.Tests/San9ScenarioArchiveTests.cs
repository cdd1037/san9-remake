using System.Text;
using San9.Importer.San9PkTc101;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class San9ScenarioArchiveTests
{
    [Fact]
    public void ReadKnownLayout_ParsesExpectedSectionsAndOffsets()
    {
        using MemoryStream stream = BuildScenarioArchive(San9ScenarioArchive.ExpectedSections);

        San9ScenarioLayout layout = San9ScenarioArchive.ReadKnownLayout(stream);

        Assert.Equal(719_714, layout.FileSize);
        Assert.Equal(17, layout.Sections.Count);
        Assert.Equal(0x13E, layout.GetRequiredSection("fbld").DataOffset);
        Assert.Equal(0x8D142, layout.GetRequiredSection("item").DataOffset);
        Assert.Equal(0x958A2, layout.GetRequiredSection("prsn").DataOffset);
    }

    [Fact]
    public void ReadHeader_DecodesScenarioIdDateAndMapSize()
    {
        using MemoryStream stream = BuildScenarioArchiveWithHeader(
            scenarioId: 1,
            encodedStartDate: 21_600);
        San9ScenarioHeader header = San9ScenarioArchive.ReadHeader(stream);

        Assert.Equal(0x20030317U, header.FormatVersion);
        Assert.Equal(1, header.ScenarioId);
        Assert.Equal(new(190, 1, 1), header.StartDate);
        Assert.Equal(200, header.MapWidth);
        Assert.Equal(200, header.MapHeight);
        Assert.Equal(50, header.ForceSlots.Count);
    }

    [Fact]
    public void ReadInitialScenario190Header_RejectsAnotherValidScenario()
    {
        using MemoryStream stream = BuildScenarioArchiveWithHeader(
            scenarioId: 2,
            encodedStartDate: 23_190);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioArchive.ReadInitialScenario190Header(stream));

        Assert.Contains("190-01-01", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(19_470, 184, 2, 1)]
    [InlineData(21_600, 190, 1, 1)]
    [InlineData(23_190, 194, 6, 1)]
    public void DecodeDate_UsesTheScenario360DayCalendar(
        ushort encodedDate,
        int year,
        int month,
        int day)
    {
        Assert.Equal(new(year, month, day), San9ScenarioArchive.DecodeDate(encodedDate));
    }

    [Fact]
    public void ReadKnownLayout_RejectsChangedSectionSize()
    {
        San9ScenarioSectionDefinition[] sections = San9ScenarioArchive.ExpectedSections
            .Select(section => section.Id == "item" ? section with { Size = section.Size + 2 } : section)
            .ToArray();
        using MemoryStream stream = BuildScenarioArchive(sections);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioArchive.ReadKnownLayout(stream));

        Assert.Contains("item", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadLayout_RejectsTruncatedRiffPayload()
    {
        using MemoryStream stream = BuildScenarioArchive(San9ScenarioArchive.ExpectedSections);
        stream.SetLength(stream.Length - 1);
        stream.Position = 0;

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioArchive.ReadLayout(stream));

        Assert.Contains("RIFF size", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadLayout_RejectsStreamNotPositionedAtFileStart()
    {
        using MemoryStream stream = BuildScenarioArchive(San9ScenarioArchive.ExpectedSections);
        stream.Position = 1;

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => San9ScenarioArchive.ReadLayout(stream));

        Assert.Contains("beginning", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadKnownItemRecords_ParsesReferencesAndBonuses()
    {
        using MemoryStream stream = BuildScenarioArchiveWithItems();
        WriteItemRecord(
            stream,
            index: 8,
            imageId: 8,
            categoryId: 1,
            loyaltyChange: 12,
            abilityBonus: 3,
            tacticCode: 3,
            holderPersonCode: 326,
            discoveryStateCode: 2,
            discoveryLocationCode: 13);

        stream.Position = 0;
        IReadOnlyList<San9ScenarioItemRecord> records =
            San9ScenarioItemArchive.ReadKnownRecords(stream);

        San9ScenarioItemRecord record = records[8];
        Assert.Equal(120, records.Count);
        Assert.Equal(8, record.SerializedId);
        Assert.Equal(3, record.AbilityBonus);
        Assert.Equal(3, record.TacticId);
        Assert.Equal(326, record.HolderPersonId);
        Assert.Equal(13, record.DiscoveryLocationId);
    }

    [Fact]
    public void ReadKnownItemRecords_MapsMissingCodesToNull()
    {
        using MemoryStream stream = BuildScenarioArchiveWithItems();

        San9ScenarioItemRecord record = San9ScenarioItemArchive.ReadKnownRecords(stream)[0];

        Assert.Null(record.TacticId);
        Assert.Null(record.HolderPersonId);
        Assert.Null(record.DiscoveryLocationId);
    }

    [Fact]
    public void ReadKnownItemRecords_RejectsInconsistentIdentity()
    {
        using MemoryStream stream = BuildScenarioArchiveWithItems();
        stream.Position = 0x8D142 + San9ScenarioItemArchive.RecordSize;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((ushort)99);
        }

        stream.Position = 0;
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioItemArchive.ReadKnownRecords(stream));

        Assert.Contains("record 1", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0x06, 100, "image")]
    [InlineData(0x08, 13, "category")]
    [InlineData(0x0E, 41, "tactic")]
    [InlineData(0x10, 850, "holder")]
    [InlineData(0x12, 3, "state")]
    [InlineData(0x14, 250, "location")]
    public void ReadKnownItemRecords_RejectsOutOfRangeFields(
        int fieldOffset,
        ushort value,
        string fieldName)
    {
        using MemoryStream stream = BuildScenarioArchiveWithItems();
        stream.Position = 0x8D142 + fieldOffset;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(value);
        }

        stream.Position = 0;
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioItemArchive.ReadKnownRecords(stream));

        Assert.Contains(fieldName, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static MemoryStream BuildScenarioArchiveWithHeader(
        ushort scenarioId,
        ushort encodedStartDate)
    {
        MemoryStream stream = BuildScenarioArchive(San9ScenarioArchive.ExpectedSections);
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            stream.Position = 0x14;
            writer.Write(San9ScenarioArchive.ExpectedFormatVersion);
            writer.Write(new byte[16]);
            writer.Write(scenarioId);
            writer.Write((ushort)0);
            writer.Write(encodedStartDate);
            writer.Write((ushort)0x24);
            writer.Write((ushort)200);
            writer.Write((ushort)200);
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream BuildScenarioArchiveWithItems()
    {
        MemoryStream stream = BuildScenarioArchive(San9ScenarioArchive.ExpectedSections);
        for (int index = 0; index < San9ScenarioItemArchive.RecordCount; index++)
        {
            WriteItemRecord(
                stream,
                index,
                imageId: index < 100 ? index : 0,
                categoryId: 0,
                loyaltyChange: 3,
                abilityBonus: 0,
                tacticCode: ushort.MaxValue,
                holderPersonCode: ushort.MaxValue,
                discoveryStateCode: 0,
                discoveryLocationCode: ushort.MaxValue);
        }

        stream.Position = 0;
        return stream;
    }

    private static void WriteItemRecord(
        Stream stream,
        int index,
        int imageId,
        int categoryId,
        int loyaltyChange,
        int abilityBonus,
        int tacticCode,
        int holderPersonCode,
        int discoveryStateCode,
        int discoveryLocationCode)
    {
        stream.Position = 0x8D142 + index * San9ScenarioItemArchive.RecordSize;
        using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write((ushort)index);
        writer.Write((ushort)index);
        writer.Write((ushort)index);
        writer.Write((ushort)imageId);
        writer.Write((ushort)categoryId);
        writer.Write((ushort)loyaltyChange);
        writer.Write((ushort)abilityBonus);
        writer.Write((ushort)tacticCode);
        writer.Write((ushort)holderPersonCode);
        writer.Write((ushort)discoveryStateCode);
        writer.Write((ushort)discoveryLocationCode);
    }

    private static MemoryStream BuildScenarioArchive(
        IReadOnlyList<San9ScenarioSectionDefinition> sections)
    {
        MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(0);
            writer.Write(Encoding.ASCII.GetBytes("S9SC"));
            foreach (San9ScenarioSectionDefinition section in sections)
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
