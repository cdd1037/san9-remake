using System.Collections.ObjectModel;
using System.Text;

namespace San9.Importer.San9PkTc101;

public sealed record San9ScenarioSectionDefinition(string Id, int Size);

public sealed record San9ScenarioSection(string Id, int Size, long DataOffset);

public sealed record San9ScenarioDate(int Year, int Month, int Day);

public sealed record San9ScenarioHeader(
    uint FormatVersion,
    IReadOnlyList<uint> UnknownWords,
    ushort ScenarioId,
    ushort PackedFlags,
    ushort EncodedStartDate,
    San9ScenarioDate StartDate,
    ushort AllianceDurationTurns,
    ushort MapWidth,
    ushort MapHeight,
    IReadOnlyList<byte> EncodedCityForceMapCodes);

public sealed class San9ScenarioLayout
{
    internal San9ScenarioLayout(long fileSize, IReadOnlyList<San9ScenarioSection> sections)
    {
        FileSize = fileSize;
        Sections = sections;
    }

    public long FileSize { get; }

    public IReadOnlyList<San9ScenarioSection> Sections { get; }

    public San9ScenarioSection GetRequiredSection(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Sections.Single(section => string.Equals(section.Id, id, StringComparison.Ordinal));
    }
}

public static class San9ScenarioArchive
{
    public const uint ExpectedFormatVersion = 0x20030317;

    private static readonly ReadOnlyCollection<San9ScenarioSectionDefinition> KnownSections =
        Array.AsReadOnly(
        new San9ScenarioSectionDefinition[]
        {
            new("head", 82),
            new("area", 90),
            new("ciki", 102),
            new("fbld", 476),
            new("hiho", 1_558),
            new("kans", 126),
            new("shak", 1_386),
            new("skrk", 70),
            new("kkgu", 240),
            new("jnki", 896),
            new("squa", 560_000),
            new("bldg", 5_600),
            new("city", 2_800),
            new("forc", 4_300),
            new("item", 2_640),
            new("unit", 32_000),
            new("prsn", 107_200),
        });

    public static IReadOnlyList<San9ScenarioSectionDefinition> ExpectedSections => KnownSections;

    public static San9ScenarioLayout ReadKnownLayout(Stream stream)
    {
        San9ScenarioLayout layout = ReadLayout(stream);

        if (layout.Sections.Count != KnownSections.Count)
        {
            throw new InvalidDataException(
                $"Expected {KnownSections.Count} scenario sections, but found {layout.Sections.Count}.");
        }

        for (int index = 0; index < KnownSections.Count; index++)
        {
            San9ScenarioSectionDefinition expected = KnownSections[index];
            San9ScenarioSection actual = layout.Sections[index];
            if (!string.Equals(actual.Id, expected.Id, StringComparison.Ordinal) || actual.Size != expected.Size)
            {
                throw new InvalidDataException(
                    $"Unexpected scenario section at index {index}: " +
                    $"expected {expected.Id}/{expected.Size}, found {actual.Id}/{actual.Size}.");
            }
        }

        return layout;
    }

    public static San9ScenarioHeader ReadHeader(Stream stream)
    {
        San9ScenarioLayout layout = ReadKnownLayout(stream);
        San9ScenarioSection head = layout.GetRequiredSection("head");
        stream.Position = head.DataOffset;

        using BinaryReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        uint formatVersion = reader.ReadUInt32();
        if (formatVersion != ExpectedFormatVersion)
        {
            throw new InvalidDataException(
                $"Expected scenario format version 0x{ExpectedFormatVersion:X8}, " +
                $"but found 0x{formatVersion:X8}.");
        }

        uint[] unknownWords = new uint[4];
        for (int index = 0; index < unknownWords.Length; index++)
        {
            unknownWords[index] = reader.ReadUInt32();
        }

        ushort scenarioId = reader.ReadUInt16();
        ushort packedFlags = reader.ReadUInt16();
        ushort encodedStartDate = reader.ReadUInt16();
        ushort allianceDurationTurns = reader.ReadUInt16();
        ushort mapWidth = reader.ReadUInt16();
        ushort mapHeight = reader.ReadUInt16();
        byte[] encodedCityForceMapCodes = reader.ReadBytes(50);
        if (encodedCityForceMapCodes.Length != 50 || stream.Position != head.DataOffset + head.Size)
        {
            throw new InvalidDataException("Scenario head chunk has an unexpected field layout.");
        }

        return new(
            formatVersion,
            Array.AsReadOnly(unknownWords),
            scenarioId,
            packedFlags,
            encodedStartDate,
            DecodeDate(encodedStartDate),
            allianceDurationTurns,
            mapWidth,
            mapHeight,
            Array.AsReadOnly(encodedCityForceMapCodes));
    }

