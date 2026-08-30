using System.Collections.ObjectModel;
using System.Text;

namespace San9.Importer.San9PkTc101;

public enum San9ForceAllianceRole
{
    None = 0,
    Target = 1,
    Member = 2,
    Leader = 3,
}

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
    ushort RivalForceCode,
    ushort RankGroupCode,
    ushort ColorCode,
    ulong TacticFlags,
    IReadOnlyList<byte> DiplomaticRelations)
{
    public bool HasFlagBit(int bit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bit);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bit, sizeof(uint) * 8);
        return (Flags & (1u << bit)) != 0;
    }

    public bool IsPlayerControlled => (Flags & San9ScenarioForceArchive.PlayerControlledFlag) != 0;

    public bool IsBarbarian => (Flags & San9ScenarioForceArchive.BarbarianFlag) != 0;

    public bool IsAllianceMember => (Flags & San9ScenarioForceArchive.AllianceMemberFlag) != 0;

    public bool IsAllianceLeader => (Flags & San9ScenarioForceArchive.AllianceLeaderFlag) != 0;

    public bool IsAllianceTarget => (Flags & San9ScenarioForceArchive.AllianceTargetFlag) != 0;

    public San9ForceAllianceRole AllianceRole => IsAllianceTarget
        ? San9ForceAllianceRole.Target
        : IsAllianceLeader
            ? San9ForceAllianceRole.Leader
            : IsAllianceMember
                ? San9ForceAllianceRole.Member
                : San9ForceAllianceRole.None;

    public int ControlOrderCode => (int)((Flags & San9ScenarioForceArchive.ControlOrderMask)
        >> San9ScenarioForceArchive.ControlOrderBitOffset);

    public int? ControlOrder => ControlOrderCode == San9ScenarioForceArchive.NoControlOrderCode
        ? null
        : ControlOrderCode;

    public bool HasTactic(int tacticId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tacticId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            tacticId,
            San9ScenarioForceArchive.TacticCount);
        return (TacticFlags & (1UL << tacticId)) != 0;
    }

    public bool HasPersistentMarker(int markerIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(markerIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            markerIndex,
            San9ScenarioForceArchive.PersistentMarkerCount);
        return (Flags & (1u << (San9ScenarioForceArchive.PersistentMarkerBitOffset + markerIndex))) != 0;
    }

    public bool HasEventHistoryMarker(int markerIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(markerIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            markerIndex,
            San9ScenarioForceArchive.EventHistoryMarkerCount);
        return (Flags & (1u << (San9ScenarioForceArchive.EventHistoryMarkerBitOffset + markerIndex))) != 0;
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

    public int? RivalForceId => RivalForceCode < San9ScenarioForceArchive.ForceCount
        ? RivalForceCode
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
    public const int SerializedTacticFlagBitCount = 48;
    public const int TacticCount = 41;
    public const int DiplomaticRelationCount = 50;
    public const uint PlayerControlledFlag = 1u << 0;
    public const uint BarbarianFlag = 1u << 2;
    public const uint AllianceMemberFlag = 1u << 4;
    public const uint AllianceLeaderFlag = 1u << 5;
    public const uint AllianceTargetFlag = 1u << 6;
    public const int ControlOrderBitOffset = 8;
    public const uint ControlOrderMask = 0x00000F00;
    public const int NoControlOrderCode = 15;
    public const int PersistentMarkerBitOffset = 13;
    public const int PersistentMarkerCount = 5;
    public const int EventHistoryMarkerBitOffset = 16;
    public const int EventHistoryMarkerCount = 15;

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
            ushort rivalForceCode = reader.ReadUInt16();
            ushort rankGroupCode = reader.ReadUInt16();
            ushort colorCode = reader.ReadUInt16();
            ulong tacticFlags = ReadUInt48(reader);
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
                rivalForceCode,
                rankGroupCode,
                colorCode,
                tacticFlags,
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
        for (int bit = 0; bit < SerializedTacticFlagBitCount; bit += 8)
        {
            value |= (ulong)reader.ReadByte() << bit;
        }

        return value;
    }
}
