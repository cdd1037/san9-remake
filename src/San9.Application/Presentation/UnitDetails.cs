using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Presentation;

public sealed record FormationMemberView(int OfficerId, string Name, string Tactic, BattleRank Rank, bool IsCommander)
{
    public FormationSlot? Position { get; init; }
    public string HoverText { get; init; } = "";
}
public sealed record FormationBoardData(FormationPresentation? Presentation, IReadOnlyList<FormationMemberView> Members);
public sealed record UnitDetailData(int Id, string Name, string Condition, string Force, string Home,
    string Soldiers, string Wounded, string Morale, string Formation, string Ship,
    IReadOnlyList<InformationField> Orders, CatalogCell[] Land, CatalogCell[] Water,
    FormationBoardData Board, FormationPresentation? LandVision, FormationPresentation? WaterVision,
    IReadOnlyList<int> OfficerIds);

public static class UnitDetails
{
    public static UnitDetailData Read(StrategySnapshot view, MilitaryRulebook? rules, TextCatalog texts, int id)
    {
        var unit = view.World.Units.Single(u => u.Id == id && UnitCatalog.IsListed(u));
        var members = view.Officers.Where(o => o.UnitId == id && o.IsServing && o.OwnerId == unit.OwnerId).ToArray();
        string Person(int person) => texts.Resolve(view.Officers.Single(o => o.Id == person).NameKey);
        var land = view.Information.Formations.FirstOrDefault(f => f.Id == unit.FormationId);
        int? navalId = unit.NavalFormationId ?? (unit.FormationId is int primary && rules?.Formations[primary].TravelMode == TravelMode.Water ? primary : null);
        var water = view.Information.Formations.FirstOrDefault(f => f.Id == navalId);
        string force = texts.Resolve(view.Forces.Single(f => f.Id == unit.OwnerId).NameKey);
        var command = new OfficerCommands(view, texts).ReadUnit(unit);
        string Policy(string value) => unit.OwnerId == view.PlayerForceId ? value : "＊＊＊";
        var boardMembers = members.OrderBy(o => unit.MemberRanks.GetValueOrDefault(o.Id, BattleRank.Middle)).ThenByDescending(o => o.Id == unit.CommanderId).ThenBy(o => o.Id).Select(o =>
        {
            var person = view.World.Officers.Single(p => p.Id == o.Id);
            var tactic = view.Information.Tactics.FirstOrDefault(t => t.Id == person.EquippedTacticId);
            return new FormationMemberView(o.Id, texts.Resolve(o.NameKey), tactic is null ? "—" : texts.Resolve(tactic.NameKey),
                unit.MemberRanks.GetValueOrDefault(o.Id, BattleRank.Middle), o.Id == unit.CommanderId);
        }).ToArray();
        var commander = view.World.Officers.FirstOrDefault(o => o.Id == unit.CommanderId);
        var owner = view.World.Forces.Single(f => f.Id == unit.OwnerId);
        uint? capacity = commander is null ? null : CommanderCapacity.Read(owner.LeaderId == commander.Id,
            view.Information.Ranks.FirstOrDefault(r => r.Id == owner.RankId)?.CommandCapacity,
            view.Information.Offices.FirstOrDefault(o => o.Id == commander.OfficeId)?.CommandCapacity);
        return new(id, (unit.CommanderId is int leader ? Person(leader) : force) + "队", UnitCatalog.Condition(unit.Condition), force,
            view.Settlements.FirstOrDefault(s => s.Id == unit.ServicePlaceId)?.ResolveName(texts) ?? "无",
            capacity is uint max ? $"{unit.Soldiers}/{max}" : unit.Soldiers.ToString(), unit.Wounded.ToString(), unit.Morale.ToString(),
            land is null ? "—" : texts.Resolve(land.NameKey), water is null ? "—" : texts.Resolve(water.NameKey),
            [new("命令", command.Order.Text), new("目标", command.Target.Text), new("敌接近时", Policy(unit.Policies.AttackApproachingEnemies ? "迎击" : "无视")),
             new("自主撤退", Policy(unit.Policies.AutonomousRetreat ? "许可" : "不许可")), new("追击", Policy(unit.Policies.Pursue ? "许可" : "不许可")),
             new("事后命令", Policy(unit.Policies.AttackAfterAction ? "攻击" : "撤退"))],
            UnitCombatPreview.Read(unit, members, rules, false), UnitCombatPreview.Read(unit, members, rules, true),
            new(land?.Presentation, boardMembers), land?.Presentation, water?.Presentation,
            view.Officers.Where(o => o.UnitId == id).Select(o => o.Id).ToArray());
    }
}
