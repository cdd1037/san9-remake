using System.Collections.ObjectModel;
using System.Text;

namespace San9.Importer.San9PkTc101;

public sealed record San9ScenarioTacticRecord(
    int Id,
    ushort SerializedId,
    ushort OpaqueField02,
    ushort OpaqueField04,
    ushort OpaqueField06,
    ushort SystemCode,
    ushort TrainingRequirement,
    ushort OpaqueField0C,
    ushort OpaqueField0E,
    ushort Power,
    ushort OpaqueField12,
    ushort OpaqueField14,
    ushort OpaqueField16,
    ushort OpaqueField18,
    ushort OpaqueField1A,
    ushort MinimumInterval,
    ushort OpaqueField1E,
    ushort AffectsFacilitiesCode,
    ushort CompatibleArrayTypeMaskLow,
    ushort CompatibleArrayTypeMaskHigh)
{
    public bool AffectsFacilities => AffectsFacilitiesCode != 0;

    public uint CompatibleArrayTypeMask =>
        CompatibleArrayTypeMaskLow | ((uint)CompatibleArrayTypeMaskHigh << 16);

    public bool SupportsArrayType(int arrayTypeId) =>
        (uint)arrayTypeId < 28 &&
        (CompatibleArrayTypeMask & (1u << arrayTypeId)) != 0;
}

public static class San9ScenarioTacticArchive
{
    public const int RecordCount = 41;
    public const int RecordSize = 38;

    public static IReadOnlyList<San9ScenarioTacticRecord> ReadKnownRecords(Stream stream)
    {
        San9ScenarioLayout layout = San9ScenarioArchive.ReadKnownLayout(stream);
        San9ScenarioSection section = layout.GetRequiredSection("hiho");
        if (section.Size != RecordCount * RecordSize)
        {
            throw new InvalidDataException("Scenario tactic section has an unexpected size.");
        }

        stream.Position = section.DataOffset;
        using BinaryReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        List<San9ScenarioTacticRecord> records = new(RecordCount);
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
            throw new InvalidDataException("Scenario tactic records do not fill the tactic section exactly.");
        }

        return new ReadOnlyCollection<San9ScenarioTacticRecord>(records);
    }
}
