using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed partial class SimulationEngine
{
    /// <summary>Configures an inactive officer from the player-facing world editor as one atomic change.</summary>
    public IReadOnlyList<DomainEvent> ConfigureOfficerAppearance(int officerId, ManualOfficerAppearance plan)
    {
        if (_world.IsExecuting || _world.Simulation.Boundary != SimulationBoundary.Stable)
            throw new InvalidOperationException("Appearance configuration requires a stable boundary.");
        ArgumentNullException.ThrowIfNull(plan);
        var snapshot = Snapshot;
        var person = snapshot.Officers.SingleOrDefault(p => p.Id == officerId);
        var place = snapshot.Settlements.SingleOrDefault(p => p.Id == plan.SettlementId);
        if (!plan.IsValid || plan.Year < snapshot.Clock.Year || person is null || person.IsDormant ||
            person.Status is not (OfficerStatus.Dead or OfficerStatus.NotAppeared or OfficerStatus.Undiscovered) ||
            place is null || place.IsDormant || place.IsDestroyed || !place.CanHostPersons || place.Durability <= 0 ||
            plan.ForceId is int forceId && (place.OwnerId != forceId || !snapshot.Forces.Any(f => f.Id == forceId && !f.IsDormant && !f.IsEliminated && f.LeaderId is not null)))
            throw new ArgumentException("Invalid officer appearance configuration.");
        _world.EnterExecution();
        try
        {
            var world = BeginMutation();
            var next = world.Officers[officerId] with { Status = OfficerStatus.NotAppeared, OwnerId = null, SettlementId = place.Id,
                RegionId = null, UnitId = null, OfficeId = null, IsCommander = false, Acted = false,
                BirthYear = plan.Year - plan.Age, AppearanceYear = plan.Year, ManualAppearance = plan };
            world.Replace(next);
            List<DomainEvent> events = [];
            if (plan.Year == world.Clock.Year) OfficerAppearance.AppearManually(world, next, events);
            world.MaintainDerivedState(events);
            WorldInvariants.Validate(world);
            _world.Commit(world);
            return events;
        }
        finally { _world.ExitExecution(); }
    }
}
