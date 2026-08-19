using System.Collections.ObjectModel;
using System.Text;

namespace San9.Importer.San9PkTc101;

public sealed record San9ScenarioOfficeRecord(
    int Id,
    uint CommandCapacity,
    ushort SerializedId,
    ushort OpaqueField06,
    ushort OpaqueField08,
    ushort RankGroupCode,
    ushort Salary,
    ushort AbilityMask,
    ushort AbilityBonus)
{
    public bool IsAssignable => Id < San9ScenarioOfficeArchive.NoOfficeId;

    public int? RankGroupId => RankGroupCode < San9ScenarioOfficeArchive.RankGroupCount
        ? RankGroupCode
        : null;

    public bool HasAbilityBonus(int abilityIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(abilityIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            abilityIndex,
            San9ScenarioOfficeArchive.AbilityCount);
        return (AbilityMask & (1 << abilityIndex)) != 0;
    }

    public bool HasLeadershipBonus => HasAbilityBonus(0);

    public bool HasStrengthBonus => HasAbilityBonus(1);

    public bool HasIntelligenceBonus => HasAbilityBonus(2);

    public bool HasPoliticsBonus => HasAbilityBonus(3);
}

public static class San9ScenarioOfficeArchive
{
    public const int RecordCount = 77;
    public const int RecordSize = 18;
    public const int NoOfficeId = 76;
    public const int AssignableOfficeCount = NoOfficeId;
    public const int RankGroupCount = 9;
    public const int AbilityCount = 4;

    public static IReadOnlyList<San9ScenarioOfficeRecord> ReadKnownRecords(Stream stream)
    {
        San9ScenarioLayout layout = San9ScenarioArchive.ReadKnownLayout(stream);
        San9ScenarioSection section = layout.GetRequiredSection("shak");
        if (section.Size != RecordCount * RecordSize)
        {
            throw new InvalidDataException("Scenario office section has an unexpected size.");
        }

        stream.Position = section.DataOffset;
        using BinaryReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        List<San9ScenarioOfficeRecord> records = new(RecordCount);
        for (int index = 0; index < RecordCount; index++)
        {
            records.Add(new(
                index,
                reader.ReadUInt32(),
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
            throw new InvalidDataException(
                "Scenario office records do not fill the office section exactly.");
        }

        return new ReadOnlyCollection<San9ScenarioOfficeRecord>(records);
    }
}
