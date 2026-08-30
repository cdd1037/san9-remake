using System.Text;
using San9.Importer.San9PkTc101;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class San9ScenarioFormationArchiveTests
{
    [Fact]
    public void ReadKnownFormations_UsesRecordOrderAndPreservesAllSlots()
    {
        using MemoryStream stream = BuildScenarioArchive();
        for (int index = 0; index < San9ScenarioFormationArchive.RecordCount; index++)
        {
            stream.Position = 0x107E + index * San9ScenarioFormationArchive.RecordSize;
            using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);
            for (int field = 0; field < 13; field++)
            {
                writer.Write((ushort)(0x2000 + index * 0x20 + field));
            }

            writer.Write(new byte[] { 1, 3, 4, 5, 7 });
            writer.Write((byte)0x39);
        }

        stream.Position = 0;
        IReadOnlyList<San9ScenarioFormationRecord> records =
            San9ScenarioFormationArchive.ReadKnownRecords(stream);

        Assert.Equal(San9ScenarioFormationArchive.RecordCount, records.Count);
        Assert.Equal(0, records[0].Id);
        Assert.Equal(0x2000, records[0].SerializedId);
        Assert.Equal(0x2001, records[0].CatalogCode);
        Assert.Equal(0x2002, records[0].ReservedField04);
        Assert.Equal(0x2003, records[0].ReservedField06);
        Assert.Equal(0x2004, records[0].AgainstUnits);
        Assert.Equal(0x2005, records[0].AgainstSoldiers);
        Assert.Equal(0x2006, records[0].AgainstWalls);
        Assert.Equal(0x2007, records[0].DefensePower);
        Assert.Equal(0x2008, records[0].Mobility);
        Assert.Equal(0x2009, records[0].VisionRadius);
        Assert.Equal(0x200A, records[0].VisionAngleCode);
        Assert.Equal(0x200A * 45, records[0].VisionAngleDegrees);
        Assert.Equal(0x200B, records[0].DeploymentCost);
        Assert.Equal(0x200C, records[0].DescriptionResourceCode);
        Assert.Equal(new byte[] { 1, 3, 4, 5, 7 }, records[0].DeploymentSlotCodes);
        Assert.Equal(new FormationGridPosition(1, 0), records[0].GetDeploymentSlotPosition(0));
        Assert.Equal(new FormationGridPosition(0, 1), records[0].GetDeploymentSlotPosition(1));
        Assert.Equal(new FormationGridPosition(1, 1), records[0].GetDeploymentSlotPosition(2));
        Assert.Equal(new FormationGridPosition(2, 1), records[0].GetDeploymentSlotPosition(3));
        Assert.Equal(new FormationGridPosition(1, 2), records[0].GetDeploymentSlotPosition(4));
        Assert.Equal(0x39, records[0].IgnoredByte);
        Assert.Null(records[0].DescriptionId);
        Assert.Equal(27, records[27].Id);
        Assert.Equal(0x2360, records[27].SerializedId);
    }

    [Fact]
    public void ReadKnownFormations_DerivesDescriptionAndNoSlotWithoutDroppingRawValues()
    {
        using MemoryStream stream = BuildScenarioArchive();
        stream.Position = 0x107E + 4 * San9ScenarioFormationArchive.RecordSize + 0x18;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((ushort)14);
        }

        stream.Position = 0;
        San9ScenarioFormationRecord record = San9ScenarioFormationArchive.ReadKnownRecords(stream)[4];

        Assert.Equal((ushort)14, record.DescriptionResourceCode);
        Assert.Equal(14, record.DescriptionId);

        stream.Position = 0x107E + 4 * San9ScenarioFormationArchive.RecordSize + 0x1A;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((byte)San9ScenarioFormationArchive.NoFormationSlotCode);
        }

        stream.Position = 0;
        record = San9ScenarioFormationArchive.ReadKnownRecords(stream)[4];
        Assert.Null(record.GetDeploymentSlotPosition(0));
    }

    [Fact]
    public void ReadKnownFormations_RejectsUnexpectedSectionSize()
    {
        using MemoryStream stream = BuildScenarioArchive();
        stream.Position = 0x107A;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(894);
        }

        stream.Position = 0;
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioFormationArchive.ReadKnownRecords(stream));

        Assert.Contains("FourCC", exception.Message);
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
