using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum CapturedRulerAction { Release, Execute }
public sealed record ChooseCapturedRulerDispositionCommand(Guid CommandId, int IssuerId, long Sequence,
    int RulerId, [property: JsonRequired] CapturedRulerAction Action) : GameCommand(CommandId, IssuerId, Sequence);
public sealed record CapturedRulerDecision(GameClock XunStarted, int RulerId, int CaptorId);
public sealed record CapturedRulerPrompt(int RulerId, int CaptorId, int OriginalForceId)
{
    public ImmutableArray<CapturedRulerAction> LegalActions { get; init; } =
        [CapturedRulerAction.Release, CapturedRulerAction.Execute];
    public int CaptorBaseTrust { get; init; }
    public OfficerAbilities RulerAbilities { get; init; }
    public int RelationValue { get; init; }
    public bool IsAllianceTarget { get; init; }
}
public sealed record CapturedRulerDispositionRequestedEvent(GameClock Clock, int RulerId, int CaptorId) : DomainEvent;

internal static class CapturedRulerDisposition
{
    internal static int? Captor(WorldMutation world, OfficerState person)
    {
        if (person.Status != OfficerStatus.Captive || person.OwnerId is not int owner ||
            world.Forces[owner].IsEliminated || world.Forces[owner].LeaderId != person.Id) return null;
        int? captor = person.UnitId is int unit ? world.Units[unit].OwnerId :
            person.SettlementId is int place ? world.Settlements[place].OwnerId : null;
        return captor is int id && !world.Forces[id].IsEliminated && PrisonerExecution.DetainedBy(world, person, id) ? id : null;
    }

    internal static void Apply(WorldMutation world, List<DomainEvent> events,
        GameClock xunStarted, int? afterRuler = null)
    {
        foreach (int id in world.Officers.Keys.Where(id => afterRuler is null || id > afterRuler).Order().ToArray())
        {
            var ruler = world.Officers[id];
            if (Captor(world, ruler) is not int captor) continue;
            world.SetSimulation(world.Simulation with { Boundary = SimulationBoundary.AwaitingDecision,
                PendingCapturedRuler = new(xunStarted, id, captor) });
            events.Add(new CapturedRulerDispositionRequestedEvent(world.Clock, id, captor));
            return;
        }
    }

    internal static void Resolve(WorldMutation world, SimulationRules rules, List<DomainEvent> events,
        CapturedRulerDecision pending, CapturedRulerAction action)
    {
        if (action == CapturedRulerAction.Execute)
        {
            if (PrisonerExecution.Start(world, pending.CaptorId, pending.RulerId,
                rules, events, pending.XunStarted) != CommandRejection.None)
                throw new InvalidOperationException("Captured ruler is no longer detained by the executing force.");
            return;
        }
        var ruler = world.Officers[pending.RulerId];
        WorldPosition? position = ruler.UnitId is int unit ? world.Units[unit].Position :
            ruler.SettlementId is int detention ? world.Settlements[detention].Facility?.Position : null;
        int origin = ruler.SettlementId ?? rules.Personnel.Places.Values
            .OrderBy(p => MapCoordinates.Distance(position!.Value, p.Position)).ThenBy(p => p.Id).First().Id;
        // The date has advanced: the shared release starts its countdown from the current day.
        long sequence = world.Simulation.NextSequence;
        var rejection = PrisonerRelease.Depart(world, ruler.Id, origin, sequence, PrisonerDepartureKind.Released,
            pending.CaptorId, rules.Personnel, rules.Economy, events, position);
        if (rejection != CommandRejection.None) throw new InvalidOperationException("Captured ruler has no return destination.");
        world.SetSimulation(world.Simulation with { NextSequence = checked(sequence + 1) });
    }

    internal static bool ValidContinuation(WorldMutation world, GameClock start) => start.IsXunBoundary &&
        start.DayNumber >= world.Simulation.Started.DayNumber && start.DayNumber < world.Clock.DayNumber &&
        world.Clock.DayNumber <= start.Advance().DayNumber && world.Simulation.ActiveControl == world.Simulation.Controls.Length;

    internal static void ValidatePending(WorldMutation world)
    {
        if (world.Simulation.PendingCapturedRuler is not { } pending) return;
        if (!ValidContinuation(world, pending.XunStarted) || !world.Officers.TryGetValue(pending.RulerId, out var ruler) ||
            Captor(world, ruler) != pending.CaptorId ||
            !world.Simulation.Controls.Any(c => c.ForceId == pending.CaptorId && !world.Forces[c.ForceId].IsEliminated))
            throw new ArgumentException("Invalid pending captured ruler disposition.");
    }
}
