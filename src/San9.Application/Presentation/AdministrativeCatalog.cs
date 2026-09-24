using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

/// <summary>Read-only catalog fields shared by administrative lists and their detail views.</summary>
public sealed class AdministrativeCatalog(StrategySnapshot view, TextCatalog texts)
{
    private static CatalogColumn C(string id, string title, int width = 4) => new(id, title, width, CatalogAlignment.Center);
    private static CatalogColumn N(string id, string title, int width = 6) => new(id, title, width, CatalogAlignment.Right);
    private static CatalogPage P(string title, string name, params CatalogColumn[] columns) => new(title,
        [new("name", name, name == "地域" ? 8 : 6, CatalogAlignment.Left), .. columns]);
    private static CatalogColumn[] Money { get; } = [N("money", "资金", 7), N("money-cost", "资金支出", 7), N("money-income", "资金收入", 7), N("money-forecast", "预测资金", 7), C("money-sufficient", "资金充裕")];
    private static CatalogColumn[] Food { get; } = [N("food", "兵粮", 7), N("food-cost", "兵粮支出", 7), N("food-income", "兵粮收入", 7), N("food-forecast", "预测兵粮", 7), C("food-sufficient", "兵粮充裕")];
    private static CatalogColumn[] People { get; } = [C("commander", "大将", 8), N("serving", "在任", 3), N("captives", "俘虏", 3), N("free", "在野", 3)];
    public static IReadOnlyList<CatalogPage> Pages(InformationCategory category) => Definitions[category];
    public static bool Supports(InformationCategory category) => Definitions.ContainsKey(category);
    private static readonly IReadOnlyDictionary<InformationCategory, CatalogPage[]> Definitions = new Dictionary<InformationCategory, CatalogPage[]>
    {
        [InformationCategory.Ranks] = [P("就任", "官爵", N("capacity", "指挥"), N("trust", "信望", 4), new("description", "说明", 28, CatalogAlignment.Left), C("holders", "势力", 28))],
        [InformationCategory.Forces] = [
            P("状态", "势力", C("country", "国号"), C("rank", "官爵", 8), N("trust", "信望", 4), C("adviser", "军师", 8), N("corps", "军团", 2), N("cities", "都市", 3), N("officers", "武将", 3)),
            P("战力", "势力", N("population", "人口", 8), N("draft", "兵役人口", 8), N("soldiers", "士兵", 8), N("wounded", "伤兵", 8), C("tactics", "兵法", 8)),
            P("资金", "势力", Money), P("兵粮", "势力", Food),
            P("外交", "势力", C("relation", "友好"), C("alliance", "联盟"), N("alliance-days", "联合期间", 4), C("requester", "请求者", 8), C("request-target", "目标设施", 12), N("request-days", "请求期间", 4))],
        [InformationCategory.Corps] = [
            P("状态", "军团", C("force", "势力", 8), C("policy", "方针", 8), C("target", "目标", 12), N("cities", "都市", 3), N("officers", "武将", 3)),
            P("战力", "军团", N("population", "人口", 8), N("draft", "兵役人口", 8), N("soldiers", "士兵", 8), N("wounded", "伤兵", 8)),
            P("资金", "军团", Money), P("兵粮", "军团", Food)],
        [InformationCategory.Cities] = [
            P("所属", "都市", [C("province", "州", 4), C("force", "势力", 8), C("corps-name", "军团", 8), .. People, C("feature", "特征", 6), C("emperor", "汉朝皇帝", 4)]),
            P("战力", "都市", N("population", "人口"), N("draft", "兵役人口"), N("soldiers", "士兵"), N("wounded", "伤兵"), N("morale", "士气", 3), C("state", "状态"), N("durability", "耐久", 4), N("max-durability", "最大耐久", 4), C("trait", "特性"), C("tactics", "兵法")),
            P("内政", "都市", N("public-order", "民心", 4), N("draft-growth", "增加兵役"), N("commerce", "收益", 4), N("max-commerce", "最大收益", 4), N("money-income", "资金收入"), N("cultivation", "收获", 4), N("max-cultivation", "最大收获", 4), N("food-income", "兵粮收入"), C("merchant", "商人", 2)),
            P("邻接", "都市", C("frontline", "前线", 2), new("adjacent", "邻接", 48, CatalogAlignment.Left)), P("地域", "都市", new CatalogColumn("regions", "地域", 72, CatalogAlignment.Left))],
        [InformationCategory.Regions] = [
            P("所属", "地域", [C("province", "州", 4), C("city", "都市", 6), C("force", "势力", 8), C("corps-name", "军团", 8), .. People]),
            P("战力", "地域", N("soldiers", "士兵"), N("wounded", "伤兵"), N("morale", "士气", 3), C("state", "状态"), C("facility", "工作物", 8), N("durability", "耐久", 4), N("max-durability", "最大耐久", 4), N("recovery", "恢复", 4), C("tactics", "兵法"), C("trait", "特性")),
            P("邻接", "地域", C("frontline", "前线", 2), new("adjacent", "邻接", 48, CatalogAlignment.Left))]
    };

