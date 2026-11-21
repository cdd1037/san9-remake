using System.Collections.Immutable;
using San9.Content.Authoring;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Application.Authoring;

public sealed record NewGameComposition(ScenarioInitialState Scenario, SessionDefinitions Definitions);

/// <summary>Composes authored startup choices without mutating the source scenario or person library.</summary>
public static class NewGameComposer
{
    public static bool CanDeploy(OfficerDefaults person, ScenarioInitialState scenario) =>
        !scenario.NaturalDeathsEnabled || (long)scenario.Year - BirthYear(person, scenario.Year) + 1 is >= 15 and <= 90;

    private static int BirthYear(OfficerDefaults person, int year) => person.AgeAtScenarioStart is int age
        ? checked(year - age) : person.BirthYear;

    public static NewGameComposition Compose(ScenarioInitialState source, NewGameSetup setup, IReadOnlyCollection<int>? playerForceIds = null)
    {
        setup.Validate();
        source = ScenarioSessionDefinition.ActivatePlayerForces(source,
            playerForceIds ?? (source.PlayerForceId is int player ? [player] : []));
        var placements = setup.Placements.ToDictionary(p => p.OfficerId);
        var founders = setup.Forces.ToDictionary(f => f.RulerId);
        var included = placements.Keys.Concat(founders.Keys).ToHashSet();
        if (included.Count == 0) return new(source, SessionDefinitions.Empty);
        var people = source.OfficerCatalog;
        var present = source.Officers.Select(o => o.State.Id).ToHashSet();
        foreach (int id in included)
            if (present.Contains(id) || !people.TryGetValue(id, out var person) || !CanDeploy(person, source))
                throw new InvalidDataException($"武将 {id} 已入局或不符合部署条件。");
        var pending = new Queue<int>(included);
        while (pending.TryDequeue(out int id))
        {
            var person = people[id];
            foreach (int dependency in new[] { person.ParentId, person.BloodlineId }.OfType<int>())
            {
                if (present.Contains(dependency) || !included.Add(dependency)) continue;
                if (!people.ContainsKey(dependency)) throw new InvalidDataException($"缺少关联武将 {dependency} 的资料。");
                pending.Enqueue(dependency);
            }
        }
        var cities = source.Settlements.Where(s => s.Kind == SettlementKind.City && !s.IsDormant && s.CanHostPersons).ToDictionary(s => s.Id);
        if (setup.Forces.Any(f => !cities.TryGetValue(f.CityId, out var city) || city.OwnerId is not null))
            throw new InvalidDataException("新势力须选择未被占领的都市。");
        if (setup.Placements.Any(p => !cities.ContainsKey(p.CityId))) throw new InvalidDataException("部署地点不是可用都市。");
        var forces = source.Forces.ToList();
        var corps = source.Information.Corps.ToList();
        var relations = source.Information.Relations.ToList();
        int forceBase = forces.Select(f => f.Id).DefaultIfEmpty(-1).Max();
        int corpsBase = corps.Select(c => c.Id).DefaultIfEmpty(-1).Max();
        var rulerForces = new Dictionary<int, int>();
        var founderOffsets = people.Values.Where(p => !present.Contains(p.Id)).OrderBy(p => p.Id)
            .Select((p, index) => (p.Id, Offset: index + 1)).ToDictionary(p => p.Id, p => p.Offset);
        foreach (var created in setup.Forces.OrderBy(f => f.RulerId))
        {
            int offset = founderOffsets[created.RulerId];
            int id = checked(forceBase + offset), root = checked(corpsBase + offset);
            var city = cities[created.CityId];
            int color = Enumerable.Range(0, 50).MinBy(c => forces.Count(f => f.ColorCode == c));
            forces.Add(new(id, people[created.RulerId].NameKey, color, 100)
            {
                BaseTrust = 100, LeaderId = created.RulerId, FlagGlyph = people[created.RulerId].FlagGlyph,
                RankId = source.Information.InitialRankId
            });
            var economy = new SettlementState(city.Id, id, city.Soldiers, city.Durability, city.MaxDurability, city.Morale,
                city.PublicOrder, city.Commerce, city.MaxCommerce, city.Cultivation, city.MaxCultivation)
                { Population = city.Population, DraftPopulation = city.DraftPopulation };
            corps.Add(new(root, id, created.RulerId, new(
                (uint)Math.Min(TreasuryRules.Capacity, 2 * EconomyMath.Revenue(economy) + 3000),
                (uint)Math.Min(TreasuryRules.Capacity, 2 * EconomyMath.Harvest(economy) + 30000))) { IsRoot = true });
            foreach (var other in forces.Where(f => f.Id != id)) relations.Add(new(Math.Min(other.Id, id), Math.Max(other.Id, id), 50));
            cities[city.Id] = city with { OwnerId = id, CorpsId = root };
            rulerForces.Add(created.RulerId, id);
        }
        if (setup.Placements.Any(p => p.JoinForce && cities[p.CityId].OwnerId is null))
            throw new InvalidDataException("空白都市不能仕官，请选择在野部署。");
        var officers = source.Officers.ToList();
        foreach (int personId in included.Order())
        {
            var p = people[personId];
            if (p.LearnedTactics.Any(t => source.Military?.Tactics.Any(rule => rule.Id == t) != true))
                throw new InvalidDataException($"人物“{p.Name}”包含当前剧本未定义的兵法。");
            int? cityId = founders.TryGetValue(p.Id, out var founded) ? founded.CityId : placements.GetValueOrDefault(p.Id)?.CityId;
            bool ruler = rulerForces.TryGetValue(p.Id, out int ownForce);
            int? owner = ruler ? ownForce : placements.TryGetValue(p.Id, out var placement) && placement.JoinForce ? cities[placement.CityId].OwnerId : null;
            bool future = source.NaturalDeathsEnabled && !ruler && p.AppearanceYear > source.Year;
            var loyalty = ruler && source.DefaultLoyalty != 100
                ? ImmutableDictionary<int, int>.Empty.Add(ownForce, 100) : ImmutableDictionary<int, int>.Empty;
            var placementState = ScenarioOfficerState.Unplaced(p.Id) with
            {
                OwnerId = future ? null : owner, SettlementId = cityId, IsDormant = cityId is null,
                Status = cityId is null ? OfficerStatus.Undiscovered : ruler ? OfficerStatus.Ruler :
                    future ? OfficerStatus.NotAppeared : owner is not null ? OfficerStatus.Serving : OfficerStatus.Free,
                StatusStartYear = !future && owner is not null ? source.Year : null, IsCommander = ruler,
                ForceLoyalties = loyalty
            };
            officers.Add(placementState.Compile(p));
        }
        var result = source with { Forces = forces.ToArray(), Officers = officers.ToArray(),
            Settlements = source.Settlements.Select(s => cities.GetValueOrDefault(s.Id) ?? s).ToArray(),
            Information = source.Information with { Corps = corps.ToArray(), Relations = relations.ToArray() } };
        result.Validate();
        return new(result, new SessionDefinitions
        {
            OfficerIds = included.Order().ToImmutableArray(),
            Forces = setup.Forces.OrderBy(f => f.RulerId).Select(f => new SessionForceDefinition(rulerForces[f.RulerId], f.RulerId)).ToImmutableArray()
        }.Resolve(source));
    }
}
