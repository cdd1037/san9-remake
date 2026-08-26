using System.Collections.ObjectModel;
using System.Text;

namespace San9.Importer.San9PkTc101;

public sealed record San9ScenarioRankRecord(
    int Id,
    uint CommandCapacity,
    ushort SerializedId,
    ushort OpaqueField06,
    ushort OpaqueField08,
    ushort PromotionLoyaltyBonus,
    ushort PrestigeRequirement);

public static class San9ScenarioRankArchive
{
    public const int RecordCount = 9;
    public const int RecordSize = 14;

    public static IReadOnlyList<San9ScenarioRankRecord> ReadKnownRecords(Stream stream)
    {
        San9ScenarioLayout layout = San9ScenarioArchive.ReadKnownLayout(stream);
        San9ScenarioSection section = layout.GetRequiredSection("kans");
        if (section.Size != RecordCount * RecordSize)
        {
            throw new InvalidDataException("Scenario rank section has an unexpected size.");
        }

        stream.Position = section.DataOffset;
        using BinaryReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        List<San9ScenarioRankRecord> records = new(RecordCount);
        for (int index = 0; index < RecordCount; index++)
        {
            records.Add(new(
                index,
                reader.ReadUInt32(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16()));
        }

        if (stream.Position != section.DataOffset + section.Size)
        {
            throw new InvalidDataException(
                "Scenario rank records do not fill the rank section exactly.");
        }

        return new ReadOnlyCollection<San9ScenarioRankRecord>(records);
    }
}
