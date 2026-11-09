using System.Text.Json.Serialization;

namespace San9.Core.Domain;

public enum PlayerCorpsAutomationMode { Disabled, DomesticAndSearch, Full }

/// <summary>Companion options supplied by the host, independently of game saves.</summary>
public sealed record CompanionSettings
{
    public static CompanionSettings Default { get; } = new();

    [JsonRequired] public PlayerCorpsAutomationMode PlayerCorpsAutomation { get; init; }
    [JsonRequired] public bool AllowDelegatedCorpsOrders { get; init; }
    [JsonRequired] public bool PlayerAutoDomesticEnabled { get; init; }
    [JsonRequired] public bool PlayerAutoSearchEnabled { get; init; }
    [JsonRequired] public int PlayerAutoSearchMinimumPolitics { get; init; } = 45;
    [JsonRequired] public bool HideRoutineSearchReports { get; init; }
    [JsonRequired] public bool ConfigureTacticsOnFormationSelection { get; init; }
    [JsonRequired] public bool DisableComputerForceConsolidation { get; init; }
    [JsonRequired] public bool DisableComputerRewardTimingRestrictions { get; init; }
    [JsonRequired] public bool DisablePlayerPrisonerExecution { get; init; }
    [JsonRequired] public bool DisableComputerPrisonerExecution { get; init; }
    [JsonRequired] public int ComputerRevenuePercent { get; init; } = 100;
    [JsonRequired] public int ComputerHarvestPercent { get; init; } = 100;
    [JsonRequired] public AutomaticStrategyPermissions ComputerStrategyPermissions { get; init; } = AutomaticStrategyPermissions.All;
    [JsonRequired] public AutomaticStrategyPermissions PlayerDelegatedStrategyPermissions { get; init; } = AutomaticStrategyPermissions.All;
    [JsonRequired] public AutomaticDiplomacyPermissions ComputerDiplomacyPermissions { get; init; } = AutomaticDiplomacyPermissions.All;
    [JsonRequired] public AutomaticDiplomacyPermissions PlayerDelegatedDiplomacyPermissions { get; init; } = AutomaticDiplomacyPermissions.All;
    [JsonRequired] public AutomaticCorpsOptions ComputerCorpsOptions { get; init; } = AutomaticCorpsOptions.Default;
    [JsonRequired] public AutomaticCorpsOptions PlayerDelegatedCorpsOptions { get; init; } = AutomaticCorpsOptions.Default;
    [JsonRequired] public bool DynamicOfficerAbilityGrowth { get; init; }
    [JsonRequired] public bool InfiniteLifespan { get; init; }
    [JsonRequired] public int OfficerAbilityCap { get; init; } = 100;
    [JsonRequired] public bool UseFacilityTacticPreset { get; init; }
    [JsonRequired] public bool EnableNavalTacticFallback { get; init; }
    [JsonRequired] public bool SuppressAdvancedNavalTactic { get; init; }
    [JsonRequired] public bool BoostMartialTacticDamage { get; init; }
    [JsonRequired] public bool ReduceSmallUnitTacticDamage { get; init; }
    [JsonRequired] public bool ReduceRangedTacticDamageAgainstFacilities { get; init; }
    [JsonRequired] public int MartialTacticDamageCoefficient { get; init; } = 10;
    [JsonRequired] public bool ReduceBattleWounded { get; init; }
    [JsonRequired] public int BattleDeathProtectionThreshold { get; init; } = 80;
    [JsonRequired] public bool AllowCommanderBattleDeaths { get; init; }
    [JsonRequired] public int DuelDeathCoefficient { get; init; }
    [JsonRequired] public bool IncreaseDurabilityDefense { get; init; }
    [JsonRequired] public int CityBattleDamagePercent { get; init; } = 100;
    [JsonRequired] public int NoRetreatCaptureBonus { get; init; } = 100;
    [JsonRequired] public bool UseHighestAbilityForCapture { get; init; }
    [JsonRequired] public bool AbilitiesAffectTacticAllocation { get; init; }
    [JsonRequired] public bool MoraleAffectsTacticCounters { get; init; }
    [JsonRequired] public bool RequireNavalTacticTraining { get; init; }
    [JsonRequired] public bool RedistributeFormationTacticBonuses { get; init; }
    [JsonRequired] public bool LeadersInspireInCombat { get; init; }
    [JsonRequired] public bool DynamicTacticProficiencyGrowth { get; init; }
    [JsonRequired] public bool AdjustIntelligenceTacticSuccess { get; init; }
    [JsonRequired] public bool ConfusionPowerAffectsDuration { get; init; }
    [JsonRequired] public bool RewardTacticParticipants { get; init; }
    [JsonRequired] public bool AffinityAffectsMartialChains { get; init; }
    [JsonRequired] public bool ProficiencyAffectsSiegePower { get; init; }
    [JsonRequired] public bool HalveFacilityIntelligenceCooldown { get; init; }
    [JsonRequired] public bool DisableDailyCombatProficiencyGain { get; init; }
    [JsonRequired] public bool AdjustSnipingChance { get; init; }
    [JsonRequired] public int SnipingChanceCoefficient { get; init; } = 5;
    [JsonRequired] public TacticAllocationCoefficients TacticAllocation { get; init; } = TacticAllocationCoefficients.Default;

    public void Validate()
    {
        if (!Enum.IsDefined(PlayerCorpsAutomation)) throw new ArgumentOutOfRangeException(nameof(PlayerCorpsAutomation));
        ArgumentOutOfRangeException.ThrowIfNegative(PlayerAutoSearchMinimumPolitics);
        ArgumentOutOfRangeException.ThrowIfNegative(ComputerRevenuePercent);
        ArgumentOutOfRangeException.ThrowIfNegative(ComputerHarvestPercent);
        ArgumentOutOfRangeException.ThrowIfNegative(CityBattleDamagePercent);
        ArgumentOutOfRangeException.ThrowIfNegative(BattleDeathProtectionThreshold);
        if (DuelDeathCoefficient is < 0 or > 10)
            throw new ArgumentOutOfRangeException(nameof(DuelDeathCoefficient), "The duel death coefficient must be between 0 and 10.");
        if (NoRetreatCaptureBonus is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(NoRetreatCaptureBonus), "The capture bonus must be between 0 and 100 percentage points.");
        ArgumentNullException.ThrowIfNull(ComputerStrategyPermissions);
        ArgumentNullException.ThrowIfNull(PlayerDelegatedStrategyPermissions);
        ArgumentNullException.ThrowIfNull(ComputerDiplomacyPermissions);
        ArgumentNullException.ThrowIfNull(PlayerDelegatedDiplomacyPermissions);
        ArgumentNullException.ThrowIfNull(ComputerCorpsOptions);
        ArgumentNullException.ThrowIfNull(PlayerDelegatedCorpsOptions);
        if (OfficerAbilityCap < 100)
            throw new ArgumentOutOfRangeException(nameof(OfficerAbilityCap), "The base ability cap must be at least 100.");
        if (MartialTacticDamageCoefficient is < 5 or > 10)
            throw new ArgumentOutOfRangeException(nameof(MartialTacticDamageCoefficient), "Martial tactic damage coefficient must be between 5 and 10.");
        if (SnipingChanceCoefficient is < 0 or > 10)
            throw new ArgumentOutOfRangeException(nameof(SnipingChanceCoefficient), "Sniping chance coefficient must be between 0 and 10.");
        ArgumentNullException.ThrowIfNull(TacticAllocation);
        TacticAllocation.Validate();
    }
}
