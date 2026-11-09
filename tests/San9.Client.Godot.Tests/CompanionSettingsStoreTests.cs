using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Client.Godot.Settings;
using San9.Core.Domain;

namespace San9.Client.Godot.Tests;

public sealed class CompanionSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "san9-companion-tests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "companion-settings.json");

    [Fact]
    public void StandaloneConfigurationRoundTripsAndInvalidDraftCannotReplaceIt()
    {
        var store = new CompanionSettingsStore(FilePath);
        Assert.Equal(CompanionConfiguration.Default, store.Load());
        Assert.False(store.Load().FormationAppearanceEnabled);
        Assert.False(Directory.Exists(_directory));
        var settings = new CompanionSettings
        {
            PlayerAutoDomesticEnabled = true, OfficerAbilityCap = 125,
            DisableComputerForceConsolidation = true,
            DisableComputerRewardTimingRestrictions = true,
            DisableComputerPrisonerExecution = true,
            DisablePlayerPrisonerExecution = true,
            InfiniteLifespan = true,
            EnableNavalTacticFallback = true, TacticAllocation = new(0, 1, 2, 3, 4),
            PlayerAutoSearchEnabled = true, PlayerAutoSearchMinimumPolitics = 60,
            HideRoutineSearchReports = true,
            ConfigureTacticsOnFormationSelection = true,
            ComputerRevenuePercent = 125, ComputerHarvestPercent = 200,
            CityBattleDamagePercent = 50,
            NoRetreatCaptureBonus = 20,
            UseHighestAbilityForCapture = true,
            BattleDeathProtectionThreshold = 125,
            AllowCommanderBattleDeaths = true,
            DuelDeathCoefficient = 5,
            ComputerStrategyPermissions = new() { Alienate = false, Burn = false },
            PlayerDelegatedStrategyPermissions = new() { Seize = false, Gossip = false },
            ComputerDiplomacyPermissions = new() { Gift = false, Surrender = false },
            PlayerDelegatedDiplomacyPermissions = new() { RequestAttack = false, ReturnPrisoner = false },
            ComputerCorpsOptions = new() { DomesticDelegationRecruitment = true, CrossCorpsTransport = false },
            PlayerDelegatedCorpsOptions = new() { CrossCorpsRelief = false },
        };
        store.Save(new(settings, true));
        Assert.Equal(new CompanionConfiguration(settings, true), new CompanionSettingsStore(FilePath).Load());
        byte[] saved = File.ReadAllBytes(FilePath);
        foreach (var invalid in new[] {
            settings with { OfficerAbilityCap = 99 }, settings with { PlayerAutoSearchMinimumPolitics = -1 },
            settings with { ComputerRevenuePercent = -1 }, settings with { ComputerHarvestPercent = -1 },
            settings with { CityBattleDamagePercent = -1 }, settings with { NoRetreatCaptureBonus = -1 },
            settings with { NoRetreatCaptureBonus = 101 }, settings with { BattleDeathProtectionThreshold = -1 },
            settings with { DuelDeathCoefficient = -1 }, settings with { DuelDeathCoefficient = 11 } })
            Assert.Throws<ArgumentOutOfRangeException>(() => store.Save(new(invalid, false)));
        foreach (var invalid in new[] {
            settings with { ComputerStrategyPermissions = null! }, settings with { PlayerDelegatedStrategyPermissions = null! },
            settings with { ComputerDiplomacyPermissions = null! }, settings with { PlayerDelegatedDiplomacyPermissions = null! },
            settings with { ComputerCorpsOptions = null! }, settings with { PlayerDelegatedCorpsOptions = null! } })
            Assert.Throws<ArgumentNullException>(() => store.Save(new(invalid, false)));
        Assert.Equal(saved, File.ReadAllBytes(FilePath));

        var document = JsonNode.Parse(saved)!;
        document.AsObject().Remove("formationAppearanceEnabled");
        File.WriteAllText(FilePath, document.ToJsonString(), Encoding.UTF8);
        Assert.Throws<JsonException>(() => store.Load());

        document = JsonNode.Parse(saved)!;
        document["settings"]!.AsObject().Remove("officerAbilityCap");
        string incomplete = document.ToJsonString();
        File.WriteAllText(FilePath, incomplete, Encoding.UTF8);
        Assert.Throws<JsonException>(() => store.Load());
        Assert.Equal(incomplete, File.ReadAllText(FilePath, Encoding.UTF8));

        document = JsonNode.Parse(saved)!;
        document["settings"]!["computerStrategyPermissions"]!.AsObject().Remove("burn");
        File.WriteAllText(FilePath, document.ToJsonString(), Encoding.UTF8);
        Assert.Throws<JsonException>(() => store.Load());
    }

    [Theory]
    [InlineData("computerDiplomacyPermissions", null)]
    [InlineData("playerDelegatedDiplomacyPermissions", null)]
    [InlineData("computerDiplomacyPermissions", "gift")]
    [InlineData("playerDelegatedDiplomacyPermissions", "requestAttack")]
    [InlineData("computerDiplomacyPermissions", "returnPrisoner")]
    [InlineData("playerDelegatedDiplomacyPermissions", "surrender")]
    [InlineData("computerCorpsOptions", null)]
    [InlineData("playerDelegatedCorpsOptions", null)]
    [InlineData("computerCorpsOptions", "domesticDelegationRecruitment")]
    [InlineData("playerDelegatedCorpsOptions", "domesticDelegationRecruitment")]
    [InlineData("computerCorpsOptions", "crossCorpsTransport")]
    [InlineData("playerDelegatedCorpsOptions", "crossCorpsTransport")]
    [InlineData("computerCorpsOptions", "crossCorpsRelief")]
    [InlineData("playerDelegatedCorpsOptions", "crossCorpsRelief")]
    public void IncompleteAutomaticOptionsAreRejectedWithoutChangingTheFile(string scope, string? field)
    {
        var store = new CompanionSettingsStore(FilePath);
        store.Save(CompanionConfiguration.Default);
        var document = JsonNode.Parse(File.ReadAllText(FilePath, Encoding.UTF8))!;
        if (field is null) document["settings"]!.AsObject().Remove(scope);
        else document["settings"]![scope]!.AsObject().Remove(field);
        string incomplete = document.ToJsonString();
        File.WriteAllText(FilePath, incomplete, Encoding.UTF8);
        Assert.Throws<JsonException>(() => store.Load());
        Assert.Equal(incomplete, File.ReadAllText(FilePath, Encoding.UTF8));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
