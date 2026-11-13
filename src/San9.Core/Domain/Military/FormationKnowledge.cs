using System.Collections.Immutable;

namespace San9.Core.Domain;

/// <summary>Officer-specific formation knowledge. Legacy initialisation is a remake rule, not source-game data.</summary>
public static class FormationKnowledge
{
    public const int MaximumProficiency = 1000;
    public const int PracticeDaysToLearn = 20;

    public static OfficerState Practice(OfficerState officer, int formationId)
    {
        if (formationId < 0) throw new ArgumentOutOfRangeException(nameof(formationId));
        if (officer.FormationProficiencies is null || Knows(officer, formationId)) return officer;
        var practice = officer.FormationPractice ?? ImmutableSortedDictionary<int, int>.Empty;
        int next = practice.GetValueOrDefault(formationId) + 1;
        if (next < PracticeDaysToLearn) return officer with { FormationPractice = practice.SetItem(formationId, next) };
        practice = practice.Remove(formationId);
        return officer with { FormationProficiencies = officer.FormationProficiencies.Add(formationId, 0),
            FormationPractice = practice.IsEmpty ? null : practice };
    }

    public static OfficerState InitializeLegacy(OfficerState officer, ForceState? force, MilitaryRulebook rules, int? currentFormation = null)
    {
        if (officer.FormationProficiencies is not null) return officer;
        var skills = ImmutableSortedDictionary.CreateBuilder<int, int>();
        foreach (var formation in rules.Formations.Values.OrderBy(f => f.Id))
        {
            if (formation.TravelMode != TravelMode.Land || rules.IsFixedFormation(formation.Id) ||
                formation.Id == rules.UnformedFormationId || rules.SiegeEquipment.Values.Any(s => s.FormationId == formation.Id)) continue;
            var categories = formation.Tactics?.FavoredCategories ?? [];
            int tactical = categories.Where(c => c >= 0 && c < officer.TacticProficiencies.Length)
                .Select(c => officer.TacticProficiencies[c]).DefaultIfEmpty(0).Max();
            int skill = (int)Math.Clamp((long)officer.BaseAbilities.Leadership * 5 + tactical, 0, MaximumProficiency);
            bool legacyResearch = formation.ResearchProjectId is string project && force?.CompletedResearch.Contains(project) == true;
            bool known = formation.Id == currentFormation || legacyResearch ||
                (!formation.MixedTroopsOnly && (formation.IsCommon || force?.FormationGrants.Any(g => g.FormationId == formation.Id && g.AppliesTo(force)) == true)) ||
                formation.MixedTroopsOnly && skill >= 400;
            if (known) skills.Add(formation.Id, skill);
        }
        return officer with { FormationProficiencies = skills.ToImmutable() };
    }

    public static bool Knows(OfficerState officer, int formationId) =>
        officer.FormationProficiencies?.ContainsKey(formationId) == true;
}
