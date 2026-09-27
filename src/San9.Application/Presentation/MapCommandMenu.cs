using San9.Core.Domain;

namespace San9.Application.Presentation;

public enum MapCommandTone { Blue, Green, Brown }
/// <summary>Presentation-only navigation; leaves deliberately carry no executable command.</summary>
public sealed record MapCommandEntry(string Id, string Title, MapCommandTone Tone, IReadOnlyList<MapCommandEntry> Children)
{
    public string? UnavailableReasonKey { get; init; }
    public bool Enabled => UnavailableReasonKey is null;
}

public static class MapCommandMenu
{
    private static MapCommandEntry Group(string id, string title, MapCommandTone tone, params (string Id, string Title)[] children) =>
        new(id, title, tone, Array.AsReadOnly(children.Select(c => new MapCommandEntry(c.Id, c.Title, tone, [])).ToArray()));

    private static readonly MapCommandEntry Facilities = Group("facilities", "设施", MapCommandTone.Blue,
        ("patrol", "巡察"), ("commerce", "商业"), ("cultivate", "开垦"), ("repair", "修筑"),
        ("recruit", "征兵"), ("train", "训练"), ("buy", "买进"), ("sell", "卖出"));
    private static readonly IReadOnlyList<MapCommandEntry> Common = Array.AsReadOnly(new[] {
        Group("military", "军事", MapCommandTone.Green, ("sortie", "出征"), ("build", "建设"), ("transport", "输送")),
        Group("talent", "人才", MapCommandTone.Green, ("summon", "召来"), ("move", "移动"), ("search", "探索"), ("employ", "登庸")),
        Group("strategy", "计略", MapCommandTone.Green, ("alienate", "离间"), ("burn", "烧夷"), ("seize", "夺取"),
            ("rumor", "流言"), ("false-report", "伪报"), ("disorder", "扰乱"), ("inspire", "激励"), ("rescue", "救援")),
        Group("diplomacy", "外交", MapCommandTone.Green, ("gift", "赠与"), ("request", "请求"), ("return", "交还"), ("surrender", "劝降")),
        Group("appointments", "任免", MapCommandTone.Brown, ("delegate", "委任"), ("strategist", "军师"), ("rank", "爵位"),
            ("reward", "褒奖"), ("bestow", "授与"), ("confiscate", "没收"), ("execute", "处斩"), ("exile", "流放"))
    });
    private static readonly IReadOnlyList<MapCommandEntry> UnitEntries = Array.AsReadOnly(new[] {
        Group("attack", "攻击", MapCommandTone.Green), Group("march", "进军", MapCommandTone.Green)
    });

    public static IReadOnlyList<MapCommandEntry> Settlement(StrategySnapshot view, int id)
    {
        var place = view.World.Settlements.SingleOrDefault(s => s.Id == id);
        if (place is null || place.IsDormant || place.IsDestroyed || !place.CanHostPersons || place.OwnerId != view.PlayerForceId) return [];
        var facilities = Facilities;
        if (place.Kind != SettlementKind.City)
        {
            bool removable = view.MilitaryRules is {} rules && rules.TryPlace(place, out var definition) && definition.CanDemolish;
            facilities = Facilities with { Children = removable
                ? [.. Facilities.Children.Where(c => c.Id is "repair" or "train"), new("demolish", "撤去", MapCommandTone.Blue, [])]
                : Facilities.Children.Where(c => c.Id is "repair" or "train").ToArray() };
        }
        facilities = facilities with { Children = facilities.Children.Select(entry => entry.Id == "patrol"
            ? entry with { UnavailableReasonKey = PatrolDraft.UnavailableReasonKey(view,id) } : entry).ToArray() };
        return [facilities, .. Common];
    }

    public static IReadOnlyList<MapCommandEntry> Unit(StrategySnapshot view, int id)
    {
        var unit = view.World.Units.SingleOrDefault(u => u.Id == id);
        if (unit is null || !UnitCatalog.IsListed(unit) || unit.FormationId is not int formation ||
            unit.Soldiers == 0 || unit.OwnerId != view.PlayerForceId) return [];
        return view.MilitaryRules?.Purpose(formation) switch {
            UnitPurpose.Construction => [UnitEntries.Single(e => e.Id == "march")],
            UnitPurpose.Transport => [Group("redirect-transport", "输送", MapCommandTone.Green)],
            _ => UnitEntries
        };
    }
}
