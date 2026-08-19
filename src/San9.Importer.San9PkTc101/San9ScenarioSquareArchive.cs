using System.Collections.ObjectModel;
using System.Text;

namespace San9.Importer.San9PkTc101;

public sealed record San9ScenarioSquareRecord(
    int Id,
    ushort SerializedId,
    ushort OpaqueField02,
    ushort ZoneCode,
    byte OpaqueField06,
    byte OpaqueField07,
    byte OffsetCode,
    byte TerrainCode,
    byte OpaqueField0A,
    byte OpaqueField0B,
    byte OpaqueField0C,
    byte OpaqueField0D)
{
    public int GridX => Id % San9ScenarioSquareArchive.MapWidth;

    public int GridY => Id / San9ScenarioSquareArchive.MapWidth;

    public int? RegionCode => ZoneCode == ushort.MaxValue ? null : ZoneCode;

    public int? CityId => ZoneCode < San9ScenarioSquareArchive.CityCount ? ZoneCode : null;

    public int? ZoneRecordId => ZoneCode >= San9ScenarioSquareArchive.ZoneCodeOffset &&
        ZoneCode < San9ScenarioSquareArchive.RegionCodeCount
        ? ZoneCode - San9ScenarioSquareArchive.ZoneCodeOffset
        : null;

    public int? OffsetValue => OffsetCode == byte.MaxValue ? null : OffsetCode;

    public int? TerrainId => TerrainCode == byte.MaxValue ? null : TerrainCode;

    public int? OpaqueField0AValue => OpaqueField0A == byte.MaxValue ? null : OpaqueField0A;

    public int? OpaqueField0BValue => OpaqueField0B == byte.MaxValue ? null : OpaqueField0B;

    public int NormalizedField0C => OpaqueField0C == byte.MaxValue ? 0 : OpaqueField0C;

    public int OpaqueField0DLowEdgeMask => OpaqueField0D & 0x0F;

    public int OpaqueField0DHighEdgeMask => OpaqueField0D >> 4;
}

public static class San9ScenarioSquareArchive
{
    public const int RecordCount = 40000;
    public const int RecordSize = 14;
    public const int MapWidth = 200;
    public const int MapHeight = 200;
    public const int CityCount = 50;
    public const int ZoneCount = 200;
    public const int ZoneCodeOffset = CityCount;
    public const int RegionCodeCount = CityCount + ZoneCount;
    public const int TerrainCount = 17;

    static San9ScenarioSquareArchive()
    {
        if (MapWidth * MapHeight != RecordCount)
        {
            throw new InvalidOperationException("Scenario square dimensions do not match the record count.");
        }
    }

    public static int GetRecordId(int x, int y)
    {
        if ((uint)x >= MapWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if ((uint)y >= MapHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        return checked(y * MapWidth + x);
    }

    public static (int X, int Y) GetCoordinates(int recordId)
    {
        if ((uint)recordId >= RecordCount)
        {
            throw new ArgumentOutOfRangeException(nameof(recordId));
        }

        return (recordId % MapWidth, recordId / MapWidth);
    }

    public static IReadOnlyList<San9ScenarioSquareRecord> ReadKnownRecords(Stream stream)
    {
        San9ScenarioLayout layout = San9ScenarioArchive.ReadKnownLayout(stream);
        San9ScenarioSection section = layout.GetRequiredSection("squa");
        if (section.Size != RecordCount * RecordSize)
        {
            throw new InvalidDataException("Scenario square section has an unexpected size.");
        }

        stream.Position = section.DataOffset;
        using BinaryReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        List<San9ScenarioSquareRecord> records = new(RecordCount);
        for (int index = 0; index < RecordCount; index++)
        {
            San9ScenarioSquareRecord record = new(
                index,
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte());

            ValidateRecord(record, index);
            records.Add(record);
        }

        if (stream.Position != section.DataOffset + section.Size)
        {
            throw new InvalidDataException("Scenario square records do not fill the square section exactly.");
        }

        return new ReadOnlyCollection<San9ScenarioSquareRecord>(records);
    }

    public static void ValidateKnownTopology(
        IReadOnlyList<San9ScenarioSquareRecord> squares,
        IReadOnlyList<San9ScenarioCityRecord> cities,
        IReadOnlyList<San9ScenarioZoneRecord> zones)
    {
        ArgumentNullException.ThrowIfNull(squares);
        ArgumentNullException.ThrowIfNull(cities);
        ArgumentNullException.ThrowIfNull(zones);

        if (squares.Count != RecordCount)
        {
            throw new InvalidDataException(
                $"Expected {RecordCount} scenario squares, but found {squares.Count}.");
        }

        if (cities.Count != CityCount)
        {
            throw new InvalidDataException(
                $"Expected {CityCount} scenario cities, but found {cities.Count}.");
        }

        if (zones.Count != ZoneCount)
        {
            throw new InvalidDataException(
                $"Expected {ZoneCount} scenario zones, but found {zones.Count}.");
        }

        for (int index = 0; index < squares.Count; index++)
        {
            if (squares[index].Id != index)
            {
                throw new InvalidDataException(
                    $"Scenario square record {index} has a non-sequential runtime id.");
            }
        }

        Dictionary<int, string> occupiedCoordinates = new();
        for (int cityId = 0; cityId < cities.Count; cityId++)
        {
            San9ScenarioCityRecord city = cities[cityId];
            ValidateObjectCoordinate(
                squares,
                occupiedCoordinates,
                city.X,
                city.Y,
                cityId,
                $"city {cityId}");
        }

        for (int zoneId = 0; zoneId < zones.Count; zoneId++)
        {
            San9ScenarioZoneRecord zone = zones[zoneId];
            bool hasX = zone.XCoordinate is not null;
            bool hasY = zone.YCoordinate is not null;
            if (hasX != hasY)
            {
                throw new InvalidDataException(
                    $"Scenario zone record {zoneId} has only one coordinate.");
            }

            if (hasX)
            {
                ValidateObjectCoordinate(
                    squares,
                    occupiedCoordinates,
                    zone.XCoordinate!.Value,
                    zone.YCoordinate!.Value,
                    zoneId + ZoneCodeOffset,
                    $"zone {zoneId}");
            }
        }
    }

    private static void ValidateRecord(San9ScenarioSquareRecord record, int index)
    {
        if (record.ZoneCode != ushort.MaxValue && record.ZoneCode >= RegionCodeCount)
        {
            throw new InvalidDataException(
                $"Scenario square record {index} has an invalid zone code.");
        }
    }

    private static void ValidateObjectCoordinate(
        IReadOnlyList<San9ScenarioSquareRecord> squares,
        IDictionary<int, string> occupiedCoordinates,
        int x,
        int y,
        int expectedZoneCode,
        string objectName)
    {
        int squareId = GetRecordId(x, y);
        if (!occupiedCoordinates.TryAdd(squareId, objectName))
        {
            throw new InvalidDataException(
                $"{objectName} overlaps {occupiedCoordinates[squareId]} at ({x}, {y}).");
        }

        San9ScenarioSquareRecord square = squares[squareId];
        if (square.ZoneCode != expectedZoneCode)
        {
            throw new InvalidDataException(
                $"{objectName} at ({x}, {y}) maps to square {squareId}, " +
                $"but its zone code is {square.ZoneCode}, expected {expectedZoneCode}.");
        }
    }
}
