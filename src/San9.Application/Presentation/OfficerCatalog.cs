using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Presentation;

public static class OfficerCatalog
{
    private static CatalogColumn C(string id, string title, int width, CatalogAlignment alignment = CatalogAlignment.Center) => new(id, title, width, alignment);
    private static CatalogColumn N(string id, string title, int width = 3) => C(id, title, width, CatalogAlignment.Right);
    private static CatalogPage P(string title, params CatalogColumn[] columns) => new(title, [C("name", "武将", 8, CatalogAlignment.Left), .. columns]);
    private static CatalogPage T(string title, int category, params string[] tactics) => P(title,
        [N($"proficiency.{category}", title + "熟练", 4), .. tactics.Select((name, index) => C($"tactic.{TacticStarts[category] + index}", name, 2))]);
    private static readonly int[] TacticStarts = [0, 3, 6, 9, 12, 15, 19, 23, 27];
    public static IReadOnlyList<CatalogPage> Pages { get; } = Array.AsReadOnly(new[]
    {
        P("能力", N("leadership", "统率"), N("war", "武力"), N("intelligence", "智力"), N("politics", "政治"), N("learned", "学得", 2), C("health", "健康", 4), N("treasures", "宝物"), C("status", "身分", 4)),
        P("所属", C("force", "势力", 8), C("corps", "军团", 8), C("home", "所属", 10), C("location", "所在", 10), C("status", "身分", 4), C("commander", "大将", 2), N("loyalty", "忠诚")),
        P("命令", C("order", "命令", 4), C("target-person", "对象", 8), C("target-place", "目标", 10), N("arrival", "到达预定", 2), C("formation", "布阵", 4), C("equipped-tactic", "兵法", 4)),
        P("个人", C("office", "爵位", 8), N("capacity", "指挥", 5), N("salary", "俸禄"), N("service", "仕官"), C("personality", "性格", 4), C("parent", "父母", 8), N("age", "年龄", 2), N("gender", "性别", 1), C("promoted", "擢升", 2)),
        T("步兵", 0, "奋战", "奋斗", "奋迅"), T("骑兵", 1, "突破", "突进", "突击"), T("弓骑", 2, "骑射", "奔射", "飞射"),
        T("弩兵", 3, "齐射", "连射", "连弩"), T("水军", 4, "艨艟", "楼船", "斗舰"), T("攻城", 5, "井阑", "冲车", "发石", "象兵"),
        T("智略", 6, "营造", "石兵", "破陷", "教谕"), T("谋略", 7, "混乱", "陷阱", "攻心", "幻术"), T("策略", 8, "辱骂", "鼓舞", "治疗", "妖术")
    });

    public static InformationTable Read(StrategySnapshot view, TextCatalog texts, int page, int? force, int? settlement, int? officerId = null)
    {
        if ((uint)page >= Pages.Count) throw new ArgumentOutOfRangeException(nameof(page));
        string Person(int? id) => view.Officers.FirstOrDefault(o => o.Id == id) is { } p ? texts.Resolve(p.NameKey) : "";
        string Place(int? id) => id is int key && view.RegionNameKeys.TryGetValue(key, out var name) ? texts.Resolve(name) : "";
        var columns = Pages[page].Columns;
        var rows = view.Officers.Where(o => (officerId is null || o.Id == officerId) && (force is null || o.OwnerId == force) && (settlement is null || o.SettlementId == settlement))
            .OrderBy(o => texts.Resolve(o.NameKey), StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("zh-CN"), false))
            .Select(o =>
            {
                var person = view.World.Officers.Single(p => p.Id == o.Id);
                var owner = view.World.Forces.FirstOrDefault(f => f.Id == o.OwnerId);
                var office = view.Information.Offices.FirstOrDefault(f => f.Id == person.OfficeId);
                var treasures = view.World.Treasures.Where(t => t.HolderId == o.Id && t.Discovered).ToArray();
                CatalogCell Cell(string id)
                {
                    if (id.StartsWith("proficiency.", StringComparison.Ordinal)) return person.TacticProficiencies[int.Parse(id.AsSpan(12))];
                    if (id.StartsWith("tactic.", StringComparison.Ordinal))
                    {
                        int tactic = int.Parse(id.AsSpan(7));
                        bool learned = person.LearnedTactics.Contains(tactic);
                        bool provided = owner?.ProvidedTactics.Contains(tactic) == true || treasures.Any(t => view.Information.Treasures.Single(d => d.Id == t.Id).TacticId == tactic);
                        return new(learned ? "○" : provided ? "△" : "", learned ? 2 : provided ? 1 : 0);
                    }
                    return id switch
                    {
                        "name" => texts.Resolve(o.NameKey), "leadership" => o.Abilities.Leadership, "war" => o.Abilities.War,
                        "intelligence" => o.Abilities.Intelligence, "politics" => o.Abilities.Politics,
                        "learned" => person.LearnedTactics.Length, "health" => new(person.Injury == 0 ? "良" : person.Injury <= 4 ? "轻伤" : "重伤", person.Injury),
                        "treasures" => treasures.Length, "status" => new(InformationQuery.Status(o.Status), (int)o.Status),
                        "force" => Person(owner?.LeaderId), "corps" => Person(view.World.Corps.FirstOrDefault(c => c.Id == o.CorpsId)?.LeaderId),
                        "home" => Place(o.IsServing ? o.SettlementId : null), "location" => o.Journey is null ? Place(o.SettlementId ?? person.RegionId) : "出行中",
                        "order" => o.Journey?.Kind switch { "personnel-search" => "搜索", "personnel-recruit" => "登用", "personnel-move" => "调动", _ => CatalogCell.Unavailable },
                        "target-person" => o.Journey is null ? CatalogCell.Unavailable : Person(o.Journey.TargetOfficerId),
                        "target-place" => o.Journey is null ? CatalogCell.Unavailable : Place(o.Journey.DestinationPlaceId),
                        "arrival" => o.Journey is null ? CatalogCell.Unavailable : $"{o.Journey.Due.Month}/{o.Journey.Due.Day}",
                        "loyalty" => o.IsServing ? person.Loyalty : CatalogCell.Unavailable,
                        "office" => office is null ? "无" : texts.Resolve(office.NameKey),
                        "capacity" => office is null ? CatalogCell.Unavailable : (long)office.CommandCapacity,
                        "salary" => office is null ? CatalogCell.Unavailable : office.Salary,
                        "service" => o.IsServing && person.ServiceStartYear is int year ? view.Year - year + 1 : CatalogCell.Unavailable,
                        "parent" => person.ParentId is null ? "" : Person(person.ParentId),
                        "age" => person.BirthYear > 0 ? view.Year - person.BirthYear + 1 : CatalogCell.Unavailable,
                        "gender" => person.IsFemale ? "女" : "男",
                        "personality" => new[] { "莽撞", "刚胆", "冷静", "慎重" }[person.Personality],
                        _ => CatalogCell.Unavailable
                    };
                }
                return new InformationRow(new(InformationCategory.Officers, o.Id), columns.Select(c => Cell(c.Id)).ToArray(), o.SettlementId);
            }).ToArray();
        return new("武将一览", Pages.Select(p => p.Title).ToArray(), columns.Select(c => c.Title).ToArray(), rows);
    }
}
