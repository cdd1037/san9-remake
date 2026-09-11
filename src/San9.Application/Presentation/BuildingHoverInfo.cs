using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Presentation;

public enum BuildingHoverTheme { Own, Other, Friendly }
public sealed record BuildingHoverRow(string Label, string Value, bool Numeric = false);
public sealed record BuildingHoverInfo(int Id, string Name, BuildingHoverTheme Theme, ForceView? Force,
    IReadOnlyList<string> Icons, IReadOnlyList<BuildingHoverRow> Rows, int Height)
{
    public static BuildingHoverTheme ThemeFor(int? owner, int player, int relation) =>
        owner == player ? BuildingHoverTheme.Own : owner is null || relation < 40 ? BuildingHoverTheme.Other : BuildingHoverTheme.Friendly;

    public static BuildingHoverInfo? Resolve(StrategySnapshot view, TextCatalog texts, int id)
    {
        var s = view.Settlements.SingleOrDefault(s => s.Id == id);
        if (s is null) return null;
        var force = view.Forces.SingleOrDefault(f => f.Id == s.OwnerId);
        var corps = view.World.Corps.SingleOrDefault(c => c.Id == s.CorpsId);
        var people = view.World.Officers.Where(o => o.SettlementId == id && o.IsServing && o.OwnerId == s.OwnerId).ToArray();
        string Person(int? person) => view.Officers.SingleOrDefault(o => o.Id == person) is { } o ? texts.Resolve(o.NameKey) : "----";
        int relation = view.World.Relations.SingleOrDefault(r =>
            r.FirstId == view.PlayerForceId && r.SecondId == s.OwnerId || r.SecondId == view.PlayerForceId && r.FirstId == s.OwnerId)?.Value ?? 0;
        bool own = s.OwnerId == view.PlayerForceId;
        List<BuildingHoverRow> rows =
        [
            new("状态", "一般"),
            new(own ? "军团" : "势力", own ? Person(corps?.LeaderId) : force is null ? "----" : texts.Resolve(force.NameKey)),
            new("大将", Person(people.SingleOrDefault(o => o.IsCommander)?.Id))
        ];
        bool city = s.Kind == SettlementKind.City;
        if (city) rows.AddRange([new("民心", s.PublicOrder.ToString(), true),
            new("收益", $"{s.Commerce}/{s.MaxCommerce}", true), new("收获", $"{s.Cultivation}/{s.MaxCultivation}", true)]);
        rows.AddRange([new("耐久", $"{s.Durability}/{s.MaxDurability}", true),
            new("士气", s.Soldiers == 0 ? "----" : $"{s.Morale}/100", true),
            new("士兵", s.Soldiers.ToString(), true), new("在任", $"{people.Count(o => !o.Acted && !view.World.Simulation.Tasks.Any(t => t.OfficerId == o.Id)),3}/{people.Length,3}", true)]);
        return new(id, texts.Resolve(s.NameKey), ThemeFor(s.OwnerId, view.PlayerForceId, relation), force,
            city ? s.Icons : [], rows, city ? 316 : 248);
    }
}
