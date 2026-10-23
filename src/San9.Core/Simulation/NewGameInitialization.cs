using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class NewGameInitialization
{
    internal static void Apply(WorldMutation world, NewGameSettings settings, IReadOnlyList<int> officerPlaces, IReadOnlySet<int> players)
    {
        settings.Validate();
        world.SetSimulation(world.Simulation with { NewGame = settings });
        bool Computer(int? owner) => owner is int id && world.Forces.ContainsKey(id) && !players.Contains(id);
        bool Ordinary(int owner) => Computer(owner) && !world.Forces[owner].IsBarbarian;
        int multiplier = settings.Difficulty == GameDifficulty.Advanced ? 14 : 12;
        foreach (var corps in world.Corps.Values.OrderBy(c => c.Id).ToArray())
            if (Ordinary(corps.OwnerId))
                world.Replace(corps with { Treasury = new(
                    (uint)Math.Min(1000000L, (long)corps.Treasury.Money * multiplier / 10 + settings.MoneyBonus),
                    (uint)Math.Min(1000000L, (long)corps.Treasury.Food * multiplier / 10 + settings.FoodBonus)) });
        foreach (var place in world.Settlements.Values.OrderBy(s => s.Id).ToArray())
        {
            bool eligible = place.Kind == SettlementKind.City ? Computer(place.OwnerId) : place.OwnerId is null || Computer(place.OwnerId);
            if (!eligible || place.IsDestroyed) continue;
            var updated = place with { Morale = Math.Min(100, place.Morale + settings.MoraleBonus) };
            if (place.Kind == SettlementKind.City)
                updated = updated with
                {
                    Soldiers = (uint)Math.Min(place.SoldierCapacity, (long)place.Soldiers + settings.SoldierBonus +
                        (Ordinary(place.OwnerId!.Value) && settings.Difficulty == GameDifficulty.Advanced ? 10000 : 0)),
                    PublicOrder = Math.Min(1000, place.PublicOrder + settings.DomesticBonus),
                    Commerce = Math.Min(place.MaxCommerce, place.Commerce + settings.DomesticBonus),
                    Cultivation = Math.Min(place.MaxCultivation, place.Cultivation + settings.DomesticBonus),
                    Durability = Math.Min(place.MaxDurability, place.Durability + settings.DomesticBonus)
                };
            world.Replace(updated);
        }
        foreach (var unit in world.Units.Values.OrderBy(u => u.Id).ToArray())
            if (Computer(unit.OwnerId)) world.Replace(unit with { Morale = Math.Min(100, unit.Morale + settings.MoraleBonus) });
        if (settings.Mode != GameMode.Fictional) return;
        var places = officerPlaces.Distinct().Order().ToArray();
        int Pick(int count) => (int)(world.NextRandom("new-game") % (ulong)count);
        var loyalties = world.AllForces.Keys.ToImmutableDictionary(id => id, _ => 50);
        foreach (var person in world.AllOfficers.Values.OrderBy(p => p.Id).ToArray())
        {
            if (person.Status == OfficerStatus.Dead) continue;
            var updated = person with
            {
                Righteousness = Pick(16), Ambition = Pick(16), Personality = Pick(4), LifespanTier = Pick(8),
                Affinity = Pick(150), ParentId = null, BloodlineId = person.Id,
                ForceLoyalties = person.OwnerId is int owner ? loyalties.SetItem(owner, 80 + Pick(21)) : loyalties
            };
            if (!person.IsDormant && person.Status is OfficerStatus.Free or OfficerStatus.Undiscovered or OfficerStatus.NotAppeared)
            {
                if (places.Length == 0) throw new ArgumentException("Fictional mode requires officer starting places.");
                int id = places[Pick(places.Length)];
                updated = updated with { SettlementId = world.AllSettlements.ContainsKey(id) ? id : null,
                    RegionId = world.AllSettlements.ContainsKey(id) ? null : id };
            }
            world.Replace(updated);
        }
        var active = world.Forces.Keys.Order().ToArray();
        for (int first = 0; first < active.Length; first++)
            for (int second = first + 1; second < active.Length; second++)
                world.SetRelation(new ForceRelation(active[first], active[second], 50));
    }
}
