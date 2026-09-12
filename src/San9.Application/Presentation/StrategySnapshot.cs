using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public sealed record ForceView(int Id,string NameKey,int ColorCode,int FlagGlyph,uint Money,uint Food,int Trust)
{
    public bool IsEliminated { get; init; }
    public bool IsBarbarian { get; init; }
    public int? LeaderId { get; init; }
    public int? RankId { get; init; }
    public int? LeaderSettlementId { get; init; }
}
public sealed record SettlementView(int Id,string NameKey,int? OwnerId,int X,int Y,uint Soldiers,int Durability,int MaxDurability,
    int Morale,bool HasAvailableOfficer,int? CityMarkerColor,IReadOnlyList<string> Icons,int Appearance,int PublicOrder,
    int Commerce,int MaxCommerce,int Cultivation,int MaxCultivation)
{
    public uint Wounded { get; init; }
    public bool IsDestroyed { get; init; }
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
        Abilities = new(new TreasureRulebook(definition.Information.Treasures.Select(t => new TreasureRule(t.Id, t.AbilityBonuses, t.LoyaltyBonus))),
            new GovernanceRulebook(definition.Information.Offices.Select(o => new GovernorOfficeRule(o.Id, o.CommandCapacity, o.GovernorPriority)
                { AbilityBonuses = o.AbilityBonuses })));
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
    internal OfficerAbilityRules Abilities { get; }
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
        var occupied = world.Simulation.Tasks.Select(t => t.OfficerId).ToHashSet();
        var journeys = world.Simulation.Tasks.Where(t => t.Journey is not null).ToDictionary(t => t.OfficerId);
        var abilities = source.Abilities.EvaluateMany(world.Officers, world.Treasures);
        Officers = Array.AsReadOnly(world.Officers.Where(o => o.IsVisible).Select(o =>
        {
            var location = o.SettlementId is int id ? settlements[id] : null;
            var definition = source.Officer(o.Id);
            var task = journeys.GetValueOrDefault(o.Id);
            int? corpsId = o.UnitId is int unitId ? world.Units.Single(u => u.Id == unitId).CorpsId : location?.CorpsId;
            return new OfficerView(o.Id, definition.NameKey, o.OwnerId, o.IsServing ? corpsId : null,
                o.SettlementId, location?.Kind == San9.Core.Domain.SettlementKind.City, o.IsServing && o.UnitId is null && !o.Acted && !occupied.Contains(o.Id), abilities[o.Id])
            {
                UnitId = o.UnitId,
                Status = o.Status,
                Journey = task is null ? null : new(task.Id, task.Kind, task.OriginSettlementId, task.Journey!.DestinationPlaceId,
                    task.Journey.TargetOfficerId, task.Due, task.ProgressDays),
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
                IsEliminated = f.IsEliminated,
                LeaderId = f.LeaderId,
                RankId = f.RankId,
                LeaderSettlementId = leaderSettlementId
            };
        }).ToArray());
        var availableSettlementIds = world.Officers
            .Where(officer => officer.IsServing && !officer.Acted && !occupied.Contains(officer.Id) && officer.SettlementId is not null)
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
                Wounded = s.Wounded,
                IsDestroyed = s.IsDestroyed,
                CorpsId = s.CorpsId,
                IsFacilityListEntry = definition.IsFacilityListEntry
            };
        }).ToArray());
    }
    public InformationCatalog Information { get; }
    public WorldSnapshot World { get; }
    public int Year => World.Clock.Year;
    public int Month => World.Clock.Month;
    public int Day => World.Clock.Day;
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
