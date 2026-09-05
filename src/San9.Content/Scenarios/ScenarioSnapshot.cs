namespace San9.Content.Scenarios;

public sealed record ForceSnapshot(int Id, string NameKey, int ColorCode, uint Money, uint Food, int Trust);
public sealed record SettlementSnapshot(int Id, string NameKey, int? OwnerId, int X, int Y,
    uint Soldiers, int Durability, int MaxDurability, int Morale, bool HasAvailableOfficer, int? CityMarkerColor, string[] Icons);
public sealed record ScenarioSnapshot(int SchemaVersion, string ScenarioId, int Year, int Month, int Day, int PlayerForceId,
    ForceSnapshot[] Forces, SettlementSnapshot[] Settlements, Dictionary<int, string> RegionNameKeys, Dictionary<int, int> RegionCityIds)
{
    public void Validate()
    {
        if (SchemaVersion != 2 || string.IsNullOrWhiteSpace(ScenarioId) || Year < 1 || Month is < 1 or > 12 || Day is < 1 or > 30 ||
            Forces.Select(f => f.Id).Distinct().Count() != Forces.Length || !Forces.Any(f => f.Id == PlayerForceId))
            throw new InvalidDataException("Invalid scenario date or player force.");
        var forceIds = Forces.Select(f => f.Id).ToHashSet();
        if (Forces.Any(f => f.Id < 0 || string.IsNullOrWhiteSpace(f.NameKey) || f.Trust < 0 || f.ColorCode < 0))
            throw new InvalidDataException("Invalid force snapshot.");
        if (Settlements.Select(s => s.Id).Distinct().Count() != Settlements.Length || Settlements.Any(s =>
            s.Id < 0 || string.IsNullOrWhiteSpace(s.NameKey) || s.OwnerId is int owner && !forceIds.Contains(owner) ||
            s.X < 0 || s.Y < 0 || s.Morale is < 0 or > 100 || s.CityMarkerColor is < 0 or > 50 || s.Id < 50 && s.CityMarkerColor is null ||
            s.Icons.Contains("merchant") && s.Icons.Contains("training") ||
            s.Icons.Distinct().Count() != s.Icons.Length || s.Icons.Any(i => i is not ("merchant" or "training" or "emperor")) ||
            s.Id >= 50 && (s.Icons.Length != 0 || s.CityMarkerColor is not null) ||
            s.Soldiers == 0 && s.Morale != 0 || s.MaxDurability <= 0 || s.Durability < 0 || s.Durability > s.MaxDurability))
            throw new InvalidDataException("Invalid settlement snapshot.");
        if (RegionCityIds.Any(p => !RegionNameKeys.ContainsKey(p.Key) || p.Value is < 0 or >= 50 || !RegionNameKeys.ContainsKey(p.Value)))
            throw new InvalidDataException("Invalid region city reference.");
        if (RegionNameKeys.Any(p => p.Key < 0 || string.IsNullOrWhiteSpace(p.Value))) throw new InvalidDataException("Invalid region names.");
    }
}
