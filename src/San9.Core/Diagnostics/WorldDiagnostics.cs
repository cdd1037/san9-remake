using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Diagnostics;

public sealed record TaskStepDiagnostic(Guid Id, int ResolutionStep, SearchEncounter? SearchEncounter,
    CompensationOffer? AttackOffer, CompensationOffer? PrisonerOffer);
public sealed record WorldInteractionDiagnostic(GameClock? DailyAdvanceStarted, DuelCombatDecision? PendingDuel,
    EventSession? PendingEvent, DailyTaskDecision? PendingDailyTask, TacticStudyDecision? PendingTacticStudy,
    SnipingCombatDecision? PendingSniping, CapturedRulerDecision? PendingCapturedRuler,
    ExecutionSuccessionDecision? PendingExecutionSuccession, ImmutableArray<TaskStepDiagnostic> Tasks);
public sealed record WorldDiagnosticSnapshot(GameClock Clock, SimulationState Simulation,
    ImmutableArray<ForceState> Forces, ImmutableArray<SettlementState> Settlements,
    ImmutableArray<OfficerState> Officers, ImmutableArray<UnitState> Units, ImmutableArray<CorpsState> Corps,
    ImmutableArray<TreasureState> Treasures, ImmutableArray<ForceRelation> Relations, WorldInteractionDiagnostic Interaction);

public static class WorldDiagnostics
{
    /// <summary>Semantic state excludes the commit revision and includes unsaved decision continuations.</summary>
    public static WorldDiagnosticSnapshot Capture(WorldSnapshot world) => new(world.Clock, NormalizeSimulation(world.Simulation),
        NormalizeForces(world.Forces), world.Settlements.OrderBy(s => s.Id).ToImmutableArray(), NormalizeOfficers(world.Officers),
        world.Units.ToImmutableArray(), world.Corps.OrderBy(c => c.Id).ToImmutableArray(),
        world.Treasures.OrderBy(t => t.Id).ToImmutableArray(), NormalizeRelations(world.Relations), CaptureInteraction(world.Simulation));

    public static JsonElement Serialize(WorldSnapshot world) =>
        JsonSerializer.SerializeToElement(Capture(world), WorldDiagnosticJsonContext.Default.WorldDiagnosticSnapshot);

    public static string ToDiagnosticJson(this WorldSnapshot world) => DiagnosticJson.Text(Serialize(world));

    public static bool AreEqual(WorldSnapshot expected, WorldSnapshot actual) => CompareParts(expected, actual, false, out _);

    public static StateDifference? Compare(WorldSnapshot expected, WorldSnapshot actual)
    {
        CompareParts(expected, actual, true, out var difference);
        return difference;
    }

    private static bool CompareParts(WorldSnapshot expected, WorldSnapshot actual, bool describeDifference, out StateDifference? difference)
    {
        difference = null;
        if (ReferenceEquals(expected, actual)) return true;
        var comparison = new DiagnosticComparison(WorldDiagnosticJsonContext.Default, describeDifference);
        bool equal = comparison.Check(expected.Clock, actual.Clock, "$.clock") &&
            (ReferenceEquals(expected.Corps, actual.Corps) || comparison.Check(
                expected.Corps.OrderBy(c => c.Id).ToImmutableArray(), actual.Corps.OrderBy(c => c.Id).ToImmutableArray(), "$.corps")) &&
            (ReferenceEquals(expected.Forces, actual.Forces) || comparison.Check(
                NormalizeForces(expected.Forces), NormalizeForces(actual.Forces), "$.forces")) &&
            (ReferenceEquals(expected.Simulation, actual.Simulation) || comparison.Check(
                CaptureInteraction(expected.Simulation), CaptureInteraction(actual.Simulation), "$.interaction")) &&
            (ReferenceEquals(expected.Officers, actual.Officers) || comparison.Check(
                NormalizeOfficers(expected.Officers), NormalizeOfficers(actual.Officers), "$.officers")) &&
            (ReferenceEquals(expected.Relations, actual.Relations) || comparison.Check(
                NormalizeRelations(expected.Relations), NormalizeRelations(actual.Relations), "$.relations")) &&
            (ReferenceEquals(expected.Settlements, actual.Settlements) || comparison.Check(
                expected.Settlements.OrderBy(s => s.Id).ToImmutableArray(), actual.Settlements.OrderBy(s => s.Id).ToImmutableArray(), "$.settlements")) &&
            (ReferenceEquals(expected.Simulation, actual.Simulation) || comparison.Check(
                NormalizeSimulation(expected.Simulation), NormalizeSimulation(actual.Simulation), "$.simulation")) &&
            (ReferenceEquals(expected.Treasures, actual.Treasures) || comparison.Check(
                expected.Treasures.OrderBy(t => t.Id).ToImmutableArray(), actual.Treasures.OrderBy(t => t.Id).ToImmutableArray(), "$.treasures")) &&
            (ReferenceEquals(expected.Units, actual.Units) || comparison.Check(
                expected.Units.ToImmutableArray(), actual.Units.ToImmutableArray(), "$.units"));
        difference = comparison.Difference;
        return equal;
    }

    private static SimulationState NormalizeSimulation(SimulationState simulation) => simulation with
    {
        Tasks = OrderedTasks(simulation),
        RandomStreams = simulation.RandomStreams.OrderBy(s => s.Name, StringComparer.Ordinal).ToImmutableArray()
    };

    private static ImmutableArray<PersistentTask> OrderedTasks(SimulationState simulation) => simulation.Tasks
        .OrderBy(t => t.Sequence).ThenBy(t => t.OriginSettlementId).ThenBy(t => t.OfficerId).ThenBy(t => t.Id).ToImmutableArray();

    private static ImmutableArray<ForceState> NormalizeForces(IReadOnlyList<ForceState> forces) => forces.OrderBy(f => f.Id)
        .Select(f => f with { AdditionalFormationIds = f.AdditionalFormationIds.Order().ToImmutableArray(),
            ProvidedTactics = f.ProvidedTactics.Order().ToImmutableArray() }).ToImmutableArray();

    private static ImmutableArray<OfficerState> NormalizeOfficers(IReadOnlyList<OfficerState> officers) => officers.OrderBy(o => o.Id).ToImmutableArray();

    private static ImmutableArray<ForceRelation> NormalizeRelations(IReadOnlyList<ForceRelation> relations) =>
        relations.OrderBy(r => r.FirstId).ThenBy(r => r.SecondId).ToImmutableArray();

    private static WorldInteractionDiagnostic CaptureInteraction(SimulationState simulation) => new(simulation.DailyAdvanceStarted,
        simulation.PendingDuel, simulation.PendingEvent, simulation.PendingDailyTask, simulation.PendingTacticStudy, simulation.PendingSniping,
        simulation.PendingCapturedRuler, simulation.PendingExecutionSuccession, OrderedTasks(simulation)
            .Where(t => t.ResolutionStep != 0 || t.SearchEncounter is not null || t.AttackRequest?.Offer is not null || t.PrisonerReturn?.Offer is not null)
            .Select(t => new TaskStepDiagnostic(t.Id, t.ResolutionStep, t.SearchEncounter, t.AttackRequest?.Offer, t.PrisonerReturn?.Offer)).ToImmutableArray());

}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WorldDiagnosticSnapshot))]
[JsonSerializable(typeof(DomainEvent))]
internal partial class WorldDiagnosticJsonContext : JsonSerializerContext;
