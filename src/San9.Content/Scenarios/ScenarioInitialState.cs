namespace San9.Content.Scenarios;

public sealed record ForceInitialState(int Id, string NameKey, int ColorCode, uint Money, uint Food, int Trust)
{
    [System.Text.Json.Serialization.JsonRequired] public int FlagGlyph { get; init; }
}
public sealed record SettlementInitialState(int Id, string NameKey, int? OwnerId, int X, int Y,
    uint Soldiers, int Durability, int MaxDurability, int Morale, string[] Icons)
{
    [System.Text.Json.Serialization.JsonRequired] public int Appearance { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int PublicOrder { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int Commerce { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int MaxCommerce { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int Cultivation { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int MaxCultivation { get; init; }
}
public sealed record ScenarioInitialState(int SchemaVersion, string ScenarioId, int Year, int Month, int Day, int PlayerForceId,
    ForceInitialState[] Forces, SettlementInitialState[] Settlements, Dictionary<int, string> RegionNameKeys, Dictionary<int, int> RegionCityIds)
{
    [System.Text.Json.Serialization.JsonRequired] public San9.Core.Domain.OfficerState[] Officers { get; init; } = [];
    public void Validate()
    {
        if (SchemaVersion != 3 || string.IsNullOrWhiteSpace(ScenarioId) || Year < 1 || Month is < 1 or > 12 || Day is < 1 or > 30 ||
            Forces.Select(f => f.Id).Distinct().Count() != Forces.Length || !Forces.Any(f => f.Id == PlayerForceId))
            throw new InvalidDataException("Invalid scenario date or player force.");
        var forceIds = Forces.Select(f => f.Id).ToHashSet();
        if (Forces.Any(f => f.Id < 0 || string.IsNullOrWhiteSpace(f.NameKey) || f.Trust < 0 || f.ColorCode is < 0 or > 50 || f.FlagGlyph is < -1 or > 185))
            throw new InvalidDataException("Invalid force snapshot.");
        if (Settlements.Select(s => s.Id).Distinct().Count() != Settlements.Length || Settlements.Any(s =>
            s.Id < 0 || string.IsNullOrWhiteSpace(s.NameKey) || s.OwnerId is int owner && !forceIds.Contains(owner) ||
            s.X < 0 || s.Y < 0 || s.Morale is < 0 or > 100 ||
            s.Icons.Contains("merchant") && s.Icons.Contains("training") ||
            s.Icons.Distinct().Count() != s.Icons.Length || s.Icons.Any(i => i is not ("merchant" or "training" or "emperor")) ||
            s.Id >= 50 && s.Icons.Length != 0 ||
            s.Appearance is < 0 or > 2 || s.PublicOrder is < 0 or > 1000 || s.Commerce < 0 || s.Commerce > s.MaxCommerce || s.Cultivation < 0 || s.Cultivation > s.MaxCultivation ||
            s.Soldiers == 0 && s.Morale != 0 || s.MaxDurability <= 0 || s.Durability < 0 || s.Durability > s.MaxDurability))
            throw new InvalidDataException("Invalid settlement snapshot.");
        var settlementIds=Settlements.Select(s=>s.Id).ToHashSet();
        if (Officers.Select(o=>o.Id).Distinct().Count()!=Officers.Length || Officers.Any(o=>o.Id<0 || o.CurrentPolitics<0 || !settlementIds.Contains(o.SettlementId) || o.OwnerId is int owner && !forceIds.Contains(owner)))
            throw new InvalidDataException("Invalid officer initial state.");
        if (RegionCityIds.Any(p => !RegionNameKeys.ContainsKey(p.Key) || p.Value is < 0 or >= 50 || !RegionNameKeys.ContainsKey(p.Value)))
            throw new InvalidDataException("Invalid region city reference.");
        if (RegionNameKeys.Any(p => p.Key < 0 || string.IsNullOrWhiteSpace(p.Value))) throw new InvalidDataException("Invalid region names.");
    }
}
