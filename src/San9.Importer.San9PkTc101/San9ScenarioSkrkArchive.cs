using System.Collections.ObjectModel;
using System.Text;

namespace San9.Importer.San9PkTc101;

public sealed record San9ScenarioSkrkRecord(
    int Id,
    ushort SerializedId,
    ushort OpaqueField02,
    ushort OpaqueField04,
    ushort OpaqueField06,
    ushort OpaqueField08);

public static class San9ScenarioSkrkArchive
{
    public const int RecordCount = 7;
    public const int RecordSize = 10;

    public static IReadOnlyList<San9ScenarioSkrkRecord> ReadKnownRecords(Stream stream)
    {
        San9ScenarioLayout layout = San9ScenarioArchive.ReadKnownLayout(stream);
        San9ScenarioSection section = layout.GetRequiredSection("skrk");
        if (section.Size != RecordCount * RecordSize)
        {
            throw new InvalidDataException("Scenario skrk section has an unexpected size.");
        }

        stream.Position = section.DataOffset;
        using BinaryReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        List<San9ScenarioSkrkRecord> records = new(RecordCount);
        for (int index = 0; index < RecordCount; index++)
        {
            records.Add(new(
                index,
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16()));
        }

        if (stream.Position != section.DataOffset + section.Size)
        {
            throw new InvalidDataException("Scenario skrk records do not fill the skrk section exactly.");
        }

        return new ReadOnlyCollection<San9ScenarioSkrkRecord>(records);
    }
}
