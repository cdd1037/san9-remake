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
            CatalogColumn Name() => new("name", "官职", 8, CatalogAlignment.Left);
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
        if (category is InformationCategory.Formations or InformationCategory.Ships)
        {
            CatalogColumn Name() => new("name", category == InformationCategory.Ships ? "船" : "阵形", category == InformationCategory.Ships ? 4 : 6, CatalogAlignment.Left);
            CatalogColumn Number(string title, int width = 8) => new(title, title, width, CatalogAlignment.Right);
            return [new("特征", [Name(), Number("对部队"), Number("对守兵"), Number("对城壁"), Number("守备力"), Number("机动力"), Number("费用", 6)]),
                new("效果", [Name(), new("effect", "效果", 50, CatalogAlignment.Left)]),
                new("说明", [Name(), new("description", "说明", 50, CatalogAlignment.Left)])];
        }
        throw new ArgumentOutOfRangeException(nameof(category));
    }

    public static InformationTable Read(GameSession session, TextCatalog texts, InformationCategory category, int forceId, int pageIndex = 0, bool personalFormations = false)
    {
        var pages = Pages(category);
        if ((uint)pageIndex >= pages.Count) throw new ArgumentOutOfRangeException(nameof(pageIndex));
        var view = session.View;
        var force = view.World.Forces.Single(f => f.Id == forceId);
        var members = view.World.Officers.Where(o => o.OwnerId == forceId && o.IsServing && !o.IsDormant && o.IsVisible).ToArray();
        var treasures = view.Rules.Treasures;
        string Person(int id) => texts.Resolve(view.Officers.Single(o => o.Id == id).NameKey);
        bool HasTactic(int id) => TacticAvailability.HasForMembers(id, members, force, view.World.Treasures, treasures, view.EquippedItems);
        string Tactic(int? id) => id is int key ? view.Information.Tactics.Single(t => t.Id == key).Name : "—";
        InformationRow Row(int id, params CatalogCell[] cells) => new(new(category, id), cells);
        IEnumerable<InformationRow> rows;
        if (category == InformationCategory.Offices)
        {
            var rules = view.Rules.Governance;
            rows = view.Information.Offices.Where(o => rules.CanAppoint(force.RankId, rules.Offices[o.Id]))
                .Select(o => pageIndex == 1
                    ? Row(o.Id, new CatalogCell(o.Name, o.GovernorPriority), o.Description)
                    : Row(o.Id, new CatalogCell(o.Name, o.GovernorPriority), string.Join("、", members.Where(p => p.OfficeId == o.Id).Select(p => Person(p.Id))),
                        (long)o.CommandCapacity, o.Salary, o.AbilityBonuses.Leadership, o.AbilityBonuses.War,
                        o.AbilityBonuses.Intelligence, o.AbilityBonuses.Politics, rules.Offices[o.Id].LoyaltyBonus));
        }
        else if (session.MilitaryRules is not { } military) rows = [];
        else if (category == InformationCategory.Tactics)
        {
            string[] categories = ["步兵", "骑兵", "弓骑", "弓弩", "水军", "攻城", "智略", "谋略", "策略"];
            rows = view.Information.Tactics.Where(t => military.Tactics[t.Id].Selectable && HasTactic(t.Id)).Select(t =>
            {
                var rule = military.Tactics[t.Id];
                if (pageIndex == 1) return Row(t.Id, t.Name, t.Description);
                string Supports(TravelMode mode) => rule.CompatibleFormations.Any(id => military.Formations[id].TravelMode == mode) ? "○" : "";
                return Row(t.Id, t.Name, rule.Category is int c ? categories[c] : "设施",
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
                bool personal = personalFormations && category == InformationCategory.Formations &&
                    view.World.Simulation.NewGame.MixedTroopsEnabled;
                return (personal ? !military.IsFixedFormation(rule.Id) : military.CanSelectFormation(force, rule.Id)) &&
                    (rule.TravelMode == TravelMode.Water) == (category == InformationCategory.Ships) &&
                    (personal || rule.RequiredTacticId is not int tactic || HasTactic(tactic));
            }).Select(f =>
            {
                var rule = military.Formations[f.Id];
                if (pageIndex == 1)
                {
                    if (category == InformationCategory.Formations)
                        return Row(f.Id, f.Name, FormationEffect(rule));
                    var tactics = military.AutomaticTacticIds(rule, session.Companion);
                    return Row(f.Id, f.Name, tactics.Length == 0 ? "" :
                        "可发动兵法" + string.Concat(tactics.Select(id => $"「{Tactic(id)}」")) + "。");
                }
                if (pageIndex == 2) return Row(f.Id, f.Name, f.Description);
                return Row(f.Id, f.Name, rule.Combat?.AgainstUnits ?? 0, rule.Combat?.AgainstGarrison ?? 0,
                    rule.Combat?.AgainstWalls ?? 0, rule.Combat?.Defense ?? 0,
                    new CatalogCell(((decimal)rule.MovementBudget / MapCoordinates.UnitsPerCell).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), rule.MovementBudget),
                    rule.DeploymentCost);
            });
        }
        else throw new ArgumentOutOfRangeException(nameof(category));
        var page = pages[pageIndex];
        return new(InformationQuery.MenuTitle(category)[1..] + "一览", pages.Select(p => p.Title).ToArray(), page.Columns.Select(c => c.Title).ToArray(), rows.ToArray());
    }
    private static string FormationEffect(FormationRule rule)
    {
        string[] categories = ["步兵", "骑兵", "弓骑", "弓弩", "水军", "攻城", "智略", "谋略", "策略"];
        List<string> effects = [];
        if (rule.Tactics is { FavoredCategories.Length: > 0 } tactics)
            effects.Add("易于发动" + string.Join("、", tactics.FavoredCategories.Select(c => categories[c])) + "系兵法");
        if (rule.DoublesCapture) effects.Add("较易俘获敌方武将");
        if (rule.Tactics is { LinkMultiplier: > 2 }) effects.Add("较易发动兵法连锁");
        if (rule.DoublesDuelChance) effects.Add("较易发动单挑");
        if (rule.SnipingChanceBonus > 0) effects.Add("较易发动狙击");
        if (rule.ImmuneToDisorder) effects.Add("不会陷入混乱");
        else if (rule.HalvesHostileStrategyChance || rule.ConfusionResistanceChance > 0)
            effects.Add("状态不易异常");
        if (rule.Combat is { WoundedPercent: > 50 }) effects.Add("士兵不易死亡");
        return effects.Count == 0 ? "" : string.Join("。", effects) + "。";
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
