using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum AttackRequestEndReason
{
    Fulfilled, Expired, Broken, AllianceRealignment,
    RequestedForceEliminated, RequesterForceEliminated, TargetForceEliminated, TargetDestroyed, TargetOwnerChanged
}
public sealed record AttackRequestEstablishedEvent(GameClock Clock, AttackRequestState Request) : DomainEvent;
public sealed record AttackRequestEndedEvent(GameClock Clock, AttackRequestState Request,
    AttackRequestEndReason Reason, int? LiableForceId) : DomainEvent;
public sealed record AllianceRequestConflict(AttackRequestState Request, int? LiableForceId);

internal static class AttackRequests
{
    internal static bool Protects(IWorldQuery world, int first, int second) =>
        world.Simulation.AttackRequests.Any(r => r.Connects(first, second) && Valid(world, r));

    // The diplomacy executor calls this only after both sides accept and before its success transfers.
    internal static bool Establish(WorldMutation world, int requested, int requester, int target, List<DomainEvent> events)
    {
        if (requested == requester || !Alive(world, requested) || !Alive(world, requester) ||
            !world.Settlements.TryGetValue(target, out var place) || place.IsDestroyed ||
            place.OwnerId is not int owner || owner == requested || owner == requester || !Alive(world, owner))
            throw new ArgumentException("Invalid attack request parties or target.");
        var existing = world.Simulation.AttackRequests.SingleOrDefault(r => r.RequestedForceId == requested);
        if (existing is not null && existing.RequesterForceId == requester && existing.TargetSettlementId == target) return false;
        if (existing is not null && existing.RequesterForceId != requester) BreakPair(world, requested, existing.RequesterForceId, events);
        var request = new AttackRequestState(requested, requester, target, owner, 19);
        world.SetSimulation(world.Simulation with { AttackRequests = world.Simulation.AttackRequests
            .Where(r => r.RequestedForceId != requested).Append(request).OrderBy(r => r.RequestedForceId).ToImmutableArray() });
        events.Add(new AttackRequestEstablishedEvent(world.Clock, request));
        return true;
    }

    internal static void UpdateXun(WorldMutation world, List<DomainEvent> events)
    {
        Cleanup(world, events);
        foreach (int force in world.Simulation.AttackRequests.Select(r => r.RequestedForceId).ToArray())
        {
            var request = world.Simulation.AttackRequests.SingleOrDefault(r => r.RequestedForceId == force);
            if (request is null) continue;
            if (request.RemainingXun == 1)
                foreach (var edge in Pair(world, force, request.RequesterForceId))
                    Penalize(world, edge, edge.RequestedForceId, AttackRequestEndReason.Expired, events);
            else world.SetSimulation(world.Simulation with { AttackRequests = world.Simulation.AttackRequests
                .Replace(request, request with { RemainingXun = request.RemainingXun - 1 }) });
        }
    }

    internal static bool BreakPair(WorldMutation world, int actor, int partner, List<DomainEvent> events)
    {
        var pair = Pair(world, actor, partner);
        foreach (var request in pair) Penalize(world, request, actor, AttackRequestEndReason.Broken, events);
        return pair.Length > 0;
    }

    private static AttackRequestState[] Pair(WorldMutation world, int first, int second) => world.Simulation.AttackRequests
        .Where(r => r.Connects(first, second)).OrderBy(r => r.RequestedForceId == first ? 0 : 1).ToArray();

    private static void Penalize(WorldMutation world, AttackRequestState request, int liable, AttackRequestEndReason reason, List<DomainEvent> events)
    {
        ForceRelations.Set(world, request.RequestedForceId, request.RequesterForceId,
            Math.Max(0, MilitaryDiplomacy.Relation(world, request.RequestedForceId, request.RequesterForceId) - 20), events);
        var force = world.Forces[liable];
        world.Replace(force with { BaseTrust = Math.Max(0, force.BaseTrust - 20) });
        foreach (var person in world.Officers.Values.Where(p => p.OwnerId == liable && p.IsServing && p.Status != OfficerStatus.Ruler)
            .OrderBy(p => p.Id).ToArray())
            RecruitmentRules.SetLoyalty(world, person.Id, liable, Math.Max(0, person.Loyalty - person.Righteousness), "attack-request-broken", events);
        End(world, request, reason, liable, events);
    }

