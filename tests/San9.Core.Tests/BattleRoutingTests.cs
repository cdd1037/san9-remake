using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Core.Tests;

public sealed class BattleRoutingTests
{
    [Fact]
    public void DefeatedUnitReturnsSurvivingArmamentsOnceDuringPersonnelResolution()
    {
        var (world, military) = Fixture(80, false, 10000);
        var state = world.BeginMutation();
        state.Replace(state.Forces[1] with { IsBarbarian = true });
        state.Replace(state.Forces[2] with { ArmamentInventory = ImmutableDictionary<string, uint>.Empty.Add("spear", 300) });
        state.Replace(state.Units[1] with { Soldiers = 4000, Wounded = 200,
            Detachments = ImmutableDictionary<int, DetachmentState>.Empty.Add(22,
                new DetachmentState(1, 1, 4000) { Weapon = new("spear", 2500, 5000, 1000) }) });
        List<DomainEvent> events = [];

        BattleAftermath.TerminateUnit(state, 1, new(0, false), new SimulationRules { Military = military }, events);

        Assert.DoesNotContain(1, state.Units.Keys);
        Assert.Equal(2300u, state.Forces[2].ArmamentInventory!["spear"]);
        Assert.Single(events.OfType<UnitTerminatedEvent>());
        state.SetSimulation(state.Simulation with { Boundary = SimulationBoundary.Stable });
        world.Commit(state);
        Assert.Equal(2300u, WorldState.Restore(world.CreateSnapshot()).CreateSnapshot().Forces.Single(f => f.Id == 2).ArmamentInventory!["spear"]);
    }

    [Fact]
    public void DefeatOrdersVictoryWoundedPostActionPersonnelAndMoraleBeforeRemoval()
    {
        var (world, military) = Fixture(80, false, 10000);
        var state = world.BeginMutation();
        state.Replace(state.Forces[1] with { IsBarbarian = true });
        state.Replace(state.Units[1] with { Soldiers = 0, Wounded = 200 });
        List<DomainEvent> events = [];
        var rules = new SimulationRules { Military = military };
        BattleAftermath.TerminateUnit(state, 1, new(0, false), rules, events);
        int defeat = events.FindIndex(e => e is UnitDefeatedEvent);
        int wounded = events.FindIndex(e => e is BattleWoundedCapturedEvent);
        int policy = events.FindIndex(e => e is UnitPolicyRespondedEvent);
        int personnel = events.FindIndex(e => e is BattlePersonnelResolvedEvent);
        int morale = events.FindIndex(e => e is BattleVictoryMoraleChangedEvent);
        int removal = events.FindIndex(e => e is UnitTerminatedEvent);
        Assert.True(defeat >= 0 && wounded > defeat && policy > wounded && personnel > policy && morale > personnel && removal > morale,
            $"defeat={defeat}, wounded={wounded}, policy={policy}, personnel={personnel}, morale={morale}, removal={removal}");
        Assert.DoesNotContain(state.Units.Values, u => u.Id == 1);
        Assert.Contains(22, Assert.Single(events.OfType<BattlePersonnelResolvedEvent>()).RetreatedIds);
    }

    [Theory]
    [InlineData(39, false, 80000u, true)]
    [InlineData(41, false, 80000u, false)]
    [InlineData(39, true, 80000u, false)]
    [InlineData(39, false, 10000u, false)]
    public void OrdinaryCombatProducesRoutOnlyWhenPostCombatGatesPass(int morale, bool protection, uint soldiers, bool routed)
    {
        var (world, military) = Fixture(morale, protection, soldiers);
        var engine = new SimulationEngine(world, EconomyRulebook.Empty, PersonnelRulebook.Empty, TreasureRulebook.Empty, military: military);
        var result = engine.AdvanceDay();
        Assert.Contains(result.Events.OfType<BattleDamageEvent>(), e => e.TargetUnitId == 1);
        var unit = result.Snapshot.Units.Single(u => u.Id == 1);
        Assert.Equal(routed ? UnitCondition.Routed : UnitCondition.Normal, unit.Condition);
        if (routed)
        {
            Assert.InRange(unit.ConditionDaysRemaining, 5, 9);
            Assert.Equal(MilitaryOrderKind.Return, unit.EffectiveOrder!.Kind);
            Assert.Single(result.Events.OfType<BattleConditionChangedEvent>(), e => e.Condition == UnitCondition.Routed);
        }
        else Assert.DoesNotContain(result.Events.OfType<BattleConditionChangedEvent>(), e => e.Condition == UnitCondition.Routed);
    }

