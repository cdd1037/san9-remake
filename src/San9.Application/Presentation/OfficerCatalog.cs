using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Presentation;

public static class OfficerCatalog
{
    public static int DisplayLoyalty(int loyalty) => Math.Min(100, loyalty);
    private static CatalogColumn C(string id, string title, int width, CatalogAlignment alignment = CatalogAlignment.Center) => new(id, title, width, alignment);
    private static CatalogColumn N(string id, string title, int width = 3) => C(id, title, width, CatalogAlignment.Right);
    private static CatalogPage P(string title, params CatalogColumn[] columns) => new(title, [C("name", "武将", 8, CatalogAlignment.Left), .. columns]);
    public static IReadOnlyList<string> TacticCategories { get; } = Array.AsReadOnly(new[]
        { "步兵", "骑兵", "弓骑", "弓弩", "水军", "攻城", "智略", "谋略", "策略" });
    public static IReadOnlyList<CatalogPage> CorePages { get; } = Array.AsReadOnly(new[]
    {
        P("能力", N("leadership", "统率"), N("war", "武力"), N("intelligence", "智力"), N("politics", "政治"), N("learned", "学得", 2), C("health", "健康", 4), N("treasures", "宝物"), C("status", "身分", 4)),
        P("所属", C("force", "势力", 8), C("corps", "军团", 8), C("home", "所属", 10), C("location", "所在", 10), C("status", "身分", 4), C("commander", "大将", 2), N("loyalty", "忠诚")),
        P("命令", C("order", "命令", 4), C("target-person", "对象", 8), C("target-place", "目标", 10), N("arrival", "到达预定", 2), C("formation", "布阵", 4), C("equipped-tactic", "兵法", 4)),
        P("个人", C("affinity", "相性", 4), C("office", "官职", 8), C("personal-peerage", "爵位", 8), N("capacity", "指挥", 5), N("salary", "俸禄"), N("service", "仕官"), C("personality", "性格", 4), C("parent", "父母", 8), N("age", "年龄", 2), N("gender", "性别", 1))
    });

    public static IReadOnlyList<CatalogPage> Pages(StrategySnapshot view, TextCatalog texts) =>
        Pages(view.Information.Tactics, view.MilitaryRules?.Tactics.Values ?? [], texts);

    public static IReadOnlyList<CatalogPage> Pages(IEnumerable<San9.Content.Scenarios.MilitaryCatalogDefinition> tactics,
        IEnumerable<TacticRule> rules, TextCatalog texts)
    {
        var personal = rules.Where(t => t.IsOfficerTactic).ToDictionary(t => t.Id);
        var groups = tactics.Where(t => personal.ContainsKey(t.Id)).OrderBy(t => t.Id).ToLookup(t => personal[t.Id].Category);
        return [.. CorePages, .. TacticCategories.Select((title, index) => P(title,
            [C($"mastery.{index}", "精通", 2), N($"proficiency.{index}", title + "熟练", 4),
             .. groups[index].Select(t => C($"tactic.{t.Id}", t.Name, 2))]))];
    }

