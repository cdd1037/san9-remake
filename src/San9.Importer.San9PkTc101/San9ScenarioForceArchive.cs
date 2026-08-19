using System.Collections.ObjectModel;
using System.Text;

namespace San9.Importer.San9PkTc101;

public sealed record San9ScenarioForceRecord(
    int Id,
    uint Money,
    uint Food,
    uint Flags,
    ushort SerializedId,
    ushort OwningForceCode,
    ushort CountryTitleCode,
    ushort LeaderCode,
    ushort BaseTrust,
    ushort Trust,
    ushort RelatedForceCode,
    ushort RankGroupCode,
    ushort ColorCode,
    ulong ForceTacticFlags,
    IReadOnlyList<byte> DiplomaticRelations)
{
    public bool HasFlagBit(int bit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bit);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bit, sizeof(uint) * 8);
        return (Flags & (1u << bit)) != 0;
    }

    public int RuntimeTypeCode => (Flags & 0x40) != 0
        ? 1
        : (Flags & 0x20) != 0
            ? 3
            : (Flags & 0x10) != 0
                ? 2
                : 0;

    public bool HasForceTacticFlagBit(int bit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bit);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            bit,
            San9ScenarioForceArchive.ForceTacticFlagBitCount);
        return (ForceTacticFlags & (1UL << bit)) != 0;
    }

    public int? OwningForceId => OwningForceCode < San9ScenarioForceArchive.ForceCount
        ? OwningForceCode
        : null;

    public int RuntimeOwningForceId => OwningForceId ?? Id;

    public int? CountryTitleId => CountryTitleCode < San9ScenarioForceArchive.CountryTitleCount
        ? CountryTitleCode
        : null;

    public int? LeaderId => LeaderCode < San9ScenarioForceArchive.PersonReferenceLimit
        ? LeaderCode
        : null;

    public int? RelatedForceId => RelatedForceCode < San9ScenarioForceArchive.ForceCount
        ? RelatedForceCode
        : null;

    public int? RankGroupId => RankGroupCode < San9ScenarioForceArchive.RankGroupCount
        ? RankGroupCode
        : null;
}

public static class San9ScenarioForceArchive
{
    public const int RecordCount = 50;
    public const int RecordSize = 86;
    public const int ForceCount = 50;
    public const int CountryTitleCount = 20;
    public const int PersonReferenceLimit = 850;
    public const int RankGroupCount = 9;
    public const int ForceTacticFlagBitCount = 48;
    public const int DiplomaticRelationCount = 50;

    public static IReadOnlyList<San9ScenarioForceRecord> ReadKnownRecords(Stream stream)
    {
        San9ScenarioLayout layout = San9ScenarioArchive.ReadKnownLayout(stream);
        San9ScenarioSection section = layout.GetRequiredSection("forc");
        if (section.Size != RecordCount * RecordSize)
        {
            throw new InvalidDataException("Scenario force section has an unexpected size.");
        }

        stream.Position = section.DataOffset;
        using BinaryReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        List<San9ScenarioForceRecord> records = new(RecordCount);
        for (int index = 0; index < RecordCount; index++)
        {
            uint money = reader.ReadUInt32();
            uint food = reader.ReadUInt32();
            uint flags = reader.ReadUInt32();
            ushort serializedId = reader.ReadUInt16();
            ushort owningForceCode = reader.ReadUInt16();
            ushort countryTitleCode = reader.ReadUInt16();
            ushort leaderCode = reader.ReadUInt16();
            ushort baseTrust = reader.ReadUInt16();
            ushort trust = reader.ReadUInt16();
            ushort relatedForceCode = reader.ReadUInt16();
            ushort rankGroupCode = reader.ReadUInt16();
            ushort colorCode = reader.ReadUInt16();
            ulong forceTacticFlags = ReadUInt48(reader);
            byte[] diplomaticRelations = reader.ReadBytes(DiplomaticRelationCount);
            if (diplomaticRelations.Length != DiplomaticRelationCount)
            {
                throw new InvalidDataException("Scenario force record is truncated.");
            }

            records.Add(new(
                index,
                money,
                food,
                flags,
                serializedId,
                owningForceCode,
                countryTitleCode,
                leaderCode,
                baseTrust,
                trust,
                relatedForceCode,
                rankGroupCode,
                colorCode,
                forceTacticFlags,
                Array.AsReadOnly(diplomaticRelations)));
        }

        if (stream.Position != section.DataOffset + section.Size)
        {
            throw new InvalidDataException("Scenario force records do not fill the force section exactly.");
        }

        return new ReadOnlyCollection<San9ScenarioForceRecord>(records);
    }

    private static ulong ReadUInt48(BinaryReader reader)
    {
        ulong value = 0;
        for (int bit = 0; bit < ForceTacticFlagBitCount; bit += 8)
        {
            value |= (ulong)reader.ReadByte() << bit;
        }

        return value;
    }
}
