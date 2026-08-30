using System.Collections.ObjectModel;
using System.Text;

namespace San9.Importer.San9PkTc101;

public sealed record San9ScenarioCountryTitleRecord(
    int Id,
    ushort SerializedId,
    ushort SerializedIdCopy02,
    ushort SerializedIdCopy04,
    ushort RulerPersonCode,
    ushort Area1Code,
    ushort Area2Code)
{
    public int NameMessageId => San9ScenarioCountryTitleArchive.GetNameMessageId(Id);

    public int NameReadingMessageId => San9ScenarioCountryTitleArchive.GetNameReadingMessageId(Id);

    public int? RulerPersonId => RulerPersonCode < San9ScenarioCountryTitleArchive.PersonReferenceLimit
        ? RulerPersonCode
        : null;

    public int? Area1Id => Area1Code < San9ScenarioCountryTitleArchive.AreaCount
        ? Area1Code
        : null;

    public int? Area2Id => Area2Code < San9ScenarioCountryTitleArchive.AreaCount
        ? Area2Code
        : null;
}

public static class San9ScenarioCountryTitleArchive
{
    public const int RecordCount = 20;
    public const int RecordSize = 12;
    public const int PersonReferenceLimit = 1_050;
    public const int AreaCount = 15;
    public const int NameMessageBaseId = 5_315;
    public const int NameReadingMessageBaseId = 5_316;
    public const int NameMessageStride = 2;

    public static int GetNameMessageId(int countryTitleId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(countryTitleId);
        if (countryTitleId >= RecordCount)
        {
            throw new ArgumentOutOfRangeException(nameof(countryTitleId));
        }

        return NameMessageBaseId + NameMessageStride * countryTitleId;
    }

    public static int GetNameReadingMessageId(int countryTitleId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(countryTitleId);
        if (countryTitleId >= RecordCount)
        {
            throw new ArgumentOutOfRangeException(nameof(countryTitleId));
        }

        return NameReadingMessageBaseId + NameMessageStride * countryTitleId;
    }

    public static IReadOnlyList<San9ScenarioCountryTitleRecord> ReadKnownRecords(Stream stream)
    {
        San9ScenarioLayout layout = San9ScenarioArchive.ReadKnownLayout(stream);
        San9ScenarioSection section = layout.GetRequiredSection("kkgu");
        if (section.Size != RecordCount * RecordSize)
        {
            throw new InvalidDataException("Scenario country-title section has an unexpected size.");
        }

        stream.Position = section.DataOffset;
        using BinaryReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        List<San9ScenarioCountryTitleRecord> records = new(RecordCount);
        for (int index = 0; index < RecordCount; index++)
        {
            records.Add(new(
                index,
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16()));
        }

        if (stream.Position != section.DataOffset + section.Size)
        {
            throw new InvalidDataException(
                "Scenario country-title records do not fill the country-title section exactly.");
        }

        return new ReadOnlyCollection<San9ScenarioCountryTitleRecord>(records);
    }
}
