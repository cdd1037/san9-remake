using System.Text;
using San9.Importer.San9PkTc101;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class San9ScenarioSquareArchiveTests
{
    [Fact]
    public void ReadKnownSquares_PreservesByteLayoutAndTargetSentinels()
    {
        using MemoryStream stream = BuildScenarioArchive();
        stream.Position = 0x1406;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((ushort)0x4000);
            writer.Write((ushort)0x4001);
            writer.Write(ushort.MaxValue);
            writer.Write(new byte[] { 0xFE, 0x10, 0xFF, 0x02, 0xFF, 0xFF, 0xFF, 0x39 });
        }

        stream.Position = 0x1406 + San9ScenarioSquareArchive.RecordSize;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((ushort)1);
            writer.Write((ushort)966);
            writer.Write((ushort)118);
            writer.Write(new byte[] { 0, 0, 4, 2, 0, 2, 0, 0 });
        }

        stream.Position = 0;
        IReadOnlyList<San9ScenarioSquareRecord> records =
            San9ScenarioSquareArchive.ReadKnownRecords(stream);

        Assert.Equal(San9ScenarioSquareArchive.RecordCount, records.Count);
        Assert.Equal(0, records[0].Id);
        Assert.Equal(0x4000, records[0].SerializedId);
        Assert.Equal(0x4001, records[0].OpaqueField02);
        Assert.Equal(ushort.MaxValue, records[0].ZoneCode);
        Assert.Null(records[0].RegionCode);
        Assert.Equal(0xFE, records[0].OpaqueField06);
        Assert.Equal(0x10, records[0].OpaqueField07);
        Assert.Equal(0, records[0].GridX);
        Assert.Equal(0, records[0].GridY);
        Assert.Equal(0xFF, records[0].OffsetCode);
        Assert.Null(records[0].OffsetValue);
        Assert.Equal(2, records[0].TerrainCode);
        Assert.Equal(2, records[0].TerrainId);
        Assert.Equal(0xFF, records[0].OpaqueField0A);
        Assert.Null(records[0].OpaqueField0AValue);
        Assert.Equal(0xFF, records[0].OpaqueField0B);
        Assert.Null(records[0].OpaqueField0BValue);
        Assert.Equal(0xFF, records[0].OpaqueField0C);
        Assert.Equal(0, records[0].NormalizedField0C);
        Assert.Equal(0x39, records[0].OpaqueField0D);
        Assert.Equal(0x09, records[0].OpaqueField0DLowEdgeMask);
        Assert.Equal(0x03, records[0].OpaqueField0DHighEdgeMask);

        Assert.Equal(1, records[1].SerializedId);
        Assert.Equal(966, records[1].OpaqueField02);
        Assert.Equal(118, records[1].ZoneCode);
        Assert.Null(records[1].CityId);
        Assert.Equal(68, records[1].ZoneRecordId);
        Assert.Equal(4, records[1].OffsetValue);
        Assert.Equal(2, records[1].TerrainId);
        Assert.Equal(27, records[27].Id);
    }

    [Fact]
    public void SquareCoordinates_UseRowMajorMapIndex()
    {
        Assert.Equal(0, San9ScenarioSquareArchive.GetRecordId(0, 0));
        Assert.Equal(164, San9ScenarioSquareArchive.GetRecordId(164, 0));
        Assert.Equal(3_564, San9ScenarioSquareArchive.GetRecordId(164, 17));
        Assert.Equal((164, 17), San9ScenarioSquareArchive.GetCoordinates(3_564));
        Assert.Equal((199, 199), San9ScenarioSquareArchive.GetCoordinates(39_999));
    }

    [Fact]
    public void ValidateKnownTopology_MapsCitiesAndZonesToEncodedRegionCodes()
    {
        San9ScenarioSquareRecord[] squares = CreateSquares();
        San9ScenarioCityRecord[] cities = CreateCities();
        San9ScenarioZoneRecord[] zones = CreateZones();

        San9ScenarioSquareArchive.ValidateKnownTopology(squares, cities, zones);
    }

    [Fact]
    public void ValidateKnownTopology_RejectsMismatchedObjectRegionCode()
    {
        San9ScenarioSquareRecord[] squares = CreateSquares();
        San9ScenarioCityRecord[] cities = CreateCities();
        San9ScenarioZoneRecord[] zones = CreateZones();
        squares[0] = squares[0] with { ZoneCode = 1 };

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioSquareArchive.ValidateKnownTopology(squares, cities, zones));

        Assert.Contains("city 0", exception.Message, StringComparison.Ordinal);
        Assert.Contains("expected 0", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadKnownSquares_RejectsUnexpectedSectionSize()
    {
        using MemoryStream stream = BuildScenarioArchive();
        stream.Position = 0x1402;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(559986);
        }

        stream.Position = 0;
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioSquareArchive.ReadKnownRecords(stream));

        Assert.Contains("FourCC", exception.Message);
    }

    [Fact]
    public void ReadKnownSquares_RejectsOutOfRangeRegionCode()
    {
        using MemoryStream stream = BuildScenarioArchive();
        stream.Position = 0x1406 + 4;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((ushort)San9ScenarioSquareArchive.RegionCodeCount);
        }

        stream.Position = 0;
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioSquareArchive.ReadKnownRecords(stream));

        Assert.Contains("zone code", exception.Message, StringComparison.Ordinal);
    }

    private static San9ScenarioSquareRecord[] CreateSquares()
    {
        San9ScenarioSquareRecord[] squares = Enumerable.Range(0, San9ScenarioSquareArchive.RecordCount)
            .Select(index => new San9ScenarioSquareRecord(
                index,
                (ushort)index,
                0,
                ushort.MaxValue,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0))
            .ToArray();

        for (int cityId = 0; cityId < San9ScenarioSquareArchive.CityCount; cityId++)
        {
            squares[cityId] = squares[cityId] with { ZoneCode = (ushort)cityId };
        }

        squares[San9ScenarioSquareArchive.CityCount] =
            squares[San9ScenarioSquareArchive.CityCount] with
            {
                ZoneCode = San9ScenarioSquareArchive.ZoneCodeOffset,
            };
        return squares;
    }

    private static San9ScenarioCityRecord[] CreateCities()
    {
        return Enumerable.Range(0, San9ScenarioSquareArchive.CityCount)
            .Select(index => new San9ScenarioCityRecord(
                0,
                0,
                0,
                0,
                (ushort)index,
                (ushort)index,
                (ushort)index,
                0,
                0,
                0,
                ushort.MaxValue,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                (ushort)index,
                0,
                0,
                0))
            .ToArray();
    }

    private static San9ScenarioZoneRecord[] CreateZones()
    {
        return Enumerable.Range(0, San9ScenarioSquareArchive.ZoneCount)
            .Select(index => new San9ScenarioZoneRecord(
                index,
                0,
                (ushort)index,
                ushort.MaxValue,
                0,
                0,
                ushort.MaxValue,
                ushort.MaxValue,
                0,
                index == 0 ? (ushort)San9ScenarioSquareArchive.CityCount : ushort.MaxValue,
                index == 0 ? (ushort)0 : ushort.MaxValue,
                0))
            .ToArray();
    }

    private static MemoryStream BuildScenarioArchive()
    {
        MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(0);
            writer.Write(Encoding.ASCII.GetBytes("S9SC"));
            foreach (San9ScenarioSectionDefinition section in San9ScenarioArchive.ExpectedSections)
            {
                writer.Write(Encoding.ASCII.GetBytes(section.Id));
                writer.Write(section.Size);
                writer.Write(new byte[section.Size]);
                if ((section.Size & 1) != 0)
                {
                    writer.Write((byte)0);
                }
            }

            stream.Position = 4;
            writer.Write(checked((int)stream.Length - 8));
        }

        stream.Position = 0;
        return stream;
    }
}
