using System.Collections.ObjectModel;
using System.Text;

namespace San9.Importer.San9PkTc101;

public sealed record San9ScenarioAreaRecord(
    int Id,
    ushort SerializedId,
    ushort OpaqueField02,
    ushort OpaqueField04);

public static class San9ScenarioAreaArchive
{
    public const int RecordCount = 15;
    public const int RecordSize = 6;

    public static IReadOnlyList<San9ScenarioAreaRecord> ReadKnownRecords(Stream stream)
    {
        San9ScenarioLayout layout = San9ScenarioArchive.ReadKnownLayout(stream);
        San9ScenarioSection section = layout.GetRequiredSection("area");
        if (section.Size != RecordCount * RecordSize)
        {
            throw new InvalidDataException("Scenario area section has an unexpected size.");
        }

        stream.Position = section.DataOffset;
        using BinaryReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        List<San9ScenarioAreaRecord> records = new(RecordCount);
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
            throw new InvalidDataException("Scenario area records do not fill the area section exactly.");
        }

        return new ReadOnlyCollection<San9ScenarioAreaRecord>(records);
    }
}
