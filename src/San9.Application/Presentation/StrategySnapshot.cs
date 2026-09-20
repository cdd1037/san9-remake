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
    public int? CountryTitleId { get; init; }
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
    public string? NameSuffixKey { get; init; }
    public string ResolveName(San9.Content.Text.TextCatalog texts) => texts.Resolve(NameKey) + (NameSuffixKey is {} key ? texts.Resolve(key) : "");
    public int? FacilityTypeId { get; init; }
    public IReadOnlyList<int> EnabledTacticIds { get; init; } = [];
    public int? ConfiguredTacticId { get; init; }
    public int? BaseAttackRange { get; init; }
    public bool UsesRockfallPower { get; init; }
    public FacilityReprisal? Reprisal { get; init; }
    public bool IsUnderConstruction { get; init; }
}

internal sealed class StrategySnapshotSource
{
    private readonly IReadOnlyDictionary<int, OfficerInitialState> _officers;
    private readonly IReadOnlyDictionary<int, ForceInitialState> _forces;
    private readonly IReadOnlyDictionary<int, SettlementInitialState> _settlements;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<string>> _settlementIcons;

    public StrategySnapshotSource(ScenarioInitialState definition, MilitaryRulebook? military)
    {
        Military = military;
        ImperialCourt = definition.ImperialCourt;
        PlayerForceId = definition.PlayerForceId;
        FacilityNameSuffixKeys = new System.Collections.ObjectModel.ReadOnlyDictionary<int, string>(new Dictionary<int, string>(definition.FacilityNameSuffixKeys));
        Information = new(definition.Information);
        Abilities = new(new TreasureRulebook(definition.Information.Treasures.Select(t => t.ToRule())),
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
    internal MilitaryRulebook? Military { get; }
    internal ImperialCourtDefinition ImperialCourt { get; }
    internal IReadOnlyDictionary<int, string> FacilityNameSuffixKeys { get; }
    public IReadOnlyDictionary<int, string> RegionNameKeys { get; }
    public IReadOnlyDictionary<int, int> RegionCityIds { get; }

    public OfficerInitialState Officer(int id) => _officers[id];
    public (string NameKey, int ColorCode, int FlagGlyph) Force(ForceState force)
    {
        int? titleGlyph = force.CountryTitleId is int title ? Information.CountryTitles.Single(t => t.Id == title).FlagGlyph : null;
        if (force.Origin is { } origin)
        {
            // Fifty authored color resources can be reused; they do not bound force identities.
            // Runtime forces have no family glyph; an acquired country title supplies its own glyph.
            return (_officers[origin.FounderId].NameKey, force.Id % 50, titleGlyph ?? -1);
        }
        var initial = _forces[force.Id];
        return (initial.NameKey, initial.ColorCode, titleGlyph ?? initial.FlagGlyph);
    }
    public SettlementInitialState? Settlement(int id) => _settlements.GetValueOrDefault(id);
    public IReadOnlyList<string> SettlementIcons(int id) => _settlementIcons.GetValueOrDefault(id) ?? [];
    public StrategySnapshot Project(WorldSnapshot world) => new(this, world);
}

public sealed class StrategySnapshot
{
    private readonly MilitaryRulebook? military;
    public StrategySnapshot(ScenarioInitialState definition, WorldSnapshot world)
        : this(new StrategySnapshotSource(definition, definition.Military is { } military ? new(military) : null), world)
    {
    }

    internal StrategySnapshot(StrategySnapshotSource source, WorldSnapshot world)
    {
        military = source.Military;
        Information = source.Information;
        World = world;
        PlayerForceId = source.PlayerForceId;
        RegionNameKeys = source.RegionNameKeys;
        RegionCityIds = source.RegionCityIds;
        var settlements = world.Settlements.ToDictionary(settlement => settlement.Id);
        var emperor = source.ImperialCourt.Resolve(world.Simulation.ImperialCourtEnded, world.Officers);
        EmperorId = emperor?.Id;
        EmperorCityId = ImperialCourtDefinition.City(emperor, world.Settlements, source.RegionCityIds,
            emperor is not null && world.Simulation.Tasks.Any(t => t.OfficerId == emperor.Id && t.Journey is not null));
        var occupied = world.Simulation.Tasks.Select(t => t.OfficerId).ToHashSet();
        var journeys = world.Simulation.Tasks.Where(t => t.Journey is not null).ToDictionary(t => t.OfficerId);
        var abilities = source.Abilities.EvaluateMany(world.Officers, world.Treasures);
        Officers = Array.AsReadOnly(world.Officers.Where(o => !o.IsDormant && o.IsVisible).Select(o =>
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
        Forces = Array.AsReadOnly(world.Forces.Where(f => !f.IsDormant).Select(f =>
        {
            var definition = source.Force(f);
            int? leaderSettlementId = f.LeaderId is int leader && officerLocations.TryGetValue(leader, out int? location)
                ? location
                : null;
            return new ForceView(f.Id, definition.NameKey, definition.ColorCode, definition.FlagGlyph, f.Money, f.Food, f.Trust)
            {
                IsBarbarian = f.IsBarbarian,
                IsEliminated = f.IsEliminated,
                LeaderId = f.LeaderId,
                RankId = f.RankId, CountryTitleId = f.CountryTitleId,
                LeaderSettlementId = leaderSettlementId
            };
        }).ToArray());
        var availableSettlementIds = world.Officers
            .Where(officer => !officer.IsDormant && officer.IsServing && !officer.Acted && !occupied.Contains(officer.Id) && officer.SettlementId is not null)
            .Select(officer => (officer.SettlementId!.Value, officer.OwnerId))
            .ToHashSet();
        Settlements = Array.AsReadOnly(world.Settlements.Where(s => !s.IsDormant).Select(s =>
        {
            var definition = source.Settlement(s.Id);
            var militaryPlace = source.Military is { } rules && rules.TryPlace(s, out var place) ? place : null;
            var attack = militaryPlace is null ? null : source.Military!.FacilityAttack(s);
            var cell = s.Facility is { } facility ? MapCoordinates.Cell(facility.Position) :
                new MapCell(definition!.X, definition.Y);
            string nameKey = s.Facility is null ? definition!.NameKey : source.RegionNameKeys[s.Id];
            int? markerColor = s.Kind == San9.Core.Domain.SettlementKind.City
                ? s.OwnerId is int owner ? Forces.Single(f => f.Id == owner).ColorCode : 50
                : null;
            return new SettlementView(s.Id, nameKey, s.OwnerId, cell.X, cell.Y, s.Soldiers,
                s.Durability, s.MaxDurability, s.Morale, availableSettlementIds.Contains((s.Id, s.OwnerId)), markerColor,
                Array.AsReadOnly(source.SettlementIcons(s.Id).Where(icon => icon != "emperor")
                    .Concat(EmperorCityId == s.Id ? ["emperor"] : Array.Empty<string>()).ToArray()), definition?.Appearance ?? 0, s.PublicOrder, s.Commerce, s.MaxCommerce,
                s.Cultivation, s.MaxCultivation)
            {
                Kind = s.Kind,
                Wounded = s.Wounded,
                IsDestroyed = s.IsDestroyed,
                CorpsId = s.CorpsId,
                IsFacilityListEntry = !s.IsDestroyed && (s.Facility is null ? definition!.IsFacilityListEntry : s.CanHostPersons),
                NameSuffixKey = s.Facility is {} built ? source.FacilityNameSuffixKeys.GetValueOrDefault(built.TypeId) : null,
                FacilityTypeId = militaryPlace?.FacilityTypeId,
                EnabledTacticIds = militaryPlace?.EnabledTacticIds ?? [],
                ConfiguredTacticId = source.Military?.FacilityTactic(s)?.Id,
                BaseAttackRange = attack?.Attack?.Range,
                UsesRockfallPower = attack?.UsesRockfallPower == true,
                Reprisal = militaryPlace?.FacilityTypeId is int reprisalType ? source.Military?.Facilities[reprisalType].Reprisal : null,
                IsUnderConstruction = s.Facility?.BuilderUnitId is not null
            };
        }).ToArray());
    }
    public InformationCatalog Information { get; }
    public WorldSnapshot World { get; }
    public int? EmperorId { get; }
    public int? EmperorCityId { get; }
    public bool ImperialCourtEnded => World.Simulation.ImperialCourtEnded;
    public int Year => World.Clock.Year;
    public int Month => World.Clock.Month;
    public int Day => World.Clock.Day;
    public int PlayerForceId { get; }
    public IReadOnlyDictionary<int,string> RegionNameKeys { get; }
    public IReadOnlyDictionary<int,int> RegionCityIds { get; }
    public IReadOnlyList<ForceView> Forces { get; }
    public IReadOnlyList<OfficerView> Officers { get; }
    public IReadOnlyList<SettlementView> Settlements { get; }
    public bool HasCityInfluence(int forceId, WorldPosition position) =>
        military?.HasCityInfluence(forceId, position, World.Settlements) == true;
}

public sealed class InformationCatalog(InformationDefinitions definitions)
{
    public IReadOnlyList<CountryTitleDefinition> CountryTitles { get; } = Array.AsReadOnly(definitions.CountryTitles.ToArray());
    public IReadOnlyList<RankDefinition> Ranks { get; } = Array.AsReadOnly(definitions.Ranks.ToArray());
    public IReadOnlyList<OfficeDefinition> Offices { get; } = Array.AsReadOnly(definitions.Offices.ToArray());
    public IReadOnlyList<TreasureDefinition> Treasures { get; } = Array.AsReadOnly(definitions.Treasures.ToArray());
}
