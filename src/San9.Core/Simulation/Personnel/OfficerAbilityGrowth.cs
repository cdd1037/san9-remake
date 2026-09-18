using San9.Core.Domain;

namespace San9.Core.Simulation;

internal enum OfficerAbility { Leadership, War, Intelligence, Politics }

internal static class OfficerAbilityGrowth
{
    internal static void TryIncrease(WorldMutation world, int officerId, OfficerAbility ability,
        string stream, List<DomainEvent> events)
    {
        if (Value(world.Officers[officerId].BaseAbilities, ability) >= 100) return;
        if (world.NextRandom(stream) % 100 < 3) Increase(world, officerId, ability, 1, events);
    }

    internal static void Increase(WorldMutation world, int officerId, OfficerAbility ability,
        int amount, List<DomainEvent> events)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        var officer = world.Officers[officerId];
        var abilities = officer.BaseAbilities;
        int before = Value(abilities, ability);
        int next = before >= 100 ? before : (int)Math.Min(100L, (long)before + amount);
        if (next == before) return;
        var updated = ability switch
        {
            OfficerAbility.Leadership => abilities with { Leadership = next },
            OfficerAbility.War => abilities with { War = next },
            OfficerAbility.Intelligence => abilities with { Intelligence = next },
            OfficerAbility.Politics => abilities with { Politics = next },
            _ => throw new ArgumentOutOfRangeException(nameof(ability))
        };
        world.Replace(officer with { BaseAbilities = updated });
        events.Add(new OfficerAbilityGrownEvent(world.Clock, officerId, ability.ToString().ToLowerInvariant(), before, next));
    }

    private static int Value(OfficerAbilities abilities, OfficerAbility ability) => ability switch
    {
        OfficerAbility.Leadership => abilities.Leadership,
        OfficerAbility.War => abilities.War,
        OfficerAbility.Intelligence => abilities.Intelligence,
        OfficerAbility.Politics => abilities.Politics,
        _ => throw new ArgumentOutOfRangeException(nameof(ability))
    };
}
