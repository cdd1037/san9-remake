using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public readonly record struct MilitaryTarget(int Id, bool IsUnit);
public enum MilitaryMarker { Enter, Attack }

/// <summary>Revision-bound map intentions. Selecting a candidate does not issue a military command.</summary>
public abstract class MilitaryTargets
{
    public StrategySnapshot View { get; }
    public MilitaryRulebook Rules { get; }
    protected int OwnerId { get; }
    protected int? MovingUnitId { get; }
    protected abstract bool CanProceed { get; }
    protected abstract int FormationId { get; }
    protected abstract int? NavalFormationId { get; }
    protected abstract WorldPosition? Start(UnitTravel travel, IReadOnlyList<MapOccupancy> occupancy);
    public abstract bool IsDeployment { get; }
    public abstract MilitaryTarget Source { get; }
    public ImmutableDictionary<MilitaryTarget, MilitaryMarker> Candidates { get; }
    public ImmutableHashSet<MapCell> ClearCells { get; }
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

    protected MilitaryTargets(StrategySnapshot view, MilitaryRulebook rules, int ownerId, int? corpsId,
        int? homeId, int? movingUnitId, UnitCommandKind? commandKind = null)
    {
        View = view; Rules = rules; OwnerId = ownerId; MovingUnitId = movingUnitId;
        var world = view.World;
        var query = movingUnitId is null ? null : new MilitaryOrderQuery(world, rules);
        var candidates = new Dictionary<MilitaryTarget, MilitaryMarker>();
        var clear = new HashSet<MapCell>();
        int? Affiliation(int? owner, int? corps) => corps ?? world.Corps.FirstOrDefault(c => c.OwnerId == owner && c.IsRoot)?.Id;
        bool SameAffiliation(int? owner, int? corps) => owner == ownerId &&
            Affiliation(owner, corps) == Affiliation(ownerId, corpsId);
        foreach (var place in world.Settlements.Where(s => s.Id != homeId && !s.IsDestroyed && !s.IsDormant && rules.TryPlace(s, out _)))
        {
            bool sameForce = SameAffiliation(place.OwnerId, place.CorpsId);
            var marker = sameForce && place.CanHostPersons ? MilitaryMarker.Enter : MilitaryMarker.Attack;
            if (commandKind is { } kind)
            {
                marker = kind == UnitCommandKind.March ? MilitaryMarker.Enter : MilitaryMarker.Attack;
                var order = new MilitaryOrder(kind == UnitCommandKind.March ? MilitaryOrderKind.Return : MilitaryOrderKind.AttackSettlement, null, place.Id);
                if (!query!.CanAssign(movingUnitId!.Value, order)) continue;
            }
            candidates.Add(new(place.Id, false), marker);
            clear.UnionWith(rules.GeographicFootprint(place));
        }
        foreach (var unit in world.Units.Where(u => !u.IsEmbedded && !u.IsPersonnel && u.Soldiers > 0 && u.FormationId is not null &&
            u.Id != movingUnitId && !SameAffiliation(u.OwnerId, u.CorpsId)))
        {
            if (commandKind == UnitCommandKind.March || query is not null &&
                !query.CanAssign(movingUnitId!.Value, new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = unit.Id })) continue;
            candidates.Add(new(unit.Id, true), MilitaryMarker.Attack);
            clear.UnionWith(rules.GeographicFootprint(unit));
        }
        if (movingUnitId is int moving) clear.UnionWith(rules.GeographicFootprint(world.Units.Single(u => u.Id == moving)));
        Candidates = candidates.ToImmutableDictionary();
        ClearCells = clear.ToImmutableHashSet();
        InfluenceCells = homeId is int home ? rules.CityInfluenceCells(world.Settlements.Single(s => s.Id == home), world.Settlements).ToImmutableHashSet() : [];
    }

    public bool Select(MilitaryTarget target)
    {
        if (EditingPolicies || !Candidates.ContainsKey(target)) return false;
        if (Selected == target) return true;
        Selected = target;
        WaypointPosition = null;
        var route = Plan(target, null);
        Reachable = route is not null;
        Route = route ?? [];
        Error = Reachable ? null : "无法到达该目标，请选择其他目标或中继点。";
        Revision++;
        return true;
    }

    public bool SetWaypoint(MapCell cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < Rules.Map.Width && cell.Y < Rules.Map.Height &&
        SetWaypointPosition(MapCoordinates.Center(cell));

    /// <summary>Show the existing intention without selecting a target for the new command.</summary>
    protected void PreviewExistingOrder(MilitaryTarget target, WorldPosition? waypoint)
    {
        var route = Plan(target, waypoint);
        Route = route ?? [];
        WaypointPosition = route is not null ? waypoint : null;
        Revision++;
    }

    protected bool SetWaypointPosition(WorldPosition position)
    {
        if (EditingPolicies || Selected is not { } target) return false;
        var rules = Rules;
        var cell = MapCoordinates.Cell(position);
        if (cell.X < 0 || cell.Y < 0 || cell.X >= rules.Map.Width || cell.Y >= rules.Map.Height ||
            View.World.Settlements.Where(s => !s.IsDestroyed && rules.TryPlace(s, out _)).Any(s => rules.GeographicFootprint(s).Contains(cell)) ||
            View.World.Units.Where(u => u.FormationId is not null).Any(u => rules.GeographicFootprint(u).Contains(cell)))
            return false;
        var route = Plan(target, position);
        if (route is null) return false;
        WaypointPosition = position; Route = route.Value; Reachable = true; Error = null; Revision++;
        return true;
    }

    public bool BeginPolicies()
    {
        if (Selected is null || !Reachable || !CanProceed) return false;
        EditingPolicies = true; Error = null; return true;
    }
    public void ReturnToTargets() => EditingPolicies = false;
    public void SetPolicies(UnitPolicies policies)
    { if (EditingPolicies) Policies = policies; }

    public bool SetDepartureDelay(int days)
    {
        if (!IsDeployment || !EditingPolicies || days is < 0 or > 9) return false;
        DepartureDelayDays = days; return true;
    }

    public abstract ArrivalEstimate EstimateArrival();

    protected MilitaryOrder OrderFor(MilitaryTarget target) => new(target.IsUnit ? MilitaryOrderKind.AttackUnit
        : Candidates[target] == MilitaryMarker.Enter ? MilitaryOrderKind.Return : MilitaryOrderKind.AttackSettlement,
        null, target.IsUnit ? null : target.Id) { UnitId = target.IsUnit ? target.Id : null };

    public abstract GameCommand CreateCommand(Guid commandId, long sequence, bool confirmAttack = false);
    protected MilitaryOrder ReadyOrder() => EditingPolicies && Selected is { } target && Reachable && CanProceed
        ? OrderFor(target) : throw new InvalidOperationException("Military command is not ready.");

    private ImmutableArray<MapCell>? Plan(MilitaryTarget target, WorldPosition? waypoint)
    {
        if (!CanProceed) return null;
        var rules = Rules;
        var world = View.World;
        var travel = rules.Travel(FormationId, NavalFormationId);
        var occupied = MilitaryRouteQuery.Occupancy(world, rules, OwnerId, Policies, movingUnit: MovingUnitId);
        WorldPosition? start = Start(travel, occupied);
        if (start is null) return null;
        MapOccupancy destination;
        if (target.IsUnit)
        {
            var unit = world.Units.Single(u => u.Id == target.Id);
            destination = new(unit.Position, rules.Formation(unit).Footprint);
        }
        else
        {
            var place = rules.Place(world.Settlements.Single(s => s.Id == target.Id));
            destination = rules.BuildingArea(place);
        }
        var steps = new List<WorldPosition> { start.Value };
        if (waypoint is { } via)
        {
            var first = travel.Map.FindRoute(start.Value, via, travel.Shape, travel.Mode, occupied);
            if (!first.Reachable) return null;
            steps.AddRange(first.Steps);
        }
        var last = travel.Map.FindRouteIntoRange(steps[^1], destination, travel.Shape, travel.Mode, occupied);
        if (!last.Reachable) return null;
        steps.AddRange(last.Steps);
        var cells = ImmutableArray.CreateBuilder<MapCell>();
        foreach (var position in steps)
        {
            var cell = MapCoordinates.Cell(position);
            if (cells.Count == 0 || cells[^1] != cell) cells.Add(cell);
        }
        return cells.ToImmutable();
    }
}