    [Theory]
    [InlineData(39999u, 39, false)]
    [InlineData(40000u, 39, true)]
    [InlineData(40000u, 40, false)]
    public void UsesExactCurrentTroopRatioAndStrictMoraleBoundary(uint soldiers, int morale, bool routed)
    {
        var (world, military) = Fixture(morale, false, soldiers);
        var state = world.BeginMutation();
        List<DomainEvent> events = [];
        BattleRouting.CompleteExchange(state, new(0, false), new(1, false), new() { Military = military }, events);
        Assert.Equal(routed ? UnitCondition.Routed : UnitCondition.Normal, state.Units[1].Condition);
        Assert.Equal(routed ? 1 : 0, events.OfType<BattleConditionChangedEvent>().Count());
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void SumsOnlyDirectedAttackersInsteadOfNearbyTroops(bool attacksSubject, bool routed)
    {
        var (world, military) = Fixture(39, false, 20000);
        var snapshot = world.CreateSnapshot();
        var second = snapshot.Units.Single(u => u.Id == 0) with
        {
            Id = 2, Position = new(37, 7), CommanderId = null,
            MemberPositions = San9.Tests.FormationTestData.Members(ImmutableDictionary<int, BattleRank>.Empty),
            Order = attacksSubject ? new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 } : null
        };
        var state = world.BeginMutation();
        state.AddUnit(second);
        BattleRouting.CompleteExchange(state, new(0, false), new(1, false), new() { Military = military }, []);
        Assert.Equal(routed ? UnitCondition.Routed : UnitCondition.Normal, state.Units[1].Condition);
    }

    [Fact]
    public void SurvivingParticipantStillChecksOtherAttackersWhenItsOpponentIsGone()
    {
        var (world, military) = Fixture(39, false, 40000);
        var state = world.BeginMutation();
        state.AddUnit(state.Units[0] with { Id = 2, CommanderId = null,
            MemberPositions = San9.Tests.FormationTestData.Members(ImmutableDictionary<int, BattleRank>.Empty) });
        state.RemoveUnit(0);
        List<DomainEvent> events = [];
        BattleRouting.CompleteExchange(state, new(0, false), new(1, false), new() { Military = military }, events);
        Assert.Equal(UnitCondition.Routed, state.Units[1].Condition);
        Assert.Single(events.OfType<BattleConditionChangedEvent>());
    }

    [Fact]
    public void OrdinaryCombatChecksSurvivorBeforeStartingTheNextExchange()
    {
        // The survivor also receives ten morale for defeating the first attacker.
        var (world, military) = Fixture(29, false, 1);
        var setup = world.BeginMutation();
        setup.Replace(setup.Officers[11] with { UnitId = null, SettlementId = 1 });
        setup.Replace(setup.Units[0] with { CommanderId = null, MemberPositions = San9.Tests.FormationTestData.Members(ImmutableDictionary<int, BattleRank>.Empty) });
        setup.AddUnit(setup.Units[0] with { Id = 2, Soldiers = 80000, Position = new(37, 7) });
        world.Commit(setup);
        var engine = new SimulationEngine(world, EconomyRulebook.Empty, PersonnelRulebook.Empty, TreasureRulebook.Empty, military: military);
        var events = engine.AdvanceDay().Events.ToList();
        int defeat = events.FindIndex(e => e is UnitTerminatedEvent { UnitId: 0 });
        int rout = events.FindIndex(e => e is BattleConditionChangedEvent { ObjectId: 1, Condition: UnitCondition.Routed });
        int nextAttack = events.FindIndex(e => e is BattleDamageEvent { AttackerId: 2 });
        Assert.True(defeat >= 0 && rout > defeat && nextAttack > rout,
            $"defeat={defeat}, rout={rout}, nextAttack={nextAttack}");
    }

