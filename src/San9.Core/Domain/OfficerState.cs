using System.Collections.Immutable;

namespace San9.Core.Domain;

public enum OfficerStatus { Ruler, Governor, Strategist, Serving, Captive, Free, Undiscovered, NotAppeared, Dead, Special }

public readonly record struct OfficerAbilities(int War, int Intelligence, int Politics, int Leadership)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsNonnegative => War >= 0 && Intelligence >= 0 && Politics >= 0 && Leadership >= 0;
}

/// <summary>A person with runtime identity, status and optional location. Names belong to content definitions.</summary>
public sealed record OfficerState(int Id, int? OwnerId, int? SettlementId, OfficerAbilities BaseAbilities, bool Acted)
{
    public ImmutableArray<int> LearnedTactics { get; init; } = [];
    public ImmutableArray<int> TacticProficiencies { get; init; } = [0, 0, 0, 0, 0, 0, 0, 0, 0];
    public int? ServiceStartYear { get; init; }
    public int? ParentId { get; init; }
    public bool IsFemale { get; init; }
    public int Personality { get; init; }
    public int Injury { get; init; }
    public OfficerStatus Status { get; init; } = OfficerStatus.Serving;
    [System.Text.Json.Serialization.JsonIgnore] public bool IsServing => Status <= OfficerStatus.Serving;
    [System.Text.Json.Serialization.JsonIgnore] public bool IsVisible => Status <= OfficerStatus.Free;
    public int? RegionId { get; init; }
    public int BirthYear { get; init; }
    public int? OfficeId { get; init; }
    public int Loyalty { get; init; }
    public OfficerAbilities ItemBonuses { get; init; }
    public OfficerAbilities OfficeBonuses { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public OfficerAbilities Abilities => new(
        Current(BaseAbilities.War, ItemBonuses.War, OfficeBonuses.War),
        Current(BaseAbilities.Intelligence, ItemBonuses.Intelligence, OfficeBonuses.Intelligence),
        Current(BaseAbilities.Politics, ItemBonuses.Politics, OfficeBonuses.Politics),
        Current(BaseAbilities.Leadership, ItemBonuses.Leadership, OfficeBonuses.Leadership));

    private int Current(int value, int itemBonus, int officeBonus) =>
        checked((int)Math.Max(1, (long)value * (10 - Injury) / 10) + itemBonus + officeBonus);

    public bool IsValid()
    {
        if (LearnedTactics.IsDefault || TacticProficiencies.IsDefault || TacticProficiencies.Length != 9 ||
            TacticProficiencies.Any(v => v < 0) || LearnedTactics.Any(v => v < 0) || LearnedTactics.Distinct().Count() != LearnedTactics.Length ||
            ParentId < 0 || Personality is < 0 or > 3) return false;
        if (Id < 0 || IsServing && SettlementId is null || SettlementId < 0 || OwnerId < 0 || RegionId < 0 || RegionId is not null && SettlementId is not null || !Enum.IsDefined(Status) || Injury is < 0 or > 14 ||
            !BaseAbilities.IsNonnegative || !ItemBonuses.IsNonnegative || !OfficeBonuses.IsNonnegative) return false;
        try { _ = Abilities; return true; }
        catch (OverflowException) { return false; }
    }
}
