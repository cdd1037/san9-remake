using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public sealed record ForceView(int Id,string NameKey,int ColorCode,int FlagGlyph,long Money,long Food,int Trust)
{
    public bool IsEliminated { get; init; }
    public bool IsBarbarian { get; init; }
    public bool UsesImperialAppearance { get; init; }
    public int? LeaderId { get; init; }
    public int? RankId { get; init; }
    public int? CountryTitleId { get; init; }
    public int? LeaderSettlementId { get; init; }
}
public sealed record SettlementView(int Id,string NameKey,int? OwnerId,int X,int Y,uint Soldiers,int Durability,int MaxDurability,
    int Morale,bool HasAvailableOfficer,int? CityMarkerColor,IReadOnlyList<string> Icons,int Appearance,int PublicOrder,
    int Commerce,int MaxCommerce,int Cultivation,int MaxCultivation)
{
    public int? WoundedRecovery { get; init; }
    public bool HasRangeBonus { get; init; }
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
    public bool IsWork { get; init; }
    public bool HasMapContextMenu { get; init; } = true;
}

internal sealed class StrategySnapshotSource
{
    private readonly IReadOnlyDictionary<int, OfficerInitialState> _officers;
    private readonly IReadOnlyDictionary<int, ForceInitialState> _forces;
    private readonly IReadOnlyDictionary<int, SettlementInitialState> _settlements;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<string>> _settlementIcons;

