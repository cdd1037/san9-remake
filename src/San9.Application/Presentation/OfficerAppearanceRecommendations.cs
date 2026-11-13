using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public sealed record OfficerAppearanceRecommendation(int OfficerId, int? ForceId, int? SettlementId,
    string? SourceScenarioId, int? SourceYear, string? Problem);

/// <summary>Derives suggestions from scenario starting states, without inferring identities from names.</summary>
public static class OfficerAppearanceRecommendations
{
    private sealed record Evidence(int Year, string SourceId, int LeaderId, int? SettlementId);
    public static OfficerAppearanceRecommendation[] Create(ScenarioInitialState current, WorldSnapshot world,
        IEnumerable<ScenarioInitialState> sources, IEnumerable<int> officerIds)
    {
        var scenarios = sources.ToArray();
        var load = world.Settlements.ToDictionary(s => s.Id,
            s => world.Officers.Count(p => p.IsServing && p.SettlementId == s.Id));
        var active = world.Forces.Where(f => !f.IsDormant && !f.IsEliminated && f.LeaderId is not null)
            .Select(f => f.Id).ToHashSet();
        bool Eligible(SettlementState s) => !s.IsDormant && !s.IsDestroyed && s.CanHostPersons && s.Durability > 0;
        List<OfficerAppearanceRecommendation> result = [];
        foreach (int id in officerIds.Distinct().Order())
        {
            string? currentKey = current.Officers.SingleOrDefault(p => p.State.Id == id)?.NameKey;
            var evidence = scenarios.SelectMany(s =>
            {
                if (!s.Officers.Any(p => p.State.Id == id && p.NameKey == currentKey)) return Array.Empty<Evidence>();
                var authored = (s.AppearanceEvidence ?? []).Where(e => e.OfficerId == id)
                    .Select(e => new Evidence(e.Year, e.SourceScenarioId, e.LeaderId, e.SettlementId)).ToArray();
                if (authored.Length != 0) return authored;
                return s.Officers.Where(p => p.State.Id == id && p.State.IsServing && p.State.OwnerId is not null)
                    .Select(p => (Person: p.State, Force: s.Forces.SingleOrDefault(f => f.Id == p.State.OwnerId)))
                    .Where(p => p.Force?.LeaderId is not null)
                    .Select(p => new Evidence(s.Year, s.ScenarioId, p.Force!.LeaderId!.Value,
                        p.Person.UnitId is int unit ? s.Units.SingleOrDefault(u => u.Id == unit)?.ServicePlaceId : p.Person.SettlementId)).ToArray();
            }).ToArray();
            if (evidence.Length == 0) { result.Add(new(id, null, null, null, null, "没有可用的剧本归属依据")); continue; }
            var mapped = evidence.Select(e => (Evidence: e, Current: current.Forces.Where(f => f.LeaderId == e.LeaderId).ToArray())).ToArray();
            var usable = mapped.Where(m => m.Current.Length == 1 && active.Contains(m.Current[0].Id)).ToArray();
            if (usable.Length == 0) { result.Add(new(id, null, null, null, null,
                mapped.Any(m => m.Current.Length == 1) ? "对应势力未建立或已灭亡" : "历史势力无法明确对应")); continue; }
            long distance = usable.Min(m => Math.Abs((long)m.Evidence.Year - current.Year));
            var matches = usable.Where(m => Math.Abs((long)m.Evidence.Year - current.Year) == distance).ToArray();
            var forceIds = matches.Select(m => m.Current[0].Id).Distinct().ToArray();
            if (forceIds.Length != 1) { result.Add(new(id, null, null, null, null, "同年代剧本归属冲突")); continue; }
            var historical = matches.Select(m => m.Evidence.SettlementId).Where(p => p is not null).ToHashSet();
            if (historical.Count > 1) { result.Add(new(id, null, null, null, null, "同年代剧本地点冲突")); continue; }
            int force = forceIds[0];
            var candidates = world.Settlements.Where(s => Eligible(s) && s.OwnerId == force &&
                (historical.Contains(s.Id) || s.Kind == SettlementKind.City)).ToArray();
            if (candidates.Length == 0) { result.Add(new(id, null, null, null, null, "对应势力没有可用城市")); continue; }
            var place = candidates.OrderBy(s => historical.Contains(s.Id) ? 0 : 1).ThenBy(s => load[s.Id]).ThenBy(s => s.Id).First();
            load[place.Id]++;
            var source = matches.Select(m => m.Evidence).OrderBy(e => e.SourceId, StringComparer.Ordinal).First();
            result.Add(new(id, force, place.Id, source.SourceId, source.Year, null));
        }
        return result.ToArray();
    }
}
