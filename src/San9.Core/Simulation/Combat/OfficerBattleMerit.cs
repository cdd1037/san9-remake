using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record OfficerBattleMeritChangedEvent(GameClock Clock, int OfficerId, int ForceId, int Previous, int Current, string Reason) : DomainEvent;

internal static class OfficerBattleMerit
{
    private const int Maximum = 1000000;
    internal static void Credit(WorldMutation world, BattleSource source, int amount, string reason, List<DomainEvent> events)
    {
        if (amount <= 0 || string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Invalid battle merit credit.");
        if (world.Simulation.NewGame.PersonalPeerageEnabled == false) return;
        int owner = source.Owner(world);
        IEnumerable<OfficerState> members;
        if (source.IsSettlement)
        {
            var commander = LeadershipMaintenance.Commander(world, source.Id);
            members = commander is null ? [] : [commander];
        }
        else
        {
            var unit = world.Units[source.Id];
            members = world.ServingUnitMembers(unit.Id).Where(p => unit.Detachments.IsEmpty ||
                unit.Detachments.TryGetValue(p.Id, out var detachment) && detachment.Soldiers > 0);
        }
        foreach (var member in members.OrderBy(p => p.Id).ToArray())
        {
            if (!world.Officers.TryGetValue(member.Id, out var person) || !person.IsServing || person.OwnerId != owner) continue;
            int previous = person.BattleMeritFor(owner);
            int current = (int)Math.Min(Maximum, (long)previous + amount);
            if (current == previous) continue;
            var values = person.BattleMerit ?? ImmutableSortedDictionary<int, int>.Empty;
            world.Replace(person with { BattleMerit = values.SetItem(owner, current) });
            events.Add(new OfficerBattleMeritChangedEvent(world.Clock, person.Id, owner, previous, current, reason));
        }
    }
}
