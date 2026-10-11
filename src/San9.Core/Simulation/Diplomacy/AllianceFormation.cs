using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record AllianceParticipation([property: JsonRequired] int ForceId, [property: JsonRequired] bool? Join);
public sealed record AllianceProposal([property: JsonRequired] int TargetForceId,
    [property: JsonRequired] ImmutableArray<int> EligibleMemberForceIds,
    [property: JsonRequired] ImmutableArray<AllianceParticipation> Choices)
{
    [JsonIgnore] public int? RespondingForceId => Choices.FirstOrDefault(p => p.Join is null)?.ForceId;
    [JsonIgnore] public ImmutableArray<int> KnownMemberForceIds => Choices.Where(p => p.Join == true).Select(p => p.ForceId).Order().ToImmutableArray();
}

public sealed record AllianceParticipationCommand(Guid CommandId, int IssuerId, long Sequence,
    [property: JsonRequired] int TargetForceId, [property: JsonRequired] bool Join)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record AllianceOrderConflict(int UnitId, int OwnerId, bool ClearsContact, bool ChangesOrder);
public sealed record AllianceParticipationPrompt(AllianceProposal Proposal, ImmutableArray<AllianceOrderConflict> PossibleOrderConflicts)
{
    public ImmutableArray<AllianceRequestConflict> PossibleRequestConflicts { get; init; } = [];
}
public enum AllianceFormationFailure { InsufficientMembers, InvalidatedProposal }
public sealed record AllianceProposedEvent(GameClock Clock, AllianceProposal Proposal) : DomainEvent;
public sealed record AllianceParticipationDecidedEvent(GameClock Clock, int ForceId, int TargetForceId, bool Join) : DomainEvent;
public sealed record AllianceFormedEvent(GameClock Clock, AllianceState Alliance) : DomainEvent;
public sealed record AllianceFormationFailedEvent(GameClock Clock, int TargetForceId, AllianceFormationFailure Reason) : DomainEvent;

internal static class AllianceFormation
{
    internal static bool Begin(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (!world.Clock.Boundary.Quarter || world.Clock.DayNumber - world.Simulation.Started.DayNumber < 360 ||
            world.Simulation.Alliance is not null || world.Simulation.PendingEvent is not null) return false;
        if (world.NextRandom("alliance-formation") % 4 != 0) return false;
        var candidates = AllianceEligibility.Find(world, rules);
        var target = candidates.LargestRealm is { } largest && candidates.LowPrestige is { } lowest
            ? (world.NextRandom("alliance-target") % 2 == 0 ? largest : lowest)
            : candidates.LargestRealm ?? candidates.LowPrestige;
        if (target is null) return false;
        var players = world.Simulation.Controls.Where(c => c.Kind == ControllerKind.Player && c.ForceId != target.ForceId &&
                !world.Forces[c.ForceId].IsEliminated).Select(c => c.ForceId).Order().ToArray();
        var proposal = new AllianceProposal(target.ForceId, target.EligibleMemberForceIds,
            world.Simulation.Controls.Where(c => players.Contains(c.ForceId) || target.EligibleMemberForceIds.Contains(c.ForceId))
                .OrderBy(c => c.Kind == ControllerKind.Player ? 0 : 1).ThenBy(c => c.ForceId)
                .Select(c => new AllianceParticipation(c.ForceId, null)).ToImmutableArray());
        events.Add(new AllianceProposedEvent(world.Clock, proposal));
        if (proposal.RespondingForceId is null) Complete(world, proposal, rules, events);
        else world.SetSimulation(world.Simulation with { Boundary = SimulationBoundary.AwaitingDecision, PendingEvent = new AllianceEventSession(proposal) });
        return true;
    }

