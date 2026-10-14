using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record PrisonerEscapedEvent(GameClock Clock, int OfficerId, int? DetentionSettlementId,
    int? DetentionUnitId, int DestinationId) : DomainEvent;

internal static class PrisonerEscapeCycle
{
    internal static int Threshold(int strength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(strength);
        return (int)Math.Min(100, EconomyMath.ExecutedAbility(strength) / 20 + 2);
    }

    internal static void Apply(WorldMutation world, PersonnelRulebook places, EconomyRulebook economy, List<DomainEvent> events)
    {
        int remaining = 3;
        var candidates = world.Officers.Values.Where(p => p.Status == OfficerStatus.Captive)
            .OrderByDescending(p => world.Abilities(p).War).ThenBy(p => p.Id).Select(p => p.Id).ToArray();
        foreach (int id in candidates)
        {
            var person = world.Officers[id];
            int? origin = person.SettlementId;
            if (origin is int settlementId && (!world.Settlements[settlementId].CanHostPersons ||
                world.Settlements[settlementId].IsDestroyed || world.Settlements[settlementId].Durability <= 0)) continue;
            if (person.UnitId is int unitId)
            {
                var unit = world.Units[unitId];
                if (!unit.IsEmbedded) continue;
                // Embedded units have geographic positions; personnel journeys use the closest defined place.
                origin = places.Places.Values.OrderBy(p => MapCoordinates.Distance(unit.Position, p.Position))
                    .ThenBy(p => p.Id).Select(p => (int?)p.Id).FirstOrDefault();
            }
            if (origin is not int from || !places.Places.ContainsKey(from)) continue;
            if (world.NextRandom("personnel-prisoner-escape") % 100 >= (ulong)Threshold(world.Abilities(person).War)) continue;
            if (PrisonerRelease.Depart(world, id, from, world.Simulation.NextSequence,
                PrisonerDepartureKind.Escaped, null, places, economy, events)
                != CommandRejection.None) continue;
            world.SetSimulation(world.Simulation with { NextSequence = checked(world.Simulation.NextSequence + 1) });
            if (--remaining == 0) break;
        }
    }
}
