using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public readonly record struct MilitaryTarget(int Id, bool IsUnit, WorldPosition? Position = null);
public enum MilitaryMarker { Enter, Attack }

/// <summary>Revision-bound map intentions. Selecting a candidate does not issue a military command.</summary>
public abstract class MilitaryTargets
{
    public StrategySnapshot View { get; }
    public MilitaryRulebook Rules { get; }
    protected int OwnerId { get; }
    protected int? MovingUnitId { get; }
    protected abstract bool CanProceed { get; }
    protected abstract uint Soldiers { get; }
    protected abstract int FormationId { get; }
    protected abstract int? NavalFormationId { get; }
    protected virtual int? SiegeEquipmentId => null;
    protected abstract WorldPosition? Start(UnitTravel travel, IReadOnlyList<MapOccupancy> occupancy);
    public abstract bool IsDeployment { get; }
    public abstract MilitaryTarget Source { get; }
    public virtual IReadOnlyList<MilitaryTarget> Sources => [Source];
    public IReadOnlyList<ImmutableArray<MapCell>> Routes { get; private set; } = [];
    public ImmutableDictionary<MilitaryTarget, MilitaryMarker> Candidates { get; protected set; }
    public ImmutableHashSet<MapCell> ClearCells { get; protected set; }
    public ImmutableHashSet<MapCell> InfluenceCells { get; }
    public MilitaryTarget? Selected { get; private set; }
    public MapCell? Waypoint => WaypointPosition is { } via ? MapCoordinates.Cell(via) : null;
    protected WorldPosition? WaypointPosition { get; private set; }
    public ImmutableArray<MapCell> Route { get; private set; } = [];
    public bool Reachable { get; private set; }
    public long Revision { get; private set; }
    public UnitPolicies Policies { get; protected set; } = new();
    public int DepartureDelayDays { get; private set; }
    public bool EditingPolicies { get; private set; }
    public string? Error { get; private set; }
    protected sealed record RoutePreview(ImmutableArray<MapCell> Cells, string? Error = null)
    {
        public bool Reachable => Error is null;
        public ImmutableArray<ImmutableArray<MapCell>> UnitRoutes { get; init; } = [];
    }

