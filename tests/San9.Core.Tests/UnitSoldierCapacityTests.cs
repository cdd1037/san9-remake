using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class UnitSoldierCapacityTests
{
    [Theory]
    [InlineData(false, false, 100000u, 100000u, 15000u)]
    [InlineData(false, false, 12000u, 100000u, 12000u)]
    [InlineData(false, false, 100000u, 11000u, 11000u)]
    [InlineData(true, false, 100000u, 100000u, 25000u)]
    [InlineData(true, true, 100000u, 100000u, 20000u)]
    public void CapacityCombinesCurrentCommanderWithBothFormations(bool ruler, bool unranked,
        uint landCapacity, uint navalCapacity, uint expected)
    {
        var governance = new GovernanceRulebook([new(5, 15000, 0)],
            new Dictionary<int, int> { [1] = 1 }, new Dictionary<int, uint> { [1] = 25000 });
        var military = new MilitaryRulebook(new(1, 1, [0], [new(0, 1, null)],
            [new(9, 1, 0, landCapacity, 5, new(1, 1), TravelMode.Land),
             new(10, 1, 0, navalCapacity, 5, new(1, 1), TravelMode.Water)], []));
        var snapshot = UnitStrategyTests.World(friendly: true).CreateSnapshot();
        snapshot = snapshot with
        {
            Forces = snapshot.Forces.Select(f => f.Id == 1 ? f with
                { LeaderId = ruler ? 22 : null, RankId = unranked ? null : 1 } : f).ToArray(),
            Officers = snapshot.Officers.Select(p => p.Id == 22 ? p with
                { Status = ruler ? OfficerStatus.Ruler : OfficerStatus.Serving, OfficeId = 5 } : p).ToArray()
        };
        var unit = snapshot.Units[0] with { NavalFormationId = 10, Soldiers = 50000 };
        Assert.Equal(expected, UnitSoldierCapacity.Read(snapshot, unit, governance, military));
    }

    [Theory]
    [InlineData(false, 10000u, 1000u)]
    [InlineData(false, 8000u, 0u)]
    [InlineData(true, 12000u, 3000u)]
    [InlineData(true, 9000u, 0u)]
    public void ReinforcementRechecksOfficeOrRulerRankAtArrivalAndPreservesOverflow(bool ruler,
        uint capacity, uint delivered)
    {
        var governance = new GovernanceRulebook([new(5, 15000, 0), new(6, capacity, 0)],
            new Dictionary<int, int> { [1] = 1, [2] = 2 },
            new Dictionary<int, uint> { [1] = 15000, [2] = capacity });
        var basis = UnitStrategyTests.Rules();
        var rules = new SimulationRules
        {
            Governance = governance, Economy = new([new(5, 0), new(6, 0)], []),
            Personnel = basis.Personnel, Military = basis.Military
        };
        var world = UnitStrategyTests.World(friendly: true, distance: 30);
        var setup = world.BeginMutation(rules.Abilities);
        setup.Replace(setup.Units[5] with { Soldiers = 9000 });
        setup.Replace(setup.Officers[22] with
            { Status = ruler ? OfficerStatus.Ruler : OfficerStatus.Serving, OfficeId = 5 });
        setup.Replace(setup.Forces[1] with { LeaderId = ruler ? 22 : null, RankId = 1 });
        setup.Replace(setup.Corps[1] with { LeaderId = ruler ? 22 : null });
        world.Commit(setup);
        UnitStrategyTests.Submit(world, rules, UnitStrategyKind.Reinforce, 0);

        var state = world.BeginMutation(rules.Abilities);
        state.Replace(state.Officers[22] with { OfficeId = 6 });
        state.Replace(state.Forces[1] with { RankId = 2 });
        world.Commit(state);
        state = world.BeginMutation(rules.Abilities);
        UnitStrategyTests.Day(state, rules);

        Assert.Equal(9000u + delivered, state.Units[5].Soldiers);
        var returning = state.Simulation.Tasks.SingleOrDefault(t => t.Kind == PersonnelReturns.Kind);
        Assert.Equal(3000u - delivered, returning?.Reinforcements?.Soldiers ?? 0);
    }
}
