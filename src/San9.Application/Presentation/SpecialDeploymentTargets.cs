using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public sealed class SpecialDeploymentTargets : MilitaryTargets
{
    public SpecialDeploymentDraft Draft { get; }
    public bool IsConstruction => Draft.Kind == SpecialDeploymentKind.Construction;
    public int? PreviewFacilityId => IsConstruction ? Draft.FacilityId : null;
    private readonly Dictionary<MapCell, MilitaryTarget> _sites;
    public SpecialDeploymentTargets(SpecialDeploymentDraft draft)
        : base(draft.View, draft.Rules, draft.Force.Id, draft.Home.Id, null, locations: Locations(draft),
            showInfluence: draft.Kind != SpecialDeploymentKind.Construction)
    {
        Draft = draft;
        _sites = Candidates.Keys.Where(t => t.Position is not null).ToDictionary(t => MapCoordinates.Cell(t.Position!.Value));
    }
    private static Dictionary<MilitaryTarget, MapOccupancy> Locations(SpecialDeploymentDraft draft) =>
        draft.Kind == SpecialDeploymentKind.Construction
            ? draft.Sites(draft.FacilityId!.Value).ToDictionary(s => new MilitaryTarget(s.RegionId, false, s.Position),
                s => draft.Rules.FacilityArea(s.Position, draft.FacilityId.Value))
            : draft.View.World.Settlements.Where(s => s.Id != draft.Home.Id && s.OwnerId == draft.Force.Id &&
                !s.IsDestroyed && !s.IsDormant && s.Durability > 0 && s.CanHostPersons && s.Facility?.BuilderUnitId is null &&
                draft.Rules.TryPlace(s, out _)).ToDictionary(s => new MilitaryTarget(s.Id, false), draft.Rules.SettlementArea);
    public MilitaryTarget? At(MapCell cell) => _sites.TryGetValue(cell, out var target) ? target : null;
    protected override bool CanProceed => Draft.CanProceed;
    protected override uint Soldiers => Draft.Soldiers;
    protected override int FormationId => Draft.FormationId;
    protected override int? NavalFormationId => Draft.NavalFormationId;
    public override bool IsDeployment => true;
    public override MilitaryTarget Source => new(Draft.Home.Id, false);
    protected override WorldPosition? Start(UnitTravel travel, IReadOnlyList<MapOccupancy> occupancy) =>
        MilitaryRouteQuery.DeploymentPosition(travel, Rules.SettlementArea(Draft.Home), occupancy);
    protected override MilitaryOrder OrderFor(MilitaryTarget target) => IsConstruction
        ? new(MilitaryOrderKind.Construct, null, null) { Construction = new(target.Id, Draft.FacilityId!.Value, target.Position!.Value, Draft.CommanderId!.Value) }
        : new(MilitaryOrderKind.Transport, null, target.Id);
    private MilitaryArrivalForecast? _forecast;
    private DeploymentArrivalRequest? _request;
    private ArrivalEstimate? _arrival;
    public override ArrivalEstimate EstimateArrival()
    {
        if (Selected is not { } target || !CanProceed) return new(ArrivalEstimateStatus.NotApplicable);
        var request = new DeploymentArrivalRequest(Draft.Home.Id, FormationId, NavalFormationId, OrderFor(target), Policies, 0, WaypointPosition, Draft.Soldiers);
        if (_request != request)
        {
            _arrival = (_forecast ??= new(View.World, Rules, View.RegionCityIds)).EstimateDeployment(request);
            _request = request;
        }
        return _arrival!;
    }
    public override GameCommand CreateCommand(Guid commandId, long sequence, bool confirmAttack = false)
    {
        var order = ReadyOrder();
        return order.Construction is { } plan
            ? new ConstructFacilityCommand(commandId, OwnerId, sequence, Draft.Home.Id, Draft.Members, Draft.Soldiers,
                plan.RegionId, plan.FacilityTypeId, plan.Position) { Policies = Policies, Waypoint = WaypointPosition }
            : new TransportUnitCommand(commandId, OwnerId, sequence, Draft.Home.Id, Draft.Members, Draft.Soldiers, order.SettlementId!.Value)
                { Policies = Policies, Waypoint = WaypointPosition };
    }
}