    internal static CommandRejection Respond(WorldMutation world, AllianceParticipationCommand command, SimulationRules rules, List<DomainEvent> events)
    {
        if (world.Simulation.PendingEvent is not AllianceEventSession { Proposal: var proposal } || proposal.TargetForceId != command.TargetForceId ||
            proposal.RespondingForceId != command.IssuerId) return CommandRejection.InvalidDecision;
        proposal = proposal with { Choices = proposal.Choices.Select(p => p.ForceId == command.IssuerId ? p with { Join = command.Join } : p).ToImmutableArray() };
        events.Add(new AllianceParticipationDecidedEvent(world.Clock, command.IssuerId, proposal.TargetForceId, command.Join));
        if (proposal.RespondingForceId is null)
        {
            world.SetSimulation(world.Simulation with { PendingEvent = null });
            Complete(world, proposal, rules, events);
        }
        else world.SetSimulation(world.Simulation with { PendingEvent = new AllianceEventSession(proposal) });
        return CommandRejection.None;
    }

    internal static AllianceParticipationPrompt? Prompt(WorldMutation world, SimulationRules rules)
    {
        if (world.Simulation.PendingEvent is not AllianceEventSession { Proposal: var proposal }) return null;
        var potential = proposal.KnownMemberForceIds.Concat(proposal.Choices.Where(p => p.Join is null).Select(p => p.ForceId)).ToHashSet();
        return new(proposal, Conflicts(world, potential, rules.Military))
        {
            PossibleRequestConflicts = AttackRequests.AllianceConflicts(world, proposal.TargetForceId, potential)
        };
    }

    private static void Complete(WorldMutation world, AllianceProposal proposal, SimulationRules rules, List<DomainEvent> events)
    {
        if (world.Simulation.Alliance is not null || !world.Forces.TryGetValue(proposal.TargetForceId, out var target) || target.IsEliminated)
        {
            events.Add(new AllianceFormationFailedEvent(world.Clock, proposal.TargetForceId, AllianceFormationFailure.InvalidatedProposal));
            return;
        }
        var members = proposal.KnownMemberForceIds.Where(id => world.Forces.TryGetValue(id, out var force) && !force.IsEliminated).ToImmutableArray();
        if (members.Length < 2)
        {
            events.Add(new AllianceFormationFailedEvent(world.Clock, proposal.TargetForceId, AllianceFormationFailure.InsufficientMembers));
            return;
        }
        int leader = members.OrderByDescending(id => world.Forces[id].Trust).ThenByDescending(id => id).First();
        var alliance = new AllianceState(target.Id, leader, members, 36);
        world.SetSimulation(world.Simulation with { Alliance = alliance });
        world.Replace(world.Forces[leader] with { BaseTrust = Math.Min(1000, world.Forces[leader].BaseTrust + 25) });
        ClearConflicts(world, members.ToHashSet(), rules.Military, events);
        foreach (int member in members)
        {
            AttackRequests.FormAlliance(world, member, target.Id, members.ToHashSet(), events);
            ForceRelations.Set(world, member, target.Id, DiplomacyMath.AllianceTargetRelation(MilitaryDiplomacy.Relation(world, member, target.Id)), events);
        }
        for (int first = 0; first < members.Length; first++)
            for (int second = first + 1; second < members.Length; second++)
                ForceRelations.Set(world, members[first], members[second],
                    DiplomacyMath.AllianceMemberRelation(MilitaryDiplomacy.Relation(world, members[first], members[second])), events);
        events.Add(new AllianceFormedEvent(world.Clock, alliance));
    }

    private static ImmutableArray<AllianceOrderConflict> Conflicts(WorldMutation world, IReadOnlySet<int> members, MilitaryRulebook? rules)
    {
        bool MemberTarget(int owner, int? target) => target is int other && other != owner && members.Contains(other);
        bool Active(UnitState unit) => !unit.IsPersonnel && (!unit.IsEmbedded || rules is not null && unit.ServicePlaceId is int home &&
            world.Settlements.TryGetValue(home, out var place) && !place.IsDestroyed && place.Facility?.BuilderUnitId is null &&
            rules.TryPlace(place, out _) && !MilitaryDiplomacy.Work(world, home, rules));
        return world.Units.Values.Where(u => members.Contains(u.OwnerId) && Active(u)).OrderBy(u => u.Id).Select(u =>
        {
            bool main = MemberTarget(u.OwnerId, MilitaryDiplomacy.TargetOwner(world, u.Order));
            bool contact = u.EngagementTargetId is int id && world.Units.TryGetValue(id, out var enemy) && MemberTarget(u.OwnerId, enemy.OwnerId);
            return new AllianceOrderConflict(u.Id, u.OwnerId, u.EngagementTargetId is not null && (main || contact), main);
        }).Where(c => c.ClearsContact || c.ChangesOrder).ToImmutableArray();
    }

