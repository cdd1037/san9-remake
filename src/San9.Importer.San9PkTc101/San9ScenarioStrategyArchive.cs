using System.Collections.ObjectModel;
using System.Text;

namespace San9.Importer.San9PkTc101;

public sealed record San9ScenarioStrategyRecord(
    int Id,
    ushort SerializedId,
    ushort SerializedIdCopy02,
    ushort OpaqueField04,
    ushort OpaqueField06,
    ushort OpaqueField08)
{
    public int? RelatedTacticId => San9ScenarioStrategyArchive.GetRelatedTacticId(Id);
}

public static class San9ScenarioStrategyArchive
{
    public const int RecordCount = 7;
    public const int RecordSize = 10;
    public const int RelatedTacticBaseId = 35;
    public const int RelatedTacticCount = 6;

    public static int? GetRelatedTacticId(int strategyId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(strategyId);
        if (strategyId >= RecordCount)
        {
            throw new ArgumentOutOfRangeException(nameof(strategyId));
        }

        return strategyId < RelatedTacticCount
            ? RelatedTacticBaseId + strategyId
            : null;
    }

    public static IReadOnlyList<San9ScenarioStrategyRecord> ReadKnownRecords(Stream stream)
    {
        San9ScenarioLayout layout = San9ScenarioArchive.ReadKnownLayout(stream);
        San9ScenarioSection section = layout.GetRequiredSection("skrk");
        if (section.Size != RecordCount * RecordSize)
        {
            throw new InvalidDataException("Scenario skrk section has an unexpected size.");
        }

        stream.Position = section.DataOffset;
        using BinaryReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        List<San9ScenarioStrategyRecord> records = new(RecordCount);
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

        return new ReadOnlyCollection<San9ScenarioStrategyRecord>(records);
    }
}
