using San9.Core.Domain;

namespace San9.Application.Presentation;

public enum MapCommandTone { Blue, Green, Brown }
/// <summary>Presentation-only navigation; leaves deliberately carry no executable command.</summary>
public sealed record MapCommandEntry(string Id, MapCommandTone Tone, IReadOnlyList<MapCommandEntry> Children)
{
    public string? UnavailableReasonKey { get; init; }
    public bool Enabled => UnavailableReasonKey is null;
}

public static class MapCommandMenu
{
    private static MapCommandEntry Group(string id, MapCommandTone tone, params string[] children) =>
        new(id, tone, Array.AsReadOnly(children.Select(id => new MapCommandEntry(id, tone, [])).ToArray()));

    private static readonly MapCommandEntry Facilities = Group("facilities", MapCommandTone.Blue,
        "patrol", "commerce", "cultivate", "repair",
        "recruit", "train", "buy", "sell");
    private static readonly IReadOnlyList<MapCommandEntry> Common = Array.AsReadOnly(new[] {
        Group("military", MapCommandTone.Green, "sortie", "build", "transport"),
        Group("talent", MapCommandTone.Green, "summon", "move", "search", "employ"),
        Group("strategy", MapCommandTone.Green, "alienate", "burn", "seize",
            "rumor", "false-report", "disorder", "inspire", "rescue"),
        Group("diplomacy", MapCommandTone.Green, "gift", "request", "return", "surrender"),
        Group("appointments", MapCommandTone.Brown, "delegate", "strategist", "rank",
            "reward", "bestow", "confiscate", "execute", "exile")
    });
    private static readonly IReadOnlyList<MapCommandEntry> UnitEntries = Array.AsReadOnly(new[] {
        Group("attack", MapCommandTone.Green), Group("march", MapCommandTone.Green)
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
                ? [.. Facilities.Children.Where(c => c.Id is "repair" or "train"), new("demolish", MapCommandTone.Blue, [])]
                : Facilities.Children.Where(c => c.Id is "repair" or "train").ToArray() };
        }
        var availability = new MapCommandAvailability(view);
        return new[] { facilities }.Concat(Common).Select(group => group with {
            Children = group.Children.Select(entry => entry with {
                UnavailableReasonKey = availability.SettlementReasonKey(id, entry.Id)
            }).ToArray()
        }).ToArray();
    }

    public static IReadOnlyList<MapCommandEntry> Unit(StrategySnapshot view, int id)
    {
        var unit = view.World.Units.SingleOrDefault(u => u.Id == id);
        if (unit is null || !UnitCatalog.IsListed(unit) || unit.FormationId is not int formation ||
            unit.Soldiers == 0 || unit.OwnerId != view.PlayerForceId) return [];
        IReadOnlyList<MapCommandEntry> entries = view.MilitaryRules?.Purpose(formation) switch {
            UnitPurpose.Construction => [UnitEntries.Single(e => e.Id == "march")],
            UnitPurpose.Transport => [Group("redirect-transport", MapCommandTone.Green)],
            _ => UnitEntries
        };
        string? reason = new MapCommandAvailability(view).UnitReasonKey(unit);
        return entries.Select(entry => entry with { UnavailableReasonKey = reason }).ToArray();
    }
}
