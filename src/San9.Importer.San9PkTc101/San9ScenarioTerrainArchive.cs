using System.Collections.ObjectModel;
using System.Text;

namespace San9.Importer.San9PkTc101;

public sealed record San9ScenarioTerrainRecord(
    int Id,
    ushort SerializedId,
    ushort OpaqueField02,
    ushort OpaqueField04);

public static class San9ScenarioTerrainArchive
{
    public const int RecordCount = 17;
    public const int RecordSize = 6;

    public static IReadOnlyList<San9ScenarioTerrainRecord> ReadKnownRecords(Stream stream)
    {
        San9ScenarioLayout layout = San9ScenarioArchive.ReadKnownLayout(stream);
        San9ScenarioSection section = layout.GetRequiredSection("ciki");
        if (section.Size != RecordCount * RecordSize)
        {
            throw new InvalidDataException("Scenario terrain section has an unexpected size.");
        }

        stream.Position = section.DataOffset;
        using BinaryReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        List<San9ScenarioTerrainRecord> records = new(RecordCount);
        for (int index = 0; index < RecordCount; index++)
        {
            records.Add(new(
                index,
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16()));
        }

        if (stream.Position != section.DataOffset + section.Size)
        {
            throw new InvalidDataException("Scenario terrain records do not fill the terrain section exactly.");
        }

        return new ReadOnlyCollection<San9ScenarioTerrainRecord>(records);
    }
}
