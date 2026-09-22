using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum ChainedPlotStep { Prelude, Coup, RulerUnavailable, ConspiratorsUnavailable, CourtUnavailable }
public sealed record ChainedPlotEvent(GameClock Clock, ChainedPlotStep Step, int? ForceId, int RulerId, int SuccessorId,
    ImmutableArray<int> KilledOfficerIds) : DomainEvent;

internal static class ChainedPlot
{
    internal static bool Apply(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (rules.ChainedPlot is not { } definition || !world.Officers.TryGetValue(definition.RulerId, out var ruler) ||
            !world.Officers.TryGetValue(definition.SuccessorId, out var successor) ||
            !world.Officers.TryGetValue(definition.InstigatorId, out var instigator)) return false;
        int? force = ruler.OwnerId;
        bool ruling = ruler.Status == OfficerStatus.Ruler && force is int owner && world.Forces[owner].LeaderId == ruler.Id;
        bool conspirators = ruling && successor.IsServing && successor.OwnerId == force && instigator.IsServing && instigator.OwnerId == force;
        bool court = !world.Simulation.ImperialCourtEnded && ImperialCourt.City(world, rules.Economy) is int city &&
            force is int controller && world.Settlements[city].OwnerId == controller;
        bool started = world.Simulation.ConsumedEventKeys.Contains(definition.StartedKey);
        if (!started)
        {
            if (!ruling || !conspirators || !court || world.Clock.DayNumber < definition.EarliestDate.DayNumber ||
                !(successor.Loyalty < definition.LoyaltyThreshold && instigator.Loyalty < definition.LoyaltyThreshold ||
                    world.Forces[force!.Value].Trust < definition.TrustThreshold)) return false;
            Consume(world, definition.StartedKey);
            events.Add(new ChainedPlotEvent(world.Clock, ChainedPlotStep.Prelude, force, ruler.Id, successor.Id, []));
            return true;
        }
        if (world.Simulation.ConsumedEventKeys.Contains(definition.CompletedKey)) return false;
        var step = world.Simulation.ImperialCourtEnded ? ChainedPlotStep.CourtUnavailable :
            !ruling ? ChainedPlotStep.RulerUnavailable : !conspirators ? ChainedPlotStep.ConspiratorsUnavailable :
            !court ? ChainedPlotStep.CourtUnavailable : ChainedPlotStep.Coup;
        var victims = step == ChainedPlotStep.Coup ? Coup(world, rules, definition, force!.Value, events) : [];
        Consume(world, definition.CompletedKey);
        events.Add(new ChainedPlotEvent(world.Clock, step, force, ruler.Id, successor.Id, victims));
        return true;
    }

    private static void Consume(WorldMutation world, PersistentEventKey key) =>
        world.ConsumeEvent(new(key.Value, EventTrigger.Xun, 0, EventConsumption.OnSuccess, key),
            new(key.Value, EventOutcomeKind.Completed, false, EventResult.Succeeded));

    private static ImmutableArray<int> Coup(WorldMutation world, SimulationRules rules, ChainedPlotDefinition definition,
        int forceId, List<DomainEvent> events)
    {
        var ruler = world.Officers[definition.RulerId];
        var traveling = world.Simulation.Tasks.Where(t => t.Journey is not null).Select(t => t.OfficerId).ToHashSet();
        var relatives = world.Officers.Values.Where(p => p.Id != ruler.Id && p.Id != definition.SuccessorId && p.IsServing && p.OwnerId == forceId &&
            ruler.BloodlineId is int family && p.BloodlineId == family &&
            !traveling.Contains(ruler.Id) && !traveling.Contains(p.Id) &&
            (ruler.UnitId is int unit ? p.UnitId == unit : ruler.SettlementId is int home && p.SettlementId == home))
            .OrderBy(p => p.Id).Select(p => p.Id).ToImmutableArray();
        foreach (int relative in relatives)
            OfficerDeath.Resolve(world, relative, OfficerDeathCause.HistoricalEvent, rules, events, afterDailyCommands: false);
        AllianceDiplomacy.RulerDied(world, forceId, events);
        OfficerDeath.Remove(world, ruler.Id, OfficerDeathCause.HistoricalEvent, rules.Personnel, events, afterDailyCommands: false);
        RulerSuccession.Appoint(world, forceId, ruler.Id, definition.SuccessorId, events);
        OfficerDeath.SettleTreasures(world, ruler.Id, definition.SuccessorId, events);
        if (ruler.UnitId is int previousUnit && world.Units.ContainsKey(previousUnit) &&
            !world.Officers.Values.Any(p => p.UnitId == previousUnit && p.IsServing))
            BattleAftermath.TerminateUnit(world, previousUnit, null, rules, events);
        BattleAftermath.Reconcile(world, new HashSet<int> { forceId }, rules, events, "chained-plot");
        var successor = world.Officers[definition.SuccessorId];
        foreach (var person in world.Officers.Values.Where(p => p.OwnerId == forceId).OrderBy(p => p.Id).ToArray())
        {
            int loyalty = definition.SupporterIds.Contains(person.Id) ? Math.Max(person.Loyalty, definition.SupporterMinimumLoyalty) :
                ruler.BloodlineId is int family && person.BloodlineId == family || definition.OpponentIds.Contains(person.Id)
                    ? Math.Min(person.Loyalty, definition.OpponentMaximumLoyalty)
                    : Math.Max(0, person.Loyalty - PersonnelRulebook.AffinityDistance(person, successor) / definition.AffinityDivisor);
            RecruitmentRules.SetLoyalty(world, person.Id, forceId, loyalty, "chained-plot", events);
        }
        world.MaintainLeadership(events);
        ResolutionPipeline.CheckConquest(world, events);
        return relatives.Add(ruler.Id);
    }
}
