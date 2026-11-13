using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Importer.San9PkTc101.Scenarios;

/// <summary>Explicit source-format polity identities; custom and fictional content keeps its existing policy.</summary>
public static class HistoricalPeerageContent
{
    public static ScenarioInitialState Apply(ScenarioInitialState scenario, bool historical)
    {
        scenario = PersonalPeeragePreset.UpgradeLegacy(scenario);
        if (!historical) return scenario;
        if (scenario.PersonalPeerageRegimes is not [{ Id: PersonalPeeragePreset.GenericId }] ||
            scenario.Forces.Any(f => f.PersonalPeerageRegimeId != PersonalPeeragePreset.GenericId) ||
            scenario.Information.CountryTitles.Any(t => t.Rule.PersonalPeerageRegimeId is not null)) return scenario;

        var bindings = new Dictionary<int, (int Bloodline, string Regime)>
        {
            [0] = (326, HistoricalPeeragePresets.Wei),
            [1] = (621, HistoricalPeeragePresets.Shu),
            [2] = (347, HistoricalPeeragePresets.Wu)
        };
        var titles = scenario.Information.CountryTitles.ToDictionary(t => t.Id);
        if (bindings.Any(b => !titles.TryGetValue(b.Key, out var title) ||
            !title.Rule.PreferredBloodlineIds.SequenceEqual([b.Value.Bloodline])) ||
            bindings.Values.Any(b => !scenario.Officers.Any(p => p.State.Id == b.Bloodline && p.State.BloodlineId == b.Bloodline)))
            return scenario;
        var ranks = scenario.Information.Ranks.Where(r => !r.IsUnranked).OrderBy(r => r.Precedence).ToArray();
        if (ranks.Length < 2) return scenario;
        int emperor = ranks[^1].Id;
        var regimes = HistoricalPeeragePresets.Create(scenario.Information.Ranks, 326, 621, 347);
        var mappedTitles = scenario.Information.CountryTitles.Select(t => bindings.TryGetValue(t.Id, out var binding)
            ? t with { Rule = t.Rule with { PersonalPeerageRegimeId = binding.Regime } } : t).ToArray();
        var forces = scenario.Forces.Select(f =>
        {
            string regime = f.RankId == emperor && f.CountryTitleId is int title && bindings.TryGetValue(title, out var binding)
                ? binding.Regime : scenario.Year <= 220 && !f.IsBarbarian && f.RankId != emperor
                    ? HistoricalPeeragePresets.Han : PersonalPeeragePreset.GenericId;
            // Historical scenario 7 explicitly starts with Sun Quan as king of Wu before his 229 proclamation.
            if (scenario.ScenarioId == "7" && scenario.Year == 225 && f.Id == 2 && f.LeaderId == 349 &&
                scenario.Officers.Any(p => p.State.Id == 349 && p.State.BloodlineId == 347)) regime = HistoricalPeeragePresets.Wu;
            return f with { PersonalPeerageRegimeId = regime };
        }).ToArray();
        return scenario with { Forces = forces, PersonalPeerageRegimes = [.. scenario.PersonalPeerageRegimes, .. regimes],
            Information = scenario.Information with { CountryTitles = mappedTitles } };
    }
}
