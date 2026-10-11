using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class MilitaryDiplomacyTests
{
    private static readonly CombatProfile Combat = new(10, 10, 10, 10, 50);
    [Fact]
    public void EnemyFacilityUsesOneScaledExtentForAvoidanceAndCollision()
    {
        var rules = Military(geographicScale: 5);
        var world = World(0).BeginMutation();
        var place = world.Settlements[2];
        var occupied = new MovementOccupancy(world, rules).Query(world, 1, new());
        var enemy = Assert.Single(occupied, o => o.Position == rules.Place(place).Position);
        Assert.True(enemy.Hard);
        Assert.Equal(NavigationAvoidance.Facility, enemy.Avoidance);
        Assert.Equal(new Footprint(15, 15), enemy.Footprint);
        Assert.Equal(rules.SettlementArea(place).Footprint, enemy.Footprint);
    }

    private static MilitaryRulebook Military(bool work = false, bool armed = false, int geographicScale = 1) => new(new MilitaryDefinition(12, 4, new int[48].ToImmutableArray(),
        [new(0, 10, null)], [new(0, 10, 0, 100000, 5, new(1, 1), TravelMode.Land)
            { Combat = Combat, Attack = new(3, true), CaptureRanks = [BattleRank.Front, BattleRank.Middle, BattleRank.Middle, BattleRank.Rear, BattleRank.Rear] }],
        [new(1, new(2, 10), new(1, 1)) { Combat = Combat, FacilityTypeId = 8 },
         new(2, new(50, 10), new(3, 3)) { Combat = Combat, Attack = armed ? new(3, false) : null, FacilityTypeId = 9 }])
        { Geography = new(geographicScale, []), Facilities = [new(8, 0, 100, new(1, 1), 0, false, true, false, 100000, null, null, null, []) { Combat = Combat }, new(9, 0, 100, new(3, 3), 0, false, false, false, 100000, null, null, null, []) { Combat = Combat, Attack = armed ? new(3, false) : null, IsWork = work }] });

    private static WorldState World(int relation = 70, bool alliance = false, bool field = false) => WorldState.Create(new(190, 1, 1), 42,
        [new(1, 100) { BaseTrust = 100, LeaderId = 11 }, new(2, 100), new(3, 100), new(4, 100)],
        [new(1, 1, 10000, 100, 100, 50, 500, 0, 100, 0, 100), new(2, 2, 10000, 100, 100, 50, 500, 0, 100, 0, 100)],
        [new OfficerState(11, 1, 1, new(70, 70, 70, 70), false) { Status = OfficerStatus.Ruler, Righteousness = 10 }.WithLoyalty(1, 100),
         new OfficerState(12, 1, null, new(70, 70, 70, 70), false) { UnitId = 0, Righteousness = 10 }.WithLoyalty(1, 100),
         new OfficerState(22, 2, null, new(70, 70, 70, 70), false) { UnitId = 1 }],
        units: [new(0, 1, new(10, 10), 10000) { FormationId = 0, Morale = 90, ServicePlaceId = 1,
            MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(12, BattleRank.Front),
            Order = field ? new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 } : new(MilitaryOrderKind.AttackSettlement, null, 2) },
            new(1, 2, new(12, 10), 10000) { MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(22, BattleRank.Front), FormationId = 0, Morale = 90, ServicePlaceId = 2 }],
        relations: [new(1, 2, relation), new(1, 3, relation), new(1, 4, 0), new(2, 3, 70), new(2, 4, 0), new(3, 4, 0)],
        controls: [new("player", 1, ControllerKind.Player)], alliance: alliance ? new(4, 2, [1, 2, 3], 36) : null, corps: [new San9.Core.Domain.CorpsState(1, 1, 11, new(1000, 1000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, null, new(1000, 1000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(3, 3, null, new(0, 0)) { IsRoot = true }, new San9.Core.Domain.CorpsState(4, 4, null, new(0, 0)) { IsRoot = true }]);

    [Theory]
    [InlineData(39, 100, 100, 39)]
    [InlineData(40, 100, 100, 35)]
    [InlineData(59, 100, 100, 35)]
    [InlineData(60, 90, 95, 35)]
    [InlineData(79, 90, 95, 35)]
    [InlineData(80, 80, 90, 35)]
    public void FacilityDeclarationOccursOnceBeforeContact(int relation, int trust, int loyalty, int after)
    {
        var world = World(relation); var m = world.BeginMutation(); List<DomainEvent> events = [];
        var rules = new SimulationRules { Military = Military() };
        MilitaryDiplomacy.BeginAdvance(m, rules, events);
        Assert.Equal(trust, m.Forces[1].BaseTrust);
        Assert.Equal(loyalty, m.Officers[11].Loyalty);
        Assert.Equal(loyalty, m.Officers[12].Loyalty);
        Assert.Equal(after, m.Relations[(1, 2)].Value);
        int count = events.Count;
        MilitaryDiplomacy.BeginAdvance(m, rules, events);
        Assert.Equal(count, events.Count);
    }

    [Fact]
    public void RequestBreachAtDeclarationExcludesRulerAndChargesOnce()
    {
        var world = World(0).BeginMutation();
        world.AddSettlement(new(3, 3, 1000, 100, 100, 50, 0, 0, 0, 0, 0));
        List<DomainEvent> events = [];
        AttackRequests.Establish(world, 1, 2, 3, events);
        var rules = new SimulationRules { Military = Military() };
        Assert.Equal(AttackWarning.AttackRequestBreach, MilitaryDiplomacy.Warning(world, 1, world.Units[0].Order, rules.Military));
        MilitaryDiplomacy.BeginAdvance(world, rules, events);
        MilitaryDiplomacy.BeginAdvance(world, rules, events);
        Assert.Equal(80, world.Forces[1].BaseTrust);
        Assert.Equal(100, world.Officers[11].Loyalty);
        Assert.Equal(90, world.Officers[12].Loyalty);
        Assert.Single(events.OfType<AttackRequestEndedEvent>());
    }

    [Fact]
    public void RequestFulfillmentRequiresActualInRangeAttackAndIsNotRepeatedNextDay()
    {
        var world = World(0).BeginMutation();
        List<DomainEvent> events = [];
        AttackRequests.Establish(world, 1, 3, 2, events);
        var rules = new SimulationRules { Military = Military() };
        MilitaryCombat.AdvanceDay(world, rules, events);
        Assert.Single(world.Simulation.AttackRequests);
        world.Replace(world.Units[0] with { Position = new(46, 10) });
        MilitaryCombat.AdvanceDay(world, rules, events);
        Assert.Empty(world.Simulation.AttackRequests);
        Assert.Equal(110, world.Forces[1].BaseTrust);
        MilitaryCombat.AdvanceDay(world, rules, events);
        Assert.Single(events.OfType<AttackRequestEndedEvent>());
    }

    [Fact]
    public void BetrayalStacksAndRemovesOnlyOrdinaryMember()
    {
        var m = World(70, true).BeginMutation(); List<DomainEvent> events = [];
        MilitaryDiplomacy.BeginAdvance(m, new() { Military = Military() }, events);
        Assert.Equal(40, m.Forces[1].BaseTrust);
        Assert.Equal(85, m.Officers[12].Loyalty);
        Assert.Equal(95, m.Officers[11].Loyalty);
        Assert.Equal(5, m.Relations[(1, 2)].Value);
        Assert.Equal(35, m.Relations[(1, 3)].Value);
        Assert.Equal(new[] { 2, 3 }, m.Simulation.Alliance!.MemberForceIds);
        Assert.True(Assert.Single(events.OfType<WarDeclaredEvent>()).BetrayedAlliance);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LeaderOrTwoMemberBetrayalDissolvesAlliance(bool leader)
    {
        var m = World(70, true).BeginMutation();
        m.SetSimulation(m.Simulation with { Alliance = leader ? new(4, 1, [1, 2, 3], 36) : new(4, 2, [1, 2], 36) });
        List<DomainEvent> events = [];
        MilitaryDiplomacy.BeginAdvance(m, new() { Military = Military() }, events);
        Assert.Null(m.Simulation.Alliance);
        Assert.Equal(AllianceEndReason.Betrayal, Assert.Single(events.OfType<AllianceEndedEvent>()).Reason);
    }

    [Fact]
    public void FieldCombatReducesRelationsAndRetaliatesWithoutBreakingAlliance()
    {
        var m = World(70, true, true).BeginMutation(); var military = Military(); List<DomainEvent> events = [];
        MilitaryDiplomacy.BeginAdvance(m, new() { Military = military }, events);
        Assert.Empty(events);
        Assert.False(MilitaryDiplomacy.AutomaticEnemy(m, 1, 2));
        MilitaryCombat.AdvanceDay(m, new() { Military = military }, events);
        Assert.Equal(39, m.Relations[(1, 2)].Value);
        Assert.Equal(100, m.Forces[1].BaseTrust);
        Assert.Equal(100, m.Officers[12].Loyalty);
        Assert.NotNull(m.Simulation.Alliance);
        Assert.Contains(events.OfType<BattleDamageEvent>(), e => e.AttackerId == 1 && e.TargetUnitId == 0 && e.SoldierLoss > 0);
        Assert.False(MilitaryDiplomacy.AutomaticEnemy(m, 1, 2));
        Assert.Empty(events.OfType<WarDeclaredEvent>());
    }

    [Theory]
    [InlineData(19, 19)]
    [InlineData(20, 19)]
    [InlineData(39, 38)]
    [InlineData(40, 39)]
    [InlineData(100, 39)]
    public void CombatOpeningRelationBoundaries(int before, int after)
    {
        var m = World(before).BeginMutation();
        MilitaryDiplomacy.CombatOpened(m, 1, 2, false, []);
        Assert.Equal(after, m.Relations[(1, 2)].Value);
    }

    [Fact]
    public void WorkBypassesConfirmationDeclarationAndCombatRelationsButAlwaysBlocks()
    {
        var m = World(80, true).BeginMutation(); var rules = Military(true); List<DomainEvent> events = [];
        Assert.Equal(AttackWarning.None, MilitaryDiplomacy.Warning(m, 1, m.Units[0].Order, rules));
        MilitaryDiplomacy.BeginAdvance(m, new() { Military = rules }, events);
        MilitaryDiplomacy.CombatOpened(m, 1, 2, true, events);
        Assert.Empty(events);
        Assert.Contains(new MovementOccupancy(m, rules).Query(m, m.Units[0].OwnerId, m.Units[0].Policies, 0), o => o.Position == new WorldPosition(50, 10));
    }

    [Theory]
    [InlineData(39, false, true)]
    [InlineData(40, false, false)]
    [InlineData(59, false, false)]
    [InlineData(39, true, false)]
    public void MovingFacilityOccupancyTracksDiplomacy(int relation, bool alliance, bool blocks)
    {
        var m = World(relation, alliance).BeginMutation(); var rules = Military();
        Assert.Equal(blocks, new MovementOccupancy(m, rules).Query(m, m.Units[0].OwnerId, m.Units[0].Policies, 0).Any(o => o.Position == new WorldPosition(50, 10)));
        var move = rules.Map.Advance(new(46, 10), new(54, 10), new(1, 1), TravelMode.Land,
            new MovementOccupancy(m, rules).Query(m, m.Units[0].OwnerId, m.Units[0].Policies, 0), 100);
        Assert.Equal(!blocks, move.Arrived);
        Assert.Contains(MilitaryMovement.Occupancy(m, rules, 0, null), o => o.Position == new WorldPosition(50, 10));
        if (!alliance && relation >= 40)
        {
            MilitaryDiplomacy.CombatOpened(m, 1, 2, false, []);
            Assert.Contains(new MovementOccupancy(m, rules).Query(m, m.Units[0].OwnerId, m.Units[0].Policies, 0), o => o.Position == new WorldPosition(50, 10));
        }
    }

    [Theory]
    [InlineData(39, false, true)]
    [InlineData(40, false, false)]
    [InlineData(80, false, false)]
    [InlineData(39, true, false)]
    public void OrdinaryAutomaticHostilityUsesRelationThresholdAndAlliance(int relation, bool alliance, bool hostile)
    {
        var m = World(relation, alliance).BeginMutation();
        Assert.Equal(hostile, MilitaryDiplomacy.AutomaticEnemy(m, 1, 2));
        Assert.True(MilitaryDiplomacy.CanFight(m, 1, 2));
        Assert.False(MilitaryDiplomacy.CanFight(m, 1, 1));
        m.Replace(m.Units[0] with { Position = new(47, 10), Order = null });
        Assert.Equal(hostile, MilitaryEngagements.FacilityTarget(m, m.Settlements[2], Military(armed: true)) is not null);
    }

    [Fact]
    public void CancellingAttackBeforeAdvanceHasNoDiplomaticCost()
    {
        var engine = new SimulationEngine(World(80, true), new SimulationRules { Military = Military() });
        Assert.True(engine.Execute(new("player"), new OrderUnitCommand(Guid.NewGuid(), 1, 0, 0,
            new(MilitaryOrderKind.Move, new(15, 15), null))).Submission.Accepted);
        engine.EndControl(new("player"));
        var result = engine.AdvanceXun();
        Assert.Empty(result.Events.OfType<WarDeclaredEvent>());
        Assert.Equal(80, result.Snapshot.Relations.Single(r => r.FirstId == 1 && r.SecondId == 2).Value);
        Assert.Equal(100, result.Snapshot.Forces.Single(f => f.Id == 1).BaseTrust);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeployAndReformCannotBypassAttackConfirmation(bool reform)
    {
        var engine = new SimulationEngine(World(80, true), new SimulationRules { Military = Military() });
        var order = new MilitaryOrder(MilitaryOrderKind.AttackSettlement, null, 2);
        GameCommand command = reform ? new ReformUnitCommand(Guid.NewGuid(), 1, 0, 0, 0, [12], order) :
            new DeployUnitCommand(Guid.NewGuid(), 1, 0, 1, [11], 1000, 0, order);
        string before = engine.Snapshot.StateHash;
        Assert.Equal(reform ? CommandRejection.UnsupportedCommand : CommandRejection.AttackConfirmationRequired, engine.Execute(new("player"), command).Submission.Rejection);
        Assert.Equal(before, engine.Snapshot.StateHash);
        GameCommand confirmed = command switch
        {
            ReformUnitCommand c => c with { ConfirmAttack = true },
            DeployUnitCommand c => c with { ConfirmAttack = true },
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(!reform, engine.Execute(new("player"), confirmed).Submission.Accepted);
        if (reform) Assert.Equal(before, engine.Snapshot.StateHash);
        Assert.Equal(80, engine.Snapshot.Relations.Single(r => r.FirstId == 1 && r.SecondId == 2).Value);
    }

    [Fact]
    public void ConfirmationIsAtomicAndPersistedCommandReplaysIdentically()
    {
        var rules = new SimulationRules { Military = Military() };
        var engine = new SimulationEngine(World(70, true), rules);
        var command = new OrderUnitCommand(Guid.NewGuid(), 1, 0, 0, new(MilitaryOrderKind.AttackSettlement, null, 2));
        Assert.Equal(AttackWarning.FriendlyRelations | AttackWarning.AllianceBetrayal, engine.GetAttackWarning(1, command.Order));
        string before = engine.Snapshot.StateHash;
        Assert.Equal(CommandRejection.AttackConfirmationRequired, engine.Execute(new("player"), command).Submission.Rejection);
        Assert.Equal(before, engine.Snapshot.StateHash);
        GameCommand confirmed = command with { ConfirmAttack = true };
        var replay = System.Text.Json.JsonSerializer.Deserialize<GameCommand>(System.Text.Json.JsonSerializer.Serialize<GameCommand>(confirmed))!;
        Assert.True(engine.Execute(new("player"), replay).Submission.Accepted);
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), rules);
        engine.EndControl(new("player")); restored.EndControl(new("player"));
        var expected = engine.AdvanceXun(); var actual = restored.AdvanceXun();
        Assert.Equal(expected.Snapshot.StateHash, actual.Snapshot.StateHash);
        Assert.Equal(CanonicalJson.Text(expected.Events), CanonicalJson.Text(actual.Events));
        Assert.Single(expected.Events.OfType<WarDeclaredEvent>());
    }
}