    [Fact]
    public void RoutRetainsOrderAndWaypointAndDoesNotRefreshExistingRout()
    {
        var (world, military) = Fixture(39, false, 40000);
        var state = world.BeginMutation();
        var order = new MilitaryOrder(MilitaryOrderKind.Move, new(25, 7), null);
        state.AssignMilitaryOrder(1, order, new(), new(30, 7));
        List<DomainEvent> events = [];
        var rules = new SimulationRules { Military = military };
        BattleRouting.CompleteExchange(state, new(0, false), new(1, false), rules, events);
        var routed = state.Units[1];
        Assert.Equal(order, routed.Order);
        Assert.Equal(new WorldPosition(30, 7), routed.Waypoint);
        Assert.Null(routed.ActiveWaypoint);
        Assert.Null(routed.ContactTarget);
        BattleRouting.CompleteExchange(state, new(0, false), new(1, false), rules, events);
        Assert.Equal(routed, state.Units[1]);
        Assert.Single(events.OfType<BattleConditionChangedEvent>());
    }

    [Fact]
    public void LearnedButUnequippedProtectionDoesNotPreventRout()
    {
        var (world, military) = Fixture(39, true, 40000);
        var state = world.BeginMutation();
        state.Replace(state.Officers[22] with { EquippedTacticId = null });
        BattleRouting.CompleteExchange(state, new(0, false), new(1, false), new() { Military = military }, []);
        Assert.Equal(UnitCondition.Routed, state.Units[1].Condition);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FacilityCombatCanRoutAFieldUnitWhetherInitiatingOrReplying(bool unitAttacks)
    {
        var (world, military) = Fixture(39, false, 80000, facility: true);
        var setup = world.BeginMutation();
        if (unitAttacks) setup.AssignMilitaryOrder(1, new(MilitaryOrderKind.AttackSettlement, null, 1), new(), null);
        world.Commit(setup);
        var engine = new SimulationEngine(world, EconomyRulebook.Empty, PersonnelRulebook.Empty, TreasureRulebook.Empty, military: military);
        var result = engine.AdvanceDay();
        Assert.Contains(result.Events.OfType<BattleDamageEvent>(), e => e.AttackerIsSettlement && e.TargetUnitId == 1);
        Assert.Equal(UnitCondition.Routed, result.Snapshot.Units.Single(u => u.Id == 1).Condition);
        Assert.Single(result.Events.OfType<BattleConditionChangedEvent>(), e => e.Condition == UnitCondition.Routed);
    }

    [Fact]
    public void InitiatingUnitIsAlsoCheckedAfterCombat()
    {
        var (world, military) = Fixture(90, false, 5000);
        var state = world.BeginMutation();
        state.Replace(state.Units[0] with { Morale = 39 });
        state.Replace(state.Units[1] with { Soldiers = 40000 });
        state.AssignMilitaryOrder(1, new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 0 }, new(), null);
        BattleRouting.CompleteExchange(state, new(0, false), new(1, false), new() { Military = military }, []);
        Assert.Equal(UnitCondition.Routed, state.Units[0].Condition);
        Assert.Equal(UnitCondition.Normal, state.Units[1].Condition);
    }

