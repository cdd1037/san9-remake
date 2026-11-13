using San9.Core.Domain;

namespace San9.Application.Presentation;

internal static class ResearchDisplay
{
    internal static string Name(MilitaryRulebook? military, string id)
    {
        if (military?.ResearchProjects.TryGetValue(id, out var project) == true && !string.IsNullOrWhiteSpace(project.Name))
            return project.Name;
        var match = military?.TroopTypes.Values
            .SelectMany(type => type.LevelResearchProjects.Select(level => new { type.Name, Level = level.Key, Id = level.Value }))
            .FirstOrDefault(item => item.Id == id);
        return match is null ? id : $"{match.Name}·{match.Level}级";
    }
}
