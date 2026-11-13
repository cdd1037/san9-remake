namespace San9.Core.Domain;

/// <summary>Resolves project availability from the modules that consume the project and its prerequisites.</summary>
public static class ResearchModuleRules
{
    public static bool IsAvailable(MilitaryRulebook military, EquipmentRulebook equipment, NewGameSettings settings, string projectId)
    {
        bool DependsOn(string root, string target, HashSet<string> visited)
        {
            if (root == target) return true;
            return visited.Add(root) && military.ResearchProjects.TryGetValue(root, out var project) &&
                project.Prerequisites.Any(parent => DependsOn(parent, target, visited));
        }
        bool Uses(IEnumerable<string> roots) => roots.Any(root => DependsOn(root, projectId, []));
        bool equipmentUse = Uses(equipment.Items.Values.Where(item => !item.IsOrdinaryArmament)
            .SelectMany(item => new[] { item.ResearchProjectId, item.PurchaseResearchProjectId }).OfType<string>());
        bool obsoleteUse = Uses(equipment.Items.Values.Where(item => item.IsOrdinaryArmament)
            .SelectMany(item => new[] { item.ResearchProjectId, item.PurchaseResearchProjectId }).OfType<string>());
        bool militaryUse = Uses(military.TroopTypes.Values.SelectMany(type => type.LevelResearchProjects.Values)
            .Concat(military.NavalTraining.Values.Select(t => t.ResearchProjectId).OfType<string>())
            .Concat(military.SiegeEquipment.Values.Select(item => item.ResearchProjectId))
            .Concat(military.Armaments.Values.Select(item => item.ResearchProjectId).OfType<string>()));
        if (obsoleteUse && !militaryUse && !equipmentUse) return false;
        if (settings.MixedTroopsEnabled && (militaryUse || !equipmentUse)) return true;
        return settings.EquipmentDevelopmentEnabled != false && equipmentUse;
    }
}
