using San9.Core.Domain;
using San9.Core.Simulation;
using System.Collections.Immutable;

namespace San9.Core.Tests;

public sealed class DemobilizationTests
{
    [Fact]
    public void MixedCombatLossesAndReturnPreserveBothDetachmentsAcrossRestore()
    {
        var world = WorldState.Create(new(190, 1, 1), 42, [new(1, 0), new(2, 0)],
            [new(1, 1, 100, 100, 100, 50, 0, 0, 100, 0, 100), new(2, 2, 100, 100, 100, 50, 0, 0, 100, 0, 100)],
            [new OfficerState(11, 1, null, new(70, 70, 70, 70), false) { UnitId = 0 },
                new OfficerState(12, 1, null, new(70, 70, 70, 70), false) { UnitId = 0 }],
            units: [new(0, 1, new(2, 2), 1000)
            {
                Morale = 90,
                MemberPositions = ImmutableDictionary<int, FormationPosition>.Empty
                    .Add(11, new(0, BattleRank.Front)).Add(12, new(0, BattleRank.Rear)),
                Detachments = ImmutableDictionary<int, DetachmentState>.Empty
                    .Add(11, new(0, 1, 400) { Weapon = new("spear", 200, 400, 0) })
                    .Add(12, new(0, 1, 600) { Armor = new("armor", 600, 600, 0) })
            }], corps: [new CorpsState(1, 1, null, new(1000, 1000)) { IsRoot = true },
                new CorpsState(2, 2, null, new(1000, 1000)) { IsRoot = true }]);
        var geography = new PersonnelRulebook([new(1, new(2, 2)), new(2, new(5, 2))]);
        var military = new MilitaryRulebook(new(8, 3, [.. Enumerable.Repeat(0, 24)],
            [new(0, 1, null)], [], [new(1, new(2, 2), new(1, 1)), new(2, new(5, 2), new(1, 1))]));
        var mutation = world.BeginMutation(); List<DomainEvent> events = [];
        var profile = new CombatProfile(10, 10, 10, 10, 50);
        MilitaryCombat.ApplyUnitLoss(mutation, mutation.Units[0], 200, 0, profile, new(2, true), events);
        MilitaryCombat.ApplyUnitLoss(mutation, mutation.Units[0], 100, 0, profile, new(2, true), events, ranged: true);
        Assert.Equal(87u, mutation.Units[0].Detachments[11].Weapon!.RemainingSets);
        Assert.Equal(525u, mutation.Units[0].Detachments[12].Armor!.RemainingSets);
        mutation.SetSimulation(mutation.Simulation with { Boundary = SimulationBoundary.Stable });
        world.Commit(mutation);

        var restored = WorldState.Restore(world.CreateSnapshot());
        mutation = restored.BeginMutation();
        MilitaryMovement.Absorb(mutation, mutation.Units[0], 1, new([]), geography, military, events);
        Assert.Empty(mutation.Units);
        Assert.Equal(87u, mutation.Forces[1].ArmamentInventory!["spear"]);
        Assert.Equal(525u, mutation.Forces[1].ArmamentInventory!["armor"]);
        Assert.Single(events.OfType<UnitAbsorbedEvent>());
        mutation.SetSimulation(mutation.Simulation with { Boundary = SimulationBoundary.Stable });
        restored.Commit(mutation);
        var saved = WorldState.Restore(restored.CreateSnapshot()).CreateSnapshot();
        Assert.Equal(87u, saved.Forces.Single(f => f.Id == 1).ArmamentInventory!["spear"]);
        Assert.Equal(525u, saved.Forces.Single(f => f.Id == 1).ArmamentInventory!["armor"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AbsorptionReturnsRemainingArmamentsExactlyOnceAndKeepsThemAfterRestore(bool full)
    {
        var force = new ForceState(1, 0) { ArmamentInventory = ImmutableDictionary<string, uint>.Empty.Add("spear", full ? uint.MaxValue : 20) };
        var world = WorldState.Create(new(190, 1, 1), 42, [force],
            [new(1, 1, 100, 100, 100, 50, 0, 0, 100, 0, 100)],
            [new(11, 1, null, new(70, 70, 70, 70), false) { UnitId = 0 }],
            units: [new(0, 1, new(2, 2), 80)
            {
                Detachments = ImmutableDictionary<int, DetachmentState>.Empty.Add(11,
                    new DetachmentState(0, 1, 80) { Weapon = new("spear", 50, 100, 20), Armor = new("armor", 100, 100, 20) })
            }], corps: [new CorpsState(1, 1, null, new(1000, 1000)) { IsRoot = true }]);
        var geography = new PersonnelRulebook([new(1, new(2, 2))]);
        var military = new MilitaryRulebook(new(8, 3, [.. Enumerable.Repeat(0, 24)],
            [new(0, 1, null)], [], [new(1, new(2, 2), new(1, 1))]));
        var mutation = world.BeginMutation(); List<DomainEvent> events = [];

        MilitaryMovement.Absorb(mutation, mutation.Units[0], 1, new([]), geography, military, events);

        Assert.Empty(mutation.Units);
        Assert.Single(events.OfType<UnitAbsorbedEvent>());
        Assert.Equal(full ? uint.MaxValue : 60u, mutation.Forces[1].ArmamentInventory!["spear"]);
        Assert.Equal(full ? 40ul : 0ul, mutation.Forces[1].ArmamentReserve?.GetValueOrDefault("spear") ?? 0);
        Assert.Equal(80u, mutation.Forces[1].ArmamentInventory!["armor"]);
        mutation.SetSimulation(mutation.Simulation with { Boundary = SimulationBoundary.Stable });
        world.Commit(mutation);
        var restored = WorldState.Restore(world.CreateSnapshot()).CreateSnapshot();
        Assert.Equal(full ? uint.MaxValue : 60u, restored.Forces.Single().ArmamentInventory!["spear"]);
        Assert.Equal(full ? 40ul : 0ul, restored.Forces.Single().ArmamentReserve?.GetValueOrDefault("spear") ?? 0);
        Assert.Equal(80u, restored.Forces.Single().ArmamentInventory!["armor"]);
    }

    [Fact]
    public void ReturnedPopulationSurvivesGrowthAndSaveRestoreAboveGrowthLimit()
    {
        var world = WorldState.Create(new(190, 4, 1), 42, [new(1, 0)],
            [new(1, 1, 0, 100, 100, 0, 100, 0, 100, 0, 100) { DraftPopulation = 499990, Population = 1000000 }], [], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(100000, 100000)) { IsRoot = true }]);
        var engine = new SimulationEngine(world, EconomyRulebook.Empty);
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Settlements[1] with { DraftPopulation = 499990 });
        List<DomainEvent> events = [];
        Demobilization.Return(mutation, 1, 120, "test-return", 2, events);
        Assert.Equal(500110u, mutation.Settlements[1].DraftPopulation);
        mutation.SetClock(new(190, 7, 1));
        EconomicCycle.Apply(ResolutionStage.QuarterUpdate, mutation, EconomyRulebook.Empty, events);
        Assert.Equal(500110u, mutation.Settlements[1].DraftPopulation);
        world.Commit(mutation);
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<WorldSnapshot>(CanonicalJson.Text(engine.CaptureStableState()), CanonicalJson.Options)!;
        var restored = WorldState.Restore(snapshot);
        Assert.Equal(500110u, restored.CreateSnapshot().Settlements[0].DraftPopulation);
        Assert.Equal(120, Assert.Single(events.OfType<ResourceChangedEvent>(), e => e.Reason == "test-return").Actual);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FullFacilityAbsorptionDemobilizesOverflowOrKeepsTheUnitWhenGeographyIsMissing(bool mapped)
    {
        var world = WorldState.Create(new(190, 1, 1), 42, [new(1, 0)],
            [new(1, 1, 100, 100, 100, 50, 0, 0, 100, 0, 100) { DraftPopulation = 500000 },
                new(2, 1, 90, 100, 100, 50, 0, 0, 100, 0, 100)
                    { Kind = SettlementKind.Facility, SoldierCapacity = 100, Wounded = 49990 }],
            [new(11, 1, null, new(70, 70, 70, 70), false) { UnitId = 0 }],
            units: [new(0, 1, new(7, 2), 40) { Wounded = 20, Morale = 100 }], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(1000, 1000)) { IsRoot = true }]);
        var geography = new PersonnelRulebook([new(1, new(2, 2)), new(2, new(7, 2))],
            mapped ? new Dictionary<int, int> { [2] = 1 } : null);
        var mutation = world.BeginMutation();
        List<DomainEvent> events = [];
        var military = new MilitaryRulebook(new(8, 3, [.. Enumerable.Repeat(0, 24)],
            [new(0, 1, null)], [], [new(1, new(2, 2), new(1, 1)), new(2, new(7, 2), new(1, 1))]));
        MilitaryMovement.Absorb(mutation, mutation.Units[0], 2, new([]), geography, military, events);
        world.Commit(mutation);
        var state = world.CreateSnapshot();
        if (mapped)
        {
            Assert.Empty(state.Units);
            Assert.Equal(100u, state.Settlements.Single(s => s.Id == 2).Soldiers);
            Assert.Equal(50000u, state.Settlements.Single(s => s.Id == 2).Wounded);
            Assert.Equal(500040u, state.Settlements.Single(s => s.Id == 1).DraftPopulation);
            Assert.Single(events.OfType<UnitAbsorbedEvent>());
        }
        else
        {
            Assert.Equal(60u, Assert.Single(state.Units).Soldiers + state.Units[0].Wounded);
            Assert.Empty(events.OfType<UnitAbsorbedEvent>());
            Assert.Equal(0, Assert.Single(state.Officers).UnitId);
        }
        Assert.Equal(550240L, state.Settlements.Sum(s => (long)s.Soldiers + s.Wounded + s.DraftPopulation)
            + state.Units.Sum(u => (long)u.Soldiers + u.Wounded));
    }
}
