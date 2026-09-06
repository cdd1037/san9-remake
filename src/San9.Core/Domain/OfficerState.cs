namespace San9.Core.Domain;

public readonly record struct OfficerAbilities(int War, int Intelligence, int Politics, int Leadership)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsNonnegative => War >= 0 && Intelligence >= 0 && Politics >= 0 && Leadership >= 0;
}

/// <summary>A serving officer resident in a settlement. Names belong to content definitions.</summary>
public sealed record OfficerState(int Id, int? OwnerId, int SettlementId, OfficerAbilities BaseAbilities, bool Acted)
{
    public int Injury { get; init; }
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
        if (Id < 0 || SettlementId < 0 || OwnerId < 0 || Injury is < 0 or > 14 ||
            !BaseAbilities.IsNonnegative || !ItemBonuses.IsNonnegative || !OfficeBonuses.IsNonnegative) return false;
        try { _ = Abilities; return true; }
        catch (OverflowException) { return false; }
    }
}
