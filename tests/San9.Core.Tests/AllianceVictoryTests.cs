using San9.Tests;
using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class AllianceVictoryTests
{
    [Theory]
    [InlineData(1, false, 100, true)]
    [InlineData(2, false, 100, true)]
    [InlineData(4, false, 100, false)]
    [InlineData(1, true, 100, false)]
    [InlineData(1, false, 995, true)]
    [InlineData(1, false, 1000, true)]
    public void LastCityCaptureCreditsOnlyAlliedVictorBeforeSourceAbsorption(int victor, bool remainingCity, int trust, bool success)
    {
        var cities = Enumerable.Range(1, 5).Select(i => new SettlementState(i, i == 5 ? (remainingCity ? 3 : 4) : i,
            i == 3 ? 0u : 100u, 100, 100, 0, 0, 0, 100, 0, 100));
        var people = Enumerable.Range(1, 4).Select(i => new OfficerState(i * 10, i, i == 3 && remainingCity ? 5 : i, new(60, 60, 60, 60), false)
            { Status = OfficerStatus.Ruler }).Append(new(11, victor, null, new(60, 60, 60, 60), false) { UnitId = 1 });
        var world = WorldState.Create(new(190, 1, 1), 42,
            Enumerable.Range(1, 4).Select(i => new ForceState(i, 100)
                { LeaderId = i * 10, BaseTrust = i == victor ? trust : 100 }),
            cities, people, units: [new(1, victor, new(32, 7), 1000) { FormationId = 1, Morale = 100, Order = new(MilitaryOrderKind.AttackSettlement, null, 3),
                MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(11, BattleRank.Middle) }],
            alliance: new(3, 1, [1, 2], 20), corps: [.. Enumerable.Range(1, 4).Select(i => new San9.Core.Domain.CorpsState(i, i, i * 10, new(1000, 1000)) { IsRoot = true })]);
        var military = new MilitaryRulebook(new(20, 3, [.. Enumerable.Repeat(0, 60)], [new(0, 1, 1)],
            [new(1, 1, 0, 10000, 1, new(1, 1), TravelMode.Land)
                { Combat = new(10, 10, 100, 10, 50), Attack = new(15, true), CaptureRanks = [BattleRank.Middle] }],
            [.. Enumerable.Range(1, 5).Select(i => new BattlefieldPlace(i, new(i * 10 + 2, 7), new(1, 1))
                { Combat = new(10, 10, 0, 10, 50) })]));
        var rules = new SimulationRules { Military = military };
        var m = world.BeginMutation();
        m.SetSimulation(m.Simulation with { Boundary = SimulationBoundary.Stable });
        List<DomainEvent> events = [];
        BattleAftermath.ResolveSettlementDefeat(m, 3, new(1, false), rules, events);
        Assert.False(m.Units.ContainsKey(1));
        Assert.Equal(!remainingCity, m.Forces[3].IsEliminated);
        Assert.Equal(success ? Math.Min(1000, trust + 15) : trust, m.Forces[victor].BaseTrust);
        if (success)
        {
            var victory = Assert.Single(events.OfType<AllianceVictoryEvent>());
            Assert.Equal((3, victor, AllianceVictoryCause.ForceDefeated, Math.Min(15, 1000 - trust)),
                (victory.TargetForceId, victory.VictorForceId, victory.Cause, victory.BaseTrustGained));
            Assert.Equal(AllianceEndReason.SubjugationSucceeded, Assert.Single(events.OfType<AllianceEndedEvent>()).Reason);
        }
        else Assert.Empty(events.OfType<AllianceVictoryEvent>());
        if (remainingCity) Assert.NotNull(m.Simulation.Alliance);
        else Assert.Null(m.Simulation.Alliance);
        int count = events.Count;
        GovernanceLifecycle.Reconcile(m, new HashSet<int> { 3 }, rules, events, true);
        Assert.Equal(count, events.Count);
        m.MaintainDerivedState(events);
        world.Commit(m);
        StateAssert.Equal(world.CreateSnapshot(), WorldState.Restore(world.CreateSnapshot()).CreateSnapshot());
    }
}