    private readonly Dictionary<int, ForceView> _forces = view.Forces.ToDictionary(f => f.Id);
    private readonly Dictionary<int, CorpsState> _corps = view.World.Corps.ToDictionary(c => c.Id);
    private readonly Dictionary<int, SettlementView> _places = view.Settlements.ToDictionary(s => s.Id);
    private readonly Dictionary<int, SettlementState> _states = view.World.Settlements.Where(s => !s.IsDormant && !s.IsDestroyed).ToDictionary(s => s.Id);
    private readonly Dictionary<int, OfficerView> _people = view.Officers.ToDictionary(o => o.Id);
    private sealed record Totals(SettlementState[] Places, SettlementState[] Cities, UnitState[] Units, OfficerView[] People, long TravelingSoldiers, long TravelingWounded);
    private readonly Dictionary<(int Owner, int? Corps), Totals> _totals = [];
    private readonly Dictionary<int, OfficerView[]> _locals = view.Officers.Where(o => o.Journey is null)
        .Join(view.World.Officers.Where(o => o.UnitId is null), o => o.Id, o => o.Id, (visible, state) => (visible, Place: state.SettlementId ?? state.RegionId))
        .Where(p => p.Place is not null).GroupBy(p => p.Place!.Value).ToDictionary(g => g.Key, g => g.Select(p => p.visible).ToArray());
    private static CatalogCell Hidden => new("＊＊＊", IsUnavailable: true);
    private readonly Dictionary<(int Owner, int? Corps), EconomicOutlook> _outlooks = [];
    private readonly IReadOnlyList<TerritoryConnection> _connections = view.TerritoryConnections();
    private HashSet<int>? _frontlineRegions;
    private Dictionary<int, int?>? _regionOwners;
    private bool IsFrontline(InformationCategory category, int id)
    {
        if (_frontlineRegions is null)
        {
            _regionOwners = view.RegionCityIds.ToDictionary(p => p.Key, p => _states.GetValueOrDefault(p.Value)?.OwnerId);
            foreach (var place in _places.Values.Where(p => _states.ContainsKey(p.Id)))
            {
                var state = _states[place.Id];
                if ((state.Kind == SettlementKind.City || state.CanHostPersons) &&
                    view.RegionAt(MapCoordinates.Center(new(place.X, place.Y))) is int region)
                    _regionOwners[region] = state.OwnerId;
            }
            _frontlineRegions = _connections.Where(c => _regionOwners.GetValueOrDefault(c.FromRegionId) is int owner &&
                _regionOwners.GetValueOrDefault(c.ToRegionId) != owner).Select(c => c.FromRegionId).ToHashSet();
        }
        if (category == InformationCategory.Regions) return _frontlineRegions.Contains(id);
        if (_states.GetValueOrDefault(id)?.OwnerId is not int cityOwner) return false;
        var city = _places[id];
        int? cityRegion = view.RegionAt(MapCoordinates.Center(new(city.X, city.Y)));
        return _frontlineRegions.Any(region => _regionOwners![region] == cityOwner &&
            (region == cityRegion || view.RegionCityIds.TryGetValue(region, out int home) && home == id));
    }
    private string Person(int? id) => id is int key && _people.TryGetValue(key, out var o) ? texts.Resolve(o.NameKey) : "—";
    private string Force(int? id) => id is int key && _forces.TryGetValue(key, out var f) ? texts.Resolve(f.NameKey) : "—";
    private string Place(int id) => _places.TryGetValue(id, out var p) ? p.ResolveName(texts) : view.RegionNameKeys.TryGetValue(id, out var name) ? texts.Resolve(name) : "—";
    private string Tactics(IEnumerable<int> ids) => string.Join("‧", ids.Select(id => OfficerCatalog.Pages.Skip(4).SelectMany(p => p.Columns).FirstOrDefault(c => c.Id == $"tactic.{id}")?.Title ?? $"兵法{id}"));
    private string RankHolders(int rankId)
    {
        var names = view.Forces.Where(f => f.RankId == rankId).Select(f => texts.Resolve(f.NameKey)).ToArray();
        return names.Length <= 3 ? string.Join("‧", names)
            : $"{string.Join("‧", names.Take(2))}及其他{names.Length - 2}势力";
    }
    internal bool PersonInRegion(OfficerView officer, int region) => _locals.GetValueOrDefault(region)?.Any(o => o.Id == officer.Id) == true;

