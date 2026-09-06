using System.Collections.ObjectModel;
using System.Text;

namespace San9.Importer.San9PkTc101;

public sealed record San9ScenarioFixedBuildingRecord(
    int Id,
    ushort SerializedId,
    ushort CatalogCode,
    ushort VisualDefinitionCode,
    ushort ConstructionCost,
    ushort MaxDurability,
    ushort OpaqueField0A,
    ushort ShapeLength,
    ushort ShapeWidth,
    ushort AppearanceId,
    ushort RecoveryPerTurn,
    ushort TacticCode,
    ulong TacticFlags)
{
    // Source definitions 10..16 map to runtime categories 7..10 (RE-0158).
    public bool IsFacilityListEntry => Id is >= 0 and < 10;

    public int FootprintWidth => ShapeLength;

    public int FootprintHeight => ShapeWidth;

    public long FootprintArea => (long)FootprintWidth * FootprintHeight;

    public int? TacticId => TacticCode < San9ScenarioFixedBuildingArchive.TacticCount
        ? TacticCode
        : null;

    public bool HasRockfall => HasTactic(San9ScenarioFixedBuildingArchive.RockfallTacticId);

    public bool HasArrowPlatform => HasTactic(San9ScenarioFixedBuildingArchive.ArrowPlatformTacticId);

    public bool HasTactic(int tacticId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tacticId);
        if (tacticId >= San9ScenarioFixedBuildingArchive.TacticCount)
        {
            throw new ArgumentOutOfRangeException(nameof(tacticId));
        }

        return (TacticFlags & (1UL << tacticId)) != 0;
    }
}

public static class San9ScenarioFixedBuildingArchive
{
    public const int RecordCount = 17;
    public const int RecordSize = 28;
    public const int TacticCount = 41;
    public const int RockfallTacticId = 39;
    public const int ArrowPlatformTacticId = 40;

    public static IReadOnlyList<San9ScenarioFixedBuildingRecord> ReadKnownRecords(Stream stream)
    {
        San9ScenarioLayout layout = San9ScenarioArchive.ReadKnownLayout(stream);
        San9ScenarioSection section = layout.GetRequiredSection("fbld");
        if (section.Size != RecordCount * RecordSize)
        {
            throw new InvalidDataException("Scenario fixed-building section has an unexpected size.");
        }

        stream.Position = section.DataOffset;
        using BinaryReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        List<San9ScenarioFixedBuildingRecord> records = new(RecordCount);
        for (int index = 0; index < RecordCount; index++)
        {
            records.Add(new(
                index,
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
                reader.ReadUInt16(),
                ReadUInt48(reader)));
        }

        if (stream.Position != section.DataOffset + section.Size)
        {
            throw new InvalidDataException(
                "Scenario fixed-building records do not fill the fixed-building section exactly.");
        }

        return new ReadOnlyCollection<San9ScenarioFixedBuildingRecord>(records);
    }

    private static ulong ReadUInt48(BinaryReader reader)
    {
        ulong value = 0;
        for (int byteIndex = 0; byteIndex < 6; byteIndex++)
        {
            value |= (ulong)reader.ReadByte() << (byteIndex * 8);
        }

        return value;
    }
}
