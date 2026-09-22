using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record QiaoMarriageEvent(GameClock Clock, int ForceId, int RulerId, int CompanionId,
    int RulerLifespanGain, int CompanionLifespanGain, int BaseTrustGain, int? RivalForceId) : DomainEvent;

internal static class QuarterHistoricalEvents
{
    internal static void Begin(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        // An alliance proposal owns this table even when all players decline it.
        if (AllianceFormation.Begin(world, rules, events)) return;
        if (ApplyQiaoMarriage(world, rules, events) || SunMarriage.Apply(world, rules, events) ||
            HistoricalRecognition.ApplyHanzhongKing(world, rules, events) || HistoricalRecognition.ApplyFiveTigers(world, rules, events)) return;
        HistoricalRecognition.ApplyExpeditionMemorial(world, rules, events);
    }

    internal static bool ApplyQiaoMarriage(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (!world.Clock.Boundary.Quarter || world.Simulation.PendingEvent is not null ||
            world.Simulation.TerminalReason is not null || rules.QiaoMarriage is not { } definition ||
            world.Simulation.Started.DayNumber > definition.LatestScenarioStart.DayNumber ||
            world.Clock.DayNumber > definition.LatestDate.DayNumber ||
            world.Simulation.ConsumedEventKeys.Contains(definition.ConsumptionKey)) return false;
        if (!world.Officers.TryGetValue(definition.RulerId, out var ruler) ||
            !world.Officers.TryGetValue(definition.CompanionId, out var companion)) return false;
        if (ruler.Status != OfficerStatus.Ruler || ruler.OwnerId is not int owner ||
            !world.Forces.TryGetValue(owner, out var force) || force.IsEliminated || force.LeaderId != ruler.Id ||
            !world.Simulation.Controls.Any(c => c.ForceId == owner && c.Kind == ControllerKind.Player) ||
            !companion.IsServing || companion.OwnerId != owner ||
            definition.RequiredLivingOfficerIds.Any(id => !world.Officers.TryGetValue(id, out var person) || person.Status == OfficerStatus.Dead)) return false;
        var cities = rules.Personnel.CityProvinces.Where(p => p.Value == definition.ProvinceId &&
            !definition.ExcludedCityIds.Contains(p.Key)).Select(p => p.Key).ToArray();
        if (cities.Length == 0 || cities.Any(id => !world.Settlements.TryGetValue(id, out var city) ||
            city.IsDestroyed || city.OwnerId != owner)) return false;

        // All references and eligibility are checked before the first write.
        int Gain(int current) => (int)Math.Min(definition.LifespanBonus, Math.Max(0L, (long)definition.LifespanCap - current));
        int rulerGain = Gain(ruler.LifespanTier), companionGain = Gain(companion.LifespanTier);
        int trustGain = (int)Math.Min(definition.BaseTrustBonus, Math.Max(0L, 1000L - force.BaseTrust));
        var rival = world.Officers.GetValueOrDefault(definition.RivalRulerId);
        int? rivalForce = rival?.Status == OfficerStatus.Ruler && rival.OwnerId is int other && other != owner &&
            world.Forces.TryGetValue(other, out var rivalRealm) && !rivalRealm.IsEliminated && rivalRealm.LeaderId == rival.Id ? other : null;
        world.Replace(ruler with { LifespanTier = ruler.LifespanTier + rulerGain });
        world.Replace(companion with { LifespanTier = companion.LifespanTier + companionGain });
        world.Replace(force with { BaseTrust = force.BaseTrust + trustGain });
        if (rivalForce is int target) ForceRelations.Change(world, owner, target, -definition.RelationPenalty, events);
        world.ConsumeEvent(definition.Candidate(), new("qiao-marriage", EventOutcomeKind.Completed, false, EventResult.Succeeded));
        events.Add(new QiaoMarriageEvent(world.Clock, owner, ruler.Id, companion.Id, rulerGain, companionGain, trustGain, rivalForce));
        return true;
    }
}