    protected MilitaryTargets(StrategySnapshot view, MilitaryRulebook rules, int ownerId,
        int? homeId, int? movingUnitId, UnitCommandKind? commandKind = null,
        IReadOnlyDictionary<MilitaryTarget, MapOccupancy>? locations = null, bool showInfluence = true)
    {
        View = view; Rules = rules; OwnerId = ownerId; MovingUnitId = movingUnitId;
        var world = view.World;
        InfluenceCells = showInfluence && homeId is int home ? rules.CityInfluenceCells(world.Settlements.Single(s => s.Id == home), world.Settlements).ToImmutableHashSet() : [];
        if (locations is not null)
        {
            Candidates = locations.Keys.ToImmutableDictionary(t => t, _ => MilitaryMarker.Enter);
            ClearCells = locations.Values.SelectMany(rules.GeographicCells).ToImmutableHashSet();
            return;
        }
        var query = movingUnitId is null ? null : new MilitaryOrderQuery(world, rules);
        var candidates = new Dictionary<MilitaryTarget, MilitaryMarker>();
        var clear = new HashSet<MapCell>();
        foreach (var place in world.Settlements.Where(s => s.Id != homeId && !s.IsDestroyed && !s.IsDormant && rules.TryPlace(s, out _)))
        {
            bool work = rules.Place(place).FacilityTypeId is int type && rules.Facilities[type].IsWork;
            if (place.OwnerId == ownerId && !work && !place.CanHostPersons) continue;
            var marker = place.OwnerId == ownerId && !work ? MilitaryMarker.Enter : MilitaryMarker.Attack;
            if (commandKind is { } kind)
            {
                marker = kind == UnitCommandKind.Attack ? MilitaryMarker.Attack : MilitaryMarker.Enter;
                var orderKind = kind switch { UnitCommandKind.March => MilitaryOrderKind.Return,
                    UnitCommandKind.Transport => MilitaryOrderKind.Transport, _ => MilitaryOrderKind.AttackSettlement };
                var order = new MilitaryOrder(orderKind, null, place.Id);
                if (!query!.CanAssign(movingUnitId!.Value, order)) continue;
            }
            candidates.Add(new(place.Id, false), marker);
            clear.UnionWith(rules.GeographicFootprint(place));
        }
        foreach (var unit in world.Units.Where(u => !u.IsEmbedded && !u.IsPersonnel && u.Soldiers > 0 && u.FormationId is not null &&
            u.Id != movingUnitId && u.OwnerId != ownerId))
        {
            if (commandKind is UnitCommandKind.March or UnitCommandKind.Transport || query is not null &&
                !query.CanAssign(movingUnitId!.Value, new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = unit.Id })) continue;
            candidates.Add(new(unit.Id, true), MilitaryMarker.Attack);
            clear.UnionWith(rules.GeographicFootprint(unit));
        }
        if (movingUnitId is int moving) clear.UnionWith(rules.GeographicFootprint(world.Units.Single(u => u.Id == moving)));
        Candidates = candidates.ToImmutableDictionary();
        ClearCells = clear.ToImmutableHashSet();
    }

    public bool Select(MilitaryTarget target)
    {
        if (EditingPolicies || !Candidates.ContainsKey(target)) return false;
        if (Selected == target) return true;
        Selected = target;
        WaypointPosition = null;
        UpdateRoute(Plan(target, null));
        return true;
    }

    public bool SetWaypoint(MapCell cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < Rules.Map.Width && cell.Y < Rules.Map.Height &&
        SetWaypointPosition(MapCoordinates.Center(cell));

    /// <summary>Show the existing intention without selecting a target for the new command.</summary>
    protected void PreviewExistingOrder(MilitaryTarget target, WorldPosition? waypoint)
    {
        var route = Plan(target, waypoint, MovingUnitId is int id ? View.World.Units.Single(u => u.Id == id).Order : null);
        Route = route.Cells; Routes = [route.Cells];
        WaypointPosition = route.Reachable ? waypoint : null;
        Revision++;
    }

    protected bool SetWaypointPosition(WorldPosition position)
    {
        if (EditingPolicies || Selected is not { } target) return false;
        var rules = Rules;
        var cell = MapCoordinates.Cell(position);
        if (cell.X < 0 || cell.Y < 0 || cell.X >= rules.Map.Width || cell.Y >= rules.Map.Height ||
            View.World.Settlements.Where(s => !s.IsDormant && !s.IsDestroyed && rules.TryPlace(s, out _)).Any(s => rules.GeographicFootprint(s).Contains(cell)) ||
            View.World.Units.Where(u => u.FormationId is not null).Any(u => rules.GeographicFootprint(u).Contains(cell)))
            return false;
        var route = Plan(target, position);
        if (!route.Reachable) return false;
        WaypointPosition = position;
        UpdateRoute(route);
        return true;
    }

    public bool BeginPolicies()
    {
        if (Selected is null || !Reachable || !CanProceed) return false;
        EditingPolicies = true; Error = null; return true;
    }
    public void ReturnToTargets() => EditingPolicies = false;
    public virtual void SetPolicies(UnitPolicies policies)
    {
        if (!EditingPolicies) return;
        Policies = policies;
        if (Selected is { } target) UpdateRoute(Plan(target, WaypointPosition));
    }

    protected void RefreshRoute()
    { if (Selected is { } target) UpdateRoute(Plan(target, WaypointPosition)); }

    private void UpdateRoute(RoutePreview route)
    {
        Route = route.Cells;
        Routes = route.UnitRoutes.IsDefaultOrEmpty ? [route.Cells] : route.UnitRoutes;
        Reachable = route.Reachable; Error = route.Error; Revision++;
    }

    public bool SetDepartureDelay(int days)
    {
        if (!IsDeployment || !EditingPolicies || days is < 0 or > 9) return false;
        DepartureDelayDays = days; return true;
    }

    public abstract ArrivalEstimate EstimateArrival();

    protected virtual MilitaryOrder OrderFor(MilitaryTarget target) => new(target.IsUnit ? MilitaryOrderKind.AttackUnit
        : Candidates[target] == MilitaryMarker.Enter ? MilitaryOrderKind.Return : MilitaryOrderKind.AttackSettlement,
        null, target.IsUnit ? null : target.Id) { UnitId = target.IsUnit ? target.Id : null };

    public abstract GameCommand CreateCommand(Guid commandId, long sequence, bool confirmAttack = false);
    protected MilitaryOrder ReadyOrder() => EditingPolicies && Selected is { } target && Reachable && CanProceed
        ? OrderFor(target) : throw new InvalidOperationException("Military command is not ready.");

    protected virtual RoutePreview Plan(MilitaryTarget target, WorldPosition? waypoint, MilitaryOrder? existingOrder = null) =>
        PlanRoute(target, waypoint, existingOrder);

    protected RoutePreview PlanUnit(UnitState unit, MilitaryTarget target, WorldPosition? waypoint, UnitPolicies policies) =>
        PlanRoute(target, waypoint, null, unit, policies);

    private RoutePreview PlanRoute(MilitaryTarget target, WorldPosition? waypoint, MilitaryOrder? existingOrder,
        UnitState? unit = null, UnitPolicies? unitPolicies = null)
    {
        const string unreachable = "无法到达该目标，请选择其他目标或中继点。";
        if (!CanProceed) return new([], unreachable);
        var rules = Rules;
        var world = View.World;
        int formation = unit?.FormationId ?? FormationId;
        int? naval = unit is null ? NavalFormationId : unit.NavalFormationId;
        uint soldiers = unit?.Soldiers ?? Soldiers;
        int? moving = unit?.Id ?? MovingUnitId;
        var policies = unitPolicies ?? Policies;
        var travel = rules.Travel(formation, naval, soldiers);
        if (waypoint is { } via && travel.Map.PassageCost(via, travel.Shape, travel.Mode, []) is null)
            return new([], unreachable);
        var occupied = MilitaryRouteQuery.Occupancy(world, rules, OwnerId, policies, movingUnit: moving);
        WorldPosition? start = unit?.Position ?? Start(travel, occupied);
        if (start is null) return new([], unreachable);
        var order = existingOrder ?? OrderFor(target);
        // Construction deploys outside the future building, just as the domain command requires.
        if (order.Construction is { } construction && travel.AreaAt(start.Value)
            .Overlaps(rules.FacilityArea(construction.Position, construction.FacilityTypeId))) return new([], unreachable);
        var source = unit ?? (moving is int unitId ? world.Units.Single(u => u.Id == unitId) :
            new UnitState(0, OwnerId, start.Value, soldiers));
        var plan = MilitaryRouteQuery.Plan(world, rules, source.WithMilitaryOrder(order, policies, waypoint) with
        {
            Position = start.Value, FormationId = formation, NavalFormationId = naval,
            SiegeEquipmentId = moving is null ? SiegeEquipmentId : source.SiegeEquipmentId,
            Condition = UnitCondition.Normal
        }, moving);
        if (plan.Status != NavigationStatus.Arrived)
            return new([], plan.Status switch
            {
                NavigationStatus.Planning => "路线规划尚未完成，请选择较近的中继点。",
                NavigationStatus.Waiting => "当前路线受阻，请选择其他目标或中继点。",
                _ => unreachable
            });
        var cells = ImmutableArray.CreateBuilder<MapCell>();
        foreach (var position in plan.Steps)
        {
            var cell = MapCoordinates.Cell(position);
            if (cells.Count == 0 || cells[^1] != cell) cells.Add(cell);
        }
        return new(cells.ToImmutable());
    }
}
