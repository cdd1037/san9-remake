using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

/// <summary>One military work owns its candidates and forecasts. Only public commands change the world.</summary>
internal sealed partial class OriginalAiMilitary(SimulationEngine engine, ScenarioInitialState content,
    MilitaryRulebook rules, int corpsId, CommandContext context, OriginalAiRandom random, Func<GameCommand, bool> submit)
{
    private WorldSnapshot World => engine.Snapshot;
    private CorpsState Corps => World.Corps.Single(c => c.Id == corpsId);
    private int Owner => Corps.OwnerId;
    private ForceState Force => World.Forces.Single(f => f.Id == Owner);
    private OriginalAiMilitaryDefinition Policy => content.OriginalAi.Military;
    private readonly GovernanceRulebook governance = new(content.Information.Offices.Select(o =>
        new GovernorOfficeRule(o.Id, o.CommandCapacity, o.GovernorPriority)));
    private readonly TreasureRulebook treasures = new(content.Information.Treasures.Select(t => t.ToRule()));
    private WorldSnapshot? projected;
    private ImmutableDictionary<int, OfficerAbilities> abilities = ImmutableDictionary<int, OfficerAbilities>.Empty;
    private OriginalAiGeography geography = null!;
    private readonly Dictionary<(UnitState Unit, int? MovingUnit), Route?> routes = [];
    private readonly Dictionary<int, MilitaryOrder?> orders = [];
    private void Refresh()
    {
        if (ReferenceEquals(projected, World)) return;
        projected = World;
        abilities = engine.GetAbilities(World);
        geography = new(content, rules, World);
        routes.Clear();
        orders.Clear();
    }
    private int attempts, submitted, rejected, empty, paths;
    private bool incomplete;
    private int limit;
    private bool More => attempts < limit && World.Simulation.Boundary == SimulationBoundary.Stable;
    private bool Direct(SettlementState s) => s.OwnerId == Owner && (s.CorpsId == corpsId || s.CorpsId is null && Corps.IsRoot);
    private bool Direct(UnitState u) => u.OwnerId == Owner && (u.CorpsId == corpsId || u.CorpsId is null && Corps.IsRoot);
    private static bool Active(UnitState u) => !u.IsEmbedded && !u.IsPersonnel && u.FormationId is not null &&
        u.Condition is not (UnitCondition.Routed or UnitCondition.Disguised or UnitCondition.NoFormation);
    private bool Departed(UnitState u) => Active(u) && (content.OriginalAi.Advanced || !u.CommandedThisXun);
    private SettlementState[] Places => World.Settlements.Where(s => Direct(s) && !s.IsDormant && !s.IsDestroyed &&
        s.Durability > 0 && s.CanHostPersons && s.Facility?.BuilderUnitId is null).ToArray();
    private OfficerState[] People(int home) => World.Officers.Where(p => !p.IsDormant && p.IsServing && p.OwnerId == Owner &&
        p.SettlementId == home && p.UnitId is null && !p.Acted && !World.Simulation.Tasks.Any(t => t.OfficerId == p.Id)).ToArray();
    private OfficerAbilities Ability(OfficerState p) { Refresh(); return abilities[p.Id]; }
    private OfficerState[] Commanders(IEnumerable<OfficerState> people) { Refresh(); return governance.OrderCommanders(people, abilities).ToArray(); }
    private uint Capacity(OfficerState p) => CommanderCapacity.Read(p.Id == Force.LeaderId,
        content.Information.Ranks.FirstOrDefault(r => r.Id == Force.RankId)?.CommandCapacity,
        content.Information.Offices.FirstOrDefault(o => o.Id == p.OfficeId)?.CommandCapacity);
    private int City(int place) => content.RegionCityIds.GetValueOrDefault(place, place);
    private bool Near(int first, int second) => City(first) == City(second) || content.CityConnections.Any(c =>
        c.FromCityId == City(first) && c.ToCityId == City(second));
    private bool Opponent(int? owner) => owner is int id && (World.Forces.FirstOrDefault(f => f.Id == id)?.IsBarbarian == true
        ? id != Owner : OriginalAiStrategy.IsOpponent(World, Owner, id));
    private bool Target(SettlementState s) => !s.IsDormant && !s.IsDestroyed && s.OwnerId != Owner && (s.OwnerId is null || Opponent(s.OwnerId));
    private bool Fortified(SettlementState s) => s.Kind == SettlementKind.City ||
        rules.TryPlace(s, out var p) && p.FacilityTypeId is int type && Policy.FortifiedFacilityIds.Contains(type);
    private long BuildingReserve(SettlementState s)
    {
        Refresh();
        if (!geography.Frontier(s.Id, Owner)) return 0;
        long amount = s.Kind == SettlementKind.City ? Policy.FrontierCityReserve : rules.TryPlace(s, out var place) &&
            place.FacilityTypeId is int type ? Policy.FrontierFacilityReserves.GetValueOrDefault(type) : 0;
        return (Math.Min(150000, amount) + 2499) / 2500 * 2500;
    }
    private long Reserve(SettlementState s) => Math.Max(Fortified(s) ? 10000L : 0,
        World.Units.Where(u => Departed(u) && KnownOrder(u) is { Kind: MilitaryOrderKind.AttackSettlement, SettlementId: int target } && target == s.Id)
            .Sum(u => (long)u.Soldiers) * 2 / 3);
    private MilitaryOrder? KnownOrder(UnitState u)
    {
        if (u.OwnerId == Owner) return u.EffectiveOrder;
        if (!World.Simulation.OtherForceOrdersVisible) return null;
        Refresh();
        if (orders.TryGetValue(u.Id, out var cached)) return cached;
        var order = u.EffectiveOrder;
        if (order?.SettlementId is int targetId && World.Settlements.FirstOrDefault(s => s.Id == targetId && !s.IsDestroyed && !s.IsDormant) is { } target &&
            u.FormationId is not null)
        {
            var travel = rules.Travel(u);
            paths++;
            var route = travel.Map.FindRouteIntoRange(u.Position, rules.SettlementArea(target), travel.Shape, travel.Mode, []);
            if (route.Reachable)
                foreach (var point in route.Steps)
                {
                    var obstacle = World.Settlements.FirstOrDefault(s => s.Id != u.ServicePlaceId && !s.IsDestroyed && !s.IsDormant &&
                        rules.TryPlace(s, out var p) && InRegion(point, s) &&
                        (p.FacilityTypeId is int type && Policy.RouteObstacleFacilityIds.Contains(type) || s.Id != targetId &&
                            s.OwnerId is int owner && OriginalAiStrategy.IsOpponent(World, u.OwnerId, owner)));
                    if (obstacle is null) continue;
                    order = new(MilitaryOrderKind.AttackSettlement, null, obstacle.Id);
                    break;
                }
        }
        orders[u.Id] = order;
        return order;
    }
    private MilitaryOrder? KnownMain(UnitState u) => u.OwnerId == Owner || World.Simulation.OtherForceOrdersVisible ? u.Order : null;
    private bool InRegion(WorldPosition point, SettlementState place) => rules.RegionAt(point) is int region
        ? region == place.Id : rules.SettlementArea(place).Overlaps(new(point, new(1, 1)));
    private GameCommand Command(Func<Guid, int, long, GameCommand> create)
    {
        long sequence = World.Simulation.NextSequence;
        return create(SimulationIdentity.Command(sequence, Owner), Owner, sequence);
    }
    private bool Send(GameCommand? command)
    {
        attempts++;
        if (command is null) { empty++; return false; }
        if (!submit(command)) { rejected++; return false; }
        submitted++;
        return true;
    }
    private bool Legal(GameCommand command) => engine.GetPreparationEligibility(context, command) == CommandRejection.None;
    internal static bool Handles(OriginalAiWork work) => work is OriginalAiWork.Attack or OriginalAiWork.FulfillRequest or
        OriginalAiWork.Deploy or OriginalAiWork.Retreat or OriginalAiWork.Transport;

    internal OriginalAiWorkReport Run(OriginalAiWork work, int maximumAttempts, OriginalAiState state, OriginalAiCorpsState strategy)
    {
        attempts = submitted = rejected = empty = paths = 0; incomplete = false; limit = maximumAttempts;
        switch (work)
        {
            case OriginalAiWork.Attack: Attack(state, strategy, false); break;
            case OriginalAiWork.FulfillRequest: Attack(state, strategy, true); break;
            case OriginalAiWork.Retreat: Retreat(); break;
            case OriginalAiWork.Deploy: Deploy(); break;
            case OriginalAiWork.Transport: Transport(); break;
        }
        return new(corpsId, work, incomplete ? OriginalAiWorkEnd.AnalysisIncomplete : attempts >= limit ?
            OriginalAiWorkEnd.AttemptsExhausted : OriginalAiWorkEnd.NoCandidates, attempts, submitted)
            { Rejected = rejected, EmptyPlans = empty, PathQueries = paths };
    }

    private sealed record Route(int Days, ImmutableArray<WorldPosition> Steps, bool Slow, bool Obstacle);
    private Route? RouteTo(UnitState unit, MilitaryOrder order, int? moving = null)
    {
        Refresh();
        var planned = unit with { Order = order, EngagementTargetId = null, DepartureDaysRemaining = 0, Waypoint = null, Lookahead = null };
        var key = (planned, moving);
        if (routes.TryGetValue(key, out var cached)) return cached;
        var result = AnalyzeRoute(planned, moving);
        routes[key] = result;
        return result;
    }
    private Route? AnalyzeRoute(UnitState unit, int? moving)
    {
        paths++;
        var path = MilitaryRouteQuery.Plan(World, rules, unit, moving);
        if (path.Status == NavigationStatus.Planning) { incomplete = true; return null; }
        if (path.Status != NavigationStatus.Arrived || path.Days is not int days) return null;
        bool slow = content.Military is { } map && path.Steps.Any(p => Policy.SlowTerrainIds.Contains(
            map.TerrainIds[MapCoordinates.Cell(p).Y * map.Width + MapCoordinates.Cell(p).X]));
        bool obstacle = path.Steps.Any(p => World.Settlements.Any(s => !s.IsDestroyed && rules.TryPlace(s, out var place) &&
            place.FacilityTypeId is int type && Policy.RouteObstacleFacilityIds.Contains(type) && InRegion(p, s)));
        return new(days, path.Steps, slow, obstacle);
    }
    private Route? RouteFrom(SettlementState source, int land, int? naval, MilitaryOrder order, UnitPolicies? policies = null)
    {
        var travel = rules.Travel(land, naval);
        if (travel.DailyBudget <= 0 || !rules.TryPlace(source, out var place)) return null;
        var occupancy = MilitaryRouteQuery.Occupancy(World, rules, Owner, policies ?? new(), includeUnits: false);
        var start = MilitaryRouteQuery.DeploymentPosition(travel, rules.BuildingArea(place), occupancy);
        return start is { } position ? RouteTo(new(0, Owner, position, 1) { FormationId = land,
            NavalFormationId = naval, Policies = policies ?? new() }, order) : null;
    }
    private (int Land, int? Naval)? Basic(OfficerState[] people, bool march = false)
    {
        var land = AvailableFormations(people, false, Corps.Treasury.Money);
        var choice = march && Policy.Formations.TryGetValue(OriginalAiFormationRole.March, out int preferred)
            ? land.FirstOrDefault(f => f.Id == preferred) : null;
        choice ??= land.FirstOrDefault();
        if (choice is null) return null;
        var naval = AvailableFormations(people, true, Corps.Treasury.Money - (uint)choice.DeploymentCost)
            .FirstOrDefault(f => f.Id == rules.BasicNavalFormationId && rules.ValidFormationPair(choice.Id, f.Id));
        return (choice.Id, naval?.Id);
    }
    private FormationRule[] AvailableFormations(OfficerState[] people, bool naval, long money) => rules.Formations.Values
        .Where(f => f.TravelMode == (naval ? TravelMode.Water : TravelMode.Land) && f.Id != rules.UnformedFormationId &&
            rules.CanSelectFormation(Force, f.Id) && f.DeploymentCost <= money && f.OfficerCapacity >= people.Length &&
            (f.RequiredTacticId is not int required || TacticAvailability.HasForMembers(required, people, Force, World.Treasures, treasures)))
        .OrderBy(f => f.Id).ToArray();
    private UnitPolicies Policies(bool fallback = false) => fallback ? new(false, false, false, true) :
        Force.IsBarbarian ? new(true, true, true, false) : new(false, false, true, true);
    internal static uint MinimumAttack(SettlementState source) => source.Kind == SettlementKind.City ? 10000u : 5000u;
}