    public static InformationTable Read(StrategySnapshot view, TextCatalog texts, int page, int? force, int? settlement, int? officerId = null, int? corpsId = null, int? regionId = null, int? unitFilter = null)
    {
        var pages = Pages(view, texts);
        if ((uint)page >= pages.Count) throw new ArgumentOutOfRangeException(nameof(page));
        var officers = view.Officers.ToDictionary(o => o.Id);
        var people = view.World.Officers.ToDictionary(o => o.Id);
        var forces = view.World.Forces.ToDictionary(f => f.Id);
        var offices = view.Information.Offices.ToDictionary(o => o.Id);
        var ranks = view.Information.Ranks.ToDictionary(r => r.Id);
        var corps = view.World.Corps.ToDictionary(c => c.Id);
        var units = view.World.Units.ToDictionary(u => u.Id);
        var commands = page == 2 ? new OfficerCommands(view, texts) : null;
        var treasuresByHolder = view.World.Treasures.Where(t => t.Discovered).ToLookup(t => t.HolderId);
        var treasureRules = view.Rules.Treasures;
        string Person(int? id) => id is int key && officers.TryGetValue(key, out var p) ? texts.Resolve(p.NameKey) : "";
        string Place(int? id) => id is int key ? view.Settlements.FirstOrDefault(s => s.Id == key)?.ResolveName(texts)
            ?? (view.RegionNameKeys.TryGetValue(key, out var name) ? texts.Resolve(name) : "") : "";
        var columns = pages[page].Columns;
        var rows = view.Officers.Where(o => (unitFilter is null || o.UnitId == unitFilter) && (corpsId is null || o.CorpsId == corpsId) && (regionId is null || o.ServicePlaceId == regionId) && (officerId is null || o.Id == officerId) && (force is null || o.OwnerId == force) && (settlement is null || o.ServicePlaceId == settlement))
            .OrderBy(o => texts.Resolve(o.NameKey), StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("zh-CN"), false))
            .Select(o =>
            {
                var person = people[o.Id];
                var command = commands?.Read(person, view.PlayerForceId);
                var owner = o.OwnerId is int forceId ? forces.GetValueOrDefault(forceId) : null;
                var office = person.OfficeId is int officeId ? offices.GetValueOrDefault(officeId) : null;
                bool ruler = person.Status == OfficerStatus.Ruler;
                CatalogCell Office() => person.Status switch
                {
                    OfficerStatus.Ruler => new("—", long.MaxValue),
                    OfficerStatus.Captive => new("—", -2),
                    OfficerStatus.Free => new("—", -3),
                    _ => new(office is null ? "" : office.Name, office?.GovernorPriority ?? -1)
                };
                var treasures = treasuresByHolder[o.Id].ToArray();
                var tactics = page == 0 || page >= 4 ? TacticAvailability.ForOfficer(person, owner, treasures, treasureRules, view.EquippedItems) : null;
                CatalogCell Commander()
                {
                    if (person.Status == OfficerStatus.Free ||
                        person.SettlementId is null && person.UnitId is null && o.Journey is null)
                        return CatalogCell.Unavailable;
                    bool commander = o.Journey is null && (person.UnitId is int unitId
                        ? units[unitId].CommanderId == person.Id : person.IsCommander);
                    return new(commander ? "○" : "", commander ? 1 : 0);
                }
                CatalogCell Location()
                {
                    if (o.Journey is not null)
                        return o.OwnerId == view.PlayerForceId ? new("移动中") : new("＊＊＊", IsUnavailable: true);
                    if (o.UnitId is not int currentUnit) return new(Place(o.SettlementId ?? person.RegionId));
                    var unit = units[currentUnit];
                    string name = unit.CommanderId is int commander ? Person(commander)
                        : view.Forces.FirstOrDefault(f => f.Id == unit.OwnerId) is { } forceView ? texts.Resolve(forceView.NameKey) : "无";
                    return new(name + "队");
                }
                CatalogCell Cell(string id)
                {
                    if (id.StartsWith("mastery.", StringComparison.Ordinal))
                    {
                        bool mastered = person.MasteredCategories.Contains(int.Parse(id.AsSpan(8)));
                        return new(mastered ? "☆" : "", mastered ? 1 : 0);
                    }
                    if (id.StartsWith("proficiency.", StringComparison.Ordinal)) return person.TacticProficiencies[int.Parse(id.AsSpan(12))];
                    if (id.StartsWith("tactic.", StringComparison.Ordinal))
                    {
                        int tactic = int.Parse(id.AsSpan(7));
                        var sources = tactics!.GetValueOrDefault(tactic);
                        bool learned = (sources & TacticSource.Learned) != 0;
                        bool provided = sources != TacticSource.None;
                        return new(learned ? "○" : provided ? "△" : "", learned ? 2 : provided ? 1 : 0);
                    }
                    return id switch
                    {
                        "name" => texts.Resolve(o.NameKey), "leadership" => o.Abilities.Leadership, "war" => o.Abilities.War,
                        "intelligence" => o.Abilities.Intelligence, "politics" => o.Abilities.Politics,
                        "learned" => tactics!.Count, "health" => new(person.Injury == 0 ? "良" : person.Injury <= 4 ? "轻伤" : "重伤", person.Injury),
                        "treasures" => treasures.Length, "status" => new(InformationQuery.Status(o.Status), (int)o.Status),
                        "force" => Person(owner?.LeaderId), "corps" => Person(o.CorpsId is int currentCorpsId ? corps.GetValueOrDefault(currentCorpsId)?.LeaderId : null),
                        "commander" => Commander(),
                        "home" => Place(o.ServicePlaceId),
                        "location" => Location(),
                        "order" => command!.Order, "target-person" => command!.Object, "target-place" => command!.Target,
                        "arrival" => command!.Arrival, "formation" => command!.Rank, "equipped-tactic" => command!.Tactic,
                        // A ruler's dash has a highest-value sort key, not unavailable-value ordering.
                        "loyalty" => o.Status == OfficerStatus.Ruler ? new("—", long.MaxValue)
                            : owner is not null ? DisplayLoyalty(person.Loyalty) : CatalogCell.Unavailable,
                        "office" => Office(),
                        "personal-peerage" => OfficerDetails.PeerageName(person.PersonalPeerage),
                        "capacity" => person.IsServing ? (long)CommanderCapacity.Read(ruler,
                            owner?.RankId is int rankId ? ranks.GetValueOrDefault(rankId)?.CommandCapacity : null,
                            office?.CommandCapacity) : CatalogCell.Unavailable,
                        "salary" => ruler ? new("—", long.MaxValue)
                            : person.IsServing ? office?.Salary ?? 1 : CatalogCell.Unavailable,
                        "service" => ruler ? new("—", long.MaxValue)
                            : o.IsServing && person.StatusStartYear is int year ? view.Year - year + 1 : CatalogCell.Unavailable,
                        "parent" => person.ParentId is null ? "" : Person(person.ParentId),
                        "age" => !view.Information.ShowOfficerAges ? new("**", IsUnavailable: true)
                            : person.BirthYear > 0 ? view.Year - person.BirthYear + 1 : CatalogCell.Unavailable,
                        "gender" => person.IsFemale ? "女" : "男",
                        "affinity" => person.Affinity,
                        "personality" => new[] { "莽撞", "刚胆", "冷静", "慎重" }[person.Personality],
                        _ => CatalogCell.Unavailable
                    };
                }
                return new InformationRow(new(InformationCategory.Officers, o.Id), columns.Select(c => Cell(c.Id)).ToArray(), o.SettlementId);
            }).ToArray();
        return new("武将一览", pages.Select(p => p.Title).ToArray(), columns.Select(c => c.Title).ToArray(), rows);
    }
}
