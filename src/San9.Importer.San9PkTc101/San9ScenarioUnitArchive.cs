using System.Collections.ObjectModel;
using System.Text;

namespace San9.Importer.San9PkTc101;

public enum San9ScenarioUnitStatus
{
    Invalid = 0,
    Normal = 1,
    Engaged = 2,
    Trap = 3,
    Disguised = 4,
    NoFormation = 5,
    Confused = 6,
    Routed = 7,
    HighSpirits = 8,
}

public enum San9ScenarioUnitTargetType
{
    Unit = 3,
    Zone = 4,
    City = 5,
}

public sealed record San9ScenarioUnitRecord(
    int Id,
    uint SoldierCount,
    uint WoundedSoldierCount,
    uint EncodedTargetObject,
    uint RuntimeFlags0C,
    ushort SerializedId,
    ushort UnitStatusCode,
    ushort StatusDurationDays,
    ushort Morale,
    ushort CommandCode,
    ushort ReservedField1A,
    ushort ReservedField1C,
    ushort HomeBaseCode,
    ushort ForceCode,
    ushort FormationCode,
    ushort CoordinateX,
    ushort CoordinateY)
{
    public bool HasRuntimeFlagBit(int bit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bit);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bit, sizeof(uint) * 8);
        return (RuntimeFlags0C & (1u << bit)) != 0;
    }

    public int LoadedSoldierCount => (int)Math.Min(
        SoldierCount,
        (uint)San9ScenarioUnitArchive.SoldierCountLimit);

    public int LoadedWoundedSoldierCount => (int)Math.Min(
        WoundedSoldierCount,
        (uint)San9ScenarioUnitArchive.WoundedSoldierCountLimit);

    public ushort TargetObjectId => (ushort)EncodedTargetObject;

    public ushort TargetObjectTypeCode => (ushort)(EncodedTargetObject >> 16);

    public San9ScenarioUnitTargetType? TargetObjectType => TargetObjectTypeCode switch
    {
        (ushort)San9ScenarioUnitTargetType.Unit => San9ScenarioUnitTargetType.Unit,
        (ushort)San9ScenarioUnitTargetType.Zone => San9ScenarioUnitTargetType.Zone,
        (ushort)San9ScenarioUnitTargetType.City => San9ScenarioUnitTargetType.City,
        _ => null,
    };

    public San9ScenarioUnitStatus? UnitStatus =>
        UnitStatusCode <= (ushort)San9ScenarioUnitStatus.HighSpirits
            ? (San9ScenarioUnitStatus)UnitStatusCode
            : null;

    public int LoadedCommandCode => CommandCode <= San9ScenarioUnitArchive.MaximumCommandCode
        ? CommandCode
        : -1;

    public bool AttacksWhenEnemyApproaches => HasRuntimeFlagBit(12);

    public bool AllowsAutonomousRetreat => HasRuntimeFlagBit(13);

    public bool AllowsPursuit => HasRuntimeFlagBit(14);

    public bool AttacksAfterAction => HasRuntimeFlagBit(15);

    public int? HomeBaseId => HomeBaseCode < San9ScenarioUnitArchive.HomeBaseCodeLimitExclusive
        ? HomeBaseCode
        : null;

    public int? ForceId => ForceCode < San9ScenarioUnitArchive.ForceCount
        ? ForceCode
        : null;

    public int? FormationId => FormationCode < San9ScenarioUnitArchive.FormationCount
        ? FormationCode
        : null;

    public int? XCoordinate => CoordinateX == ushort.MaxValue ? null : CoordinateX;

    public int? YCoordinate => CoordinateY == ushort.MaxValue ? null : CoordinateY;
}

public static class San9ScenarioUnitArchive
{
    public const int RecordCount = 800;
    public const int RecordSize = 40;
    public const int ForceCount = 50;
    public const int FormationCount = 28;
    public const int HomeBaseCodeLimitExclusive = 250;
    public const int MaximumCommandCode = 70;
    public const int SoldierCountLimit = 60_000;
    public const int WoundedSoldierCountLimit = 50_000;

    public static IReadOnlyList<San9ScenarioUnitRecord> ReadKnownRecords(Stream stream)
    {
        San9ScenarioLayout layout = San9ScenarioArchive.ReadKnownLayout(stream);
        San9ScenarioSection section = layout.GetRequiredSection("unit");
        if (section.Size != RecordCount * RecordSize)
        {
            throw new InvalidDataException("Scenario unit section has an unexpected size.");
        }

        stream.Position = section.DataOffset;
        using BinaryReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        List<San9ScenarioUnitRecord> records = new(RecordCount);
        for (int index = 0; index < RecordCount; index++)
        {
            records.Add(new(
                index,
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
                reader.ReadUInt16()));
        }

        if (stream.Position != section.DataOffset + section.Size)
        {
            throw new InvalidDataException("Scenario unit records do not fill the unit section exactly.");
        }

        return new ReadOnlyCollection<San9ScenarioUnitRecord>(records);
    }
}