    internal static bool Fulfill(WorldMutation world, int force, int target, List<DomainEvent> events)
    {
        var request = world.Simulation.AttackRequests.SingleOrDefault(r => r.RequestedForceId == force && r.TargetSettlementId == target);
        if (request is null || !Valid(world, request)) return false;
        var state = world.Forces[force];
        world.Replace(state with { BaseTrust = Math.Min(1000, state.BaseTrust + 10) });
        End(world, request, AttackRequestEndReason.Fulfilled, null, events);
        return true;
    }

    internal static ImmutableArray<AllianceRequestConflict> AllianceConflicts(WorldMutation world, int target, IReadOnlySet<int> members) =>
        world.Simulation.AttackRequests.Select(r => r.Connects(r.RequestedForceId, target) && members.Contains(r.RequestedForceId)
                ? new AllianceRequestConflict(r, r.RequestedForceId)
                : r.RequestedForceId == target && members.Contains(r.RequesterForceId)
                    ? new AllianceRequestConflict(r, r.RequesterForceId)
                    : members.Contains(r.RequestedForceId) && members.Contains(r.RequesterForceId) && r.TargetForceId != target &&
                        world.Forces[r.TargetForceId].LeaderId is not null
                        ? new AllianceRequestConflict(r, null) : null)
            .OfType<AllianceRequestConflict>().ToImmutableArray();

    internal static void FormAlliance(WorldMutation world, int member, int target, IReadOnlySet<int> members, List<DomainEvent> events)
    {
        BreakPair(world, member, target, events);
        var request = world.Simulation.AttackRequests.SingleOrDefault(r => r.RequestedForceId == member);
        if (request is not null && members.Contains(request.RequesterForceId) && request.TargetForceId != target &&
            world.Forces[request.TargetForceId].LeaderId is not null)
            End(world, request, AttackRequestEndReason.AllianceRealignment, null, events);
    }

    internal static void Cleanup(WorldMutation world, List<DomainEvent> events)
    {
        foreach (var request in world.Simulation.AttackRequests.Where(r => !Valid(world, r)).ToArray())
        {
            var reason = !Alive(world, request.RequestedForceId) ? AttackRequestEndReason.RequestedForceEliminated
                : !Alive(world, request.RequesterForceId) ? AttackRequestEndReason.RequesterForceEliminated
                : world.Settlements.GetValueOrDefault(request.TargetSettlementId) is not { IsDestroyed: false } ? AttackRequestEndReason.TargetDestroyed
                : world.Settlements[request.TargetSettlementId].OwnerId != request.TargetForceId ? AttackRequestEndReason.TargetOwnerChanged
                : !Alive(world, request.TargetForceId) ? AttackRequestEndReason.TargetForceEliminated
                : throw new InvalidOperationException("Invalid attack request has no world-state cause.");
            End(world, request, reason, null, events);
        }
    }

    private static void End(WorldMutation world, AttackRequestState request, AttackRequestEndReason reason, int? liable, List<DomainEvent> events)
    {
        world.SetSimulation(world.Simulation with { AttackRequests = world.Simulation.AttackRequests.Remove(request) });
        events.Add(new AttackRequestEndedEvent(world.Clock, request, reason, liable));
    }

    private static bool Alive(IWorldQuery world, int id) => world.Forces.TryGetValue(id, out var force) && !force.IsEliminated;
    private static bool Valid(IWorldQuery world, AttackRequestState r) => Valid(r, id => Alive(world, id), id => world.Settlements.GetValueOrDefault(id));
    internal static bool Valid(AttackRequestState r, Func<int, bool> alive, Func<int, SettlementState?> place) => r.RemainingXun > 0 && r.RequestedForceId != r.RequesterForceId &&
        r.TargetForceId != r.RequestedForceId && r.TargetForceId != r.RequesterForceId && alive(r.RequestedForceId) &&
        alive(r.RequesterForceId) && alive(r.TargetForceId) &&
        place(r.TargetSettlementId) is { IsDestroyed: false } target && target.OwnerId == r.TargetForceId;

    internal static void Validate(WorldMutation world)
    {
        var requests = world.Simulation.AttackRequests;
        if (requests.IsDefault || requests.Any(r => r is null || !Valid(world, r)) ||
            !requests.Select(r => r.RequestedForceId).SequenceEqual(requests.Select(r => r.RequestedForceId).Distinct().Order()))
            throw new ArgumentException("Invalid directed attack requests.");
    }
}
