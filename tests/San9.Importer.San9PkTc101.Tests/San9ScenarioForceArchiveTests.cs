using System.Text;
using San9.Importer.San9PkTc101;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class San9ScenarioForceArchiveTests
{
    [Fact]
    public void ReadKnownForces_PreservesFieldsAndResolvesRuntimeCodes()
    {
        using MemoryStream stream = BuildScenarioArchive();
        stream.Position = 0x8C06E;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((uint)3_100);
            writer.Write((uint)30_800);
            writer.Write((uint)0x10);
            writer.Write((ushort)31);
            writer.Write((ushort)7);
            writer.Write((ushort)3);
            writer.Write((ushort)326);
            writer.Write((ushort)150);
            writer.Write((ushort)156);
            writer.Write(ushort.MaxValue);
            writer.Write((ushort)8);
            writer.Write((ushort)12);
            writer.Write(new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50, 0x60 });
            writer.Write(Enumerable.Range(0, 50).Select(value => (byte)value).ToArray());
        }

        stream.Position = 0;
        IReadOnlyList<San9ScenarioForceRecord> records = San9ScenarioForceArchive.ReadKnownRecords(stream);
        San9ScenarioForceRecord record = records[0];

        Assert.Equal(San9ScenarioForceArchive.RecordCount, records.Count);
        Assert.Equal(0, record.Id);
        Assert.Equal((uint)3_100, record.Money);
        Assert.Equal((uint)30_800, record.Food);
        Assert.Equal((uint)0x10, record.Flags);
        Assert.Equal(31, record.SerializedId);
        Assert.Equal(7, record.OwningForceId);
        Assert.Equal(7, record.RuntimeOwningForceId);
        Assert.Equal(3, record.CountryTitleId);
        Assert.Equal(326, record.LeaderId);
        Assert.Equal(150, record.BaseTrust);
        Assert.Equal(156, record.Trust);
        Assert.Null(record.RelatedForceId);
        Assert.Equal(8, record.RankGroupId);
        Assert.Equal((ushort)12, record.ColorCode);
        Assert.Equal(2, record.RuntimeTypeCode);
        Assert.True(record.HasFlagBit(4));
        Assert.False(record.HasFlagBit(5));
        Assert.Equal(0x605040302010UL, record.ForceTacticFlags);
        Assert.True(record.HasForceTacticFlagBit(4));
        Assert.False(record.HasForceTacticFlagBit(5));
        Assert.Equal(50, record.DiplomaticRelations.Count);
        Assert.Equal((byte)49, record.DiplomaticRelations[49]);
    }

    [Fact]
    public void ForceFlags_RejectOutOfRangeBitQueries()
    {
        using MemoryStream stream = BuildScenarioArchive();
        San9ScenarioForceRecord record = San9ScenarioForceArchive.ReadKnownRecords(stream)[0];

        Assert.Throws<ArgumentOutOfRangeException>(() => record.HasFlagBit(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => record.HasFlagBit(32));
        Assert.Throws<ArgumentOutOfRangeException>(() => record.HasForceTacticFlagBit(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => record.HasForceTacticFlagBit(48));
    }

    [Fact]
    public void ReadKnownForces_UsesRuntimeFallbacksForInvalidReferences()
    {
        using MemoryStream stream = BuildScenarioArchive();
        stream.Position = 0x8C06E;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((uint)0);
            writer.Write((uint)0);
            writer.Write((uint)0x70);
            writer.Write((ushort)0);
            writer.Write(ushort.MaxValue);
            writer.Write(ushort.MaxValue);
            writer.Write(ushort.MaxValue);
            writer.Write((ushort)100);
            writer.Write((ushort)100);
            writer.Write((ushort)1000);
            writer.Write((ushort)9);
            writer.Write((ushort)0);
            writer.Write(new byte[6]);
            writer.Write(new byte[50]);
        }

        stream.Position = 0;
        San9ScenarioForceRecord record = San9ScenarioForceArchive.ReadKnownRecords(stream)[0];

        Assert.Null(record.OwningForceId);
        Assert.Equal(0, record.RuntimeOwningForceId);
        Assert.Null(record.CountryTitleId);
        Assert.Null(record.LeaderId);
        Assert.Null(record.RelatedForceId);
        Assert.Null(record.RankGroupId);
        Assert.Equal(1, record.RuntimeTypeCode);
    }

    [Fact]
    public void ReadKnownForces_RejectsUnexpectedSectionSize()
    {
        using MemoryStream stream = BuildScenarioArchive();
        stream.Position = 0x8C06A;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(4_299);
        }

        stream.Position = 0;
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioForceArchive.ReadKnownRecords(stream));

        Assert.Contains("Unexpected scenario section", exception.Message);
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

        stream.Position = 0x8C06E;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            for (int index = 0; index < San9ScenarioForceArchive.RecordCount; index++)
            {
                writer.Write((uint)0);
                writer.Write((uint)0);
                writer.Write((uint)0);
                writer.Write((ushort)index);
                writer.Write(ushort.MaxValue);
                writer.Write(ushort.MaxValue);
                writer.Write(ushort.MaxValue);
                writer.Write((ushort)100);
                writer.Write((ushort)100);
                writer.Write(ushort.MaxValue);
                writer.Write((ushort)9);
                writer.Write((ushort)index);
                writer.Write(new byte[6]);
                writer.Write(new byte[50]);
            }
        }

        stream.Position = 0;
        return stream;
    }
}
