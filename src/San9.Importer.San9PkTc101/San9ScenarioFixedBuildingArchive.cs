using System.Collections.ObjectModel;
using System.Text;

namespace San9.Importer.San9PkTc101;

public sealed record San9ScenarioFixedBuildingRecord(
    int Id,
    ushort SerializedId,
    ushort OpaqueField02,
    ushort OpaqueField04,
    ushort ConstructionCost,
    ushort MaxDurability,
    ushort OpaqueField0A,
    ushort ShapeLength,
    ushort ShapeWidth,
    ushort AppearanceId,
    ushort RecoveryPerTurn,
    ushort TacticCode,
    IReadOnlyList<byte> OpaqueTail)
{
    public int FootprintWidth => ShapeLength;

    public int FootprintHeight => ShapeWidth;

    public long FootprintArea => (long)FootprintWidth * FootprintHeight;

    public int? TacticId => TacticCode < San9ScenarioFixedBuildingArchive.TacticCount
        ? TacticCode
        : null;
}

public static class San9ScenarioFixedBuildingArchive
{
    public const int RecordCount = 17;
    public const int RecordSize = 28;
    public const int TacticCount = 41;
    public const int OpaqueTailSize = 6;

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
                Array.AsReadOnly(reader.ReadBytes(OpaqueTailSize))));
        }

        if (stream.Position != section.DataOffset + section.Size)
        {
            throw new InvalidDataException(
                "Scenario fixed-building records do not fill the fixed-building section exactly.");
        }

        return new ReadOnlyCollection<San9ScenarioFixedBuildingRecord>(records);
    }
}
