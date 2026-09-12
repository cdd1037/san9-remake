using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class RecruitmentRules
{
    internal static int Threshold(WorldMutation world, int actorId, int targetId)
    {
        var actor = world.Officers[actorId];
        var target = world.Officers[targetId];
        var force = world.Forces[actor.OwnerId!.Value];
        var leader = world.Officers[force.LeaderId!.Value];
        int recruitingLoyalty = target.LoyaltyTo(force.Id);
        if (recruitingLoyalty < 50) return 0;
        var oldForce = target.OwnerId is int owner ? world.Forces[owner] : null;
        int currentLoyalty = oldForce is null ? 70 : target.Loyalty;
        if (target.Status != OfficerStatus.Captive && currentLoyalty > 99) return 0;
        if (target.IsCommander && target.SettlementId is int location && world.Settlements[location].Kind == SettlementKind.City)
            currentLoyalty += 20;
        int oldAffinity = oldForce?.LeaderId is int oldLeader ? PersonnelRulebook.AffinityDistance(target, world.Officers[oldLeader]) : 50;
        int newAffinity = PersonnelRulebook.AffinityDistance(target, leader);
        int politicsBonus = actor.HasRecruitmentPoliticsBonus ? Math.Max(0, 100 - actor.BaseAbilities.Politics) : 0;
        long threshold = (long)world.Abilities(actor).Politics + politicsBonus + (target.Status == OfficerStatus.Captive ? 30 : 0)
            + (force.Trust - (oldForce?.Trust ?? 0)) / 5 + (oldAffinity - newAffinity) / 5
            - target.Righteousness - currentLoyalty + recruitingLoyalty - 37;
        return (int)Math.Clamp(threshold, int.MinValue, int.MaxValue);
    }

    internal static void Failure(WorldMutation world, int forceId, int targetId, int threshold, List<DomainEvent> events)
    {
        var target = world.Officers[targetId];
        if (target.Status != OfficerStatus.Captive) ChangeRelation(world, forceId, target.OwnerId, 1, events);
        var leader = world.Officers[world.Forces[forceId].LeaderId!.Value];
        int increaseThreshold = 75 - PersonnelRulebook.AffinityDistance(target, leader);
        if (world.NextRandom("personnel-recruit-feedback") % 100 < (ulong)increaseThreshold)
            SetLoyalty(world, targetId, forceId, Math.Min(255, target.LoyaltyTo(forceId) + 1), "recruit-failure-interest", events);
        if (target.OwnerId is int oldForce && target.Status is OfficerStatus.Governor or OfficerStatus.Strategist or OfficerStatus.Serving &&
            target.Righteousness < 15 && world.Forces[oldForce].LeaderId is int oldLeader &&
            PersonnelRulebook.AffinityDistance(target, world.Officers[oldLeader]) > 0)
        {
            int roll = (int)(world.NextRandom("personnel-recruit-feedback") % 100);
            if (roll < threshold) SetLoyalty(world, targetId, oldForce, Math.Max(0, target.LoyaltyTo(oldForce) - 1), "recruit-failure-doubt", events);
        }
    }

    internal static void SetLoyalty(WorldMutation world, int personId, int forceId, int value, string reason, List<DomainEvent> events)
    {
        var person = world.Officers[personId];
        world.Replace(person.WithLoyalty(forceId, value));
        events.Add(new OfficerLoyaltyChangedEvent(world.Clock, personId, forceId, person.LoyaltyTo(forceId), value, reason));
    }

    internal static void ChangeRelation(WorldMutation world, int forceId, int? otherId, int baseLoss, List<DomainEvent> events)
    {
        if (otherId is not int other || other == forceId) return;
        var key = (Math.Min(forceId, other), Math.Max(forceId, other));
        if (!world.Relations.TryGetValue(key, out var relation)) throw new InvalidOperationException("Missing recruitment force relation.");
        int next = Math.Max(0, relation.Value - baseLoss - (int)(world.NextRandom("personnel-recruit-relations") % 3));
        world.Replace(relation with { Value = next });
        events.Add(new ForceRelationChangedEvent(world.Clock, key.Item1, key.Item2, relation.Value, next));
    }
}
