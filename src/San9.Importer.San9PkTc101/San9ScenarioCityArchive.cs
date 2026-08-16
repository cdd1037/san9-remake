using System.Collections.ObjectModel;

namespace San9.Importer.San9PkTc101;

public sealed record San9ScenarioCityRecord(
    uint CityFlags,
    uint Population,
    uint DraftPopulation,
    uint Soldiers,
    ushort SerializedId,
    ushort AuxiliaryId1,
    ushort AuxiliaryId2,
    ushort AreaCode,
    ushort Reserved18,
    ushort Reserved1A,
    ushort ForceCode,
    ushort PublicOrder,
    ushort MaxIncome,
    ushort MaxHarvest,
    ushort Income,
    ushort Harvest,
    ushort Durability,
    ushort MaxDurability,
    ushort X,
    ushort Y,
    ushort TerrainCode,
    ulong TacticFlags)
{
    public bool HasTrader => (CityFlags & San9ScenarioCityArchive.TraderFlag) != 0;

    public int? ForceId => ForceCode < San9ScenarioCityArchive.ForceCount ? ForceCode : null;

    public bool HasRockfall => HasTactic(San9ScenarioCityArchive.RockfallTacticId);

    public bool HasArrowPlatform => HasTactic(San9ScenarioCityArchive.ArrowPlatformTacticId);

    public bool HasTactic(int tacticId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tacticId);
        if (tacticId >= San9ScenarioCityArchive.TacticCount)
        {
            throw new ArgumentOutOfRangeException(nameof(tacticId));
        }

        return (TacticFlags & (1UL << tacticId)) != 0;
    }
}

public static class San9ScenarioCityArchive
{
    public const int RecordCount = 50;
    public const int RecordSize = 56;
    public const int AreaCount = 15;
    public const int ForceCount = 50;
    public const int TacticCount = 41;
    public const int RockfallTacticId = 39;
    public const int ArrowPlatformTacticId = 40;
    public const uint TraderFlag = 1;

    private const ushort MissingCode = ushort.MaxValue;
    private const int PopulationLimit = 1_000_000;
    private const int MilitaryPopulationLimit = 500_000;
    private const int PublicOrderLimit = 1_000;
    private const int DomesticLimit = 1_000;
    private const int MaxDurabilityLimit = 1_500;
    private const int MapExtent = 200;
    private const int TerrainCount = 3;

    public static IReadOnlyList<San9ScenarioCityRecord> ReadKnownRecords(Stream stream)
    {
        San9ScenarioLayout layout = San9ScenarioArchive.ReadKnownLayout(stream);
        San9ScenarioSection section = layout.GetRequiredSection("city");
        if (section.Size != RecordCount * RecordSize)
        {
            throw new InvalidDataException("Scenario city section has an unexpected size.");
        }

        stream.Position = section.DataOffset;
        using BinaryReader reader = new(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        List<San9ScenarioCityRecord> records = new(RecordCount);
        for (int index = 0; index < RecordCount; index++)
        {
            San9ScenarioCityRecord record = new(
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
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
            throw new InvalidDataException("Scenario city records do not fill the city section exactly.");
        }

        return new ReadOnlyCollection<San9ScenarioCityRecord>(records);
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

    private static void ValidateRecord(San9ScenarioCityRecord record, int index)
    {
        if ((record.CityFlags & ~TraderFlag) != 0)
        {
            throw new InvalidDataException($"Scenario city record {index} has unknown city flags.");
        }

        if (record.Population > PopulationLimit)
        {
            throw new InvalidDataException($"Scenario city record {index} has an invalid population.");
        }

        if (record.DraftPopulation > MilitaryPopulationLimit || record.Soldiers > MilitaryPopulationLimit)
        {
            throw new InvalidDataException($"Scenario city record {index} has invalid military population.");
        }

        if (record.SerializedId != index || record.AuxiliaryId1 != index || record.AuxiliaryId2 != index)
        {
            throw new InvalidDataException($"Scenario city record {index} has inconsistent identity fields.");
        }

        if (record.AreaCode >= AreaCount)
        {
            throw new InvalidDataException($"Scenario city record {index} has an invalid area code.");
        }

        if (record.ForceCode != MissingCode && record.ForceCode >= ForceCount)
        {
            throw new InvalidDataException($"Scenario city record {index} has an invalid force code.");
        }

        if (record.PublicOrder > PublicOrderLimit)
        {
            throw new InvalidDataException($"Scenario city record {index} has invalid public order.");
        }

        if (record.MaxIncome > DomesticLimit || record.Income > record.MaxIncome)
        {
            throw new InvalidDataException($"Scenario city record {index} has invalid income.");
        }

        if (record.MaxHarvest > DomesticLimit || record.Harvest > record.MaxHarvest)
        {
            throw new InvalidDataException($"Scenario city record {index} has invalid harvest.");
        }

        if (record.MaxDurability > MaxDurabilityLimit || record.Durability > record.MaxDurability)
        {
            throw new InvalidDataException($"Scenario city record {index} has invalid durability.");
        }

        if (record.X >= MapExtent || record.Y >= MapExtent)
        {
            throw new InvalidDataException($"Scenario city record {index} has invalid coordinates.");
        }

        if (record.TerrainCode >= TerrainCount)
        {
            throw new InvalidDataException($"Scenario city record {index} has an invalid terrain code.");
        }

        if ((record.TacticFlags >> TacticCount) != 0)
        {
            throw new InvalidDataException($"Scenario city record {index} has out-of-range tactic flags.");
        }
    }
}
