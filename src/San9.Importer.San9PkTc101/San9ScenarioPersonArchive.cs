using System.Collections.ObjectModel;

namespace San9.Importer.San9PkTc101;

public enum San9ScenarioPersonHealthState
{
    Healthy,
    LightInjury,
    SevereInjury,
    Unknown,
}

public sealed record San9ScenarioPersonRecord(
    uint PersonFlags,
    ushort SerializedId,
    ushort FamilyNameResourceCode,
    ushort GivenNameResourceCode,
    ushort CourtesyNameResourceCode,
    ushort NameReadingResourceCode,
    ushort PortraitResourceCode,
    ushort BiographyResourceCode,
    ushort EncodedBirthYear,
    ushort EncodedAppearanceYear,
    ushort EncodedStatusStartYear,
    ushort LocationCode,
    ushort Reserved1A,
    ushort StatusCode,
    ushort Command,
    ushort Strength,
    ushort Intelligence,
    ushort Politics,
    ushort TitleCode,
    ushort LifespanTier,
    ushort Health,
    ushort BloodlineRootPersonCode,
    ushort ParentPersonCode,
    ushort Affinity,
    ushort Righteousness,
    ushort Ambition,
    ushort Personality,
    IReadOnlyList<ushort> TacticProficiencies,
    ushort EquippedTacticCode,
    ushort FlagCode,
    IReadOnlyList<byte> ForceLoyaltyValues,
    ulong LearnedTacticFlags)
{
    public int BirthYear => San9ScenarioPersonArchive.DecodeYear(EncodedBirthYear);

    public int AppearanceYear => San9ScenarioPersonArchive.DecodeYear(EncodedAppearanceYear);

    public int StatusStartYear => San9ScenarioPersonArchive.DecodeYear(EncodedStatusStartYear);

    public int? StatusDurationYears(int currentYear)
    {
        if (EncodedStatusStartYear == 0 || currentYear < StatusStartYear)
        {
            return null;
        }

        return currentYear - StatusStartYear + 1;
    }

    public int? LocationId =>
        LocationCode < San9ScenarioPersonArchive.LocationCount ? LocationCode : null;

    public int? BloodlineRootPersonId =>
        BloodlineRootPersonCode < San9ScenarioPersonArchive.BloodlineRootReferenceLimit
            ? BloodlineRootPersonCode
            : null;

    public int? ParentPersonId =>
        ParentPersonCode < San9ScenarioPersonArchive.ParentReferenceLimit
            ? ParentPersonCode
            : null;

    public int? StatusId => StatusCode < San9ScenarioPersonArchive.StatusCount ? StatusCode : null;

    public int? TitleId => TitleCode < San9ScenarioPersonArchive.TitleCount ? TitleCode : null;

    public int? EquippedTacticId =>
        EquippedTacticCode < San9ScenarioPersonArchive.TacticCount ? EquippedTacticCode : null;

    public San9ScenarioPersonHealthState HealthState => Health switch
    {
        0 => San9ScenarioPersonHealthState.Healthy,
        <= 4 => San9ScenarioPersonHealthState.LightInjury,
        <= 14 => San9ScenarioPersonHealthState.SevereInjury,
        _ => San9ScenarioPersonHealthState.Unknown,
    };

    public int GenderFlag => (int)(PersonFlags & 1U);

    public bool IsOwnerLeader => (PersonFlags & (1U << 2)) != 0;

    public bool IsCommandParticipant => (PersonFlags & (1U << 12)) != 0;

    public bool IsSpecialPrisonerState =>
        StatusCode == San9ScenarioPersonArchive.PrisonerStatusId &&
        (PersonFlags & (1U << 13)) != 0;

    public bool IsTacticTraining => (PersonFlags & (1U << 15)) != 0;

    public int? FormationSlot
    {
        get
        {
            int slot = (int)((PersonFlags & San9ScenarioPersonArchive.FormationSlotMask) >> 16);
            return slot < San9ScenarioPersonArchive.FormationSlotCount ? slot : null;
        }
    }

    public bool HasTacticCategoryMastery(int categoryId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(categoryId);
        if (categoryId >= San9ScenarioPersonArchive.TacticCategoryCount)
        {
            throw new ArgumentOutOfRangeException(nameof(categoryId));
        }

        return (PersonFlags & (1U << (categoryId + 3))) != 0;
    }

    public bool HasLearnedTactic(int tacticId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tacticId);
        if (tacticId >= San9ScenarioPersonArchive.TacticCount)
        {
            throw new ArgumentOutOfRangeException(nameof(tacticId));
        }

        return (LearnedTacticFlags & (1UL << tacticId)) != 0;
    }
}

