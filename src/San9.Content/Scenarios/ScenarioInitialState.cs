namespace San9.Content.Scenarios;

public sealed record ForceInitialState(int Id, string NameKey, int ColorCode, uint Money, uint Food, int Trust)
{
    [System.Text.Json.Serialization.JsonRequired] public int BaseTrust { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public System.Collections.Immutable.ImmutableArray<int> ProvidedTactics { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired] public bool IsBarbarian { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int FlagGlyph { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int? LeaderId { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int? RankId { get; init; }
}
public sealed record SettlementInitialState(int Id, string NameKey, int? OwnerId, int X, int Y,
    uint Soldiers, int Durability, int MaxDurability, int Morale, string[] Icons)
{
    [System.Text.Json.Serialization.JsonRequired] public uint SoldierCapacity { get; init; } = 500000;
    [System.Text.Json.Serialization.JsonRequired] public bool ConsumesFood { get; init; } = true;
    [System.Text.Json.Serialization.JsonRequired] public San9.Core.Domain.SettlementKind Kind { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int Appearance { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int? CorpsId { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public bool IsFacilityListEntry { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int PublicOrder { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int Commerce { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int MaxCommerce { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int Cultivation { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int MaxCultivation { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public uint Population { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public uint DraftPopulation { get; init; }
}
public sealed record ScenarioInitialState(int SchemaVersion, string ScenarioId, int Year, int Month, int Day, int PlayerForceId,
    ForceInitialState[] Forces, SettlementInitialState[] Settlements, Dictionary<int, string> RegionNameKeys, Dictionary<int, int> RegionCityIds)
{
    [System.Text.Json.Serialization.JsonRequired] public System.Collections.Immutable.ImmutableArray<San9.Core.Domain.DomesticPair> DomesticPairs { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired] public int? ImperialSealId { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int? EmperorId { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public OfficerInitialState[] Officers { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired] public San9.Core.Domain.UnitState[] Units { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired] public InformationDefinitions Information { get; init; } = InformationDefinitions.Empty;
    public void Validate()
    {
        if (SchemaVersion != 12 || string.IsNullOrWhiteSpace(ScenarioId) || Year < 1 || Month is < 1 or > 12 || Day is < 1 or > 30 ||
            Forces.Select(f => f.Id).Distinct().Count() != Forces.Length || !Forces.Any(f => f.Id == PlayerForceId))
            throw new InvalidDataException("Invalid scenario date or player force.");
        Information.Validate(this);
        new San9.Core.Domain.EconomyRulebook([], DomesticPairs, ImperialSealId, EmperorId).Validate();
        if (DomesticPairs.Any(p => !Officers.Any(o => o.State.Id == p.FirstId) || !Officers.Any(o => o.State.Id == p.SecondId)) ||
            ImperialSealId is int seal && !Information.Treasures.Any(t => t.Id == seal) ||
            EmperorId is int emperor && !Officers.Any(o => o.State.Id == emperor)) throw new InvalidDataException("Invalid economy references.");
        if (Officers.Any(o => o.State.RegionId is int region && !RegionNameKeys.ContainsKey(region))) throw new InvalidDataException("Invalid officer region.");
        var forceIds = Forces.Select(f => f.Id).ToHashSet();
        if (Units.Select(u => u.Id).Distinct().Count() != Units.Length || Units.Any(u => !u.IsValid || !forceIds.Contains(u.OwnerId)))
            throw new InvalidDataException("Invalid field unit initial state.");
        if (Forces.Any(f => f.ProvidedTactics.IsDefault || f.ProvidedTactics.Any(t => t < 0) || f.ProvidedTactics.Distinct().Count() != f.ProvidedTactics.Length || f.Id < 0 || string.IsNullOrWhiteSpace(f.NameKey) || f.Trust is < 0 or > 1000 || f.BaseTrust is < 0 or > 1000 || f.ColorCode is < 0 or > 50 || f.FlagGlyph is < -1 or > 185))
            throw new InvalidDataException("Invalid force snapshot.");
        if (Settlements.Select(s => s.Id).Distinct().Count() != Settlements.Length || Settlements.Any(s =>
            s.Id < 0 || !Enum.IsDefined(s.Kind) || string.IsNullOrWhiteSpace(s.NameKey) || s.OwnerId is int owner && !forceIds.Contains(owner) ||
            s.X < 0 || s.Y < 0 || s.Morale is < 0 or > 100 ||
            s.Icons.Contains("merchant") && s.Icons.Contains("training") ||
            s.Icons.Distinct().Count() != s.Icons.Length || s.Icons.Any(i => i is not ("merchant" or "training" or "emperor")) ||
            s.Kind != San9.Core.Domain.SettlementKind.City && s.Icons.Length != 0 ||
            s.Appearance is < 0 or > 2 || s.PublicOrder is < 0 or > 1000 || s.Commerce < 0 || s.Commerce > s.MaxCommerce || s.Cultivation < 0 || s.Cultivation > s.MaxCultivation ||
            s.SoldierCapacity is 0 or > 500000 || s.Soldiers > s.SoldierCapacity || s.Population > 1000000 || s.DraftPopulation > 500000 || s.MaxCommerce > 2500 || s.MaxCultivation > 4000 || s.Soldiers == 0 && s.Morale != 0 || s.MaxDurability <= 0 || s.Durability < 0 || s.Durability > s.MaxDurability))
            throw new InvalidDataException("Invalid settlement snapshot.");
        var settlementIds=Settlements.Select(s=>s.Id).ToHashSet();
        if (Officers.Any(o => string.IsNullOrWhiteSpace(o.NameKey) || o.State is null || !o.State.IsValid()) ||
            Officers.Select(o => o.State.Id).Distinct().Count() != Officers.Length || Officers.Any(o =>
                o.State.SettlementId is int location && !settlementIds.Contains(location) ||
                o.State.OwnerId is int owner && !forceIds.Contains(owner) ||
                o.State.IsServing && Settlements.Single(s => s.Id == o.State.SettlementId).OwnerId != o.State.OwnerId))
            throw new InvalidDataException("Invalid officer initial state.");
        if (Forces.Any(f => f.LeaderId is int leader && !Officers.Any(o => o.State.Id == leader && o.State.OwnerId == f.Id)))
            throw new InvalidDataException("Invalid force leader reference.");
        if (Settlements.Any(s => s.CorpsId < 0 || s.CorpsId is not null && s.OwnerId is null) ||
            Settlements.Where(s => s.CorpsId is not null).GroupBy(s => s.CorpsId).Any(g => g.Select(s => s.OwnerId).Distinct().Count() != 1))
            throw new InvalidDataException("Invalid direct corps ownership.");
        var cityIds = Settlements.Where(s => s.Kind == San9.Core.Domain.SettlementKind.City).Select(s => s.Id).ToHashSet();
        if (RegionCityIds.Any(p => !RegionNameKeys.ContainsKey(p.Key) || !cityIds.Contains(p.Value)))
            throw new InvalidDataException("Invalid region city reference.");
        if (RegionNameKeys.Any(p => p.Key < 0 || string.IsNullOrWhiteSpace(p.Value))) throw new InvalidDataException("Invalid region names.");
    }
}
