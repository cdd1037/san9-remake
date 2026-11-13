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

    public static bool CanOpenSettlement(StrategySnapshot view, int id) =>
        CanOpenSettlement(view, view.World.Settlements.SingleOrDefault(s => s.Id == id));

    private static bool CanOpenSettlement(StrategySnapshot view, SettlementState? place) =>
        view.PlayerForceId is not null && place is not null && !place.IsDormant && !place.IsDestroyed &&
        place.CanHostPersons && place.OwnerId == view.PlayerForceId;

    public static bool CanOpenUnit(StrategySnapshot view, int id) =>
        CanOpenUnit(view, view.World.Units.SingleOrDefault(u => u.Id == id));

    private static bool CanOpenUnit(StrategySnapshot view, UnitState? unit) =>
        unit is not null && UnitCatalog.IsListed(unit) && unit.FormationId is not null &&
        unit.Soldiers > 0 && unit.OwnerId == view.PlayerForceId;

    public static IReadOnlyList<MapCommandEntry> Settlement(StrategySnapshot view, int id)
    {
        var place = view.World.Settlements.SingleOrDefault(s => s.Id == id);
        if (!CanOpenSettlement(view, place)) return [];
        var facilities = Facilities;
        if (place!.Kind != SettlementKind.City)
        {
            bool removable = view.MilitaryRules is {} rules && rules.TryPlace(place, out var definition) && definition.CanDemolish;
            facilities = Facilities with { Children = removable
                ? [.. Facilities.Children.Where(c => c.Id is "repair" or "train"), new("demolish", MapCommandTone.Blue, [])]
                : Facilities.Children.Where(c => c.Id is "repair" or "train").ToArray() };
        }
        var availability = new MapCommandAvailability(view);
        bool researchAvailable = view.MilitaryRules is not null && view.World.Simulation.NewGame.MixedTroopsEnabled;
        bool armamentsAvailable = view.World.Simulation.NewGame.MixedTroopsEnabled && view.MilitaryRules?.Armaments.IsEmpty == false;
        bool purchaseAvailable = armamentsAvailable;
        bool peerageAvailable = view.World.Simulation.NewGame.PersonalPeerageEnabled != false && view.World.Forces.FirstOrDefault(f => f.Id == view.PlayerForceId)?.PersonalPeerageRegimeId is not null;
        var groups = Common.Select(group => group.Id == "appointments" && peerageAvailable
            ? group with { Children = [.. group.Children, new MapCommandEntry("personal-peerage", MapCommandTone.Brown, [])] }
            : group.Id == "military"
            ? group with { Children = [.. group.Children,
                .. view.MilitaryRules is not null && view.World.Simulation.NewGame.FormationTrainingEnabled ? new[] { new MapCommandEntry("formation-teaching", MapCommandTone.Green, []) } : [],
                .. researchAvailable ? new[] { new MapCommandEntry("research", MapCommandTone.Green, []) } : [],
                .. armamentsAvailable ? new[] { new MapCommandEntry("equipment-production", MapCommandTone.Green, []) } : [],
                .. purchaseAvailable ? new[] { new MapCommandEntry("equipment-purchase", MapCommandTone.Green, []) } : []] } : group);
        return new[] { facilities }.Concat(groups).Select(group => group with {
            Children = group.Children.Select(entry => entry with {
                UnavailableReasonKey = availability.SettlementReasonKey(id, entry.Id)
            }).ToArray()
        }).ToArray();
    }

    public static IReadOnlyList<MapCommandEntry> Unit(StrategySnapshot view, int id)
    {
        var unit = view.World.Units.SingleOrDefault(u => u.Id == id);
        if (!CanOpenUnit(view, unit)) return [];
        int formation = unit!.FormationId!.Value;
        IReadOnlyList<MapCommandEntry> entries = view.MilitaryRules?.Purpose(formation) switch {
            UnitPurpose.Construction => [UnitEntries.Single(e => e.Id == "march")],
            UnitPurpose.Transport => [Group("redirect-transport", MapCommandTone.Green)],
            _ => view.World.Simulation.NewGame.MixedTroopsEnabled && !unit.Detachments.IsEmpty
                ? [.. UnitEntries, Group("armament-resupply", MapCommandTone.Green), Group("armament-configure", MapCommandTone.Green)] : UnitEntries
        };
        string? reason = new MapCommandAvailability(view).UnitReasonKey(unit);
        return entries.Select(entry => entry with { UnavailableReasonKey = reason }).ToArray();
    }
}
