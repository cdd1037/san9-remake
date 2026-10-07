using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public enum UnitCommandKind { Attack, March }

public sealed class UnitCommandTargets : MilitaryTargets
{
    public UnitState Unit { get; }
    protected override bool CanProceed => true;
    protected override int FormationId => Unit.FormationId!.Value;
    protected override int? NavalFormationId => Unit.NavalFormationId;
    public override bool IsDeployment => false;
    public override MilitaryTarget Source => new(Unit.Id, true);
    protected override WorldPosition? Start(UnitTravel travel, IReadOnlyList<MapOccupancy> occupancy) => Unit.Position;

    public UnitCommandTargets(StrategySnapshot view, int unitId, UnitCommandKind kind)
        : this(view, SourceUnit(view, unitId, kind), kind) { }

    private UnitCommandTargets(StrategySnapshot view, UnitState unit, UnitCommandKind kind)
        : base(view, view.MilitaryRules!, unit.OwnerId, unit.CorpsId, null, unit.Id, kind)
    {
        Unit = unit; Policies = unit.Policies;
        var current = unit.Order;
        var target = current?.UnitId is int enemy ? new MilitaryTarget(enemy, true) :
            current?.SettlementId is int place ? new MilitaryTarget(place, false) : (MilitaryTarget?)null;
        if (target is { } selected && Candidates.ContainsKey(selected))
        {
            Select(selected);
            if (unit.Waypoint is { } via) SetWaypointPosition(via);
        }
        else if (target is { } existing && current is not null &&
            new MilitaryOrderQuery(view.World, Rules).CanAssign(unit.Id, current))
            PreviewExistingOrder(existing, unit.Waypoint);
    }

    private static UnitState SourceUnit(StrategySnapshot view, int unitId, UnitCommandKind kind)
    {
        string command = kind == UnitCommandKind.Attack ? "attack" : "march";
        if (!Enum.IsDefined(kind) || view.MilitaryRules is null || !MapCommandMenu.Unit(view, unitId).Any(e => e.Id == command && e.Enabled))
            throw new ArgumentException("Unit command is unavailable.", nameof(unitId));
        return view.World.Units.Single(u => u.Id == unitId);
    }

    private MilitaryArrivalForecast? _forecast;
    private (MilitaryOrder Order, UnitPolicies Policies, WorldPosition? Waypoint)? _request;
    private ArrivalEstimate? _arrival;
    public override ArrivalEstimate EstimateArrival()
    {
        if (Selected is not { } target) return new(ArrivalEstimateStatus.NotApplicable);
        var request = (OrderFor(target), Policies, WaypointPosition);
        if (_request != request)
        {
            _arrival = (_forecast ??= new(View.World, Rules)).EstimateOrder(Unit.Id, request.Item1, Policies, WaypointPosition);
            _request = request;
        }
        return _arrival!;
    }

    public override OrderUnitCommand CreateCommand(Guid commandId, long sequence, bool confirmAttack = false) =>
        new(commandId, OwnerId, sequence, Unit.Id, ReadyOrder())
        { Policies = Policies, Waypoint = WaypointPosition, ConfirmAttack = confirmAttack };
}