    public StrategySnapshotSource(ScenarioInitialState definition, ScenarioRules rules)
    {
        Rules = rules;
        Military = rules.Military;
        ImperialCourt = definition.ImperialCourt;
        FacilityNameSuffixKeys = new System.Collections.ObjectModel.ReadOnlyDictionary<int, string>(new Dictionary<int, string>(definition.FacilityNameSuffixKeys));
        Information = new(definition.Information);
        Treasures = rules.Treasures;
        Abilities = rules.Abilities;
        TacticCourses = Array.AsReadOnly(definition.TacticCourses.ToArray());
        PlacePositions = definition.Settlements.Select(s => new PersonPlace(s.Id,s.Position)).Concat(definition.UnbuiltPlaces)
            .ToDictionary(p=>p.Id,p=>p.Position);
        ProvinceNameKeys = new System.Collections.ObjectModel.ReadOnlyDictionary<int, string>(new Dictionary<int, string>(definition.ProvinceNameKeys));
        CityConnections = Array.AsReadOnly(definition.CityConnections.ToArray());
        CityProvinceIds = new System.Collections.ObjectModel.ReadOnlyDictionary<int, int>(new Dictionary<int, int>(definition.CityProvinceIds));
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

    internal ScenarioRules Rules { get; }
    public InformationCatalog Information { get; }
    internal OfficerAbilityRules Abilities { get; }
    internal TreasureRulebook Treasures { get; }
    internal MilitaryRulebook? Military { get; }
    internal ImperialCourtDefinition ImperialCourt { get; }
    internal IReadOnlyDictionary<int, string> FacilityNameSuffixKeys { get; }
    public IReadOnlyList<CityTacticCourse> TacticCourses { get; }
    internal IReadOnlyDictionary<int,WorldPosition> PlacePositions { get; }
    public IReadOnlyDictionary<int, string> ProvinceNameKeys { get; }
    public IReadOnlyList<CityConnection> CityConnections { get; }
    public IReadOnlyDictionary<int, int> CityProvinceIds { get; }
    public IReadOnlyDictionary<int, string> RegionNameKeys { get; }
    public IReadOnlyDictionary<int, int> RegionCityIds { get; }

    public OfficerInitialState Officer(int id) => _officers[id];
    public (string NameKey, int ColorCode, int FlagGlyph) Force(ForceState force)
    {
        string? rulerName = !force.IsBarbarian && force.LeaderId is int ruler && _officers.TryGetValue(ruler, out var person)
            ? person.NameKey : null;
        int? titleGlyph = force.CountryTitleId is int title ? Information.CountryTitles.Single(t => t.Id == title).FlagGlyph : null;
        if (force.Origin is { } origin)
        {
            int glyph = force.LeaderId is int leader ? _officers[leader].FlagGlyph : -1;
            return (rulerName ?? _officers[origin.FounderId].NameKey, force.ColorCode, titleGlyph ?? glyph);
        }
        var initial = _forces[force.Id];
        return (rulerName ?? initial.NameKey, force.ColorCode, titleGlyph ?? initial.FlagGlyph);
    }
    public SettlementInitialState? Settlement(int id) => _settlements.GetValueOrDefault(id);
    public IReadOnlyList<string> SettlementIcons(int id) => _settlementIcons.GetValueOrDefault(id) ?? [];
    public StrategySnapshot Project(WorldSnapshot world) => new(this, world);
}

public sealed class StrategySnapshot
{
    private readonly StrategySnapshotSource _source;
    internal ScenarioRules Rules => _source.Rules;
    // Pending encounters can introduce people before they enter the visible officer catalog.
    internal DecisionPerson DecisionPerson(int id)
    {
        var person = World.Officers.Single(p => p.Id == id);
        return new(id, _source.Officer(id).NameKey, _source.Abilities.Evaluate(person, World.Treasures));
    }
    /// <summary>Dialogue speakers include NPCs that are intentionally absent from the officer browser.</summary>
    public (string NameKey, int? OwnerId) MessageSpeaker(int id) =>
        (_source.Officer(id).NameKey, World.Officers.Single(p => p.Id == id).OwnerId);
    private readonly MilitaryRulebook? military;
    internal MilitaryRulebook? MilitaryRules => military;
    private MilitaryEngagementSnapshot? engagements;
    private MilitaryEngagementSnapshot? Engagements => military is null ? null :
        engagements ??= MilitaryEngagementQuery.Capture(World, military);
    public bool IsUnitEngaged(int id) => Engagements?.Units.Contains(id) == true;
    public bool IsSettlementEngaged(int id) => Engagements?.Settlements.Contains(id) == true;
    public UnitPurpose UnitPurpose(UnitState unit) => unit.FormationId is int formation && military is not null
        ? military.Purpose(formation) : San9.Core.Domain.UnitPurpose.Combat;
    public int? FormationId(UnitState unit) => unit.FormationId is null ? null : military!.Formation(unit).Id;
    public MapOccupancy? SettlementGeographicArea(int id)
    {
        var settlement = World.Settlements.FirstOrDefault(s => s.Id == id);
        return military is not null && settlement is { IsDestroyed: false } && military.TryPlace(settlement, out _)
            ? military.SettlementArea(settlement) : null;
    }
    public MapOccupancy? BattleArea(int id, bool settlement, WorldPosition? position = null)
    {
        if (military is null) return null;
        if (settlement)
        {
            var s = World.Settlements.FirstOrDefault(s => s.Id == id);
            return s is not null && military.TryPlace(s, out var place) ? military.BuildingArea(place) : null;
        }
        var unit = World.Units.FirstOrDefault(u => u.Id == id);
        return unit?.FormationId is not null ? new(position ?? unit.Position, military.UnitFootprint(unit, position)) : null;
    }
    public IReadOnlyList<MapCell> FacilityRockfallPath(int id, WorldPosition target) => military is not null &&
        World.Settlements.FirstOrDefault(s => s.Id == id) is {} source ? military.RockfallPath(source, target, World.Settlements) : [];
    public BattleRank? FormationRank(UnitState unit, int officerId) => unit.FormationId is null || !unit.MemberRanks.TryGetValue(officerId, out var rank)
        ? null : unit.NavalFormationId is null ? rank : military!.Rank(unit, officerId);
    private MilitaryArrivalForecast? arrivalForecast;
    private readonly TreasureRulebook arrivalTreasures;
    private readonly Dictionary<int, ArrivalEstimate> arrivals = [];

    public ArrivalEstimate UnitArrival(int unitId)
    {
        if (military is null || !World.Units.Any(u => u.Id == unitId && u.OwnerId == PlayerForceId))
            return new(ArrivalEstimateStatus.NotApplicable);
        if (!arrivals.TryGetValue(unitId, out var estimate))
            arrivals.Add(unitId, estimate = (arrivalForecast ??= new(World, military, RegionCityIds, arrivalTreasures)).Estimate(unitId));
        return estimate;
    }
    public StrategySnapshot(ScenarioInitialState definition, WorldSnapshot world)
        : this(new StrategySnapshotSource(definition, new ScenarioRules(definition)), world)
    {
    }

