using System.Collections.ObjectModel;

namespace San9.Importer.San9PkTc101;

public enum San9ScenarioItemDiscoveryState : ushort
{
    Invalid = 0,
    NotUnearthed = 1,
    Unearthed = 2,
}

public sealed record San9ScenarioItemRecord(
    ushort SerializedId,
    ushort ResourceId1,
    ushort ResourceId2,
    ushort ImageId,
    ushort CategoryId,
    ushort LoyaltyChange,
    ushort AbilityBonus,
    ushort TacticCode,
    ushort HolderPersonCode,
    ushort DiscoveryStateCode,
    ushort DiscoveryLocationCode)
{
    public San9ScenarioItemDiscoveryState DiscoveryState =>
        (San9ScenarioItemDiscoveryState)DiscoveryStateCode;

    public bool IsInvalid => DiscoveryState == San9ScenarioItemDiscoveryState.Invalid;

    public bool CanBeUnearthed =>
        DiscoveryState == San9ScenarioItemDiscoveryState.NotUnearthed;

    public bool IsUnearthed => DiscoveryState == San9ScenarioItemDiscoveryState.Unearthed;

    public int? TacticId => TacticCode < San9ScenarioItemArchive.TacticCount ? TacticCode : null;

    public int? HolderPersonId =>
        HolderPersonCode < San9ScenarioItemArchive.RuntimePersonCount ? HolderPersonCode : null;

    public int? DiscoveryLocationId =>
        DiscoveryLocationCode < San9ScenarioItemArchive.DiscoveryLocationLimit
            ? DiscoveryLocationCode
            : null;
}

public static class San9ScenarioItemArchive
{
    public const int RecordCount = 120;
    public const int RecordSize = 22;
    public const int TacticCount = 41;
    public const int RuntimePersonCount = 850;
    public const int DiscoveryLocationLimit = 250;

    private const ushort MissingCode = ushort.MaxValue;
    private const int ImageCount = 100;
    private const int CategoryCount = 13;
    private const int DiscoveryStateCount = 3;

    public static IReadOnlyList<San9ScenarioItemRecord> ReadKnownRecords(Stream stream)
    {
        San9ScenarioLayout layout = San9ScenarioArchive.ReadKnownLayout(stream);
        San9ScenarioSection section = layout.GetRequiredSection("item");
        if (section.Size != RecordCount * RecordSize)
        {
            throw new InvalidDataException("Scenario item section has an unexpected size.");
        }

        stream.Position = section.DataOffset;
        using BinaryReader reader = new(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        List<San9ScenarioItemRecord> records = new(RecordCount);
        for (int index = 0; index < RecordCount; index++)
        {
            San9ScenarioItemRecord record = new(
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16());

            ValidateRecord(record, index);
            records.Add(record);
        }

        if (stream.Position != section.DataOffset + section.Size)
        {
            throw new InvalidDataException("Scenario item records do not fill the item section exactly.");
        }

        return new ReadOnlyCollection<San9ScenarioItemRecord>(records);
    }

    private static void ValidateRecord(San9ScenarioItemRecord record, int index)
    {
        if (record.SerializedId != index || record.ResourceId1 != index || record.ResourceId2 != index)
        {
            throw new InvalidDataException($"Scenario item record {index} has inconsistent identity fields.");
        }

        if (record.ImageId >= ImageCount)
        {
            throw new InvalidDataException($"Scenario item record {index} has an invalid image id.");
        }

        if (record.CategoryId >= CategoryCount)
        {
            throw new InvalidDataException($"Scenario item record {index} has an invalid category id.");
        }

        if (record.TacticCode != MissingCode && record.TacticCode >= TacticCount)
        {
            throw new InvalidDataException($"Scenario item record {index} has an invalid tactic code.");
        }

        if (record.HolderPersonCode != MissingCode && record.HolderPersonCode >= RuntimePersonCount)
        {
            throw new InvalidDataException($"Scenario item record {index} has an invalid holder person code.");
        }

        if (record.DiscoveryStateCode >= DiscoveryStateCount)
        {
            throw new InvalidDataException($"Scenario item record {index} has an invalid discovery state code.");
        }

        if (record.DiscoveryLocationCode != MissingCode &&
            record.DiscoveryLocationCode >= DiscoveryLocationLimit)
        {
            throw new InvalidDataException($"Scenario item record {index} has an invalid discovery location code.");
        }
    }
}
