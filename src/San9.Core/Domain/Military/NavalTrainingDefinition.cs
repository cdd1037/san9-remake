using System.Collections.Immutable;

namespace San9.Core.Domain;

public sealed record NavalTrainingDefinition(int Id, string Name, string? ResearchProjectId,
    int DeploymentCost, ImmutableArray<int> AllowedTactics);