    [Fact]
    public void RoutedMovementForecastAndSavedContinuationShareTheTemporaryReturn()
    {
        var rules = UnitStrategyTests.Rules();
        var world = UnitStrategyTests.World();
        var setup = world.BeginMutation(rules.Abilities);
        setup.Replace(setup.Units[5] with { ServicePlaceId = 2 });
        setup.AssignMilitaryOrder(5, new(MilitaryOrderKind.Move, new(20, 2), null), new(), new(25, 2));
        setup.Replace(MilitaryConditions.Apply(setup.Units[5], UnitCondition.Routed, 2));
        world.Commit(setup);
        var first = new SimulationEngine(world, rules);
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<WorldSnapshot>(
            CanonicalJson.Text(first.Snapshot), CanonicalJson.Options)!;
        var second = new SimulationEngine(WorldState.Restore(snapshot), rules);
        Assert.Equal(ArrivalEstimateStatus.Known, new MilitaryArrivalForecast(snapshot, rules.Military!).Estimate(5).Status);
        first.EndControl(new("player"));
        second.EndControl(new("player"));
        EventAssert.Equal(first.AdvanceXun().Events, second.AdvanceXun().Events);
        StateAssert.Equal(first.Snapshot, second.Snapshot);
        Assert.Equal(MilitaryOrderKind.Move, first.Snapshot.Units.Single().EffectiveOrder!.Kind);
    }

    private static (WorldState World, MilitaryRulebook Military) Fixture(int morale, bool protection, uint attackingSoldiers, bool facility = false)
    {
        var profile = new CombatProfile(10, 10, 10, 10, 50);
        var military = new MilitaryRulebook(new(8, 3, Enumerable.Repeat(0, 24).ToImmutableArray(), [new(0, 1, null)],
            [new(7, 10, 0, 100000, 5, new(1, 1), TravelMode.Land)
                { Combat = profile, Attack = new(1, true), Positions = San9.Tests.FormationTestData.Layout([BattleRank.Front, BattleRank.Middle, BattleRank.Middle, BattleRank.Rear, BattleRank.Rear]) }],
            [new(1, facility ? new(35, 7) : new(2, 7), new(1, 1)) { Combat = profile, Attack = facility ? new(1, true) : null },
             new(2, new(37, 2), new(1, 1)) { Combat = profile }])
        { Tactics = [new(9000, 7, TacticEffect.CounterConfusion, 0, 0, 0, true, false, [])] });
        var world = WorldState.Create(new(190, 1, 1), 42,
            [new(1, 100) { LeaderId = 11 }, new(2, 100) { LeaderId = 22 }],
            [new(1, 1, facility ? attackingSoldiers : 100, 100, 100, 70, 500, 0, 100, 0, 100),
             new(2, 2, 100, 100, 100, 70, 500, 0, 100, 0, 100)],
            [new(11, 1, null, new(70, 70, 70, 70), false) { UnitId = 0, Status = OfficerStatus.Ruler },
             new(22, 2, null, new(70, 70, 70, 70), false) { UnitId = 1, Status = OfficerStatus.Ruler,
                 LearnedTactics = protection ? [9000] : [], EquippedTacticId = protection ? 9000 : null }],
            [new(0, 1, facility ? new(2, 7) : new(35, 7), attackingSoldiers) { FormationId = 7, Morale = 90, ServicePlaceId = 1,
                CommanderId = 11, MemberPositions = San9.Tests.FormationTestData.Members(ImmutableDictionary<int, BattleRank>.Empty.Add(11, BattleRank.Front)),
                Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 } },
             new(1, 2, new(36, 7), 5000) { FormationId = 7, Morale = morale, ServicePlaceId = 2,
                CommanderId = 22, MemberPositions = San9.Tests.FormationTestData.Members(ImmutableDictionary<int, BattleRank>.Empty.Add(22, BattleRank.Front)) }],
            corps: [new(1, 1, 11, new(100000, 100000)) { IsRoot = true }, new(2, 2, 22, new(100000, 100000)) { IsRoot = true }]);
        return (world, military);
    }
}
