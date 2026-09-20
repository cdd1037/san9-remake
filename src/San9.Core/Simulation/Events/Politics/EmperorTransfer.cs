using System.Text.Json.Serialization;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record EmperorTransferPending([property: JsonRequired] int? PreviousForceId);
public enum EmperorTransferChoice { Support, Depose }
public sealed record EmperorTransferPrompt(int ForceId, int EmperorId, int CityId);
public sealed record EmperorTransferEventSession(int ForceId, int EmperorId, int CityId) : EventSession
{
    public override ResolutionStage Stage => ResolutionStage.XunCleanup;
    public override int RespondingForceId => ForceId;
}
public sealed record RespondEmperorTransferCommand(Guid CommandId, int IssuerId, long Sequence,
    [property: JsonRequired] int EmperorId, [property: JsonRequired] int CityId,
    [property: JsonRequired] EmperorTransferChoice Choice) : GameCommand(CommandId, IssuerId, Sequence);
public sealed record EmperorTransferOfferedEvent(GameClock Clock, int ForceId, int EmperorId, int CityId) : DomainEvent;
public sealed record EmperorTransferResolvedEvent(GameClock Clock, int ForceId, int EmperorId, int CityId,
    EmperorTransferChoice Choice, bool Forced, bool Invalidated) : DomainEvent;

internal static class EmperorTransfer
{
    internal static void Observe(WorldMutation world, EconomyRulebook economy, SettlementState before, SettlementState after)
    {
        if (economy.ImperialCourt.TransferEvent is null || world.Simulation.EmperorTransfer is not null ||
            before.OwnerId == after.OwnerId || before.IsDormant || ImperialCourt.City(world, economy) != before.Id) return;
        world.SetSimulation(world.Simulation with { EmperorTransfer = new(before.OwnerId) });
    }

    internal static void Begin(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        var pending = world.Simulation.EmperorTransfer;
        world.SetSimulation(world.Simulation with { EmperorTransfer = null });
        if (pending is not null && Candidate(world, rules) is { } candidate && candidate.ForceId != pending.PreviousForceId)
        {
            bool forced = Forced(world.Forces[candidate.ForceId], rules.Politics);
            if (!forced && world.Simulation.Controls.Any(c => c.ForceId == candidate.ForceId && c.Kind == ControllerKind.Player))
            {
                events.Add(new EmperorTransferOfferedEvent(world.Clock, candidate.ForceId, candidate.EmperorId, candidate.CityId));
                world.SetSimulation(world.Simulation with { PendingEvent = candidate, Boundary = SimulationBoundary.AwaitingDecision });
                return;
            }
            Apply(world, rules, candidate, forced ? EmperorTransferChoice.Depose : EmperorTransferChoice.Support, forced, events);
        }
        EconomicCycle.UpdateTrust(world, rules.Economy, events);
    }

    private static EmperorTransferEventSession? Candidate(WorldMutation world, SimulationRules rules)
    {
        var court = rules.Economy.ImperialCourt;
        if (court.TransferEvent is null || court.Resolve(world.Simulation.ImperialCourtEnded, world.Officers.Values) is not { } emperor ||
            ImperialCourt.City(world, rules.Economy) is not int city || world.Settlements[city].OwnerId is not int owner ||
            !world.Forces.TryGetValue(owner, out var force) || force.IsEliminated) return null;
        return new(owner, emperor.Id, city);
    }

    private static bool Forced(ForceState force, PoliticalRulebook politics) => force.IsBarbarian ||
        politics.ImperialRankId is int rank && force.RankId == rank ||
        force.CountryTitleId is int title && politics.Titles.TryGetValue(title, out var rule) && rule.ForcesEmperorDeposition;

    internal static CommandRejection Respond(WorldMutation world, SimulationRules rules, RespondEmperorTransferCommand command,
        List<DomainEvent> events)
    {
        if (world.Simulation.PendingEvent is not EmperorTransferEventSession session || session.ForceId != command.IssuerId ||
            session.EmperorId != command.EmperorId || session.CityId != command.CityId || !Enum.IsDefined(command.Choice))
            return CommandRejection.InvalidDecision;
        world.SetSimulation(world.Simulation with { PendingEvent = null, Boundary = SimulationBoundary.Stable });
        bool valid = Candidate(world, rules) == session;
        bool forced = valid && Forced(world.Forces[session.ForceId], rules.Politics);
        if (valid) Apply(world, rules, session, forced ? EmperorTransferChoice.Depose : command.Choice, forced, events);
        else events.Add(new EmperorTransferResolvedEvent(world.Clock, session.ForceId, session.EmperorId, session.CityId,
            command.Choice, false, true));
        EconomicCycle.UpdateTrust(world, rules.Economy, events);
        return CommandRejection.None;
    }

    private static void Apply(WorldMutation world, SimulationRules rules, EmperorTransferEventSession session,
        EmperorTransferChoice choice, bool forced, List<DomainEvent> events)
    {
        var definition = rules.Economy.ImperialCourt.TransferEvent!;
        bool depose = choice == EmperorTransferChoice.Depose;
        foreach (var person in world.Officers.Values.Where(p => p.OwnerId == session.ForceId && p.IsServing &&
            p.Status != OfficerStatus.Ruler && p.Righteousness >= definition.MinimumRighteousness &&
            p.Righteousness <= definition.MaximumRighteousness).OrderBy(p => p.Id).ToArray())
            RecruitmentRules.SetLoyalty(world, person.Id, session.ForceId,
                Math.Clamp(person.LoyaltyTo(session.ForceId) + (depose ? -definition.LoyaltyChange : definition.LoyaltyChange), 0, 255),
                depose ? "emperor-deposed" : "emperor-supported", events);
        if (depose)
        {
            world.Replace(world.Forces[session.ForceId] with { HasDeposedEmperor = true });
            world.SetSimulation(world.Simulation with { ImperialCourtEnded = true });
        }
        events.Add(new EmperorTransferResolvedEvent(world.Clock, session.ForceId, session.EmperorId, session.CityId, choice, forced, false));
    }

    internal static void ValidateState(WorldMutation world)
    {
        if (world.Simulation.EmperorTransfer?.PreviousForceId is int previous && !world.AllForces.ContainsKey(previous))
            throw new ArgumentException("Unknown former emperor city owner.");
        if (world.Simulation.PendingEvent is not EmperorTransferEventSession session) return;
        if (world.Simulation.EmperorTransfer is not null || world.Simulation.ImperialCourtEnded ||
            !world.Forces.TryGetValue(session.ForceId, out var force) || force.IsEliminated ||
            !world.Officers.ContainsKey(session.EmperorId) || !world.Settlements.TryGetValue(session.CityId, out var city) ||
            city.Kind != SettlementKind.City || city.IsDestroyed || city.OwnerId != session.ForceId ||
            !world.Simulation.Controls.Any(c => c.ForceId == session.ForceId && c.Kind == ControllerKind.Player))
            throw new ArgumentException("Invalid emperor transfer session.");
    }

    internal static void ValidateRules(WorldMutation world, SimulationRules rules)
    {
        if (world.Simulation.EmperorTransfer is not null && rules.Economy.ImperialCourt.TransferEvent is null ||
            world.Simulation.PendingEvent is EmperorTransferEventSession session && Candidate(world, rules) != session)
            throw new ArgumentException("Emperor transfer state does not match the event definition.");
    }
}
