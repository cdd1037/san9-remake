using System.Text;
using San9.Importer.San9PkTc101;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class San9ScenarioUnitArchiveTests
{
    [Fact]
    public void ReadKnownUnits_PreservesFieldsAndResolvesKnownReferences()
    {
        using MemoryStream stream = BuildScenarioArchive();
        San9ScenarioSection unitSection = GetUnitSection(stream);
        stream.Position = unitSection.DataOffset;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((uint)60_001);
            writer.Write((uint)50_001);
            writer.Write((uint)0x5002C);
            writer.Write((uint)0xF000);
            writer.Write((ushort)326);
            writer.Write((ushort)1);
            writer.Write((ushort)0);
            writer.Write((ushort)100);
            writer.Write((ushort)38);
            writer.Write((ushort)0x1A1A);
            writer.Write((ushort)0x1C1C);
            writer.Write((ushort)43);
            writer.Write((ushort)7);
            writer.Write((ushort)11);
            writer.Write((ushort)9);
            writer.Write((ushort)189);
        }

        stream.Position = 0;
        IReadOnlyList<San9ScenarioUnitRecord> records =
            San9ScenarioUnitArchive.ReadKnownRecords(stream);
        San9ScenarioUnitRecord record = records[0];

        Assert.Equal(San9ScenarioUnitArchive.RecordCount, records.Count);
        Assert.Equal(0, record.Id);
        Assert.Equal(60_001U, record.SoldierCount);
        Assert.Equal(50_001U, record.WoundedSoldierCount);
        Assert.Equal(0x5002CU, record.EncodedTargetObject);
        Assert.Equal(0x2C, record.TargetObjectCode);
        Assert.Equal(5, record.TargetObjectTypeCode);
        Assert.Equal(San9ScenarioUnitTargetType.City, record.TargetObjectType);
        Assert.Equal(0xF000U, record.RuntimeFlags0C);
        Assert.True(record.HasRuntimeFlagBit(12));
        Assert.True(record.HasRuntimeFlagBit(15));
        Assert.False(record.HasRuntimeFlagBit(0));
        Assert.True(record.AttacksWhenEnemyApproaches);
        Assert.True(record.AllowsAutonomousRetreat);
        Assert.True(record.AllowsPursuit);
        Assert.True(record.AttacksAfterAction);
        Assert.Equal(326, record.SerializedId);
        Assert.Equal(1, record.UnitStatusCode);
        Assert.Equal(San9ScenarioUnitStatus.Normal, record.UnitStatus);
        Assert.Equal(0, record.OpaqueField14);
        Assert.Equal(100, record.Morale);
        Assert.Equal(38, record.CommandCode);
        Assert.Equal(38, record.LoadedCommandCode);
        Assert.Equal(0x1A1A, record.OpaqueField1A);
        Assert.Equal(0x1C1C, record.OpaqueField1C);
        Assert.Equal(43, record.AreaId);
        Assert.Equal(7, record.ForceId);
        Assert.Equal(11, record.ArrayId);
        Assert.Equal(9, record.XCoordinate);
        Assert.Equal(189, record.YCoordinate);
        Assert.Equal(San9ScenarioUnitArchive.SoldierCountLimit, record.LoadedSoldierCount);
        Assert.Equal(San9ScenarioUnitArchive.WoundedSoldierCountLimit, record.LoadedWoundedSoldierCount);
    }

    [Fact]
    public void UnitFlags_RejectOutOfRangeBitQueries()
    {
        using MemoryStream stream = BuildScenarioArchive();
        San9ScenarioUnitRecord record = San9ScenarioUnitArchive.ReadKnownRecords(stream)[0];

        Assert.Throws<ArgumentOutOfRangeException>(() => record.HasRuntimeFlagBit(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => record.HasRuntimeFlagBit(32));
    }

    [Fact]
    public void ReadKnownUnits_MapsRuntimeInvalidReferencesAndMissingCoordinatesToNull()
    {
        using MemoryStream stream = BuildScenarioArchive();
        San9ScenarioSection unitSection = GetUnitSection(stream);
        stream.Position = unitSection.DataOffset;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(new byte[16]);
            writer.Write((ushort)71);
            writer.Write((ushort)9);
            writer.Write((ushort)0);
            writer.Write((ushort)100);
            writer.Write(ushort.MaxValue);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)250);
            writer.Write((ushort)50);
            writer.Write((ushort)28);
            writer.Write(ushort.MaxValue);
            writer.Write(ushort.MaxValue);
        }

        stream.Position = 0;
        San9ScenarioUnitRecord record = San9ScenarioUnitArchive.ReadKnownRecords(stream)[0];

        Assert.Null(record.TargetObjectType);
        Assert.Null(record.UnitStatus);
        Assert.Equal(-1, record.LoadedCommandCode);
        Assert.Null(record.AreaId);
        Assert.Null(record.ForceId);
        Assert.Null(record.ArrayId);
        Assert.Null(record.XCoordinate);
        Assert.Null(record.YCoordinate);
    }

    [Theory]
    [InlineData(3, San9ScenarioUnitTargetType.Unit)]
    [InlineData(4, San9ScenarioUnitTargetType.Zone)]
    [InlineData(5, San9ScenarioUnitTargetType.City)]
    public void TargetObjectType_MapsKnownRuntimeTypes(
        ushort typeCode,
        San9ScenarioUnitTargetType expected)
    {
        San9ScenarioUnitRecord record = new(
            0, 0, 0, (uint)(typeCode << 16) | 42, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        Assert.Equal(expected, record.TargetObjectType);
        Assert.Equal(42, record.TargetObjectCode);
    }

    [Theory]
    [InlineData(0, San9ScenarioUnitStatus.Invalid)]
    [InlineData(1, San9ScenarioUnitStatus.Normal)]
    [InlineData(2, San9ScenarioUnitStatus.Engaged)]
    [InlineData(3, San9ScenarioUnitStatus.Trap)]
    [InlineData(4, San9ScenarioUnitStatus.Disguised)]
    [InlineData(5, San9ScenarioUnitStatus.NoFormation)]
    [InlineData(6, San9ScenarioUnitStatus.Confused)]
    [InlineData(7, San9ScenarioUnitStatus.Routed)]
    [InlineData(8, San9ScenarioUnitStatus.HighSpirits)]
    public void UnitStatus_MapsKnownStatusDomain(
        ushort statusCode,
        San9ScenarioUnitStatus expected)
    {
        San9ScenarioUnitRecord record = new(
            0, 0, 0, 0, 0, 0, statusCode, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        Assert.Equal(expected, record.UnitStatus);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(70, 70)]
    [InlineData(71, -1)]
    [InlineData(ushort.MaxValue, -1)]
    public void LoadedCommandCode_NormalizesRuntimeCommandDomain(
        ushort commandCode,
        int expected)
    {
        San9ScenarioUnitRecord record = new(
            0, 0, 0, 0, 0, 0, 0, 0, 0, commandCode, 0, 0, 0, 0, 0, 0, 0);

        Assert.Equal(expected, record.LoadedCommandCode);
    }

    [Fact]
    public void ReadKnownUnits_RejectsUnexpectedSectionSize()
    {
        using MemoryStream stream = BuildScenarioArchive();
        San9ScenarioSection unitSection = GetUnitSection(stream);
        stream.Position = unitSection.DataOffset - 4;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(San9ScenarioUnitArchive.RecordCount * San9ScenarioUnitArchive.RecordSize - 1);
        }

        stream.Position = 0;
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioUnitArchive.ReadKnownRecords(stream));

        Assert.Contains("unit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static San9ScenarioSection GetUnitSection(Stream stream)
    {
        stream.Position = 0;
        return San9ScenarioArchive.ReadKnownLayout(stream).GetRequiredSection("unit");
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
