using System.Collections.Immutable;

namespace San9.Core.Domain;

public sealed record ResearchProjectDefinition(string Id, int Cost, int Days, ImmutableArray<string> Prerequisites)
{
    public string Name { get; init; } = "";

    internal static ImmutableDictionary<string, ResearchProjectDefinition> ValidateCatalog(ImmutableArray<ResearchProjectDefinition> projects)
    {
        if (projects.IsDefault || projects.Any(p => p is null || string.IsNullOrWhiteSpace(p.Id) || p.Name is null || p.Cost < 0 || p.Days <= 0 ||
            p.Prerequisites.IsDefault || p.Prerequisites.Any(string.IsNullOrWhiteSpace) ||
            p.Prerequisites.Distinct(StringComparer.Ordinal).Count() != p.Prerequisites.Length))
            throw new ArgumentException("Invalid research project.");
        var catalog = projects.ToImmutableDictionary(p => p.Id, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string id)
        {
            if (visited.Contains(id)) return;
            if (!catalog.TryGetValue(id, out var project) || !visiting.Add(id))
                throw new ArgumentException("Invalid research prerequisite graph.");
            foreach (string prerequisite in project.Prerequisites) Visit(prerequisite);
            visiting.Remove(id);
            visited.Add(id);
        }
        foreach (string id in catalog.Keys) Visit(id);
        return catalog;
    }
}

public sealed record ResearchProgress(string ProjectId, int SettlementId, int DaysRemaining);
