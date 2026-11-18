using System.Collections.Immutable;
using San9.Content.Authoring;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Application.Authoring;

public sealed record NewGameComposition(ScenarioInitialState Scenario, SessionDefinitions Definitions);

/// <summary>Composes authored startup choices without mutating the source scenario or person library.</summary>
public static class NewGameComposer
{
    public static bool CanDeploy(CreatedOfficer person, ScenarioInitialState scenario) =>
        !scenario.NaturalDeathsEnabled || scenario.Year - BirthYear(person, scenario.Year) + 1 is >= 15 and <= 90;

    private static int BirthYear(CreatedOfficer person, int year) => person.AgeAtScenarioStart is int age
        ? checked(year - age) : person.BirthYear;

    public static IReadOnlyDictionary<Guid, int> OfficerIds(ScenarioInitialState source, NewGameSetup setup)
    {
        int next = source.Officers.Select(o => o.State.Id).DefaultIfEmpty(-1).Max();
        return setup.Library.Officers.OrderBy(p => p.Id).ToDictionary(p => p.Id, _ => checked(++next));
    }

    private static HashSet<Guid> Participants(NewGameSetup setup)
    {
        var people = setup.Library.Officers.ToDictionary(p => p.Id);
        var included = setup.Placements.Select(p => p.OfficerId).Concat(setup.Forces.Select(f => f.RulerId)).ToHashSet();
        var pending = new Queue<Guid>(included);
        while (pending.TryDequeue(out var id))
            foreach (var reference in new[] { people[id].ParentId, people[id].BloodlineRootId }.OfType<Guid>())
                if (included.Add(reference)) pending.Enqueue(reference);
        return included;
    }

