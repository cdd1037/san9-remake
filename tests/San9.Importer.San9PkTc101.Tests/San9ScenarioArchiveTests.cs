using System.Text;
using San9.Importer.San9PkTc101;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class San9ScenarioArchiveTests
{
    [Fact]
    public void ReadKnownLayout_ParsesExpectedSectionsAndOffsets()
    {
        using MemoryStream stream = BuildScenarioArchive(San9ScenarioArchive.ExpectedSections);

        San9ScenarioLayout layout = San9ScenarioArchive.ReadKnownLayout(stream);

        Assert.Equal(719_714, layout.FileSize);
        Assert.Equal(17, layout.Sections.Count);
        Assert.Equal(0x13E, layout.GetRequiredSection("fbld").DataOffset);
        Assert.Equal(0x8D142, layout.GetRequiredSection("item").DataOffset);
        Assert.Equal(0x958A2, layout.GetRequiredSection("prsn").DataOffset);
    }

    [Fact]
    public void ReadHeader_DecodesScenarioIdDateAndMapSize()
    {
        using MemoryStream stream = BuildScenarioArchiveWithHeader(
            scenarioId: 1,
            encodedStartDate: 21_600);
        San9ScenarioHeader header = San9ScenarioArchive.ReadHeader(stream);

        Assert.Equal(0x20030317U, header.FormatVersion);
        Assert.Equal(1, header.ScenarioId);
        Assert.Equal(new(190, 1, 1), header.StartDate);
        Assert.Equal(36, header.AllianceDurationTurns);
        Assert.Equal(200, header.MapWidth);
        Assert.Equal(200, header.MapHeight);
        Assert.Equal(50, header.EncodedCityForceMapCodes.Count);
    }

    [Fact]
    public void ReadInitialScenario190Header_RejectsAnotherValidScenario()
    {
        using MemoryStream stream = BuildScenarioArchiveWithHeader(
            scenarioId: 2,
            encodedStartDate: 23_190);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioArchive.ReadInitialScenario190Header(stream));

        Assert.Contains("190-01-01", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(19_470, 184, 2, 1)]
    [InlineData(21_600, 190, 1, 1)]
    [InlineData(23_190, 194, 6, 1)]
    public void DecodeDate_UsesTheScenario360DayCalendar(
        ushort encodedDate,
        int year,
        int month,
        int day)
    {
        Assert.Equal(new(year, month, day), San9ScenarioArchive.DecodeDate(encodedDate));
    }

    [Fact]
    public void ReadKnownLayout_RejectsChangedSectionSize()
    {
        San9ScenarioSectionDefinition[] sections = San9ScenarioArchive.ExpectedSections
            .Select(section => section.Id == "item" ? section with { Size = section.Size + 2 } : section)
            .ToArray();
        using MemoryStream stream = BuildScenarioArchive(sections);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioArchive.ReadKnownLayout(stream));

        Assert.Contains("item", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadLayout_RejectsTruncatedRiffPayload()
    {
        using MemoryStream stream = BuildScenarioArchive(San9ScenarioArchive.ExpectedSections);
        stream.SetLength(stream.Length - 1);
        stream.Position = 0;

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioArchive.ReadLayout(stream));

        Assert.Contains("RIFF size", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadLayout_RejectsStreamNotPositionedAtFileStart()
    {
        using MemoryStream stream = BuildScenarioArchive(San9ScenarioArchive.ExpectedSections);
        stream.Position = 1;

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => San9ScenarioArchive.ReadLayout(stream));

        Assert.Contains("beginning", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadKnownItemRecords_ParsesReferencesAndBonuses()
    {
        using MemoryStream stream = BuildScenarioArchiveWithItems();
        WriteItemRecord(
            stream,
            index: 8,
            imageId: 8,
            categoryId: 1,
            loyaltyChange: 12,
            abilityBonus: 3,
            tacticCode: 3,
            holderPersonCode: 326,
            discoveryStateCode: 2,
            discoveryLocationCode: 13);

        stream.Position = 0;
        IReadOnlyList<San9ScenarioItemRecord> records =
            San9ScenarioItemArchive.ReadKnownRecords(stream);

        San9ScenarioItemRecord record = records[8];
        Assert.Equal(120, records.Count);
        Assert.Equal(8, record.SerializedId);
        Assert.Equal(3, record.AbilityBonus);
        Assert.Equal(3, record.TacticId);
        Assert.Equal(326, record.HolderPersonId);
        Assert.Equal(13, record.DiscoveryLocationId);
        Assert.Equal(San9ScenarioItemDiscoveryState.Unearthed, record.DiscoveryState);
        Assert.False(record.IsInvalid);
        Assert.False(record.CanBeUnearthed);
        Assert.True(record.IsUnearthed);
    }

    [Fact]
    public void ReadKnownItemRecords_MapsMissingCodesToNullAndInvalidStateToNeverUnearthable()
    {
        using MemoryStream stream = BuildScenarioArchiveWithItems();

        San9ScenarioItemRecord record = San9ScenarioItemArchive.ReadKnownRecords(stream)[0];

        Assert.Null(record.TacticId);
        Assert.Null(record.HolderPersonId);
        Assert.Null(record.DiscoveryLocationId);
        Assert.Equal(San9ScenarioItemDiscoveryState.Invalid, record.DiscoveryState);
        Assert.True(record.IsInvalid);
        Assert.False(record.CanBeUnearthed);
        Assert.False(record.IsUnearthed);
    }

    [Fact]
    public void ReadKnownItemRecords_MapsNotUnearthedStateToUnearthable()
    {
        using MemoryStream stream = BuildScenarioArchiveWithItems();
        WriteItemRecord(
            stream,
            index: 0,
            imageId: 0,
            categoryId: 0,
            loyaltyChange: 3,
            abilityBonus: 0,
            tacticCode: ushort.MaxValue,
            holderPersonCode: ushort.MaxValue,
            discoveryStateCode: (int)San9ScenarioItemDiscoveryState.NotUnearthed,
            discoveryLocationCode: ushort.MaxValue);

        stream.Position = 0;
        San9ScenarioItemRecord record = San9ScenarioItemArchive.ReadKnownRecords(stream)[0];

        Assert.Equal(San9ScenarioItemDiscoveryState.NotUnearthed, record.DiscoveryState);
        Assert.False(record.IsInvalid);
        Assert.True(record.CanBeUnearthed);
        Assert.False(record.IsUnearthed);
    }

    [Fact]
    public void ReadKnownItemRecords_RejectsInconsistentIdentity()
    {
        using MemoryStream stream = BuildScenarioArchiveWithItems();
        stream.Position = 0x8D142 + San9ScenarioItemArchive.RecordSize;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((ushort)99);
        }

        stream.Position = 0;
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioItemArchive.ReadKnownRecords(stream));

        Assert.Contains("record 1", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0x06, 100, "image")]
    [InlineData(0x08, 13, "category")]
    [InlineData(0x0E, 41, "tactic")]
    [InlineData(0x10, 850, "holder")]
    [InlineData(0x12, 3, "state")]
    [InlineData(0x14, 250, "location")]
    public void ReadKnownItemRecords_RejectsOutOfRangeFields(
        int fieldOffset,
        ushort value,
        string fieldName)
    {
        using MemoryStream stream = BuildScenarioArchiveWithItems();
        stream.Position = 0x8D142 + fieldOffset;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(value);
        }

        stream.Position = 0;
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioItemArchive.ReadKnownRecords(stream));

        Assert.Contains(fieldName, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadKnownCityRecords_ParsesDomesticValuesReferencesAndFlags()
    {
        using MemoryStream stream = BuildScenarioArchiveWithCities();
        WriteCityRecord(
            stream,
            index: 0,
            cityFlags: San9ScenarioCityArchive.TraderFlag,
            population: 58_000,
            draftPopulation: 7_000,
            soldiers: 8_000,
            areaCode: 0,
            forceCode: 12,
            publicOrder: 200,
            maxIncome: 600,
            maxHarvest: 400,
            income: 240,
            harvest: 180,
            durability: 380,
            maxDurability: 600,
            x: 164,
            y: 17,
            terrainCode: 2,
            tacticFlags: (1UL << San9ScenarioCityArchive.RockfallTacticId) |
                (1UL << San9ScenarioCityArchive.ArrowPlatformTacticId));

        stream.Position = 0;
        San9ScenarioCityRecord record = San9ScenarioCityArchive.ReadKnownRecords(stream)[0];

        Assert.Equal(58_000U, record.Population);
        Assert.Equal(7_000U, record.DraftPopulation);
        Assert.Equal(8_000U, record.Soldiers);
        Assert.Equal(12, record.ForceId);
        Assert.Equal(240, record.Income);
        Assert.Equal(600, record.MaxIncome);
        Assert.Equal(164, record.X);
        Assert.Equal(17, record.Y);
        Assert.True(record.HasTrader);
        Assert.True(record.HasRockfall);
        Assert.True(record.HasArrowPlatform);
    }

    [Fact]
    public void ReadKnownCityRecords_MapsUnownedCityToNullForce()
    {
        using MemoryStream stream = BuildScenarioArchiveWithCities();

        San9ScenarioCityRecord record = San9ScenarioCityArchive.ReadKnownRecords(stream)[0];

        Assert.Null(record.ForceId);
    }

    [Fact]
    public void ReadKnownCityRecords_PreservesReservedScenarioFields()
    {
        using MemoryStream stream = BuildScenarioArchiveWithCities();
        stream.Position = 0x8B576 + 0x18;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((ushort)0x1234);
            writer.Write((ushort)0x5678);
        }

        stream.Position = 0;
        San9ScenarioCityRecord record = San9ScenarioCityArchive.ReadKnownRecords(stream)[0];

        Assert.Equal(0x1234, record.Reserved18);
        Assert.Equal(0x5678, record.Reserved1A);
    }

    [Fact]
    public void ReadKnownCityRecords_RejectsInconsistentIdentity()
    {
        using MemoryStream stream = BuildScenarioArchiveWithCities();
        stream.Position = 0x8B576 + San9ScenarioCityArchive.RecordSize + 0x12;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((ushort)99);
        }

        stream.Position = 0;
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioCityArchive.ReadKnownRecords(stream));

        Assert.Contains("record 1", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0x00, 2, 4, "flags")]
    [InlineData(0x04, 1_000_001, 4, "population")]
    [InlineData(0x08, 500_001, 4, "population")]
    [InlineData(0x0C, 500_001, 4, "population")]
    [InlineData(0x16, 15, 2, "area")]
    [InlineData(0x1C, 50, 2, "force")]
    [InlineData(0x1E, 1_001, 2, "public order")]
    [InlineData(0x2C, 200, 2, "coordinates")]
    [InlineData(0x30, 3, 2, "terrain")]
    public void ReadKnownCityRecords_RejectsOutOfRangeFields(
        int fieldOffset,
        int value,
        int fieldSize,
        string fieldName)
    {
        using MemoryStream stream = BuildScenarioArchiveWithCities();
        stream.Position = 0x8B576 + fieldOffset;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            if (fieldSize == 4)
            {
                writer.Write((uint)value);
            }
            else
            {
                writer.Write((ushort)value);
            }
        }

        stream.Position = 0;
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioCityArchive.ReadKnownRecords(stream));

        Assert.Contains(fieldName, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadKnownCityRecords_RejectsCurrentValueAboveMaximum()
    {
        using MemoryStream stream = BuildScenarioArchiveWithCities();
        stream.Position = 0x8B576 + 0x24;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((ushort)601);
        }

        stream.Position = 0;
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioCityArchive.ReadKnownRecords(stream));

        Assert.Contains("income", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadKnownCityRecords_RejectsTacticBitsOutsideKnownRange()
    {
        using MemoryStream stream = BuildScenarioArchiveWithCities();
        stream.Position = 0x8B576 + 0x32 + 5;
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((byte)0x02);
        }

        stream.Position = 0;
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => San9ScenarioCityArchive.ReadKnownRecords(stream));

        Assert.Contains("tactic", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadKnownPersonRecords_ParsesAbilitiesYearsAndTactics()
    {
        using MemoryStream stream = BuildScenarioArchiveWithPersons();
        WritePersonRecord(
            stream,
            index: 0,
            personFlags:
                (1U << (3 + 4)) |
                (1U << 2) |
                (1U << 12) |
                (1U << 15) |
                (7U << 16),
            familyNameResourceCode: 11,
            givenNameResourceCode: 22,
            courtesyNameResourceCode: 33,
            nameReadingResourceCode: 44,
            encodedBirthYear: 60,
            encodedAppearanceYear: 89,
            encodedStatusStartYear: 89,
            locationCode: 17,
            statusCode: 6,
            command: 66,
            strength: 73,
            intelligence: 30,
            politics: 42,
            titleCode: 3,
            lifespanTier: 2,
            health: 5,
            bloodlineRootPersonCode: 900,
            parentPersonCode: 1_000,
            affinity: 62,
            temperament: 1,
            duty: 8,
            ambition: 4,
            learnedTacticFlags: (1UL << 2) | (1UL << 40));

        stream.Position = 0;
        IReadOnlyList<San9ScenarioPersonRecord> records =
            San9ScenarioPersonArchive.ReadKnownRecords(stream);
        San9ScenarioPersonRecord record = records[0];

        Assert.Equal(800, records.Count);
        Assert.Equal(11, record.FamilyNameResourceCode);
        Assert.Equal(22, record.GivenNameResourceCode);
        Assert.Equal(33, record.CourtesyNameResourceCode);
        Assert.Equal(44, record.NameReadingResourceCode);
        Assert.Equal(190, record.BirthYear);
        Assert.Equal(219, record.AppearanceYear);
        Assert.Equal(219, record.StatusStartYear);
        Assert.Equal(2, record.StatusDurationYears(220));
        Assert.Equal(17, record.LocationId);
        Assert.Equal(900, record.BloodlineRootPersonId);
        Assert.Equal(1_000, record.ParentPersonId);
        Assert.Equal(6, record.StatusId);
        Assert.Equal(66, record.Command);
        Assert.Equal(73, record.Strength);
        Assert.Equal(30, record.Intelligence);
        Assert.Equal(42, record.Politics);
        Assert.Equal(62, record.Affinity);
        Assert.Equal(San9ScenarioPersonHealthState.SevereInjury, record.HealthState);
        Assert.True(record.HasTacticCategoryMastery(4));
        Assert.True(record.IsOwnerLeader);
        Assert.True(record.IsCommandParticipant);
        Assert.True(record.IsTacticTraining);
        Assert.Equal(7, record.FormationSlot);
        Assert.True(record.HasLearnedTactic(2));
        Assert.True(record.HasLearnedTactic(40));
    }

    [Fact]
    public void ReadKnownPersonRecords_NormalizesOutOfRangeRuntimeReferencesToNull()
    {
        using MemoryStream stream = BuildScenarioArchiveWithPersons();
        WritePersonRecord(
            stream,
            index: 0,
            locationCode: ushort.MaxValue,
            statusCode: ushort.MaxValue,
            titleCode: 76,
            health: 15,
            bloodlineRootPersonCode: San9ScenarioPersonArchive.BloodlineRootReferenceLimit,
            parentPersonCode: San9ScenarioPersonArchive.ParentReferenceLimit,
            equippedTacticCode: ushort.MaxValue);

        stream.Position = 0;
        San9ScenarioPersonRecord record = San9ScenarioPersonArchive.ReadKnownRecords(stream)[0];

        Assert.Null(record.LocationId);
        Assert.Null(record.StatusId);
        Assert.Null(record.TitleId);
        Assert.Null(record.BloodlineRootPersonId);
        Assert.Null(record.ParentPersonId);
        Assert.Null(record.EquippedTacticId);
        Assert.Equal(San9ScenarioPersonHealthState.Unknown, record.HealthState);
        Assert.Null(record.StatusDurationYears(200));
        Assert.Equal(ushort.MaxValue, record.LocationCode);
    }

    [Fact]
    public void ReadKnownPersonRecords_PreservesOpaqueTablesAndAllFortyEightTacticBits()
    {
        using MemoryStream stream = BuildScenarioArchiveWithPersons();
        byte[] loyalty = Enumerable.Range(0, San9ScenarioPersonArchive.ForceCount)
            .Select(value => (byte)value)
            .ToArray();
        WritePersonRecord(
            stream,
            index: 0,
            forceLoyaltyValues: loyalty,
            learnedTacticFlags: 1UL << 47);

        stream.Position = 0;
        San9ScenarioPersonRecord record = San9ScenarioPersonArchive.ReadKnownRecords(stream)[0];

        Assert.Equal(loyalty, record.ForceLoyaltyValues);
        Assert.Equal(1UL << 47, record.LearnedTacticFlags);
    }

    private static MemoryStream BuildScenarioArchiveWithHeader(
        ushort scenarioId,
        ushort encodedStartDate)
    {
        MemoryStream stream = BuildScenarioArchive(San9ScenarioArchive.ExpectedSections);
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            stream.Position = 0x14;
            writer.Write(San9ScenarioArchive.ExpectedFormatVersion);
            writer.Write(new byte[16]);
            writer.Write(scenarioId);
            writer.Write((ushort)0);
            writer.Write(encodedStartDate);
            writer.Write((ushort)0x24);
            writer.Write((ushort)200);
            writer.Write((ushort)200);
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream BuildScenarioArchiveWithItems()
    {
        MemoryStream stream = BuildScenarioArchive(San9ScenarioArchive.ExpectedSections);
        for (int index = 0; index < San9ScenarioItemArchive.RecordCount; index++)
        {
            WriteItemRecord(
                stream,
                index,
                imageId: index < 100 ? index : 0,
                categoryId: 0,
                loyaltyChange: 3,
                abilityBonus: 0,
                tacticCode: ushort.MaxValue,
                holderPersonCode: ushort.MaxValue,
                discoveryStateCode: 0,
                discoveryLocationCode: ushort.MaxValue);
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream BuildScenarioArchiveWithCities()
    {
        MemoryStream stream = BuildScenarioArchive(San9ScenarioArchive.ExpectedSections);
        for (int index = 0; index < San9ScenarioCityArchive.RecordCount; index++)
        {
            WriteCityRecord(
                stream,
                index,
                cityFlags: 0,
                population: 30_000,
                draftPopulation: 1_000,
                soldiers: 0,
                areaCode: Math.Min(index / 4, San9ScenarioCityArchive.AreaCount - 1),
                forceCode: ushort.MaxValue,
                publicOrder: 100,
                maxIncome: 600,
                maxHarvest: 600,
                income: 100,
                harvest: 100,
                durability: 300,
                maxDurability: 600,
                x: index,
                y: index,
                terrainCode: 0,
                tacticFlags: 0);
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream BuildScenarioArchiveWithPersons()
    {
        MemoryStream stream = BuildScenarioArchive(San9ScenarioArchive.ExpectedSections);
        for (int index = 0; index < San9ScenarioPersonArchive.RecordCount; index++)
        {
            WritePersonRecord(stream, index);
        }

        stream.Position = 0;
        return stream;
    }

    private static void WritePersonRecord(
        Stream stream,
        int index,
        uint personFlags = 0,
        int familyNameResourceCode = -1,
        int givenNameResourceCode = -1,
        int courtesyNameResourceCode = -1,
        int nameReadingResourceCode = -1,
        int encodedBirthYear = 0,
        int encodedAppearanceYear = 0,
        int encodedStatusStartYear = 0,
        int locationCode = ushort.MaxValue,
        int statusCode = ushort.MaxValue,
        int command = 0,
        int strength = 0,
        int intelligence = 0,
        int politics = 0,
        int titleCode = 76,
        int lifespanTier = 0,
        int health = 0,
        int bloodlineRootPersonCode = ushort.MaxValue,
        int parentPersonCode = ushort.MaxValue,
        int affinity = 0,
        int temperament = 0,
        int duty = 0,
        int ambition = 0,
        int equippedTacticCode = ushort.MaxValue,
        byte[]? forceLoyaltyValues = null,
        ulong learnedTacticFlags = 0)
    {
        stream.Position = 0x958A2 + index * San9ScenarioPersonArchive.RecordSize;
        using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(personFlags);
        writer.Write((ushort)index);
        writer.Write((ushort)(familyNameResourceCode < 0 ? index : familyNameResourceCode));
        writer.Write((ushort)(givenNameResourceCode < 0 ? index : givenNameResourceCode));
        writer.Write((ushort)(courtesyNameResourceCode < 0 ? index : courtesyNameResourceCode));
        writer.Write((ushort)(nameReadingResourceCode < 0 ? index : nameReadingResourceCode));

        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)encodedBirthYear);
        writer.Write((ushort)encodedAppearanceYear);
        writer.Write((ushort)encodedStatusStartYear);
        writer.Write((ushort)locationCode);
        writer.Write((ushort)0);
        writer.Write((ushort)statusCode);
        writer.Write((ushort)command);
        writer.Write((ushort)strength);
        writer.Write((ushort)intelligence);
        writer.Write((ushort)politics);
        writer.Write((ushort)titleCode);
        writer.Write((ushort)lifespanTier);
        writer.Write((ushort)health);
        writer.Write((ushort)bloodlineRootPersonCode);
        writer.Write((ushort)parentPersonCode);
        writer.Write((ushort)affinity);
        writer.Write((ushort)temperament);
        writer.Write((ushort)duty);
        writer.Write((ushort)ambition);
        for (int categoryIndex = 0; categoryIndex < San9ScenarioPersonArchive.TacticCategoryCount; categoryIndex++)
        {
            writer.Write((ushort)(categoryIndex * 100));
        }

        writer.Write((ushort)equippedTacticCode);
        writer.Write(ushort.MaxValue);
        writer.Write(forceLoyaltyValues ?? new byte[San9ScenarioPersonArchive.ForceCount]);
        for (int byteIndex = 0; byteIndex < 6; byteIndex++)
        {
            writer.Write((byte)(learnedTacticFlags >> (byteIndex * 8)));
        }
    }

    private static void WriteItemRecord(
        Stream stream,
        int index,
        int imageId,
        int categoryId,
        int loyaltyChange,
        int abilityBonus,
        int tacticCode,
        int holderPersonCode,
        int discoveryStateCode,
        int discoveryLocationCode)
    {
        stream.Position = 0x8D142 + index * San9ScenarioItemArchive.RecordSize;
        using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write((ushort)index);
        writer.Write((ushort)index);
        writer.Write((ushort)index);
        writer.Write((ushort)imageId);
        writer.Write((ushort)categoryId);
        writer.Write((ushort)loyaltyChange);
        writer.Write((ushort)abilityBonus);
        writer.Write((ushort)tacticCode);
        writer.Write((ushort)holderPersonCode);
        writer.Write((ushort)discoveryStateCode);
        writer.Write((ushort)discoveryLocationCode);
    }

    private static void WriteCityRecord(
        Stream stream,
        int index,
        uint cityFlags,
        uint population,
        uint draftPopulation,
        uint soldiers,
        int areaCode,
        int forceCode,
        int publicOrder,
        int maxIncome,
        int maxHarvest,
        int income,
        int harvest,
        int durability,
        int maxDurability,
        int x,
        int y,
        int terrainCode,
        ulong tacticFlags)
    {
        stream.Position = 0x8B576 + index * San9ScenarioCityArchive.RecordSize;
        using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(cityFlags);
        writer.Write(population);
        writer.Write(draftPopulation);
        writer.Write(soldiers);
        writer.Write((ushort)index);
        writer.Write((ushort)index);
        writer.Write((ushort)index);
        writer.Write((ushort)areaCode);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)forceCode);
        writer.Write((ushort)publicOrder);
        writer.Write((ushort)maxIncome);
        writer.Write((ushort)maxHarvest);
        writer.Write((ushort)income);
        writer.Write((ushort)harvest);
        writer.Write((ushort)durability);
        writer.Write((ushort)maxDurability);
        writer.Write((ushort)x);
        writer.Write((ushort)y);
        writer.Write((ushort)terrainCode);
        for (int byteIndex = 0; byteIndex < 6; byteIndex++)
        {
            writer.Write((byte)(tacticFlags >> (byteIndex * 8)));
        }
    }

    private static MemoryStream BuildScenarioArchive(
        IReadOnlyList<San9ScenarioSectionDefinition> sections)
    {
        MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(0);
            writer.Write(Encoding.ASCII.GetBytes("S9SC"));
            foreach (San9ScenarioSectionDefinition section in sections)
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