public static class San9ScenarioPersonArchive
{
    public const int RecordCount = 800;
    public const int RecordSize = 134;
    public const int RuntimePersonCount = 850;
    public const int BloodlineRootReferenceLimit = 1_050;
    public const int ParentReferenceLimit = 1_031;
    public const int LocationCount = 1_100;
    public const int StatusCount = 10;
    public const int PrisonerStatusId = 4;
    public const int TitleCount = 76;
    public const int TacticCategoryCount = 9;
    public const int ForceCount = 50;
    public const int TacticCount = 41;
    public const int FormationSlotCount = 9;
    public const uint FormationSlotMask = 0x001F0000;

    private const int YearEpoch = 130;

    public static IReadOnlyList<San9ScenarioPersonRecord> ReadKnownRecords(Stream stream)
    {
        San9ScenarioLayout layout = San9ScenarioArchive.ReadKnownLayout(stream);
        San9ScenarioSection section = layout.GetRequiredSection("prsn");
        if (section.Size != RecordCount * RecordSize)
        {
            throw new InvalidDataException("Scenario person section has an unexpected size.");
        }

        stream.Position = section.DataOffset;
        using BinaryReader reader = new(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        List<San9ScenarioPersonRecord> records = new(RecordCount);
        for (int index = 0; index < RecordCount; index++)
        {
            uint personFlags = reader.ReadUInt32();
            ushort serializedId = reader.ReadUInt16();
            ushort familyNameResourceCode = reader.ReadUInt16();
            ushort givenNameResourceCode = reader.ReadUInt16();
            ushort courtesyNameResourceCode = reader.ReadUInt16();
            ushort nameReadingResourceCode = reader.ReadUInt16();
            ushort portraitResourceCode = reader.ReadUInt16();
            ushort biographyResourceCode = reader.ReadUInt16();
            ushort encodedBirthYear = reader.ReadUInt16();
            ushort encodedAppearanceYear = reader.ReadUInt16();
            ushort encodedStatusStartYear = reader.ReadUInt16();
            ushort locationCode = reader.ReadUInt16();
            ushort reserved1A = reader.ReadUInt16();
            ushort statusCode = reader.ReadUInt16();
            ushort command = reader.ReadUInt16();
            ushort strength = reader.ReadUInt16();
            ushort intelligence = reader.ReadUInt16();
            ushort politics = reader.ReadUInt16();
            ushort titleCode = reader.ReadUInt16();
            ushort lifespanTier = reader.ReadUInt16();
            ushort health = reader.ReadUInt16();
            ushort bloodlineRootPersonCode = reader.ReadUInt16();
            ushort parentPersonCode = reader.ReadUInt16();
            ushort affinity = reader.ReadUInt16();
            ushort righteousness = reader.ReadUInt16();
            ushort ambition = reader.ReadUInt16();
            ushort personality = reader.ReadUInt16();
            ushort[] tacticProficiencies = ReadUInt16Values(reader, TacticCategoryCount);
            ushort equippedTacticCode = reader.ReadUInt16();
            ushort flagCode = reader.ReadUInt16();
            byte[] forceLoyaltyValues = reader.ReadBytes(ForceCount);
            if (forceLoyaltyValues.Length != ForceCount)
            {
                throw new EndOfStreamException("Scenario person loyalty values are truncated.");
            }

            ulong learnedTacticFlags = ReadUInt48(reader);
            records.Add(new(
                personFlags,
                serializedId,
                familyNameResourceCode,
                givenNameResourceCode,
                courtesyNameResourceCode,
                nameReadingResourceCode,
                portraitResourceCode,
                biographyResourceCode,
                encodedBirthYear,
                encodedAppearanceYear,
                encodedStatusStartYear,
                locationCode,
                reserved1A,
                statusCode,
                command,
                strength,
                intelligence,
                politics,
                titleCode,
                lifespanTier,
                health,
                bloodlineRootPersonCode,
                parentPersonCode,
                affinity,
                righteousness,
                ambition,
                personality,
                Array.AsReadOnly(tacticProficiencies),
                equippedTacticCode,
                flagCode,
                Array.AsReadOnly(forceLoyaltyValues),
                learnedTacticFlags));
        }

        if (stream.Position != section.DataOffset + section.Size)
        {
            throw new InvalidDataException("Scenario person records do not fill the person section exactly.");
        }

        return new ReadOnlyCollection<San9ScenarioPersonRecord>(records);
    }

    public static int DecodeYear(ushort encodedYear) => YearEpoch + encodedYear;

    private static ushort[] ReadUInt16Values(BinaryReader reader, int count)
    {
        ushort[] values = new ushort[count];
        for (int index = 0; index < count; index++)
        {
            values[index] = reader.ReadUInt16();
        }

        return values;
    }

    private static ulong ReadUInt48(BinaryReader reader)
    {
        ulong value = 0;
        for (int byteIndex = 0; byteIndex < 6; byteIndex++)
        {
            value |= (ulong)reader.ReadByte() << (byteIndex * 8);
        }

        return value;
    }
}
