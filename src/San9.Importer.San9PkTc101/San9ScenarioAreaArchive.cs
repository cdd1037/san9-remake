using System.Collections.ObjectModel;
using System.Text;

namespace San9.Importer.San9PkTc101;

public sealed record San9ScenarioAreaRecord(
    int Id,
    ushort SerializedId,
    ushort SerializedIdCopy02,
    ushort SerializedIdCopy04)
{
    public int NameMessageId => San9ScenarioAreaArchive.GetNameMessageId(Id);

    public int NameReadingMessageId => San9ScenarioAreaArchive.GetNameReadingMessageId(Id);
}

public static class San9ScenarioAreaArchive
{
    public const int RecordCount = 15;
    public const int RecordSize = 6;
    public const int NameMessageBaseId = 136;
    public const int NameReadingMessageBaseId = 137;
    public const int NameMessageStride = 2;

    public static int GetNameMessageId(int areaId)
    {
        ValidateId(areaId);
        return NameMessageBaseId + NameMessageStride * areaId;
    }

    public static int GetNameReadingMessageId(int areaId)
    {
        ValidateId(areaId);
        return NameReadingMessageBaseId + NameMessageStride * areaId;
    }

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

    private static void ValidateId(int areaId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(areaId);
        if (areaId >= RecordCount)
        {
            throw new ArgumentOutOfRangeException(nameof(areaId));
        }
    }
}
