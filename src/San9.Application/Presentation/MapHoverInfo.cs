using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Presentation;

public enum MapHoverTheme { Own, Other, Friendly }
public enum MapInformationContext { Strategy, Execution }
public sealed record MapHoverRow(string Label, string Value, bool Numeric = false);
public sealed record MapHoverInfo(int Id, string Name, MapHoverTheme Theme, ForceView? Force,
    IReadOnlyList<string> Icons, IReadOnlyList<MapHoverRow> Rows, int Height)
{
    public int? FlagGlyph { get; init; }
    public bool IsRulerCommander { get; init; }

    public static MapHoverTheme ThemeFor(int? owner, int? player, int relation) =>
        player is not null && owner == player ? MapHoverTheme.Own : owner is null || relation < 40 ? MapHoverTheme.Other : MapHoverTheme.Friendly;

    public static MapHoverInfo? Resolve(StrategySnapshot view, TextCatalog texts, int id, MapInformationContext context)
    {
        var s = view.Settlements.SingleOrDefault(s => s.Id == id);
        if (s is null || s.IsWork) return null;
        var force = view.Forces.SingleOrDefault(f => f.Id == s.OwnerId);
        var corps = view.World.Corps.SingleOrDefault(c => c.Id == s.CorpsId);
        var staffing = SettlementStaffing.Read(view, id);
        var people = staffing.Present.Where(o => o.IsServing && o.OwnerId == s.OwnerId).ToArray();
        int serving = context switch
        {
            MapInformationContext.Strategy => staffing.Available,
            MapInformationContext.Execution => people.Length,
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };
        string Person(int? person) => view.Officers.SingleOrDefault(o => o.Id == person) is { } o ? texts.Resolve(o.NameKey) : "----";
        int relation = view.World.Relations.SingleOrDefault(r =>
            r.FirstId == view.PlayerForceId && r.SecondId == s.OwnerId || r.SecondId == view.PlayerForceId && r.FirstId == s.OwnerId)?.Value ?? 0;
        bool own = view.PlayerForceId is not null && s.OwnerId == view.PlayerForceId;
        List<MapHoverRow> rows =
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
            new("士兵", s.Soldiers.ToString(), true), new("在任", $"{serving,3}/{staffing.Assigned,3}", true)]);
        return new(id, s.ResolveName(texts), ThemeFor(s.OwnerId, view.PlayerForceId, relation), force,
            city ? s.Icons : [], rows, city ? 316 : 248);
    }
}
