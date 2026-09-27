using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class MilitaryEngagementTests
{
    private static MilitaryOrder Attack(int id) => new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = id };
    private static (WorldState World, MilitaryRulebook Military) Create()
    {
        var combat = new CombatProfile(10, 10, 10, 10, 50);
        var military = new MilitaryRulebook(new(20, 3, [.. Enumerable.Repeat(0, 60)], [new(0, 1, null)],
            [new(1, 5, 0, 100000, 1, new(1, 1), TravelMode.Land)
                { Combat = combat, Attack = new(10, true), CaptureRanks = [BattleRank.Middle] }],
            [new(1, new(2, 7), new(1, 1)) { Combat = combat, WoundedRecovery = 1 },
             new(2, new(40, 7), new(1, 1)) { Combat = combat, Attack = new(10, false), WoundedRecovery = 1 },
             new(3, new(60, 7), new(1, 1)) { Combat = combat, Attack = new(10, false) }]));
        var world = WorldState.Create(new(190, 1, 1), 42,
            [new(1, 100), new(2, 100), new(3, 100)],
            [new(1, 1, 10000, 100, 100, 100, 100, 0, 100, 0, 100),
             new(2, 2, 10000, 100, 100, 100, 100, 0, 100, 0, 100)],
            [new(11, 1, 1, new(70, 70, 70, 70), false)],
            units: [new(1, 1, new(32, 7), 10000) { FormationId = 1, Morale = 90 },
                    new(2, 2, new(36, 7), 10000) { FormationId = 1, Morale = 90, Order = Attack(1) },
                    new(3, 2, new(35, 7), 10000) { FormationId = 1, Morale = 90, Order = Attack(1) }],
            relations: [new(1, 2, 0), new(1, 3, 0), new(2, 3, 0)], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(1000, 100000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, null, new(1000, 100000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(3, 3, null, new(1000, 100000)) { IsRoot = true }]);
        return (world, military);
    }

    [Fact]
    public void PassiveEngagementSurvivesUntilLastAttackerLeavesAndTracksNewArrivals()
    {
        var (world, military) = Create(); var m = world.BeginMutation();
        Assert.Equal(3, m.Engagements(military).IncomingCount(new(1, false)));
        Assert.True(MilitaryEngagements.Engaged(m, m.Units[1], military));
        Assert.Null(m.Engagements(military).Target(new(1, false)));
        Assert.False(m.Engagements(military).Engaged(new(2, true)));
        m.Replace(m.Units[2] with { Order = null });
        Assert.Equal(2, m.Engagements(military).IncomingCount(new(1, false)));
        m.RemoveUnit(3);
        Assert.Equal(1, m.Engagements(military).IncomingCount(new(1, false)));
        m.Replace(m.Settlements[2] with { ConfusionDaysRemaining = 2 });
        Assert.False(MilitaryEngagements.Engaged(m, m.Units[1], military));
        m.AddUnit(new(4, 2, new(34, 7), 1000) { FormationId = 1, Morale = 90, Order = Attack(1) });
        Assert.True(MilitaryEngagements.Engaged(m, m.Units[1], military));
        m.Replace(m.Units[1] with { Position = new(70, 7) });
        Assert.False(MilitaryEngagements.Engaged(m, m.Units[1], military));
        Assert.Null(m.Engagements(military).Target(new(4, false)));
    }

    [Theory]
    [InlineData(30, UnitCondition.Normal, true)]
    [InlineData(29, UnitCondition.Normal, false)]
    [InlineData(30, UnitCondition.Confused, false)]
    public void PublicSettlementProjectionUsesPassiveReachWithoutChangingTheSnapshot(int x, UnitCondition condition, bool engaged)
    {
        var (world, military) = Create();
        var snapshot = new SimulationEngine(world, EconomyRulebook.Empty, PersonnelRulebook.Empty,
            TreasureRulebook.Empty, military: military).CaptureStableState();
        Assert.Empty(MilitaryEngagementQuery.EngagedSettlements(snapshot, military));
        snapshot = snapshot with { Units = snapshot.Units.Select(u => u.Id != 1 ? u : u with
            { Position = new(x, 7), Condition = condition, Order = new(MilitaryOrderKind.AttackSettlement, null, 2) }).ToArray() };
        string hash = snapshot.StateHash;
        Assert.Equal(engaged, MilitaryEngagementQuery.EngagedSettlements(snapshot, military).Contains(2));
        Assert.Equal(hash, snapshot.StateHash);
    }

    [Fact]
    public void UnrelatedDailyChangesReuseRelationsAndReturnCurrentTargetState()
    {
        var (world, military) = Create(); var m = world.BeginMutation();
        var relations = m.Engagements(military);
        m.Replace(m.Units[1] with { Soldiers = 9000, Morale = 80, Wounded = 500, CargoFood = 50 });
        m.Replace(m.Settlements[2] with { Soldiers = 9000, Morale = 80, Durability = 90 });
        m.Replace(m.Officers[11] with { TacticCooldown = 3, Acted = true });
        m.Replace(ResourceAccounts.Root(m, 1) with { Treasury = ResourceAccounts.Root(m, 1).Treasury with { Food = 99000 } });
        m.SetClock(m.Clock.NextDay());
        m.NextRandom("engagement-test");
        BattleLearning.Daily(m, military, []);
        Assert.Same(relations, m.Engagements(military));
        Assert.Same(m.Units[1], MilitaryEngagements.FacilityTarget(m, m.Settlements[2], military));
        Assert.Equal(9000u, MilitaryEngagements.FacilityTarget(m, m.Settlements[2], military)!.Soldiers);
    }

    [Theory]
    [InlineData("order")]
    [InlineData("temporary-target")]
    [InlineData("confusion")]
    [InlineData("soldiers")]
    [InlineData("morale")]
    [InlineData("owner")]
    [InlineData("elimination")]
    public void AttackEligibilityChangesAreVisibleBeforeTheNextQuery(string change)
    {
        var (world, military) = Create(); var m = world.BeginMutation();
        Assert.Equal(new BattleSource(1, false), m.Engagements(military).Target(new(2, false)));
        var unit = m.Units[2];
        if (change == "elimination") m.Replace(m.Forces[2] with { IsEliminated = true });
        else m.Replace(change switch
        {
            "order" => unit with { Order = new(MilitaryOrderKind.Return, null, 2) },
            "temporary-target" => unit with { EngagementTargetId = 3 },
            "confusion" => unit with { Condition = UnitCondition.Confused },
            "soldiers" => unit with { Soldiers = 0 },
            "morale" => unit with { Morale = 0 },
            "owner" => unit with { OwnerId = 1 },
            _ => throw new InvalidOperationException()
        });
        Assert.Null(m.Engagements(military).Target(new(2, false)));
    }

    [Fact]
    public void FacilitySelectionRespondsToDiplomacyOwnershipAndTargetRemoval()
    {
        var (world, military) = Create(); var m = world.BeginMutation();
        Assert.Equal(1, MilitaryEngagements.FacilityTarget(m, m.Settlements[2], military)!.Id);
        m.Replace(m.Relations[(1, 2)] with { Value = 40 });
        Assert.Null(MilitaryEngagements.FacilityTarget(m, m.Settlements[2], military));
        // Explicit attacks still engage even when automatic attacks are disallowed.
        Assert.Equal(2, m.Engagements(military).IncomingCount(new(1, false)));
        m.Replace(m.Relations[(1, 2)] with { Value = 39 });
        Assert.Equal(1, MilitaryEngagements.FacilityTarget(m, m.Settlements[2], military)!.Id);
        m.SetSimulation(m.Simulation with { Alliance = new(3, 1, [1, 2], 10) });
        Assert.Null(MilitaryEngagements.FacilityTarget(m, m.Settlements[2], military));
        m.SetSimulation(m.Simulation with { Alliance = null });
        Assert.Equal(1, MilitaryEngagements.FacilityTarget(m, m.Settlements[2], military)!.Id);
        m.AddUnit(new(4, 1, new(38, 7), 1000) { FormationId = 1, Morale = 90 });
        Assert.Equal(4, MilitaryEngagements.FacilityTarget(m, m.Settlements[2], military)!.Id);
        m.RemoveUnit(4);
        Assert.Equal(1, MilitaryEngagements.FacilityTarget(m, m.Settlements[2], military)!.Id);
        m.Replace(m.Settlements[2] with { OwnerId = 1 });
        Assert.Equal(2, MilitaryEngagements.FacilityTarget(m, m.Settlements[2], military)!.Id);
        m.Replace(m.Settlements[2] with { IsDestroyed = true });
        Assert.Null(MilitaryEngagements.FacilityTarget(m, m.Settlements[2], military));
    }

    [Fact]
    public void NewFacilityImmediatelyParticipatesInRelations()
    {
        var (world, military) = Create(); var m = world.BeginMutation();
        m.Replace(m.Units[1] with { Position = new(52, 7) });
        Assert.False(MilitaryEngagements.Engaged(m, m.Units[1], military));
        m.AddSettlement(new(3, 2, 1000, 100, 100, 100, 100, 0, 100, 0, 100) { Kind = SettlementKind.Facility });
        Assert.Equal(new BattleSource(1, false), m.Engagements(military).Target(new(3, true)));
        Assert.True(MilitaryEngagements.Engaged(m, m.Units[1], military));
    }

    [Fact]
    public void RestoreRebuildsRelationsAndProducesTheSameCombatEvents()
    {
        var (world, military) = Create();
        SimulationEngine Engine(WorldState state) => new(state, EconomyRulebook.Empty, PersonnelRulebook.Empty,
            TreasureRulebook.Empty, military: military);
        var engine = Engine(world);
        var restored = Engine(WorldState.Restore(engine.CaptureStableState()));
        var result = engine.AdvanceXun();
        Assert.NotEmpty(result.Events.OfType<BattleDamageEvent>());
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
    }

    [Theory]
    [InlineData(30, UnitCondition.Normal, true)]
    [InlineData(29, UnitCondition.Normal, false)]
    [InlineData(30, UnitCondition.Confused, false)]
    [InlineData(30, UnitCondition.Routed, false)]
    public void WoundedRecoveryUsesTheSamePassiveEngagementAndStrictRangeBoundary(int x, UnitCondition condition, bool engaged)
    {
        var (world, military) = Create(); var m = world.BeginMutation();
        // The defending facility's own outgoing attack does not count as being besieged.
        Assert.False(m.Engagements(military).Engaged(new(2, true)));
        m.Replace(m.Settlements[2] with { Wounded = 10000 });
        m.Replace(m.Units[1] with { Position = new(x, 7), Condition = condition,
            Order = new(MilitaryOrderKind.AttackSettlement, null, 2) });
        Assert.Equal(engaged, m.Engagements(military).Engaged(new(2, true)));
        WoundedRecovery.Apply(m, military, []);
        uint recovered = 10000 - m.Settlements[2].Wounded;
        Assert.InRange(recovered, engaged ? 1100u : 2200u, engaged ? 1199u : 2398u);
    }
}
