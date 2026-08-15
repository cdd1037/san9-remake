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
