using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

/// <summary>Read-only force selection derived from one scenario, before new-game initialization.</summary>
public sealed record ScenarioForceSelection(string Title, int Year, int Month, OpeningForce[] Forces,
    Dictionary<int, string> CityColors)
{
    public static ScenarioForceSelection Read(ScenarioInitialState scenario, TextCatalog text,
        IReadOnlyDictionary<string, string> illustrations) => Read(ScenarioSelectionState.From(scenario), text, illustrations);

    public static ScenarioForceSelection Read(ScenarioSelectionState scenario, TextCatalog text,
        IReadOnlyDictionary<string, string> illustrations)
    {
        string Text(string key) => text.Resolve(key);
        string Officer(int? id) => scenario.Officers.FirstOrDefault(o => o.State.Id == id) is { } o ? Text(o.NameKey) : "";
        var cities = scenario.Settlements.Where(s => s.Kind == SettlementKind.City).ToArray();
        var clock = new GameClock(scenario.Year, scenario.Month, scenario.Day);
        // Reserved groups are selectable too. Project every authored group as available
        // for its preview, including payroll, without activating or modifying the source.
        var world = new WorldSnapshot(clock, new(clock, 0))
        {
            Forces = scenario.Forces.Select(f => new ForceState(f.Id, f.Trust) { IsBarbarian = f.IsBarbarian, PersonalPeerageRegimeId = f.PersonalPeerageRegimeId }).ToArray(),
            Settlements = scenario.Settlements.Select(s => new SettlementState(s.Id, s.OwnerId, s.Soldiers, s.Durability,
                s.MaxDurability, s.Morale, s.PublicOrder, s.Commerce, s.MaxCommerce, s.Cultivation, s.MaxCultivation)
                { Kind = s.Kind, CorpsId = s.CorpsId, Population = s.Population, DraftPopulation = s.DraftPopulation,
                    ConsumesFood = s.ConsumesFood, Wounded = s.Wounded }).ToArray(),
            Officers = PersonalPeerageMigration.Initialize(scenario.Officers.Select(o => o.State with { IsDormant = false }), scenario.Officers, clock),
            Units = scenario.Units, Corps = scenario.Information.Corps
        };
        var salaries = scenario.Information.Offices.ToDictionary(o => o.Id, o => o.Salary);
        var regimes = (scenario.PersonalPeerageRegimes ?? []).ToDictionary(r => r.Id, r => new PersonalPeerageRegime(r.Periods), StringComparer.Ordinal);
        PersonalPeeragePolicy PeeragePolicy(ForceInitialState force) => force.PersonalPeerageRegimeId is string id &&
            regimes.TryGetValue(id, out var regime) && regime.Period(scenario.Year) is { } period ? period.Policy(PersonalPeeragePolicy.Default) : PersonalPeeragePolicy.Default;
        var forces = scenario.Forces.Select(f =>
        {
            var owned = cities.Where(c => c.OwnerId == f.Id).ToArray();
            return new OpeningForce(f.Id, Text(f.NameKey), Officer(f.AdviserId),
                scenario.Information.Ranks.FirstOrDefault(r => r.Id == f.RankId && !r.IsUnranked) is { } rank ? rank.Name : "",
                f.Trust, scenario.Officers.Count(o => o.State.OwnerId == f.Id && o.State.IsServing),
                owned.Sum(c => (long)c.Population + c.DraftPopulation), owned.Sum(c => (long)c.DraftPopulation),
                scenario.Settlements.Where(s => s.OwnerId == f.Id).Sum(s => (long)s.Soldiers) + scenario.Units.Where(u => u.OwnerId == f.Id).Sum(u => (long)u.Soldiers),
                owned.Select(c => c.Id).ToArray())
            {
                Portrait = f.LeaderId is int leader ? illustrations.GetValueOrDefault($"catalog-person-{leader}") : null,
                IsBarbarian = f.IsBarbarian,
                Tactics = string.Join("、", f.ProvidedTactics.Select(id => scenario.Information.Tactics.Single(t => t.Id == id).Name)),
                Country = scenario.Information.CountryTitles.FirstOrDefault(c => c.Id == f.CountryTitleId) is { } country ? Text(country.NameKey) : "",
                Corps = scenario.Information.Corps.Count(c => c.OwnerId == f.Id),
                Wounded = scenario.Settlements.Where(s => s.OwnerId == f.Id).Sum(s => (long)s.Wounded) + scenario.Units.Where(u => u.OwnerId == f.Id).Sum(u => (long)u.Wounded),
                AllianceDays = scenario.Alliance is { } alliance && (alliance.TargetForceId == f.Id || alliance.MemberForceIds.Contains(f.Id)) ? alliance.RemainingXun * 10 : 0,
                IsAllianceLeader = scenario.Alliance?.LeaderForceId == f.Id, IsAllianceTarget = scenario.Alliance?.TargetForceId == f.Id,
                Outlook = EconomicOutlook.Read(world, salaries, f.Id, CompanionSettings.Default, peerages: PeeragePolicy(f))
            };
        }).Where(f => f.CityIds.Length > 0).ToArray();
        return new(text.Resolve("scenario.title"), scenario.Year, scenario.Month, forces, cities.ToDictionary(c => c.Id,
            c => c.OwnerId is int id ? $"color-{scenario.Forces.Single(f => f.Id == id).ColorCode}" : "color-50"));
    }
}

public sealed record OpeningForce(int Id, string Name, string Adviser, string Rank, int Trust,
    int Officers, long Population, long DraftPopulation, long Soldiers, int[] CityIds)
{
    public string? Portrait { get; init; }
    public bool IsBarbarian { get; init; }
    public string Tactics { get; init; } = "";
    public string Country { get; init; } = "";
    public int Corps { get; init; }
    public long Wounded { get; init; }
    public EconomicOutlook Outlook { get; init; } = null!;
    public int AllianceDays { get; init; }
    public bool IsAllianceLeader { get; init; }
    public bool IsAllianceTarget { get; init; }
}
