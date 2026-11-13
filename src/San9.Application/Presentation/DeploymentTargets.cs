using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public sealed class DeploymentTargets(DeploymentDraft draft)
    : MilitaryTargets(draft.View, draft.Rules, draft.Force.Id, draft.Home.Id, null)
{
    public DeploymentDraft Draft { get; } = draft;
    protected override bool CanProceed => Draft.CanProceed;
    protected override uint Soldiers => Draft.Soldiers;
    protected override int FormationId => Draft.FormationId!.Value;
    protected override int? NavalFormationId => Draft.NavalFormationId;
    protected override int? SiegeEquipmentId => Draft.SiegeEquipmentId;
    public override bool IsDeployment => true;
    public override MilitaryTarget Source => new(Draft.Home.Id, false);
    protected override WorldPosition? Start(UnitTravel travel, IReadOnlyList<MapOccupancy> occupancy) =>
        MilitaryRouteQuery.DeploymentPosition(travel, Rules.SettlementArea(Draft.Home), occupancy);

    private MilitaryArrivalForecast? _forecast;
    private DeploymentArrivalRequest? _request;
    private ArrivalEstimate? _arrival;
    public override ArrivalEstimate EstimateArrival()
    {
        if (Selected is not { } target || !CanProceed) return new(ArrivalEstimateStatus.NotApplicable);
        var request = new DeploymentArrivalRequest(Draft.Home.Id, FormationId, NavalFormationId,
            OrderFor(target), Policies, DepartureDelayDays, WaypointPosition, Draft.Soldiers)
            { SiegeEquipmentId = Draft.SiegeEquipmentId };
        if (_request != request)
        {
            _arrival = (_forecast ??= new(View.World, Rules)).EstimateDeployment(request);
            _request = request;
        }
        return _arrival!;
    }

    public override DeployUnitCommand CreateCommand(Guid commandId, long sequence, bool confirmAttack = false) =>
        new(commandId, OwnerId, sequence, Draft.Home.Id, Draft.Members, Draft.Soldiers, FormationId, ReadyOrder())
        {
            NavalFormationId = NavalFormationId, Policies = Policies, ConfirmAttack = confirmAttack,
            DepartureDelayDays = DepartureDelayDays, Waypoint = WaypointPosition,
            Tactics = Draft.Tactics, Detachments = Draft.Detachments,
            NavalTrainingId = NavalFormationId is not null || Draft.FormationId is int land && Draft.Rules.NavalFormationForDeployment(land) is not null
                ? Draft.NavalTrainingId : null,
            SiegeEquipmentId = Draft.SiegeEquipmentId,
            Positions = Draft.Positions.ToImmutableDictionary(p => p.Key, p => new FormationPosition(p.Value.Column, (BattleRank)p.Value.Row))
        };
}