    public static San9ScenarioHeader ReadInitialScenario190Header(Stream stream)
    {
        San9ScenarioHeader header = ReadHeader(stream);
        if (header.ScenarioId != 1 ||
            header.EncodedStartDate != 21_600 ||
            header.MapWidth != 200 ||
            header.MapHeight != 200)
        {
            throw new InvalidDataException(
                "Scenario does not match the registered 190-01-01 full-map content input.");
        }

        return header;
    }

    public static San9ScenarioDate DecodeDate(ushort encodedDate)
    {
        const int EpochYear = 130;
        const int DaysPerYear = 360;
        const int DaysPerMonth = 30;

        int year = EpochYear + encodedDate / DaysPerYear;
        int dayOfYear = encodedDate % DaysPerYear;
        int month = dayOfYear / DaysPerMonth + 1;
        int day = dayOfYear % DaysPerMonth + 1;
        return new(year, month, day);
    }

    public static San9ScenarioLayout ReadLayout(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("Scenario stream must be readable and seekable.", nameof(stream));
        }

        if (stream.Position != 0)
        {
            throw new ArgumentException("Scenario stream must be positioned at the beginning.", nameof(stream));
        }

        long start = stream.Position;
        if (stream.Length - start < 12)
        {
            throw new InvalidDataException("Scenario file is too short for a RIFF header.");
        }

        using BinaryReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        string signature = ReadTag(reader);
        if (!string.Equals(signature, "RIFF", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Scenario file does not start with RIFF.");
        }

        uint declaredPayloadSize = reader.ReadUInt32();
        long declaredEnd = checked(start + 8L + declaredPayloadSize);
        if (declaredEnd != stream.Length)
        {
            throw new InvalidDataException(
                $"RIFF size declares {declaredEnd - start} bytes, but the stream contains {stream.Length - start}.");
        }

        string formType = ReadTag(reader);
        if (!string.Equals(formType, "S9SC", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Expected S9SC form type, but found {formType}.");
        }

        List<San9ScenarioSection> sections = [];
        while (stream.Position < declaredEnd)
        {
            if (declaredEnd - stream.Position < 8)
            {
                throw new InvalidDataException("Scenario section header is truncated.");
            }

            string id = ReadTag(reader);
            uint rawSize = reader.ReadUInt32();
            if (rawSize > int.MaxValue)
            {
                throw new InvalidDataException($"Scenario section {id} is too large.");
            }

            long dataOffset = stream.Position - start;
            long paddedSize = rawSize + (rawSize & 1U);
            if (paddedSize > declaredEnd - stream.Position)
            {
                throw new InvalidDataException($"Scenario section {id} exceeds the RIFF boundary.");
            }

            sections.Add(new(id, (int)rawSize, dataOffset));
            stream.Seek(paddedSize, SeekOrigin.Current);
        }

        return new(stream.Length - start, sections.AsReadOnly());
    }

    private static string ReadTag(BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(4);
        if (bytes.Length != 4)
        {
            throw new InvalidDataException("FourCC value is truncated.");
        }

        if (bytes.Any(value => value is < 0x20 or > 0x7E))
        {
            throw new InvalidDataException("FourCC value contains non-ASCII characters.");
        }

        return Encoding.ASCII.GetString(bytes);
    }
}