    internal static void ClearConflicts(WorldMutation world, IReadOnlySet<int> members, MilitaryRulebook? rules, List<DomainEvent> events)
    {
        foreach (var conflict in Conflicts(world, members, rules))
        {
            var unit = world.Units[conflict.UnitId];
            MilitaryOrder? order = unit.Order;
            if (conflict.ChangesOrder)
                order = unit.ServicePlaceId is int home && world.Settlements.TryGetValue(home, out var place) &&
                    !place.IsDestroyed && place.OwnerId == unit.OwnerId && place.CanHostPersons && place.Durability > 0
                    ? new(MilitaryOrderKind.Return, null, home) : null;
            world.Replace(unit with { Order = order,
                EngagementTargetId = conflict.ClearsContact ? null : unit.EngagementTargetId,
                AwaitingPostAction = conflict.ChangesOrder ? false : unit.AwaitingPostAction });
            if (conflict.ChangesOrder) events.Add(new UnitOrderChangedEvent(world.Clock, unit.Id, order, "alliance-formed"));
            if (conflict.ClearsContact) events.Add(new UnitEngagementChangedEvent(world.Clock, unit.Id, null, "alliance-formed"));
        }
    }

    internal static void ValidatePending(WorldMutation world)
    {
        var sim = world.Simulation;
        if (sim.PendingEvent is not AllianceEventSession { Proposal: var proposal }) return;
        bool Alive(int id) => world.Forces.TryGetValue(id, out var force) && !force.IsEliminated;
        if (!world.Clock.Boundary.Quarter || world.Clock.DayNumber - sim.Started.DayNumber < 360 || sim.Alliance is not null ||
            sim.ActiveControl != sim.Controls.Length ||
            !Alive(proposal.TargetForceId) || proposal.EligibleMemberForceIds.IsDefault || proposal.EligibleMemberForceIds.Length < 3 || proposal.Choices.IsDefaultOrEmpty ||
            world.Forces[proposal.TargetForceId].IsBarbarian &&
                !sim.Controls.Any(c => c.ForceId == proposal.TargetForceId && c.Kind == ControllerKind.Player) ||
            proposal.Choices.Any(p => p is null) ||
            !proposal.EligibleMemberForceIds.SequenceEqual(proposal.EligibleMemberForceIds.Distinct().Order()) ||
            proposal.Choices.Select(p => p.ForceId).Distinct().Count() != proposal.Choices.Length ||
            !proposal.Choices.Select(p => p.ForceId).SequenceEqual(sim.Controls.Where(c => c.ForceId != proposal.TargetForceId &&
                (c.Kind == ControllerKind.Player || proposal.EligibleMemberForceIds.Contains(c.ForceId)))
                .OrderBy(c => c.Kind == ControllerKind.Player ? 0 : 1).ThenBy(c => c.ForceId).Select(c => c.ForceId)) ||
            proposal.EligibleMemberForceIds.Any(id => id == proposal.TargetForceId || !Alive(id) || world.Forces[id].IsBarbarian && !sim.Controls.Any(c => c.ForceId == id && c.Kind == ControllerKind.Player)) ||
            proposal.Choices.Any(p => p.ForceId == proposal.TargetForceId || !Alive(p.ForceId) ||
                !sim.Controls.Any(c => c.ForceId == p.ForceId)) ||
            proposal.RespondingForceId is null || proposal.Choices.SkipWhile(p => p.Join is not null).Any(p => p.Join is not null))
            throw new ArgumentException("Invalid pending alliance proposal.");
    }
}
