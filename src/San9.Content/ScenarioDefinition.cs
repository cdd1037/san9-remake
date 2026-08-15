using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Content;

public sealed record ScenarioDefinition(
    string ScenarioId,
    string NameKey,
    GameClock StartDate,
    IReadOnlyList<Officer> Officers,
    IReadOnlyList<Force> Forces,
    IReadOnlyList<Settlement> Settlements,
    IReadOnlyList<Formation> Formations,
    IReadOnlyList<Tactic> Tactics,
    IReadOnlyList<Facility> Facilities);

public sealed record ContentPackage(
    ContentManifest Manifest,
    ScenarioDefinition Scenario);
