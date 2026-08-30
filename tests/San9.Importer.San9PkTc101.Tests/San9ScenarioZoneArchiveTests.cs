using System.Text;
using San9.Importer.San9PkTc101;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class San9ScenarioZoneArchiveTests
{
    [Fact]
    public void ReadKnownZones_PreservesVisibleFieldsAndTacticFlags()
    {
        using MemoryStream stream = BuildScenarioArchive();
        stream.Position = 0x89F8E;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((uint)1_000);
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)12);
            writer.Write((ushort)0);
            writer.Write((ushort)300);
            writer.Write((ushort)184);
            writer.Write((ushort)26);
            writer.Write(new byte[] { 0, 0, 0, 0, 0x80, 0 });
        }

        stream.Position = 0;
        IReadOnlyList<San9ScenarioZoneRecord> records =
            San9ScenarioZoneArchive.ReadKnownRecords(stream);

        San9ScenarioZoneRecord record = records[0];
        Assert.Equal(San9ScenarioZoneArchive.RecordCount, records.Count);
        Assert.Equal(0, record.Id);
        Assert.Equal((uint)1_000, record.Soldiers);
        Assert.Equal(0, record.SerializedId);
        Assert.Equal(1, record.FixedBuildingId);
        Assert.Equal(0, record.NameResourceCode);
        Assert.Equal(0, record.NameReadingResourceCode);
        Assert.Equal(166, record.NameMessageId);
        Assert.Equal(167, record.NameReadingMessageId);
        Assert.Equal(12, record.ForceId);
        Assert.Equal(0, record.CityId);
        Assert.Equal((ushort)300, record.Durability);
        Assert.Equal(184, record.XCoordinate);
        Assert.Equal(26, record.YCoordinate);
        Assert.Equal(San9ScenarioZoneArchive.OtherSoldierCapacity, record.SoldierCapacity);
        Assert.Equal((uint)1_000, record.RuntimeSoldiers);
        Assert.Equal(1UL << San9ScenarioZoneArchive.RockfallTacticId, record.TacticFlags);
        Assert.True(record.HasRockfall);
        Assert.False(record.HasArrowPlatform);
        Assert.True(record.HasTactic(San9ScenarioZoneArchive.RockfallTacticId));
    }

    [Fact]
    public void ReadKnownZones_MapsMissingReferencesAndCoordinatesToNull()
    {
        using MemoryStream stream = BuildScenarioArchive();
        stream.Position = 0x89F8E;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((uint)0);
            writer.Write((ushort)0);
            writer.Write(ushort.MaxValue);
            writer.Write((ushort)1);
            writer.Write((ushort)1);
            writer.Write(ushort.MaxValue);
            writer.Write(ushort.MaxValue);
            writer.Write((ushort)0);
            writer.Write(ushort.MaxValue);
            writer.Write(ushort.MaxValue);
            writer.Write(new byte[6]);
        }

        stream.Position = 0;
        San9ScenarioZoneRecord record = San9ScenarioZoneArchive.ReadKnownRecords(stream)[0];

        Assert.Null(record.FixedBuildingId);
        Assert.Null(record.ForceId);
        Assert.Null(record.CityId);
        Assert.Null(record.XCoordinate);
        Assert.Null(record.YCoordinate);
        Assert.False(record.HasRockfall);
    }

    [Fact]
    public void ReadKnownZones_PreservesReservedHighTacticBitsWithoutExposingThemAsTactics()
    {
        using MemoryStream stream = BuildScenarioArchive();
        stream.Position = 0x89F8E + 0x16;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(new byte[] { 0, 0, 0, 0, 0, 0x80 });
        }

        stream.Position = 0;
        San9ScenarioZoneRecord record = San9ScenarioZoneArchive.ReadKnownRecords(stream)[0];

        Assert.Equal(1UL << 47, record.TacticFlags);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => record.HasTactic(San9ScenarioZoneArchive.TacticCount));
    }

    [Theory]
    [InlineData(0, 500_000, 500_000)]
    [InlineData(1, 500_000, 250_000)]
    [InlineData(16, 300_000, 250_000)]
    [InlineData(ushort.MaxValue, 300_000, 250_000)]
    public void ReadKnownZones_DerivesRuntimeSoldierCapacityFromFixedBuildingType(
        ushort fixedBuildingCode,
        uint soldiers,
        uint expectedRuntimeSoldiers)
    {
        using MemoryStream stream = BuildScenarioArchive();
        stream.Position = 0x89F8E;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(soldiers);
            writer.Write((ushort)0);
            writer.Write(fixedBuildingCode);
        }

        stream.Position = 0;
        San9ScenarioZoneRecord record = San9ScenarioZoneArchive.ReadKnownRecords(stream)[0];

        uint expectedCapacity = fixedBuildingCode == San9ScenarioZoneArchive.CityFixedBuildingId
            ? San9ScenarioZoneArchive.CitySoldierCapacity
            : San9ScenarioZoneArchive.OtherSoldierCapacity;
        Assert.Equal(expectedCapacity, record.SoldierCapacity);
        Assert.Equal(expectedRuntimeSoldiers, record.RuntimeSoldiers);
    }

    [Fact]
    public void ZoneNameResourceCodes_MapToInterleavedMessagePairs()
    {
        Assert.Equal(166, San9ScenarioZoneArchive.GetNameMessageId(0));
        Assert.Equal(167, San9ScenarioZoneArchive.GetNameReadingMessageId(0));
        Assert.Equal(564, San9ScenarioZoneArchive.GetNameMessageId(199));
        Assert.Equal(565, San9ScenarioZoneArchive.GetNameReadingMessageId(199));
    }

    [Fact]
    public void ReadKnownZones_RejectsInvalidReference()
    {
        using MemoryStream stream = BuildScenarioArchive();
        stream.Position = 0x89F8E + 6;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((ushort)San9ScenarioZoneArchive.FixedBuildingCount);
        }

        stream.Position = 0;
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioZoneArchive.ReadKnownRecords(stream));

        Assert.Contains("fixed building code", exception.Message);
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

        stream.Position = 0x89F8E;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            for (int index = 0; index < San9ScenarioZoneArchive.RecordCount; index++)
            {
                writer.Write((uint)0);
                writer.Write((ushort)index);
                writer.Write(ushort.MaxValue);
                writer.Write((ushort)index);
                writer.Write((ushort)index);
                writer.Write(ushort.MaxValue);
                writer.Write(ushort.MaxValue);
                writer.Write((ushort)0);
                writer.Write(ushort.MaxValue);
                writer.Write(ushort.MaxValue);
                writer.Write(new byte[6]);
            }
        }

        stream.Position = 0;
        return stream;
    }
}
