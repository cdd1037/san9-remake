using System.Collections.ObjectModel;
using System.Text;

namespace San9.Importer.San9PkTc101;

public readonly record struct FormationGridPosition(int Column, int Row);

public sealed record San9ScenarioFormationRecord(
    int Id,
    ushort SerializedId,
    ushort CatalogCode,
    ushort ReservedField04,
    ushort ReservedField06,
    ushort AgainstUnits,
    ushort AgainstSoldiers,
    ushort AgainstWalls,
    ushort DefensePower,
    ushort Mobility,
    ushort VisionRadius,
    ushort VisionAngleCode,
    ushort DeploymentCost,
    ushort DescriptionResourceCode,
    IReadOnlyList<byte> DeploymentSlotCodes,
    byte IgnoredByte)
{
    public int VisionAngleDegrees => VisionAngleCode * San9ScenarioFormationArchive.VisionSectorDegrees;

    public int? DescriptionId => DescriptionResourceCode <= San9ScenarioFormationArchive.DescriptionCodeMax
        ? DescriptionResourceCode
        : null;

    public FormationGridPosition? GetDeploymentSlotPosition(int officerIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(officerIndex);
        if (officerIndex >= DeploymentSlotCodes.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(officerIndex));
        }

        byte code = DeploymentSlotCodes[officerIndex];
        return code < San9ScenarioFormationArchive.FormationGridSlotCount
            ? new FormationGridPosition(code % 3, code / 3)
            : null;
    }
}

public static class San9ScenarioFormationArchive
{
    public const int RecordCount = 28;
    public const int RecordSize = 32;
    public const int DeploymentSlotCount = 5;
    public const int FormationGridSlotCount = 9;
    public const int NoFormationSlotCode = 9;
    public const int VisionSectorDegrees = 45;
    public const int DescriptionCodeMax = 14;

    public static IReadOnlyList<San9ScenarioFormationRecord> ReadKnownRecords(Stream stream)
    {
        San9ScenarioLayout layout = San9ScenarioArchive.ReadKnownLayout(stream);
        San9ScenarioSection section = layout.GetRequiredSection("jnki");
        if (section.Size != RecordCount * RecordSize)
        {
            throw new InvalidDataException("Scenario formation section has an unexpected size.");
        }

        stream.Position = section.DataOffset;
        using BinaryReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        List<San9ScenarioFormationRecord> records = new(RecordCount);
        for (int index = 0; index < RecordCount; index++)
        {
            records.Add(new(
                index,
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                Array.AsReadOnly(reader.ReadBytes(DeploymentSlotCount)),
                reader.ReadByte()));
        }

        if (stream.Position != section.DataOffset + section.Size)
        {
            throw new InvalidDataException("Scenario formation records do not fill the formation section exactly.");
        }

        return new ReadOnlyCollection<San9ScenarioFormationRecord>(records);
    }
}
