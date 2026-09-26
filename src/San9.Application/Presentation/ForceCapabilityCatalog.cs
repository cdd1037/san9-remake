using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Presentation;

public static class ForceCapabilityCatalog
{
    public static bool Supports(InformationCategory category) => category is InformationCategory.Offices or
        InformationCategory.Tactics or InformationCategory.Formations or InformationCategory.Ships;

    public static IReadOnlyList<CatalogPage> Pages(InformationCategory category)
    {
        CatalogColumn Column(string title, int width = 8) => new(title, title, width, CatalogAlignment.Center);
        if (category == InformationCategory.Offices)
        {
            CatalogColumn Name() => new("name", "爵位", 8, CatalogAlignment.Left);
            CatalogColumn Number(string title, int width = 6) => new(title, title, width, CatalogAlignment.Right);
            return [new("特征", [Name(), new("holder", "武将", 8, CatalogAlignment.Center), Number("指挥"), Number("俸禄"),
                Number("统率"), Number("武力"), Number("智力"), Number("政治"), Number("忠诚上升", 10)]),
                new("说明", [Name(), new("description", "说明", 34, CatalogAlignment.Left)])];
        }
        if (category == InformationCategory.Tactics)
            return [new("特征", [new("name", "兵法", 6, CatalogAlignment.Left), Column("系统", 6),
                new("power", "威力", 6, CatalogAlignment.Right), Column("陆上", 6), Column("水上", 6), Column("对设施", 8),
                new("training", "熟练要求", 10, CatalogAlignment.Right), new("interval", "使用间隔", 10, CatalogAlignment.Right)]),
                new("说明", [new("name", "兵法", 6, CatalogAlignment.Left), new("description", "说明", 40, CatalogAlignment.Left)])];
        if (category == InformationCategory.Ships)
        {
            CatalogColumn Name() => new("name", "船", 4, CatalogAlignment.Left);
            CatalogColumn Number(string title, int width = 8) => new(title, title, width, CatalogAlignment.Right);
            return [new("特征", [Name(), Number("对部队"), Number("对守兵"), Number("对城壁"), Number("守备力"), Number("机动力"), Number("费用", 6)]),
                new("效果", [Name(), new("effect", "效果", 50, CatalogAlignment.Left)]),
                new("说明", [Name(), new("description", "说明", 50, CatalogAlignment.Left)])];
        }
        return [new("特征", category switch
        {
            InformationCategory.Formations or InformationCategory.Ships => [Column(category == InformationCategory.Ships ? "船" : "阵形", 12),
                Column("对部队"), Column("对驻军"), Column("对城墙"), Column("防御"), Column("移动预算", 10), Column("费用"), Column("所需兵法", 12)],
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        })];
    }

