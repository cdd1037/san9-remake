namespace San9.Core.Domain;

public sealed record SiegeEquipmentDefinition(int Id, string Name, string ResearchProjectId,
    int FormationId, int DeploymentCost);
