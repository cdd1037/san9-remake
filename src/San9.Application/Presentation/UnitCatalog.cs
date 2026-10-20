using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Presentation;

public static class UnitCatalog
{
    private static CatalogColumn Name => new("name", "部队", 10, CatalogAlignment.Left);
    private static CatalogColumn Text(string title, int cells = 8) => new(title, title, cells, CatalogAlignment.Center);
    private static CatalogColumn Number(string title, int cells = 6) => new(title, title, cells, CatalogAlignment.Right);
    public static IReadOnlyList<CatalogPage> Pages { get; } = [
        new("所属", [Name, Text("势力"), Text("军团"), Text("所属", 10), Number("在任"), Number("俘虏")]),
        new("战力", [Name, Number("士兵"), Number("伤兵"), Number("士气"), Text("状态", 6), Text("阵形", 6), Text("船", 4)]),
        new("陆上", [Name, Number("对部队", 8), Number("对守卫", 8), Number("对城墙", 8), Number("守备力", 8), Number("机动力", 8)]),
        new("水上", [Name, Number("对部队", 8), Number("对守卫", 8), Number("对城墙", 8), Number("守备力", 8), Number("机动力", 8)]),
        new("命令", [Name, Text("命令", 6), Text("目标", 10), Text("敌接近时", 10), Text("自主撤退", 10), Text("追击", 6), Text("事后命令", 10), Text("命令变更", 10)])
    ];

    public static bool IsListed(UnitState unit) => !unit.IsEmbedded && !unit.IsPersonnel;

    public static InformationTable Read(StrategySnapshot view, MilitaryRulebook? rules, TextCatalog texts,
        int page = 0, int? force = null, int? corps = null, int? region = null)
    {
        if ((uint)page >= Pages.Count) throw new ArgumentOutOfRangeException(nameof(page));
        string Person(int? id) => view.Officers.FirstOrDefault(o => o.Id == id) is { } person ? texts.Resolve(person.NameKey) : "无";
        string Force(int id) => view.Forces.FirstOrDefault(f => f.Id == id) is { } owner ? texts.Resolve(owner.NameKey) : "无";
        string Formation(int? id) => view.Information.Formations.FirstOrDefault(f => f.Id == id) is { } formation ? texts.Resolve(formation.NameKey) : "—";
        var commands = new OfficerCommands(view, texts);
        var rows = view.World.Units.Where(u => IsListed(u) && (force is null || u.OwnerId == force) &&
            (corps is null || u.CorpsId == corps) && (region is null || view.RegionAt(u.Position) == region)).Select(unit =>
        {
            CatalogCell name = (unit.CommanderId is int commander ? Person(commander) : Force(unit.OwnerId)) + "队";
            var members = view.Officers.Where(o => o.UnitId == unit.Id).ToArray();
            CatalogCell[] cells;
            if (page == 0)
            {
                var group = view.World.Corps.FirstOrDefault(c => c.Id == unit.CorpsId);
                var home = ServicePlace(view, unit);
                cells = [name, Force(unit.OwnerId), Person(group?.LeaderId), home?.ResolveName(texts) ?? "无",
                    members.Count(o => o.IsServing && o.OwnerId == unit.OwnerId), members.Count(o => o.Status == OfficerStatus.Captive)];
            }
            else if (page == 1)
                cells = [name, (long)unit.Soldiers, (long)unit.Wounded, unit.Morale, Condition(unit.Condition), Formation(unit.FormationId), Formation(unit.NavalFormationId)];
            else if (page is 2 or 3)
            {
                cells = [name, .. UnitCombatPreview.Read(unit, members, rules, page == 3)];
            }
            else
            {
                var command = commands.ReadUnit(unit);
                CatalogCell Policy(string value) => unit.OwnerId == view.PlayerForceId ? new(value) : new("＊＊＊", IsUnavailable: true);
                cells = [name, command.Order, command.Target,
                    Policy(unit.Policies.AttackApproachingEnemies ? "迎击" : "无视"),
                    Policy(unit.Policies.AutonomousRetreat ? "许可" : "不许可"), Policy(unit.Policies.Pursue ? "许可" : "不许可"),
                    Policy(unit.Policies.AttackAfterAction ? "攻击" : "撤退"), Policy(unit.CommandedThisXun ? "完" : "未")];
            }
            return new InformationRow(new(InformationCategory.Units, unit.Id), cells);
        }).ToArray();
        return new("部队一览", Pages.Select(p => p.Title).ToArray(), Pages[page].Columns.Select(c => c.Title).ToArray(), rows);
    }

    internal static SettlementView? ServicePlace(StrategySnapshot view, UnitState unit)
    {
        int? root = view.World.Corps.SingleOrDefault(c => c.OwnerId == unit.OwnerId && c.IsRoot)?.Id;
        return view.World.Settlements.FirstOrDefault(s => s.Id == unit.ServicePlaceId &&
            UnitServicePlaceRules.IsEligible(unit, s, root)) is { } place
                ? view.Settlements.FirstOrDefault(s => s.Id == place.Id) : null;
    }

    internal static string Condition(UnitCondition condition) => condition switch
    {
        UnitCondition.Normal => "一般", UnitCondition.Engaged => "交战", UnitCondition.Trap => "陷阱",
        UnitCondition.Disguised => "伪报", UnitCondition.NoFormation => "无阵", UnitCondition.Confused => "混乱",
        UnitCondition.Routed => "溃败", UnitCondition.HighSpirits => "高昂", _ => throw new ArgumentOutOfRangeException(nameof(condition))
    };
}
