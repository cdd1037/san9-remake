using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Content.Authoring;
using San9.Content.Packages;

namespace San9.Content.Tests;

public sealed class CreatedOfficerLibraryTests
{
    [Fact]
    public void BiographyMarkupSurvivesLibraryRoundTripAndIsValidatedAtBothBoundaries()
    {
        var person = new CreatedOfficer(Guid.NewGuid(), "人物")
        { Biography = "[preset=blue][wave amp=50 freq=5]列传[/wave][/preset]" };
        var library = CreatedOfficerLibrary.Empty with { Officers = [person] };
        Assert.Equal(person.Biography, Assert.Single(CreatedOfficerLibrary.Decode(library.Encode()).Officers).Biography);

        var invalid = person with { Biography = "[preset=blue]未闭合" };
        Assert.Throws<InvalidDataException>(() => invalid.Validate());
        var definitions = San9.Content.Scenarios.SessionDefinitions.Empty with
        { Officers = [new(1000, "", person.Name, "", invalid.Biography, null)] };
        Assert.Throws<InvalidDataException>(() => definitions.Validate());
    }

    [Fact]
    public void PortableLibraryPreservesLiteralNamesPortraitsAndIndependentTacticInputs()
    {
        var person = new CreatedOfficer(Guid.NewGuid(), "任意长度的自创人物姓名")
        {
            Surname = "诸葛",
            FlagGlyph = 84,
            PortraitKey = "portrait-officer-42",
            Proficiencies = [1, 20, 300, 0, 0, 0, 0, 0, 0], MasteredCategories = [8], LearnedTactics = [1000],
            BirthYear = 160, AppearanceYear = 199, Abilities = new(125, 88, 73, 91), Biography = "自定列传"
        };
        var library = CreatedOfficerLibrary.Empty with { Officers = [person] };
        var decoded = Assert.Single(CreatedOfficerLibrary.Decode(library.Encode()).Officers);
        Assert.Equal(person.Name, decoded.Name); Assert.Equal(person.PortraitKey, decoded.PortraitKey);
        Assert.Equal(person.Surname + person.Name, decoded.FullName);
        Assert.False(JsonNode.Parse(library.Encode())!["officers"]![0]!.AsObject().ContainsKey("fullName"));
        Assert.Equal(person.Proficiencies.ToArray(), decoded.Proficiencies.ToArray()); Assert.Equal(new[] { 8 }, decoded.MasteredCategories.ToArray());
        Assert.Equal(new[] { 1000 }, decoded.LearnedTactics.ToArray()); Assert.Equal(199, decoded.AppearanceYear);
        Assert.Equal(person.Abilities, decoded.Abilities);
        Assert.Equal(person.FlagGlyph, decoded.FlagGlyph);
        Assert.False(JsonNode.Parse(library.Encode())!["officers"]![0]!.AsObject().ContainsKey("flagMode"));
    }

    [Fact]
    public void InitialTreasuresAreOptionalForExistingLibrariesAndRoundTripAsSortedIdentities()
    {
        var person = new CreatedOfficer(Guid.NewGuid(), "人物");
        Assert.Empty(person.InitialTreasureIds);
        var library = CreatedOfficerLibrary.Empty with { Officers = [person] };
        var legacy = JsonNode.Parse(library.Encode())!;
        Assert.Equal(3, legacy["schemaVersion"]!.GetValue<int>());
        legacy["officers"]![0]!.AsObject().Remove("initialTreasureIds");
        var decodedLegacy = Assert.Single(CreatedOfficerLibrary.Decode(legacy.ToJsonString()).Officers);
        Assert.Equal(person.Id, decodedLegacy.Id);
        Assert.Empty(decodedLegacy.InitialTreasureIds);

        library = library with { Officers = [person with { InitialTreasureIds = [5000, 0, 42] }] };
        var encoded = library.Encode();
        Assert.Equal(new[] { 0, 42, 5000 }, JsonNode.Parse(encoded)!["officers"]![0]!["initialTreasureIds"]!
            .AsArray().Select(id => id!.GetValue<int>()).ToArray());
        Assert.Equal(new[] { 0, 42, 5000 }, Assert.Single(CreatedOfficerLibrary.Decode(encoded).Officers).InitialTreasureIds.ToArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    public void InitialTreasureNullAndNegativeIdentitiesAreRejected(int? treasureId)
    {
        var person = new CreatedOfficer(Guid.NewGuid(), "人物")
            { InitialTreasureIds = treasureId is int id ? [id] : null! };
        Assert.Throws<InvalidDataException>(person.Validate);
        var library = CreatedOfficerLibrary.Empty with { Officers = [person] };
        Assert.Throws<InvalidDataException>(() => library.Encode());

        var json = JsonNode.Parse((library with { Officers = [person with { InitialTreasureIds = [] }] }).Encode())!;
        json["officers"]![0]!["initialTreasureIds"] = treasureId is int value ? new JsonArray(value) : null;
        if (treasureId is null)
            Assert.Throws<JsonException>(() => CreatedOfficerLibrary.Decode(json.ToJsonString()));
        else
            Assert.Throws<InvalidDataException>(() => CreatedOfficerLibrary.Decode(json.ToJsonString()));
    }

    [Fact]
    public void ParentCyclesAndDuplicateIdentityAreRejectedWithoutRestrictingDuplicateNames()
    {
        var first = new CreatedOfficer(Guid.NewGuid(), "同名");
        var second = new CreatedOfficer(Guid.NewGuid(), "同名") { ParentId = first.Id };
        var library = CreatedOfficerLibrary.Empty with { Officers = [first, second] };
        library.Validate();
        Assert.Throws<InvalidDataException>(() => (library with { Officers = [first with { ParentId = second.Id }, second] }).Validate());
        Assert.Throws<InvalidDataException>(() => (library with { Officers = [first, first] }).Validate());
    }

    [Theory]
    [InlineData("birthYear")]
    [InlineData("appearanceYear")]
    [InlineData("portraitKey")]
    [InlineData("flagGlyph")]
    public void AuthoredFieldsAreRequiredRatherThanSilentlyRegenerated(string field)
    {
        var person = new CreatedOfficer(Guid.NewGuid(), "人物");
        var root = JsonNode.Parse(JsonSerializer.Serialize(person, PackageFiles.JsonOptions))!.AsObject(); root.Remove(field);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CreatedOfficer>(root.ToJsonString(), PackageFiles.JsonOptions));
    }

    [Fact]
    public void SessionFlagsAreRequiredAndValidatedWithoutAFixedGlyphRange()
    {
        var person = new San9.Content.Scenarios.SessionOfficerDefinition(1000, "新", "人物", "", "", null) { FlagGlyph = 6000 };
        var definitions = San9.Content.Scenarios.SessionDefinitions.Empty with { Officers = [person] };
        definitions.Validate();
        Assert.Throws<InvalidDataException>(() => (definitions with { Officers = [person with { FlagGlyph = -2 }] }).Validate());
        var json = JsonNode.Parse(JsonSerializer.Serialize(person, PackageFiles.JsonOptions))!.AsObject();
        json.Remove("flagGlyph");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<San9.Content.Scenarios.SessionOfficerDefinition>(json.ToJsonString(), PackageFiles.JsonOptions));
    }
}
