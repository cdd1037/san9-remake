using System.Collections.ObjectModel;
using System.Text;

namespace San9.Importer.San9PkTc101;

public sealed record San9ScenarioZoneRecord(
    int Id,
    uint Soldiers,
    ushort SerializedId,
    ushort FixedBuildingCode,
    ushort NameResourceCode,
    ushort NameReadingResourceCode,
    ushort ForceCode,
    ushort CityCode,
    ushort Durability,
    ushort X,
    ushort Y,
    ulong TacticFlags)
{
    public int? FixedBuildingId => FixedBuildingCode == ushort.MaxValue ? null : FixedBuildingCode;

    public int? ForceId => ForceCode == ushort.MaxValue ? null : ForceCode;

    public int? CityId => CityCode == ushort.MaxValue ? null : CityCode;

    public int? XCoordinate => X == ushort.MaxValue ? null : X;

    public int? YCoordinate => Y == ushort.MaxValue ? null : Y;

    public uint SoldierCapacity => FixedBuildingCode == San9ScenarioZoneArchive.CityFixedBuildingId
        ? San9ScenarioZoneArchive.CitySoldierCapacity
        : San9ScenarioZoneArchive.OtherSoldierCapacity;

    public uint RuntimeSoldiers => Math.Min(Soldiers, SoldierCapacity);

    public int NameMessageId => San9ScenarioZoneArchive.GetNameMessageId(NameResourceCode);

    public int NameReadingMessageId =>
        San9ScenarioZoneArchive.GetNameReadingMessageId(NameReadingResourceCode);

    public bool HasRockfall => HasTactic(San9ScenarioZoneArchive.RockfallTacticId);

    public bool HasArrowPlatform => HasTactic(San9ScenarioZoneArchive.ArrowPlatformTacticId);

    public bool HasTactic(int tacticId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tacticId);
        if (tacticId >= San9ScenarioZoneArchive.TacticCount)
        {
            throw new ArgumentOutOfRangeException(nameof(tacticId));
        }

        return (TacticFlags & (1UL << tacticId)) != 0;
    }
}

public static class San9ScenarioZoneArchive
{
    public const int RecordCount = 200;
    public const int RecordSize = 28;
    public const int FixedBuildingCount = 17;
    public const int ForceCount = 50;
    public const int CityCount = 50;
    public const int MapExtent = 200;
    public const int SerializedTacticFlagBitCount = 48;
    public const int TacticCount = 41;
    public const int RockfallTacticId = 39;
    public const int ArrowPlatformTacticId = 40;
    public const ushort CityFixedBuildingId = 0;
    public const uint CitySoldierCapacity = 500_000;
    public const uint OtherSoldierCapacity = 250_000;
    public const uint SoldiersLimit = CitySoldierCapacity;
    public const int NameMessageBaseId = 166;
    public const int NameReadingMessageBaseId = 167;
    public const int NameMessageStride = 2;

    public static int GetNameMessageId(ushort resourceCode) =>
        NameMessageBaseId + NameMessageStride * resourceCode;

    public static int GetNameReadingMessageId(ushort resourceCode) =>
        NameReadingMessageBaseId + NameMessageStride * resourceCode;

    public static IReadOnlyList<San9ScenarioZoneRecord> ReadKnownRecords(Stream stream)
    {
        San9ScenarioLayout layout = San9ScenarioArchive.ReadKnownLayout(stream);
        San9ScenarioSection section = layout.GetRequiredSection("bldg");
        if (section.Size != RecordCount * RecordSize)
        {
            throw new InvalidDataException("Scenario zone section has an unexpected size.");
        }

        stream.Position = section.DataOffset;
        using BinaryReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        List<San9ScenarioZoneRecord> records = new(RecordCount);
        for (int index = 0; index < RecordCount; index++)
        {
            San9ScenarioZoneRecord record = new(
                index,
                reader.ReadUInt32(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                ReadUInt48(reader));

            ValidateRecord(record, index);
            records.Add(record);
        }

        if (stream.Position != section.DataOffset + section.Size)
        {
            throw new InvalidDataException("Scenario zone records do not fill the zone section exactly.");
        }

        return new ReadOnlyCollection<San9ScenarioZoneRecord>(records);
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

    private static void ValidateRecord(San9ScenarioZoneRecord record, int index)
    {
        if (record.SerializedId != index)
        {
            throw new InvalidDataException($"Scenario zone record {index} has an inconsistent serialized id.");
        }

        if (record.Soldiers > SoldiersLimit)
        {
            throw new InvalidDataException($"Scenario zone record {index} has invalid soldiers.");
        }

        if (record.FixedBuildingCode != ushort.MaxValue && record.FixedBuildingCode >= FixedBuildingCount)
        {
            throw new InvalidDataException($"Scenario zone record {index} has an invalid fixed building code.");
        }

        if (record.ForceCode != ushort.MaxValue && record.ForceCode >= ForceCount)
        {
            throw new InvalidDataException($"Scenario zone record {index} has an invalid force code.");
        }

        if (record.CityCode != ushort.MaxValue && record.CityCode >= CityCount)
        {
            throw new InvalidDataException($"Scenario zone record {index} has an invalid city code.");
        }

        if (record.X != ushort.MaxValue && record.X >= MapExtent)
        {
            throw new InvalidDataException($"Scenario zone record {index} has an invalid x coordinate.");
        }

        if (record.Y != ushort.MaxValue && record.Y >= MapExtent)
        {
            throw new InvalidDataException($"Scenario zone record {index} has an invalid y coordinate.");
        }
    }
}
