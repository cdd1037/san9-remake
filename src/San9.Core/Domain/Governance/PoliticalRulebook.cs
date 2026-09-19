using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Simulation;

namespace San9.Core.Domain;

public sealed record PoliticalEventsDefinition(
    [property: JsonRequired] int ProclamationTrust,
    [property: JsonRequired] PersistentEventKey CourtEndedConsumptionKey);

public sealed record PeerageRule(int Id, int Precedence, int RequiredTrust, int LoyaltyBonus);

public sealed record CountryTitleRule(
    [property: JsonRequired] int Id,
    [property: JsonRequired] bool ManualSelectionAllowed,
    [property: JsonRequired] bool AutomaticSelectionAllowed,
    [property: JsonRequired] bool PreventsInvestiture,
    [property: JsonRequired] ImmutableArray<int> PreferredRulerIds,
    [property: JsonRequired] ImmutableArray<int> PreferredBloodlineIds);

public sealed class PoliticalRulebook
{
    public static PoliticalRulebook Empty { get; } = new(null, [], [], ImmutableDictionary<int, int>.Empty);
    public PoliticalRulebook(PoliticalEventsDefinition? events, IEnumerable<PeerageRule> ranks,
        IEnumerable<CountryTitleRule> titles, IReadOnlyDictionary<int, int> provinceTitles)
    {
        Events = events;
        Ranks = ranks.ToImmutableDictionary(r => r.Id);
        Titles = titles.ToImmutableDictionary(t => t.Id);
        ProvinceTitles = provinceTitles.ToImmutableDictionary();
        if (Ranks.Values.Any(r => r.Id < 0 || r.Precedence < 0 || r.RequiredTrust < 0 || r.LoyaltyBonus < 0) ||
            Ranks.Values.Select(r => r.Precedence).Distinct().Count() != Ranks.Count ||
            Titles.Values.Any(t => t.Id < 0 || t.PreferredRulerIds.IsDefault || t.PreferredBloodlineIds.IsDefault ||
                t.PreferredRulerIds.Any(id => id < 0) || t.PreferredBloodlineIds.Any(id => id < 0)) ||
            Duplicate(Titles.Values.SelectMany(t => t.PreferredRulerIds)) ||
            Duplicate(Titles.Values.SelectMany(t => t.PreferredBloodlineIds)) ||
            ProvinceTitles.Any(p => p.Key < 0 || !Titles.ContainsKey(p.Value)))
            throw new ArgumentException("Invalid political rules.");
        var ordered = Ranks.Values.OrderBy(r => r.Precedence).ToArray();
        ImperialRankId = ordered.LastOrDefault()?.Id;
        ProclamationRankId = ordered.Length >= 2 ? ordered[^2].Id : null;
        ProvinceEntryRankId = ordered.FirstOrDefault()?.Id;
        if (events is { } e && (ordered.Length < 2 || e.ProclamationTrust < 0 || string.IsNullOrWhiteSpace(e.CourtEndedConsumptionKey.Value)))
            throw new ArgumentException("Invalid political event rank references.");
    }
    private static bool Duplicate(IEnumerable<int> ids) { var values = ids.ToArray(); return values.Distinct().Count() != values.Length; }
    public PoliticalEventsDefinition? Events { get; }
    public int? ImperialRankId { get; }
    public int? ProclamationRankId { get; }
    public int? ProvinceEntryRankId { get; }
    public ImmutableDictionary<int, PeerageRule> Ranks { get; }
    public ImmutableDictionary<int, CountryTitleRule> Titles { get; }
    public ImmutableDictionary<int, int> ProvinceTitles { get; }
    public PeerageRule? NextRank(int? current) => current is int id && Ranks.TryGetValue(id, out var rank)
        ? Ranks.Values.Where(r => r.Precedence > rank.Precedence).OrderBy(r => r.Precedence).FirstOrDefault() : null;

    public ImmutableArray<int> ManualCandidates(IReadOnlySet<int> occupied) => Titles.Values
        .Where(t => t.ManualSelectionAllowed && !occupied.Contains(t.Id)).Select(t => t.Id).Order().ToImmutableArray();

    public int? SelectAutomatic(int rulerId, int? bloodlineId, int? serviceProvince, IEnumerable<int> controlledProvinces,
        IReadOnlySet<int> occupied, Func<ulong> random)
    {
        var available = Titles.Values.Where(t => t.AutomaticSelectionAllowed && !occupied.Contains(t.Id)).OrderBy(t => t.Id).ToArray();
        if (available.Length == 0) return null;
        var preferred = available.FirstOrDefault(t => t.PreferredRulerIds.Contains(rulerId)) ??
            available.FirstOrDefault(t => bloodlineId is int family && t.PreferredBloodlineIds.Contains(family));
        if (preferred is not null) return preferred.Id;
        var provinces = controlledProvinces.Distinct().Order().ToList();
        int? FromProvince(int province) => ProvinceTitles.TryGetValue(province, out int title) && available.Any(t => t.Id == title) ? title : null;
        if (serviceProvince is int home && provinces.Contains(home) && FromProvince(home) is int homeTitle) return homeTitle;
        while (provinces.Count > 0)
        {
            int index = (int)(random() % (ulong)provinces.Count);
            int province = provinces[index]; provinces.RemoveAt(index);
            if (FromProvince(province) is int title) return title;
        }
        var fallback = available.Where(t => t.ManualSelectionAllowed).ToArray();
        return fallback.Length == 0 ? null : fallback[random() % (ulong)fallback.Length].Id;
    }
}
