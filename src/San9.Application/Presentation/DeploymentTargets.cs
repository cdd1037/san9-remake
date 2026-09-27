using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public readonly record struct DeploymentTarget(int Id, bool IsUnit);
public enum DeploymentMarker { Enter, Attack }

/// <summary>Revision-bound map presentation. Selecting a candidate does not issue a deployment command.</summary>
public sealed class DeploymentTargets
{
    public DeploymentDraft Draft { get; }
    public ImmutableDictionary<DeploymentTarget, DeploymentMarker> Candidates { get; }
    public ImmutableHashSet<MapCell> ClearCells { get; }
    public ImmutableHashSet<MapCell> InfluenceCells { get; }
    public DeploymentTarget? Selected { get; private set; }
    public MapCell? Waypoint { get; private set; }
    public ImmutableArray<MapCell> Route { get; private set; } = [];
    public bool Reachable { get; private set; }
    public long Revision { get; private set; }
    public UnitPolicies Policies { get; private set; } = new();
    public int DepartureDelayDays { get; private set; }
    public bool EditingPolicies { get; private set; }
    public string? Error { get; private set; }

    public DeploymentTargets(DeploymentDraft draft)
    {
        Draft = draft;
        var world = draft.View.World;
        var candidates = new Dictionary<DeploymentTarget, DeploymentMarker>();
        var clear = new HashSet<MapCell>();
        int? Affiliation(int? owner, int? corps) => corps ?? world.Corps.FirstOrDefault(c => c.OwnerId == owner && c.IsRoot)?.Id;
        bool SameAffiliation(int? owner, int? corps) => owner == draft.Home.OwnerId &&
            Affiliation(owner, corps) == Affiliation(draft.Home.OwnerId, draft.Home.CorpsId);
        foreach (var place in world.Settlements.Where(s => s.Id != draft.Home.Id && !s.IsDestroyed && !s.IsDormant && draft.Rules.TryPlace(s, out _)))
        {
            bool sameForce = SameAffiliation(place.OwnerId, place.CorpsId);
            candidates.Add(new(place.Id, false), sameForce && place.CanHostPersons ? DeploymentMarker.Enter : DeploymentMarker.Attack);
            clear.UnionWith(draft.Rules.GeographicFootprint(place));
        }
        foreach (var unit in world.Units.Where(u => !u.IsEmbedded && !u.IsPersonnel && u.Soldiers > 0 && u.FormationId is not null &&
            !SameAffiliation(u.OwnerId, u.CorpsId)))
        {
            candidates.Add(new(unit.Id, true), DeploymentMarker.Attack);
            clear.UnionWith(draft.Rules.GeographicFootprint(unit));
        }
        Candidates = candidates.ToImmutableDictionary();
        ClearCells = clear.ToImmutableHashSet();
        InfluenceCells = draft.Rules.CityInfluenceCells(draft.Home, world.Settlements).ToImmutableHashSet();
    }

    public bool Select(DeploymentTarget target)
    {
        if (EditingPolicies || !Candidates.ContainsKey(target)) return false;
        if (Selected == target) return true;
        Selected = target;
        Waypoint = null;
        var route = Plan(target, null);
        Reachable = route is not null;
        Route = route ?? [];
        Error = Reachable ? null : "无法到达该目标，请选择其他目标或中继点。";
        Revision++;
        return true;
    }

    public bool SetWaypoint(MapCell cell)
    {
        if (EditingPolicies || Selected is not { } target) return false;
        var rules = Draft.Rules;
        if (cell.X < 0 || cell.Y < 0 || cell.X >= rules.Map.Width || cell.Y >= rules.Map.Height ||
            Draft.View.World.Settlements.Where(s => !s.IsDestroyed && rules.TryPlace(s, out _)).Any(s => rules.GeographicFootprint(s).Contains(cell)) ||
            Draft.View.World.Units.Where(u => u.FormationId is not null).Any(u => rules.GeographicFootprint(u).Contains(cell)))
            return false;
        var route = Plan(target, cell);
        if (route is null) return false;
        Waypoint = cell; Route = route.Value; Reachable = true; Error = null; Revision++;
        return true;
    }

    public bool BeginPolicies()
    {
        if (Selected is null || !Reachable || !Draft.CanProceed) return false;
        EditingPolicies = true; Error = null; return true;
    }
    public void ReturnToTargets() => EditingPolicies = false;
    public void SetPolicies(UnitPolicies policies)
    { if (EditingPolicies) Policies = policies; }

    public bool SetDepartureDelay(int days)
    {
        if (!EditingPolicies || days is < 0 or > 9) return false;
        DepartureDelayDays = days; return true;
    }

    public DeployUnitCommand CreateCommand(Guid commandId, long sequence, bool confirmAttack = false)
    {
        if (!EditingPolicies || Selected is not { } target || !Reachable || !Draft.CanProceed)
            throw new InvalidOperationException("Deployment is not ready.");
        var kind = target.IsUnit ? MilitaryOrderKind.AttackUnit : Candidates[target] == DeploymentMarker.Enter
            ? MilitaryOrderKind.Return : MilitaryOrderKind.AttackSettlement;
        return new(commandId, Draft.Force.Id, sequence, Draft.Home.Id, Draft.Members, Draft.Soldiers,
            Draft.FormationId!.Value, new(kind, null, target.IsUnit ? null : target.Id) { UnitId = target.IsUnit ? target.Id : null })
        {
            NavalFormationId = Draft.NavalFormationId, Policies = Policies, ConfirmAttack = confirmAttack,
            DepartureDelayDays = DepartureDelayDays, Waypoint = Waypoint is { } via ? MapCoordinates.Center(via) : null,
            Tactics = Draft.Tactics, Ranks = Draft.Positions.ToImmutableDictionary(p => p.Key, p => (BattleRank)Math.Min(2, p.Value.Row))
        };
    }

    private ImmutableArray<MapCell>? Plan(DeploymentTarget target, MapCell? waypoint)
    {
        if (!Draft.CanProceed) return null;
        var rules = Draft.Rules;
        var world = Draft.View.World;
        var travel = rules.Travel(Draft.FormationId!.Value, Draft.NavalFormationId);
        var occupied = MilitaryRouteQuery.Occupancy(world, rules, Draft.Force.Id, Policies);
        WorldPosition? start = MilitaryRouteQuery.DeploymentPosition(travel, rules.Place(Draft.Home), occupied);
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
            destination = new(place.Position, place.Footprint);
        }
        var steps = new List<WorldPosition> { start.Value };
        if (waypoint is { } via)
        {
            var first = travel.Map.FindRoute(start.Value, MapCoordinates.Center(via), travel.Shape, travel.Mode, occupied);
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