    public static NewGameComposition Compose(ScenarioInitialState source, NewGameSetup setup, IReadOnlyCollection<int>? playerForceIds = null)
    {
        setup.Validate();
        source = ScenarioSessionDefinition.ActivatePlayerForces(source,
            playerForceIds ?? (source.PlayerForceId is int player ? [player] : []));
        var included = Participants(setup);
        if (included.Count == 0) return new(source, SessionDefinitions.Empty);
        var ids = OfficerIds(source, setup);
        var people = setup.Library.Officers.ToDictionary(p => p.Id);
        var placements = setup.Placements.ToDictionary(p => p.OfficerId);
        var founders = setup.Forces.ToDictionary(f => f.RulerId);
        foreach (var id in placements.Keys.Concat(founders.Keys))
            if (!CanDeploy(people[id], source)) throw new InvalidDataException("部署人物的年龄须为15至90岁。");
        var cities = source.Settlements.Where(s => s.Kind == SettlementKind.City && !s.IsDormant && s.CanHostPersons).ToDictionary(s => s.Id);
        if (setup.Forces.Any(f => !cities.TryGetValue(f.CityId, out var city) || city.OwnerId is not null))
            throw new InvalidDataException("新势力须选择未被占领的都市。");
        if (setup.Placements.Any(p => !cities.ContainsKey(p.CityId))) throw new InvalidDataException("部署地点不是可用都市。");
        var forces = source.Forces.ToList();
        var corps = source.Information.Corps.ToList();
        var relations = source.Information.Relations.ToList();
        int forceBase = forces.Select(f => f.Id).DefaultIfEmpty(-1).Max();
        int corpsBase = corps.Select(c => c.Id).DefaultIfEmpty(-1).Max();
        int officerBase = source.Officers.Select(o => o.State.Id).DefaultIfEmpty(-1).Max();
        var rulerForces = new Dictionary<Guid, int>();
        foreach (var created in setup.Forces.OrderBy(f => f.RulerId))
        {
            int offset = checked(ids[created.RulerId] - officerBase);
            int id = checked(forceBase + offset), root = checked(corpsBase + offset);
            var city = cities[created.CityId];
            int color = Enumerable.Range(0, 50).MinBy(c => forces.Count(f => f.ColorCode == c));
            forces.Add(new(id, NameKey(ids[created.RulerId]), color, 100)
            {
                BaseTrust = 100, LeaderId = ids[created.RulerId], FlagGlyph = people[created.RulerId].FlagGlyph,
                RankId = source.Information.InitialRankId
            });
            var economy = new SettlementState(city.Id, id, city.Soldiers, city.Durability, city.MaxDurability, city.Morale,
                city.PublicOrder, city.Commerce, city.MaxCommerce, city.Cultivation, city.MaxCultivation)
                { Population = city.Population, DraftPopulation = city.DraftPopulation };
            corps.Add(new(root, id, ids[created.RulerId], new(
                (uint)Math.Min(TreasuryRules.Capacity, 2 * EconomyMath.Revenue(economy) + 3000),
                (uint)Math.Min(TreasuryRules.Capacity, 2 * EconomyMath.Harvest(economy) + 30000))) { IsRoot = true });
            foreach (var other in forces.Where(f => f.Id != id)) relations.Add(new(Math.Min(other.Id, id), Math.Max(other.Id, id), 50));
            cities[city.Id] = city with { OwnerId = id, CorpsId = root };
            rulerForces.Add(created.RulerId, id);
        }
        if (setup.Placements.Any(p => p.JoinForce && cities[p.CityId].OwnerId is null))
            throw new InvalidDataException("空白都市不能仕官，请选择在野部署。");
        var historical = source.Officers.ToDictionary(p => p.State.Id);
        // Family identities may also name absent ancestors; allocate beyond both namespaces.
        int bloodline = source.Officers.SelectMany(p => new[] { p.State.Id, p.State.BloodlineId ?? -1 }).DefaultIfEmpty(-1).Max();
        var bloodlines = new Dictionary<Guid, int>();
        int Family(CreatedOfficer p)
        {
            if (bloodlines.TryGetValue(p.Id, out int family)) return family;
            if (p.ScenarioParentId is int parent && !historical.ContainsKey(parent))
                throw new InvalidDataException($"人物“{p.FullName}”的历史亲代不在当前剧本中。");
            if (p.HistoricalBloodlineRootId is int root && !historical.ContainsKey(root))
                throw new InvalidDataException($"人物“{p.FullName}”的历史血缘根不在当前剧本中。");
            family = p.HistoricalBloodlineRootId is int historicRoot ? historical[historicRoot].State.BloodlineId ?? historicRoot
                : p.BloodlineRootId is Guid own && own == p.Id ? checked(++bloodline)
                : p.BloodlineRootId is Guid familyRoot ? Family(people[familyRoot])
                : p.ParentId is Guid authored ? Family(people[authored]) : p.ScenarioParentId is int original
                ? historical[original].State.BloodlineId ?? original : checked(++bloodline);
            return bloodlines[p.Id] = family;
        }
        var officers = source.Officers.ToList();
        foreach (var p in setup.Library.Officers.Where(p => included.Contains(p.Id)).OrderBy(p => p.Id))
        {
            if (p.LearnedTactics.Any(t => source.Military?.Tactics.Any(rule => rule.Id == t) != true))
                throw new InvalidDataException($"人物“{p.FullName}”包含当前剧本未定义的兵法。");
            int? cityId = founders.TryGetValue(p.Id, out var founded) ? founded.CityId : placements.GetValueOrDefault(p.Id)?.CityId;
            bool ruler = rulerForces.TryGetValue(p.Id, out int ownForce);
            int? owner = ruler ? ownForce : placements.TryGetValue(p.Id, out var placement) && placement.JoinForce ? cities[placement.CityId].OwnerId : null;
            bool future = source.NaturalDeathsEnabled && !ruler && p.AppearanceYear > source.Year;
            var loyalty = ruler && source.DefaultLoyalty != 100
                ? ImmutableDictionary<int, int>.Empty.Add(ownForce, 100) : ImmutableDictionary<int, int>.Empty;
            var state = new OfficerState(ids[p.Id], future ? null : owner, cityId, p.Abilities, false)
            {
                IsDormant = cityId is null, Status = cityId is null ? OfficerStatus.Undiscovered : ruler ? OfficerStatus.Ruler :
                    future ? OfficerStatus.NotAppeared : owner is not null ? OfficerStatus.Serving : OfficerStatus.Free,
                IsCommander = ruler, BirthYear = BirthYear(p, source.Year), AppearanceYear = p.AppearanceYear, LifespanTier = p.LifespanTier,
                StatusStartYear = !future && owner is not null ? source.Year : null,
                Surname = p.Surname, IsFemale = p.IsFemale, Personality = p.Personality, Affinity = p.Affinity,
                Ambition = p.Ambition, Righteousness = p.Righteousness, TacticProficiencies = p.Proficiencies,
                MasteredCategories = p.MasteredCategories, LearnedTactics = p.LearnedTactics, ForceLoyalties = loyalty,
                ParentId = p.ParentId is Guid parent ? ids[parent] : p.ScenarioParentId, BloodlineId = Family(p),
                SuccessionPriority = p.SuccessionPriority, ForceFounderPriority = p.ForceFounderPriority,
                TacticLinkBonus = p.TacticLinkBonus, DuelAdvantages = p.DuelAdvantages,
                PrefersConsolidationLeadership = p.PrefersConsolidationLeadership,
                AutomaticSurrenderAllowed = p.AutomaticSurrenderAllowed, HasRecruitmentPoliticsBonus = p.HasRecruitmentPoliticsBonus
            };
            officers.Add(new(NameKey(state.Id), state) { CourtesyNameKey = p.CourtesyName.Length > 0 ? $"created.{state.Id}.courtesy" : null,
                BiographyKey = p.Biography.Length > 0 ? $"created.{state.Id}.biography" : null, FlagGlyph = p.FlagGlyph });
        }
        // Only explicit deployments and rulers claim treasures, not their family dependencies.
        var treasures = source.Information.TreasureStates.ToDictionary(t => t.Id);
        var treasureIds = source.Information.Treasures.Select(t => t.Id).ToHashSet();
        var claimedTreasures = new HashSet<int>();
        foreach (var id in placements.Keys.Concat(founders.Keys).Order())
            foreach (int treasureId in people[id].InitialTreasureIds)
            {
                if (!treasureIds.Contains(treasureId))
                    throw new InvalidDataException($"人物“{people[id].FullName}”包含当前剧本未定义的宝物。");
                if (!claimedTreasures.Add(treasureId))
                    throw new InvalidDataException("多个部署人物不能领取同一件初始宝物。");
                var placement = new OfficerTreasurePlacement(ids[id]);
                treasures[treasureId] = treasures.TryGetValue(treasureId, out var treasure)
                    ? treasure with { Placement = placement } : new(treasureId, placement);
            }
        var result = source with { Forces = forces.ToArray(), Officers = officers.ToArray(),
            Settlements = source.Settlements.Select(s => cities.GetValueOrDefault(s.Id) ?? s).ToArray(),
            Information = source.Information with { Corps = corps.ToArray(), Relations = relations.ToArray(), TreasureStates = treasures.Values.ToArray() } };
        result.Validate();
        return new(result, new()
        {
            Officers = setup.Library.Officers.Where(p => included.Contains(p.Id)).OrderBy(p => p.Id)
                .Select(p => new SessionOfficerDefinition(ids[p.Id], p.Surname, p.Name, p.CourtesyName, p.Biography, p.PortraitKey)
                    { FlagGlyph = p.FlagGlyph }).ToImmutableArray(),
            Forces = setup.Forces.OrderBy(f => f.RulerId).Select(f => new SessionForceDefinition(rulerForces[f.RulerId], ids[f.RulerId])).ToImmutableArray()
        });
    }

    private static string NameKey(int id) => $"created.{id}.name";
}