    internal StrategySnapshot(StrategySnapshotSource source, WorldSnapshot world)
    {
        _source = source;
        military = source.Military;
        arrivalTreasures = source.Treasures;
        Information = source.Information;
        FacilityNameSuffixKeys = source.FacilityNameSuffixKeys;
        World = world;
        PlayerForceId = ResolvePlayerForce(world);
        TacticCourses = source.TacticCourses;
        ProvinceNameKeys = source.ProvinceNameKeys;
        CityConnections = source.CityConnections; CityProvinceIds = source.CityProvinceIds;
        RegionNameKeys = source.RegionNameKeys;
        RegionCityIds = source.RegionCityIds;
        var settlements = world.Settlements.ToDictionary(settlement => settlement.Id);
        var units = world.Units.ToDictionary(unit => unit.Id);
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
            var unit = o.UnitId is int unitId ? units[unitId] : null;
            int? corpsId = unit is not null ? unit.CorpsId : location?.CorpsId;
            return new OfficerView(o.Id, definition.NameKey, o.OwnerId, o.IsServing ? corpsId : null,
                o.SettlementId, location?.Kind == San9.Core.Domain.SettlementKind.City, o.IsServing && o.UnitId is null && !o.Acted && !occupied.Contains(o.Id), abilities[o.Id])
            {
                FlagGlyph = definition.FlagGlyph,
                UnitId = o.UnitId,
                ServicePlaceId = unit is not null ? unit.ServicePlaceId : o.SettlementId ?? o.RegionId,
                Status = o.Status,
                Journey = task is null ? null : new(task.Id, task.Kind, task.OriginSettlementId, task.Journey!.DestinationPlaceId,
                    task.Journey.TargetOfficerId, task.Due, task.ProgressDays),
                CourtesyNameKey = definition.CourtesyNameKey,
                BiographyKey = definition.BiographyKey
            };
        }).ToArray());
        var officerLocations = world.Officers.ToDictionary(officer => officer.Id, officer => officer.SettlementId);
        int? imperialRank = Information.Ranks.MaxBy(r => r.Precedence)?.Id;
        int? emperorOwner = EmperorCityId is int emperorCity ? settlements[emperorCity].OwnerId : null;
        Forces = Array.AsReadOnly(world.Forces.Where(f => !f.IsDormant).Select(f =>
        {
            var definition = source.Force(f);
            int? leaderSettlementId = f.LeaderId is int leader && officerLocations.TryGetValue(leader, out int? location)
                ? location
                : null;
            return new ForceView(f.Id, definition.NameKey, definition.ColorCode, definition.FlagGlyph, world.Corps.Where(c => c.OwnerId == f.Id).Sum(c => (long)c.Treasury.Money),
                world.Corps.Where(c => c.OwnerId == f.Id).Sum(c => (long)c.Treasury.Food), f.Trust)
            {
                UsesImperialAppearance = imperialRank is int rank && f.RankId == rank || emperorOwner == f.Id,
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
            var facilityRule = militaryPlace?.FacilityTypeId is int type ? source.Military!.Facilities[type] : null;
            var attack = militaryPlace is null ? null : source.Military!.FacilityAttack(s);
            var cell = s.Facility is { } facility ? MapCoordinates.Cell(facility.Position) :
                new MapCell(definition!.X, definition.Y);
            string nameKey = s.Facility is null ? definition!.NameKey : source.RegionNameKeys[s.Id];
            int? markerColor = s.Kind == San9.Core.Domain.SettlementKind.City
                ? s.OwnerId is int owner ? Forces.Single(f => f.Id == owner).ColorCode : 50
                : null;
            return new SettlementView(s.Id, nameKey, s.OwnerId, cell.X, cell.Y, s.Soldiers,
                s.Durability, s.MaxDurability, s.Morale, availableSettlementIds.Contains((s.Id, s.OwnerId)), markerColor,
                Array.AsReadOnly(source.SettlementIcons(s.Id).Where(icon => icon != "emperor" &&
                        (icon != "training" || s.MerchantPresenceChance != 100))
                    .Concat(s.MerchantPresenceChance == 100 ? ["merchant"] : Array.Empty<string>())
                    .Concat(EmperorCityId == s.Id ? ["emperor"] : Array.Empty<string>()).ToArray()), definition?.Appearance ?? 0, s.PublicOrder, s.Commerce, s.MaxCommerce,
                s.Cultivation, s.MaxCultivation)
            {
                Kind = s.Kind,
                Wounded = s.Wounded, WoundedRecovery = militaryPlace?.WoundedRecovery,
                HasRangeBonus = attack?.Attack is { } enhanced && militaryPlace?.Attack is { } original && enhanced.Range > original.Range,
                IsDestroyed = s.IsDestroyed,
                CorpsId = s.CorpsId,
                IsFacilityListEntry = !s.IsDestroyed && s.CanHostPersons,
                NameSuffixKey = s.Facility is {} built ? source.FacilityNameSuffixKeys.GetValueOrDefault(built.TypeId) : null,
                FacilityTypeId = militaryPlace?.FacilityTypeId,
                IsWork = facilityRule?.IsWork == true,
                HasMapContextMenu = facilityRule is null || !source.Information.MapContextExcludedFacilityIds.Contains(facilityRule.Id),
                EnabledTacticIds = militaryPlace?.EnabledTacticIds ?? [],
                ConfiguredTacticId = source.Military?.FacilityTactic(s)?.Id,
                BaseAttackRange = attack?.Attack?.Range,
                UsesRockfallPower = attack?.UsesRockfallPower == true,
                Reprisal = facilityRule?.Reprisal,
                IsUnderConstruction = s.Facility?.BuilderUnitId is not null
            };
        }).ToArray());
    }
    public InformationCatalog Information { get; }
    public WorldSnapshot World { get; }
    public MapCell? PlaceCell(int id) => Settlements.FirstOrDefault(p=>p.Id==id) is { } place ? new(place.X,place.Y) :
        _source.PlacePositions.TryGetValue(id,out var position) ? MapCoordinates.Cell(position) : null;
    /// <summary>Projects a world from the same scenario, reusing definitions and this view when the world is unchanged.</summary>
    public StrategySnapshot ForWorld(WorldSnapshot world) => ReferenceEquals(World, world) ? this : _source.Project(world);
    public int? EmperorId { get; }
    public int? EmperorCityId { get; }
    public bool ImperialCourtEnded => World.Simulation.ImperialCourtEnded;
    public int Year => World.Clock.Year;
    public int Month => World.Clock.Month;
    public int Day => World.Clock.Day;
    public int? PlayerForceId { get; }
    public int RequirePlayerForce() => PlayerForceId ?? throw new InvalidOperationException("No player force owns the current interaction.");
    private static int? ResolvePlayerForce(WorldSnapshot world)
    {
        var simulation = world.Simulation;
        if (simulation.ActiveControl < simulation.Controls.Length &&
            simulation.Controls[simulation.ActiveControl] is { Kind: ControllerKind.Player } active) return active.ForceId;
        return null;
    }
    public CorpsState? PlayerCorps => World.Corps.SingleOrDefault(c => c.OwnerId == PlayerForceId && c.IsRoot);
    public EconomicOutlook? PlayerEconomicOutlook => PlayerCorps is {} corps
        ? EconomicOutlook.Read(World, Information.Offices.ToDictionary(o => o.Id, o => o.Salary), corps.OwnerId, corps.Id)
        : null;
    public IReadOnlyList<CityTacticCourse> TacticCourses { get; }
    public IReadOnlyDictionary<int, string> ProvinceNameKeys { get; }
    public IReadOnlyList<CityConnection> CityConnections { get; }
    public IReadOnlyDictionary<int, int> CityProvinceIds { get; }
    public IReadOnlyDictionary<int,string> RegionNameKeys { get; }
    public IReadOnlyDictionary<int,int> RegionCityIds { get; }
    public IReadOnlyList<ForceView> Forces { get; }
    public IReadOnlyList<OfficerView> Officers { get; }
    public IReadOnlyList<SettlementView> Settlements { get; }
    internal IReadOnlyDictionary<int, string> FacilityNameSuffixKeys { get; }
    internal IReadOnlyList<TerritoryConnection> TerritoryConnections() => military?.TerritoryConnections(World.Settlements) ?? [];
    internal int? TacticCategory(int id) => military?.Tactics.GetValueOrDefault(id)?.Category;
    internal int? RegionAt(WorldPosition position) => military?.RegionAt(position);
    public bool HasCityInfluence(int forceId, WorldPosition position) =>
        military?.HasCityInfluence(forceId, position, World.Settlements) == true;
}

public sealed class InformationCatalog(InformationDefinitions definitions)
{
    public IReadOnlySet<int> MapContextExcludedFacilityIds { get; } = definitions.MapContextExcludedFacilityIds.ToImmutableHashSet();
    public IReadOnlyList<CountryTitleDefinition> CountryTitles { get; } = Array.AsReadOnly(definitions.CountryTitles.ToArray());
    public IReadOnlyList<MilitaryCatalogDefinition> Tactics { get; } = Array.AsReadOnly(definitions.Tactics.ToArray());
    public IReadOnlyList<FormationCatalogDefinition> Formations { get; } = Array.AsReadOnly(definitions.Formations.ToArray());
    public IReadOnlyList<RankDefinition> Ranks { get; } = Array.AsReadOnly(definitions.Ranks.ToArray());
    public IReadOnlyList<OfficeDefinition> Offices { get; } = Array.AsReadOnly(definitions.Offices.ToArray());
    public IReadOnlyList<TreasureDefinition> Treasures { get; } = Array.AsReadOnly(definitions.Treasures.ToArray());
}
