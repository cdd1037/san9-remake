using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class DomesticDevelopmentHandler
{
    internal static DevelopmentOutcome Apply(WorldMutation world, DevelopmentCommand command, EconomyRulebook rules, List<DomainEvent> events)
    {
        if (!Enum.IsDefined(command.Activity)) return new(DevelopmentRejection.UnsupportedActivity);
        if (command.OfficerIds.IsDefaultOrEmpty || command.OfficerIds.Length > 5 || command.OfficerIds.Distinct().Count() != command.OfficerIds.Length)
            return new(DevelopmentRejection.InvalidParticipants);
        if (!world.Forces.TryGetValue(command.ForceId, out var force) || !world.Settlements.TryGetValue(command.SettlementId, out var city) ||
            command.OfficerIds.Any(id => !world.Officers.ContainsKey(id))) return new(DevelopmentRejection.UnknownObject);
        var people = command.OfficerIds.Select(id => world.Officers[id]).ToArray();
        if (city.OwnerId != force.Id || people.Any(p => p.OwnerId != force.Id)) return new(DevelopmentRejection.WrongOwner);
        if (people.Any(p => !p.IsServing || p.Acted || p.SettlementId != city.Id || world.Simulation.Tasks.Any(t => t.OfficerId == p.Id)))
            return new(DevelopmentRejection.OfficerUnavailable);
        var abilities = world.AbilitiesFor(people);
        var activity = command.Activity;
        if (city.Kind != SettlementKind.City && activity is not (DomesticActivity.Repair or DomesticActivity.Train)) return new(DevelopmentRejection.UnsupportedActivity);
        bool used = activity switch
        {
            DomesticActivity.Patrol => city.PatrolUsed, DomesticActivity.Commerce => city.CommerceUsed,
            DomesticActivity.Cultivation => city.CultivationUsed, DomesticActivity.Repair => city.RepairUsed,
            DomesticActivity.Recruit => city.RecruitUsed, _ => city.TrainUsed
        };
        if (used) return new(DevelopmentRejection.AlreadyUsed);
        var account = ResourceAccounts.Resolve(world, force.Id, city.CorpsId);
        uint funds = ResourceAccounts.Balance(world, account, ResourceKind.Money);
        int cost = 0, increment;
        if (activity == DomesticActivity.Recruit)
        {
            if (city.Soldiers >= city.SoldierCapacity) return new(DevelopmentRejection.AtMaximum);
            long potential = 3 * (100 * people.Sum(p => (long)abilities[p.Id].Leadership) / (people.Length + 4)) / 2;
            increment = (int)Math.Min(Math.Min(potential, city.DraftPopulation), Math.Min(10L * funds, city.SoldierCapacity - city.Soldiers));
            if (increment == 0) return new(DevelopmentRejection.NoRecruits);
            cost = (increment + 9) / 10;
            int morale = (int)(((long)city.Soldiers * city.Morale + (long)increment * EconomyMath.RecruitMorale(force.Trust)) / (city.Soldiers + increment));
            world.Replace(city with { Soldiers = city.Soldiers + (uint)increment, DraftPopulation = city.DraftPopulation - (uint)increment, Morale = morale, RecruitUsed = true });
            ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, city.Id), ResourceKind.DraftPopulation, "recruit", city.Id, -increment, -increment, events);
            ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, city.Id), ResourceKind.Soldiers, "recruit", city.Id, increment, increment, events);
        }
        else
        {
            (int current, int maximum) = activity switch
            {
                DomesticActivity.Patrol => (city.PublicOrder, 1000), DomesticActivity.Commerce => (city.Commerce, city.MaxCommerce),
                DomesticActivity.Cultivation => (city.Cultivation, city.MaxCultivation), DomesticActivity.Repair => (city.Durability, city.MaxDurability),
                _ => (city.Morale, 100)
            };
            if (current >= maximum) return new(DevelopmentRejection.AtMaximum);
            if (activity == DomesticActivity.Train)
            {
                if (city.Soldiers == 0) return new(DevelopmentRejection.NoSoldiers);
                long scale = 10L * (11 - people.Length) * (EconomyMath.RecruitMorale(force.Trust) - city.Morale + 100) / 100;
                increment = (int)Math.Min(100 - current, people.Length + people.Sum(p => EconomyMath.ExecutedAbility(abilities[p.Id].Leadership)) * scale / EconomyMath.SquareRoot(city.Soldiers + 10000L) / 10);
            }
            else
            {
                cost = people.Length * 50;
                if (funds < cost) return new(DevelopmentRejection.InsufficientFunds);
                increment = DomesticDevelopment.CalculateBaseEffect(activity, current, maximum, (int)Math.Min(funds, int.MaxValue), people.Select(p => abilities[p.Id]).ToArray());
            }
            if (increment < maximum - current && Doubled(world, rules, force, people, abilities, activity)) increment = (int)Math.Min(maximum - current, 2L * increment);
            world.Replace(activity switch
            {
                DomesticActivity.Patrol => city with { PublicOrder = current + increment, PatrolUsed = true },
                DomesticActivity.Commerce => city with { Commerce = current + increment, CommerceUsed = true },
                DomesticActivity.Cultivation => city with { Cultivation = current + increment, CultivationUsed = true },
                DomesticActivity.Repair => city with { Durability = current + increment, RepairUsed = true },
                _ => city with { Morale = current + increment, TrainUsed = true }
            });
        }
        if (cost > 0) ResourceAccounts.Change(world, account, ResourceKind.Money, -cost, activity.ToString().ToLowerInvariant(), city.Id, events, true);
        foreach (var person in people)
        {
            int[] categories = activity == DomesticActivity.Train ? [0, 1, 2, 3, 4] : activity == DomesticActivity.Repair ? [5, 6] : [];
            var proficiency = person.TacticProficiencies.ToBuilder();
            foreach (int category in categories) proficiency[category] = Math.Min(1000, proficiency[category] + (person.MasteredCategories.Contains(category) ? 3 : 1));
            world.Replace(person with { Acted = true, TacticProficiencies = proficiency.ToImmutable() });
        }
        if (activity == DomesticActivity.Patrol) EconomicCycle.UpdateTrust(world, rules, events);
        return new(DevelopmentRejection.None, increment, cost);
    }

    private static bool Doubled(WorldMutation world, EconomyRulebook rules, ForceState force, OfficerState[] people, IReadOnlyDictionary<int, OfficerAbilities> abilities, DomesticActivity activity)
    {
        if (people.Length < 2) return false;
        long Ability(OfficerState p) => activity switch { DomesticActivity.Patrol => abilities[p.Id].Intelligence,
            DomesticActivity.Commerce or DomesticActivity.Cultivation => abilities[p.Id].Politics, _ => abilities[p.Id].Leadership };
        var sorted = people.OrderByDescending(p => p.Status == OfficerStatus.Ruler)
            .ThenByDescending(Ability).ThenByDescending(p => p.Id).ToArray();
        foreach (var first in sorted)
        foreach (var second in sorted)
        {
            var pair = rules.Pairs.FirstOrDefault(p => p.Project == activity && p.FirstId == first.Id && p.SecondId == second.Id);
            if (pair is null || world.NextRandom("domestic-combination") % 100 >= 10) continue;
            if (force.LeaderId is int leader && world.Officers.TryGetValue(leader, out var ruler) &&
                Math.Min(EconomyMath.AffinityDistance(first.Affinity, ruler.Affinity), EconomyMath.AffinityDistance(second.Affinity, ruler.Affinity)) <= 5 &&
                (first.Status == OfficerStatus.Ruler) == pair.FirstRequiresRuler && (second.Status == OfficerStatus.Ruler) == pair.SecondRequiresRuler) return true;
        }
        int chance = 5 * (people.GroupBy(p => p.Affinity).Max(g => g.Count()) + 1);
        return world.NextRandom("domestic-combination") % 100 < (uint)chance;
    }
}