    public InformationTable Read(InformationCategory category, int page, int? force = null, int? settlement = null, int? corps = null, int? region = null)
    {
        var pages = Pages(category);
        if ((uint)page >= pages.Count) throw new ArgumentOutOfRangeException(nameof(page));
        IEnumerable<int> ids = category switch
        {
            InformationCategory.Ranks => view.Information.Ranks.Where(r => !r.IsUnranked).OrderByDescending(r => r.Precedence).Select(r => r.Id),
            InformationCategory.Forces => view.Forces.Where(f => force is null || f.Id == force).Select(f => f.Id),
            InformationCategory.Corps => view.World.Corps.Where(c => _forces.ContainsKey(c.OwnerId) && (force is null || c.OwnerId == force) && (corps is null || c.Id == corps)).Select(c => c.Id),
            InformationCategory.Cities => view.Settlements.Where(s => s.Kind == SettlementKind.City && (force is null || s.OwnerId == force) && (corps is null || s.CorpsId == corps) && (settlement is null || s.Id == settlement)).Select(s => s.Id),
            InformationCategory.Regions => view.RegionNameKeys.Keys.Where(id => (region is null || id == region) &&
                (settlement is null || City(id) == settlement) && (force is null || _places.GetValueOrDefault(id)?.OwnerId == force) &&
                (corps is null || _places.GetValueOrDefault(id)?.CorpsId == corps)),
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };
        var rows = ids.Select(id => new InformationRow(new(category, id), pages[page].Columns.Select(c => Cell(category, id, c.Id)).ToArray(), MapTarget(category, id))).ToArray();
        return new(InformationQuery.MenuTitle(category)[1..] + "一览", pages.Select(p => p.Title).ToArray(), pages[page].Columns.Select(c => c.Title).ToArray(), rows);
    }
    internal int? City(int region) => view.RegionCityIds.TryGetValue(region, out int city) ? city : _places.GetValueOrDefault(region)?.Kind == SettlementKind.City ? region : null;
    internal int? MapTarget(InformationCategory category, int id) => category switch
    {
        InformationCategory.Forces => _forces[id].LeaderSettlementId,
        InformationCategory.Corps => _people.GetValueOrDefault(_corps[id].LeaderId)?.SettlementId,
        InformationCategory.Cities => id,
        InformationCategory.Regions => _places.ContainsKey(id) ? id : City(id),
        _ => null
    };
    public CatalogCell Cell(InformationCategory category, int id, string field)
    {
        if (category == InformationCategory.Ranks)
        {
            var rank = view.Information.Ranks.Single(r => r.Id == id);
            return field switch
            {
                "name" => texts.Resolve(rank.NameKey), "capacity" => (long)rank.CommandCapacity, "trust" => rank.RequiredTrust,
                "description" => rank.DescriptionKey is { } description ? texts.Resolve(description) : CatalogCell.Unavailable,
                "holders" => RankHolders(id),
                _ => throw new ArgumentOutOfRangeException(nameof(field))
            };
        }
        return category is InformationCategory.Forces or InformationCategory.Corps ? ForceCell(category, id, field) : PlaceCell(category, id, field);
    }
    private CatalogCell ForceCell(InformationCategory category, int id, string field)
    {
        var corps = category == InformationCategory.Corps ? _corps[id] : null;
        int owner = corps?.OwnerId ?? id;
        var force = _forces[owner];
        if (!_totals.TryGetValue((owner, corps?.Id), out var totals))
        {
            bool Includes(int? c) => corps is null || c == corps.Id;
            var owned = view.World.Settlements.Where(s => !s.IsDormant && !s.IsDestroyed && s.OwnerId == owner && Includes(s.CorpsId)).ToArray();
            var traveling = view.World.Simulation.Tasks.Where(t => t.ForceId == owner && t.Reinforcements is not null &&
                Includes(_states.GetValueOrDefault(t.OriginSettlementId) is { } home && home.OwnerId == owner ? home.CorpsId : null)).Select(t => t.Reinforcements!).ToArray();
            totals = new(owned, owned.Where(s => s.Kind == SettlementKind.City).ToArray(),
                view.World.Units.Where(u => u.OwnerId == owner && Includes(u.CorpsId)).ToArray(),
                view.Officers.Where(o => o.OwnerId == owner && o.IsServing && Includes(o.CorpsId)).ToArray(),
                traveling.Sum(t => (long)t.Soldiers), traveling.Sum(t => (long)t.Wounded));
            _totals.Add((owner, corps?.Id), totals);
        }
        var (places, cities, units, people, travelingSoldiers, travelingWounded) = totals;
        var alliance = view.World.Simulation.Alliance;
        bool allied = alliance is not null && (alliance.MemberForceIds.Contains(owner) || alliance.TargetForceId == owner);
        var request = view.World.Simulation.AttackRequests.FirstOrDefault(r => r.RequestedForceId == owner);
        bool requestVisible = owner == view.PlayerForceId || request?.RequesterForceId == view.PlayerForceId;
        var relation = view.World.Relations.FirstOrDefault(r => r.FirstId == Math.Min(owner, view.PlayerForceId) && r.SecondId == Math.Max(owner, view.PlayerForceId));
        if (force.IsBarbarian && (field == "trust" || field.StartsWith("money", StringComparison.Ordinal) || field.StartsWith("food", StringComparison.Ordinal))) return Hidden;
        switch (field)
        {
            case "name": return corps is null ? Force(owner) : Person(corps.LeaderId);
            case "force": return Force(owner);
            case "leader": return Person(corps?.LeaderId ?? force.LeaderId);
            case "country": return view.Information.CountryTitles.FirstOrDefault(t => t.Id == force.CountryTitleId) is { } title ? texts.Resolve(title.NameKey) : CatalogCell.Unavailable;
            case "rank": return view.Information.Ranks.FirstOrDefault(r => r.Id == force.RankId) is { } rank ? texts.Resolve(rank.NameKey) : CatalogCell.Unavailable;
            case "trust": return force.Trust;
            case "adviser": return Person(view.World.Forces.Single(f => f.Id == owner).AdviserId);
            case "corps": return view.World.Corps.Count(c => c.OwnerId == owner);
            case "cities": return cities.Length;
            case "officers": return people.Length;
            case "population": return cities.Sum(s => (long)s.Population);
            case "draft": return cities.Sum(s => (long)s.DraftPopulation);
            case "soldiers": return places.Sum(s => (long)s.Soldiers) + units.Sum(u => (long)u.Soldiers) + travelingSoldiers;
            case "wounded": return places.Sum(s => (long)s.Wounded) + units.Sum(u => (long)u.Wounded) + travelingWounded;
            case "tactics": return Tactics(view.World.Forces.Single(f => f.Id == owner).ProvidedTactics);
            case "money": return corps is null ? (long)force.Money + view.World.Corps.Where(c => c.OwnerId == owner).Sum(c => (long)(c.Treasury?.Money ?? 0)) : (long)(corps.Treasury?.Money ?? force.Money);
            case "food": return corps is null ? (long)force.Food + view.World.Corps.Where(c => c.OwnerId == owner).Sum(c => (long)(c.Treasury?.Food ?? 0)) : (long)(corps.Treasury?.Food ?? force.Food);
            case "policy":
            case "target":
                if (corps is null || corps.Treasury is null) return CatalogCell.Unavailable;
                if (owner != view.PlayerForceId) return Hidden;
                if (field == "policy") return corps.Delegation switch { GeneralDelegation => "全权", DomesticDelegation => "内政", DefeatForceDelegation => "势力攻略", CaptureSettlementDelegation => "都市攻略", _ => throw new InvalidOperationException("Unknown delegation.") };
                return corps.Delegation switch { DefeatForceDelegation d => Force(d.ForceId), CaptureSettlementDelegation c => Place(c.SettlementId), _ => CatalogCell.Unavailable };
            case "relation": return relation is null || owner == view.PlayerForceId ? CatalogCell.Unavailable : relation.Value;
            case "alliance": return !allied ? CatalogCell.Unavailable : alliance!.TargetForceId == owner ? "×" : alliance.LeaderForceId == owner ? "◎" : "○";
            case "alliance-days": return allied ? alliance!.RemainingXun * 10 : CatalogCell.Unavailable;
            case "alliance-leader": return allied ? Force(alliance!.LeaderForceId) : CatalogCell.Unavailable;
            case "alliance-target": return allied ? Force(alliance!.TargetForceId) : CatalogCell.Unavailable;
            case "requester": return !requestVisible ? Hidden : request is null ? CatalogCell.Unavailable : Force(request.RequesterForceId);
            case "request-target": return !requestVisible ? Hidden : request is null ? CatalogCell.Unavailable : Place(request.TargetSettlementId);
            case "request-days": return !requestVisible ? Hidden : request is null ? CatalogCell.Unavailable : request.RemainingXun * 10;
            case "money-cost": case "food-cost": case "money-forecast": case "food-forecast": case "money-sufficient": case "food-sufficient": case "money-income": case "food-income":
                if (!_outlooks.TryGetValue((owner, corps?.Id), out var outlook))
                {
                    outlook = EconomicOutlook.Read(view.World, view.Information.Offices.ToDictionary(o => o.Id,o => o.Salary), owner, corps?.Id);
                    _outlooks.Add((owner, corps?.Id), outlook);
                }
                var resource = field.StartsWith("money", StringComparison.Ordinal) ? outlook.Money : outlook.Food;
                return field[(field.IndexOf('-')+1)..] switch { "cost" => resource.Expenditure, "income" => resource.Income, "forecast" => resource.Forecast,
                    _ => new CatalogCell(resource.Sufficient ? "○" : "×", resource.Sufficient ? 1 : 0) };
            default: throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown force field.");
        }
    }
    private long DraftGrowth(SettlementState city)
    {
        var cities = _states.Values.Where(s => s.Kind == SettlementKind.City).ToArray();
        long military = cities.Sum(s => (long)s.DraftPopulation) + _states.Values.Sum(s => (long)s.Soldiers + s.Wounded) +
            view.World.Units.Sum(u => (long)u.Soldiers + u.Wounded) + view.World.Simulation.Tasks.Where(t => t.Reinforcements is not null).Sum(t => (long)t.Reinforcements!.Soldiers + t.Reinforcements.Wounded);
        long increase = EconomyMath.DraftIncrease(cities.Sum(s => (long)s.Population), military, cities.Sum(s => (long)s.PublicOrder), city.PublicOrder);
        return Math.Min(Math.Max(500000L, city.DraftPopulation), city.DraftPopulation + increase) - city.DraftPopulation;
    }
    private CatalogCell PlaceCell(InformationCategory category, int id, string field)
    {
        var place = _places.GetValueOrDefault(id);
        var state = _states.GetValueOrDefault(id);
        int? city = City(id);
        var people = _locals.GetValueOrDefault(id) ?? [];
        if (field == "name") return category == InformationCategory.Regions ? texts.Resolve(view.RegionNameKeys[id]) : Place(id);
        if (field == "city") return city is int c ? Place(c) : CatalogCell.Unavailable;
        if (field == "province") return city is int home && view.CityProvinceIds.TryGetValue(home, out int province) && view.ProvinceNameKeys.TryGetValue(province, out var key) ? texts.Resolve(key) : CatalogCell.Unavailable;
        if (field == "serving") return people.Count(o => o.IsServing);
        if (field == "captives") return people.Count(o => o.Status == OfficerStatus.Captive);
        if (field == "free") return people.Count(o => o.Status == OfficerStatus.Free);
        if (field == "regions") return string.Join("‧", view.RegionCityIds.Where(p => p.Value == id).Select(p => texts.Resolve(view.RegionNameKeys[p.Key])));
        if (field is "adjacent" or "frontline")
        {
            var neighbors = category == InformationCategory.Regions
                ? _connections.Where(c => c.FromRegionId == id).Select(c => c.ToRegionId).Distinct().ToArray()
                : view.CityConnections.Where(c => c.FromCityId == id).Select(c => c.ToCityId).Distinct().ToArray();
            return field == "adjacent" ? string.Join("‧", neighbors.Select(n => category == InformationCategory.Regions
                ? view.RegionNameKeys.TryGetValue(n, out var name) ? texts.Resolve(name) : "—"
                : Place(n))) : new CatalogCell(IsFrontline(category, id) ? "○" : "");
        }
        if (state is null || place is null) return CatalogCell.Unavailable;
        return field switch
        {
            "force" => Force(place.OwnerId), "corps-name" => Person(place.CorpsId is int corps ? _corps.GetValueOrDefault(corps)?.LeaderId : null),
            "commander" => Person(people.FirstOrDefault(o => view.World.Officers.Single(p => p.Id == o.Id).IsCommander)?.Id),
            "emperor" => new(view.EmperorCityId == id ? "○" : "", view.EmperorCityId == id ? 1 : 0),
            "population" => (long)state.Population, "draft" => (long)state.DraftPopulation,
            "soldiers" => (long)state.Soldiers, "wounded" => (long)state.Wounded,
            "morale" => state.Soldiers == 0 ? CatalogCell.Unavailable : state.Morale,
            "state" => state.ConfusionDaysRemaining > 0 ? "混乱" : "正常",
            "durability" => state.Durability, "max-durability" => state.MaxDurability,
            "public-order" => state.PublicOrder, "commerce" => state.Commerce, "max-commerce" => state.MaxCommerce,
            "cultivation" => state.Cultivation, "max-cultivation" => state.MaxCultivation,
            "money-income" => EconomyMath.Revenue(state), "food-income" => EconomyMath.Harvest(state),
            "merchant" => state.MerchantPresenceChance == 100 ? new("常驻", 2)
                : state.MerchantPresent ? new("驻在", 1) : new("", 0),
            "tactics" => place.ConfiguredTacticId is int tactic ? Tactics([tactic]) : CatalogCell.Unavailable,
            "facility" => place.Kind == SettlementKind.City ? "都市" : place.NameSuffixKey is { } suffix ? texts.Resolve(suffix) : place.ResolveName(texts),
            "draft-growth" => DraftGrowth(state),
            "feature" => view.TacticCourses.FirstOrDefault(c => c.CityId == id) is { } course && view.TacticCategory(course.TacticId) is int group ? OfficerCatalog.Pages[group + 4].Title : CatalogCell.Unavailable,
            "trait" => string.Join("‧", (place.UsesRockfallPower ? new[] { "落石" } : []).Concat(place.HasRangeBonus ? new[] { "射台" } : [])),
            "recovery" => place.WoundedRecovery is int recovery ? recovery : CatalogCell.Unavailable,
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown place field.")
        };
    }
}
