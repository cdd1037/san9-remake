using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

internal static class PersonnelConfiguration
{
    internal static ImmutableArray<AllocationCandidate> Capture(SimulationEngine engine, ControlSeat active,
        ImmutableArray<(string ControllerId, EconomicObservation Observation)> economies, AllocationWork work)
    {
        var result = ImmutableArray.CreateBuilder<AllocationCandidate>();
        var world = engine.Snapshot;
        // Force appointments are not an extension of a player's delegation grant.
        if (active.Kind != ControllerKind.Automatic) return [];
        var economy = economies.FirstOrDefault(e => e.ControllerId == active.ControllerId).Observation;
        if (economy is null) return [];
        var force = world.Forces.Single(f => f.Id == active.ForceId);
        var root = world.Corps.Single(c => c.OwnerId == force.Id && c.IsRoot);
        if (!economy.Accounts.Any(a => a.Id == root.Id)) return [];
        var people = world.Officers.Where(p => !p.IsDormant && p.OwnerId == force.Id && p.IsServing).OrderBy(p => p.Id).ToArray();
        var abilities = engine.GetAbilities(world);
        int current = force.AdviserId is int adviser ? abilities[adviser].Intelligence : 0;
        foreach (var person in people)
        {
            if (!work.Spend()) return result.ToImmutable();
            if (person.Loyalty < 95 && person.Righteousness < 20)
                Add(new RewardOfficerCommand(Guid.Empty, force.Id, 0, person.Id), person.Id,
                    (100 - person.Loyalty) * 100L, 100, $"reward:{person.Id}", 1);
            // Appointment only changes consultation ownership. Equipment and offices remain independent decisions.
            if (person.Status == OfficerStatus.Serving && abilities[person.Id].Intelligence > current)
                Add(new AppointAdviserCommand(Guid.Empty, force.Id, 0, person.Id), person.Id,
                    (AdviserAccuracy.Percent(abilities[person.Id].Intelligence) - (force.AdviserId is null ? 50 : AdviserAccuracy.Percent(current))) * 120L,
                    0, "adviser", 2);
        }
        foreach (var office in engine.GetAvailableOffices(force.Id))
        {
            if (!work.Spend()) return result.ToImmutable();
            var holder = people.FirstOrDefault(p => p.OfficeId == office.OfficeId);
            // Compare a quarter of recovered post value, including salary and the old holder's real loyalty loss.
            long Value(OfficerState p) => PostValue(engine.EvaluateOfficerAbilities(p with { Injury = 0, OfficeId = null },
                world.Treasures), office.AbilityBonuses) * 9;
            var best = people.Where(p => p.Id != force.LeaderId && p.OfficeId is null)
                .OrderByDescending(Value).ThenBy(p => p.Id).FirstOrDefault();
            if (best is null) continue;
            long salaryIncrease = holder is null ? Math.Max(0, office.Salary - 1) * 9L : 0;
            long benefit = Value(best) - (holder is null ? 0 : Value(holder) + office.LoyaltyBonus * 100L) - salaryIncrease;
            if (benefit <= 0) continue;
            ImmutableArray<OfficeAssignment> assignments = holder is null ? [new(best.Id, office.OfficeId)]
                : [new(holder.Id, null), new(best.Id, office.OfficeId)];
            Add(new AssignOfficesCommand(Guid.Empty, force.Id, 0, assignments), best.Id, benefit,
                salaryIncrease, $"office:{office.OfficeId}", 3);
        }
        foreach (var treasure in world.Treasures.Where(t => t.Discovered && t.HolderId is int holder && people.Any(p => p.Id == holder)))
        {
            if (!work.Spend()) return result.ToImmutable();
            var definition = engine.GetTreasureDefinition(treasure.Id);
            if (definition is null) continue;
            var without = world.Treasures.Where(t => t.Id != treasure.Id).ToArray();
            long Gain(OfficerState p)
            {
                var healthy = p with { Injury = 0 };
                var before = engine.EvaluateOfficerAbilities(healthy, without);
                var after = engine.EvaluateOfficerAbilities(healthy, without.Append(treasure with { HolderId = p.Id }));
                return PostValue(before, new(after.War - before.War, after.Intelligence - before.Intelligence,
                    after.Politics - before.Politics, after.Leadership - before.Leadership)) * 9;
            }
            var best = people.OrderByDescending(Gain).ThenBy(p => p.Id).First();
            var holder = people.Single(p => p.Id == treasure.HolderId);
            long benefit = Gain(best) - Gain(holder) - (holder.Id == force.LeaderId ? 0 : 3L * definition.LoyaltyBonus / 2 * 100);
            if (best.Id == holder.Id || benefit <= 100) continue;
            GameCommand command = holder.Id == force.LeaderId
                ? new GrantTreasureCommand(Guid.Empty, force.Id, 0, treasure.Id, best.Id)
                : new ConfiscateTreasureCommand(Guid.Empty, force.Id, 0, treasure.Id);
            Add(command, best.Id, benefit, 0, $"treasure:{treasure.Id}", 3);
        }
        return result.ToImmutable();

        void Add(GameCommand intent, int person, long benefit, long cost, string purpose, int priority)
        {
            if (benefit <= 0 || !work.Spend()) return;
            var query = intent with { CommandId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Sequence = world.Simulation.NextSequence };
            if (!engine.Validate(new(active.ControllerId), query).Accepted) return;
            result.Add(new(active.ControllerId, root.Id, intent, [], priority, benefit, cost, 0,
                people.Single(p => p.Id == person).SettlementId, person, Purpose: purpose));
        }
    }

    private static long PostValue(OfficerAbilities abilities, OfficerAbilities bonus) =>
        (long)abilities.Politics * bonus.Politics + (long)abilities.Leadership * bonus.Leadership +
        (long)abilities.War * bonus.War + (long)abilities.Intelligence * bonus.Intelligence;
}
