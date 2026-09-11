using San9.Content.Scenarios;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public sealed record ForceView(int Id,string NameKey,int ColorCode,int FlagGlyph,uint Money,uint Food,int Trust)
{
    public bool IsBarbarian { get; init; }
    public int? LeaderId { get; init; }
    public int? RankId { get; init; }
    public int? LeaderSettlementId { get; init; }
}
public sealed record SettlementView(int Id,string NameKey,int? OwnerId,int X,int Y,uint Soldiers,int Durability,int MaxDurability,
    int Morale,bool HasAvailableOfficer,int? CityMarkerColor,IReadOnlyList<string> Icons,int Appearance,int PublicOrder,
    int Commerce,int MaxCommerce,int Cultivation,int MaxCultivation)
{
    public San9.Core.Domain.SettlementKind Kind { get; init; }
    public int? CorpsId { get; init; }
    public bool IsFacilityListEntry { get; init; }
}

internal sealed class StrategySnapshotSource
{
    private readonly IReadOnlyDictionary<int, OfficerInitialState> _officers;
    private readonly IReadOnlyDictionary<int, ForceInitialState> _forces;
    private readonly IReadOnlyDictionary<int, SettlementInitialState> _settlements;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<string>> _settlementIcons;

    public StrategySnapshotSource(ScenarioInitialState definition)
    {
        PlayerForceId = definition.PlayerForceId;
        Information = new(definition.Information);
        RegionNameKeys = new System.Collections.ObjectModel.ReadOnlyDictionary<int, string>(
            new Dictionary<int, string>(definition.RegionNameKeys));
        RegionCityIds = new System.Collections.ObjectModel.ReadOnlyDictionary<int, int>(
            new Dictionary<int, int>(definition.RegionCityIds));
        _officers = definition.Officers.ToDictionary(officer => officer.State.Id);
        _forces = definition.Forces.ToDictionary(force => force.Id);
        _settlements = definition.Settlements.ToDictionary(settlement => settlement.Id);
        _settlementIcons = definition.Settlements.ToDictionary(
            settlement => settlement.Id,
            settlement => (IReadOnlyList<string>)Array.AsReadOnly(settlement.Icons.ToArray()));
    }

    public int PlayerForceId { get; }
    public InformationCatalog Information { get; }
    public IReadOnlyDictionary<int, string> RegionNameKeys { get; }
    public IReadOnlyDictionary<int, int> RegionCityIds { get; }

    public OfficerInitialState Officer(int id) => _officers[id];
    public ForceInitialState Force(int id) => _forces[id];
    public SettlementInitialState Settlement(int id) => _settlements[id];
    public IReadOnlyList<string> SettlementIcons(int id) => _settlementIcons[id];
    public StrategySnapshot Project(WorldSnapshot world) => new(this, world);
}

public sealed class StrategySnapshot
{
    public StrategySnapshot(ScenarioInitialState definition, WorldSnapshot world)
        : this(new StrategySnapshotSource(definition), world)
    {
    }

    internal StrategySnapshot(StrategySnapshotSource source, WorldSnapshot world)
    {
        Information = source.Information;
        World = world;
        PlayerForceId = source.PlayerForceId;
        RegionNameKeys = source.RegionNameKeys;
        RegionCityIds = source.RegionCityIds;
        var settlements = world.Settlements.ToDictionary(settlement => settlement.Id);
        Officers = Array.AsReadOnly(world.Officers.Where(o => o.IsVisible).Select(o =>
        {
            var location = o.SettlementId is int id ? settlements[id] : null;
            var definition = source.Officer(o.Id);
            return new OfficerView(o.Id, definition.NameKey, o.OwnerId, o.IsServing ? location?.CorpsId : null,
                o.SettlementId, location?.Kind == San9.Core.Domain.SettlementKind.City, o.IsServing && !o.Acted, o.Abilities)
            {
                Status = o.Status,
                CourtesyNameKey = definition.CourtesyNameKey,
                BiographyKey = definition.BiographyKey
            };
        }).ToArray());
        var officerLocations = world.Officers.ToDictionary(officer => officer.Id, officer => officer.SettlementId);
        Forces = Array.AsReadOnly(world.Forces.Select(f =>
        {
            var definition = source.Force(f.Id);
            int? leaderSettlementId = f.LeaderId is int leader && officerLocations.TryGetValue(leader, out int? location)
                ? location
                : null;
            return new ForceView(f.Id, definition.NameKey, definition.ColorCode, definition.FlagGlyph, f.Money, f.Food, f.Trust)
            {
                IsBarbarian = f.IsBarbarian,
                LeaderId = f.LeaderId,
                RankId = f.RankId,
                LeaderSettlementId = leaderSettlementId
            };
        }).ToArray());
        var availableSettlementIds = world.Officers
            .Where(officer => officer.IsServing && !officer.Acted && officer.SettlementId is not null)
            .Select(officer => (officer.SettlementId!.Value, officer.OwnerId))
            .ToHashSet();
        Settlements = Array.AsReadOnly(world.Settlements.Select(s =>
        {
            var definition = source.Settlement(s.Id);
            int? markerColor = s.Kind == San9.Core.Domain.SettlementKind.City
                ? s.OwnerId is int owner ? source.Force(owner).ColorCode : 50
                : null;
            return new SettlementView(s.Id, definition.NameKey, s.OwnerId, definition.X, definition.Y, s.Soldiers,
                s.Durability, s.MaxDurability, s.Morale, availableSettlementIds.Contains((s.Id, s.OwnerId)), markerColor,
                source.SettlementIcons(s.Id), definition.Appearance, s.PublicOrder, s.Commerce, s.MaxCommerce,
                s.Cultivation, s.MaxCultivation)
            {
                Kind = s.Kind,
                CorpsId = s.CorpsId,
                IsFacilityListEntry = definition.IsFacilityListEntry
            };
        }).ToArray());
    }
    public InformationCatalog Information { get; }
    public WorldSnapshot World { get; }
    public int Year => World.Clock.Year;
    public int Month => World.Clock.Month;
    public int Day => 1+10*((int)World.Clock.Xun-1);
    public int PlayerForceId { get; }
    public IReadOnlyDictionary<int,string> RegionNameKeys { get; }
    public IReadOnlyDictionary<int,int> RegionCityIds { get; }
    public IReadOnlyList<ForceView> Forces { get; }
    public IReadOnlyList<OfficerView> Officers { get; }
    public IReadOnlyList<SettlementView> Settlements { get; }
}

public sealed class InformationCatalog(InformationDefinitions definitions)
{
    public IReadOnlyList<RankDefinition> Ranks { get; } = Array.AsReadOnly(definitions.Ranks.ToArray());
    public IReadOnlyList<OfficeDefinition> Offices { get; } = Array.AsReadOnly(definitions.Offices.ToArray());
    public IReadOnlyList<TreasureDefinition> Treasures { get; } = Array.AsReadOnly(definitions.Treasures.ToArray());
}