    public static InformationTable Read(GameSession session, TextCatalog texts, InformationCategory category, int forceId, int pageIndex = 0)
    {
        var pages = Pages(category);
        if ((uint)pageIndex >= pages.Count) throw new ArgumentOutOfRangeException(nameof(pageIndex));
        var view = session.View;
        var force = view.World.Forces.Single(f => f.Id == forceId);
        var members = view.World.Officers.Where(o => o.OwnerId == forceId && o.IsServing && !o.IsDormant && o.IsVisible).ToArray();
        var treasures = new TreasureRulebook(view.Information.Treasures.Select(t => t.ToRule()));
        string Person(int id) => texts.Resolve(view.Officers.Single(o => o.Id == id).NameKey);
        bool HasTactic(int id) => TacticAvailability.HasForMembers(id, members, force, view.World.Treasures, treasures);
        string Tactic(int? id) => id is int key ? texts.Resolve(view.Information.Tactics.Single(t => t.Id == key).NameKey) : "—";
        InformationRow Row(int id, params CatalogCell[] cells) => new(new(category, id), cells);
        IEnumerable<InformationRow> rows;
        if (category == InformationCategory.Offices)
        {
            var rules = new GovernanceRulebook(view.Information.Offices.Select(o => new GovernorOfficeRule(o.Id, o.CommandCapacity, o.GovernorPriority)
                { RequiredRankId = o.RequiredRankId, Salary = o.Salary }), view.Information.Ranks.ToDictionary(r => r.Id, r => r.Precedence));
            rows = view.Information.Offices.Where(o => rules.CanAppoint(force.RankId, rules.Offices[o.Id]))
                .Select(o => pageIndex == 1
                    ? Row(o.Id, texts.Resolve(o.NameKey), o.DescriptionKey is { } key ? texts.Resolve(key) : "")
                    : Row(o.Id, texts.Resolve(o.NameKey), string.Join("、", members.Where(p => p.OfficeId == o.Id).Select(p => Person(p.Id))),
                        (long)o.CommandCapacity, o.Salary, o.AbilityBonuses.Leadership, o.AbilityBonuses.War,
                        o.AbilityBonuses.Intelligence, o.AbilityBonuses.Politics, rules.Offices[o.Id].LoyaltyBonus));
        }
        else if (session.MilitaryRules is not { } military) rows = [];
        else if (category == InformationCategory.Tactics)
        {
            string[] categories = ["步兵", "骑兵", "弓骑", "弩兵", "水军", "攻城", "智略", "谋略", "策略"];
            rows = view.Information.Tactics.Where(t => military.Tactics[t.Id].Selectable && HasTactic(t.Id)).Select(t =>
            {
                var rule = military.Tactics[t.Id];
                if (pageIndex == 1) return Row(t.Id, texts.Resolve(t.NameKey), t.DescriptionKey is { } key ? texts.Resolve(key) : "");
                string Supports(TravelMode mode) => rule.CompatibleFormations.Any(id => military.Formations[id].TravelMode == mode) ? "○" : "";
                return Row(t.Id, texts.Resolve(t.NameKey), rule.Category is int c ? categories[c] : "设施",
                    rule.Effect == TacticEffect.Martial ? (CatalogCell)rule.Power : CatalogCell.Unavailable,
                    Supports(TravelMode.Land), Supports(TravelMode.Water), rule.AffectsFacilities ? "○" : "",
                    rule.TrainingRequirement, Interval(rule, military));
            });
        }
        else if (category is InformationCategory.Formations or InformationCategory.Ships)
        {
            rows = view.Information.Formations.Where(f =>
            {
                var rule = military.Formations[f.Id];
                return rule.IsCandidateFor(force) &&
                    (rule.TravelMode == TravelMode.Water) == (category == InformationCategory.Ships) &&
                    (rule.RequiredTacticId is not int tactic || HasTactic(tactic));
            }).Select(f =>
            {
                var rule = military.Formations[f.Id];
                if (category == InformationCategory.Ships)
                {
                    if (pageIndex == 1)
                    {
                        var tactics = rule.Tactics?.AutomaticTactics ?? [];
                        return Row(f.Id, texts.Resolve(f.NameKey), tactics.Length == 0 ? "" :
                            "可发动兵法" + string.Concat(tactics.Select(id => $"「{Tactic(id)}」")) + "。");
                    }
                    if (pageIndex == 2) return Row(f.Id, texts.Resolve(f.NameKey), f.DescriptionKey is { } key ? texts.Resolve(key) : "");
                    return Row(f.Id, texts.Resolve(f.NameKey), rule.Combat?.AgainstUnits ?? 0, rule.Combat?.AgainstGarrison ?? 0,
                        rule.Combat?.AgainstWalls ?? 0, rule.Combat?.Defense ?? 0,
                        new CatalogCell(((decimal)rule.MovementBudget / MapCoordinates.UnitsPerCell).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), rule.MovementBudget),
                        rule.DeploymentCost);
                }
                return Row(f.Id, texts.Resolve(f.NameKey), rule.Combat?.AgainstUnits ?? 0, rule.Combat?.AgainstGarrison ?? 0,
                    rule.Combat?.AgainstWalls ?? 0, rule.Combat?.Defense ?? 0, rule.MovementBudget, rule.DeploymentCost, Tactic(rule.RequiredTacticId));
            });
        }
        else throw new ArgumentOutOfRangeException(nameof(category));
        var page = pages[pageIndex];
        return new(InformationQuery.MenuTitle(category)[1..] + "一览", pages.Select(p => p.Title).ToArray(), page.Columns.Select(c => c.Title).ToArray(), rows.ToArray());
    }
    private static CatalogCell Interval(TacticRule rule, MilitaryRulebook military)
    {
        if (rule.Effect != TacticEffect.TrapSelection) return rule.MinimumInterval;
        var intervals = military.Tactics.Values.Where(t => t.Effect == TacticEffect.Trap)
            .Select(t => t.MinimumInterval).ToArray();
        if (intervals.Length == 0) return CatalogCell.Unavailable;
        int minimum = intervals.Min(), maximum = intervals.Max();
        return minimum == maximum ? (CatalogCell)minimum : new CatalogCell($"{minimum}～{maximum}");
    }

}
