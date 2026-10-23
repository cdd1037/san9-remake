using San9.Tests;
using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class MilitaryCombatTests
{
    [Fact]
    public void OpeningCombatPersistsMovementEngagementThroughDamage()
    {
        var state = World(defenders: 100000);
        var rules = Rules();
        _ = Engine(state, rules);
        var world = state.BeginMutation();
        world.Replace(world.Units[0] with { Position = new(36, 7) });
        Assert.True(MilitaryCombat.CanInitiate(world.Units[0]));
        Assert.True(MilitaryCombat.ValidTarget(world, 1, world.Units[0].EffectiveOrder!, rules.Formation(world.Units[0]), rules));
        Assert.True(MilitaryCombat.Area(world.Units[0], rules).IntersectsExpanded(MilitaryCombat.TargetArea(world, world.Units[0].EffectiveOrder!, rules),
            MilitaryCombat.UnitRange(world, world.Units[0], world.Units[0].EffectiveOrder!, rules)));
        int before = MilitaryNavigation.Budget(world.Units[0], rules);
        List<DomainEvent> events = [];
        MilitaryCombat.AdvanceDay(world, new() { Military = rules }, events);
        Assert.Contains(events, e => e is BattleDamageEvent);
        Assert.True(world.Units[0].ActiveEngagement);
        Assert.Equal(before / 2, MilitaryNavigation.Budget(world.Units[0], rules));
    }

    [Theory]
    [InlineData(99)]
    [InlineData(100)]
    public void WorkDestructionUsesIdentityOrderAndDoesNotReselectCappedMember(int leadership)
    {
        var world = World(defenders: 0, durability: 1);
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Officers[11] with { Status = OfficerStatus.Serving, BaseAbilities = new(70, 70, 70, leadership) });
        mutation.Replace(mutation.Officers[12] with { UnitId = 0, SettlementId = null,
            Personality = 3, BaseAbilities = new(70, 70, 70, 120), TacticProficiencies = [0, 0, 0, 0, 0, 0, 3, 0, 0] });
        mutation.Replace(mutation.Officers[11] with { TacticProficiencies = [0, 0, 0, 0, 0, 0, 3, 0, 0] });
        mutation.Replace(mutation.Units[0] with { CommanderId = 12,
            MemberRanks = mutation.Units[0].MemberRanks.Add(12, BattleRank.Middle) });
        mutation.AddSettlement(mutation.Settlements[2] with { Id = 3, Durability = 100 });
        mutation.Replace(mutation.Officers[22] with { SettlementId = 3 });
        mutation.Replace(mutation.Settlements[2] with { Kind = SettlementKind.Facility, CanHostPersons = false });
        mutation.SetSimulation(mutation.Simulation with { RandomStreams =
            [new("military-destruction-growth", 52), new("military-destruction-growth-member", 52), new("military-destruction-learning", 52)] });
        world.Commit(mutation);
        var baseline = Rules();
        var military = new MilitaryRulebook(new(8, 3, Enumerable.Repeat(0, 24).ToImmutableArray(), [new(0, 1, null)],
            [baseline.Formations[7]], baseline.Places.Values.OrderBy(p => p.Id)
                .Select(p => p with { FacilityTypeId = p.Id == 2 ? 9000 : 0 }).ToImmutableArray())
        {
            Facilities = [new(0, 0, 100, new(1, 1), 0, false, true, true, 100000, null, null, null, []) { Combat = Profile }, new(9000, 0, 100, new(1, 1), 0, false, false, false, 100000, null, null, null, [])
                { Combat = Profile, IsWork = true, DestroyedAtZeroDurability = true, DestructionGrowthTacticId = 19000 }],
            Tactics = [new(19000, 6, TacticEffect.Construction, 0, 0, 3, false, false, [])]
        });
        var engine = Engine(world, military);
        var restored = Engine(WorldState.Restore(engine.CaptureStableState()), military);
        var result = engine.AdvanceXun();
        EventAssert.Equal(result.Events, restored.AdvanceXun().Events);
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
        Assert.Single(result.Events.OfType<FacilityDestroyedEvent>());
        Assert.Equal(11, Assert.Single(result.Events.OfType<TacticLearnedEvent>()).OfficerId);
        Assert.Equal(new[] { 11, 12 }, result.Events.OfType<TacticProficiencyGrownEvent>()
            .Where(e => e.Category == 5 && e.Current - e.Previous == 3).Select(e => e.OfficerId));
        var growth = result.Events.OfType<OfficerAbilityGrownEvent>().ToArray();
        if (leadership == 99) Assert.Equal(11, Assert.Single(growth).OfficerId);
        else
        {
            Assert.Empty(growth);
            Assert.Equal(52UL, engine.Snapshot.Simulation.RandomStreams.Single(s => s.Name == "military-destruction-growth").State);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RealCaptureGrowsRandomMemberAndTeachesFormationTechniqueOnlyForOwnedTarget(bool owned)
    {
        var world = World(defenders: 0, controlled: true);
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Officers[12] with { UnitId = 0, SettlementId = null });
        mutation.Replace(mutation.Units[0] with { CommanderId = 11,
            MemberRanks = mutation.Units[0].MemberRanks.Add(12, BattleRank.Middle) });
        if (!owned)
        {
            mutation.Replace(mutation.Settlements[2] with { OwnerId = null });
            mutation.AddSettlement(mutation.Settlements[2] with { Id = 3, OwnerId = 2 });
            mutation.Replace(mutation.Officers[22] with { SettlementId = 3 });
        }
        mutation.SetSimulation(mutation.Simulation with { RandomStreams =
            [new("military-capture-growth", 52), new("military-capture-growth-member", 0), new("military-capture-learning", 52)] });
        world.Commit(mutation);
        var baseline = Rules();
        var military = new MilitaryRulebook(new(8, 3, Enumerable.Repeat(0, 24).ToImmutableArray(), [new(0, 1, null)],
            [baseline.Formations[7] with { CaptureTacticLearningId = 15000 }], baseline.Places.Values.OrderBy(p => p.Id).ToImmutableArray())
        { Tactics = [new(15000, 5, TacticEffect.FormationTechnology, 0, 0, 3, false, false, [])] });
        var engine = Engine(world, military);
        var restored = Engine(WorldState.Restore(engine.CaptureStableState()), military);
        var context = new CommandContext("player");
        engine.EndControl(context);
        restored.EndControl(context);
        var result = engine.AdvanceXun();
        EventAssert.Equal(result.Events, restored.AdvanceXun().Events);
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
        Assert.Single(result.Events.OfType<SettlementCapturedEvent>());
        if (owned)
        {
            // State zero selects index one, so a non-commander receives the capability reward.
            Assert.Equal(1UL, new StableRandom(0).NextUInt64() % 2);
            var growth = Assert.Single(result.Events.OfType<OfficerAbilityGrownEvent>());
            Assert.Equal(12, growth.OfficerId);
            Assert.Equal("leadership", growth.Ability);
            Assert.Equal(71, growth.Current);
            Assert.All(engine.Snapshot.Officers.Where(p => p.Id is 11 or 12), p => Assert.Equal(4, p.TacticProficiencies[5]));
            Assert.Equal(2, result.Events.OfType<TacticProficiencyGrownEvent>().Count(e => e.Category == 5 && e.Current - e.Previous == 3));
            Assert.Contains(result.Events.OfType<TacticLearnedEvent>(), e => e.OfficerId == 11 && e.TacticId == 15000);
        }
        else
        {
            Assert.Empty(result.Events.OfType<OfficerAbilityGrownEvent>());
            Assert.Empty(result.Events.OfType<TacticLearnedEvent>());
            Assert.All(engine.Snapshot.Officers.Where(p => p.Id is 11 or 12), p => Assert.Equal(1, p.TacticProficiencies[5]));
            Assert.Equal(52UL, engine.Snapshot.Simulation.RandomStreams.Single(s => s.Name == "military-capture-growth").State);
        }
    }

    [Fact]
    public void PublicAttackRequiresConfirmationForAllianceMember()
    {
        var initial = Engine(World(controlled: true)).CaptureStableState();
        initial = initial with { Forces = initial.Forces.Append(new ForceState(3, 0)).ToArray(), Corps = [..initial.Corps, new(3, 3, null, new(0, 0)) { IsRoot = true }] };
        var allied = Engine(WorldState.Restore(initial with
        {
            Simulation = initial.Simulation with { Alliance = new(3, 1, [1, 2], 36) }
        }));
        var command = new OrderUnitCommand(Guid.NewGuid(), 1, 0, 0, new(MilitaryOrderKind.AttackSettlement, null, 2));
        var before = allied.Snapshot;
        Assert.Equal(CommandRejection.AttackConfirmationRequired, allied.Execute(new("player"), command).Submission.Rejection);
        StateAssert.Equal(before, allied.Snapshot);
        Assert.True(allied.Execute(new("player"), command with { ConfirmAttack = true }).Submission.Accepted);
        var expired = Engine(WorldState.Restore(initial));
        Assert.True(expired.Execute(new("player"), command).Submission.Accepted);
    }

    [Fact]
    public void PartialMemberRanksAreRejectedEvenForMovementOnlyFormations()
    {
        var definition = new MilitaryDefinition(1, 1, [0], [new(0, 1, null)],
            [new(1, 1, 0, 10000, 5, new(1, 1), TravelMode.Land) { CaptureRanks = [BattleRank.Front] }], []);
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition));
    }

    private static readonly CombatProfile Profile = new(10, 10, 10, 10, 50);
    private static MilitaryRulebook Rules(int range = 1, bool destroyed = false) => new(new(8, 3,
        Enumerable.Repeat(0, 24).ToImmutableArray(), [new(0, 1, null)],
        [new(7, 10, 0, 100000, 5, new(1, 1), TravelMode.Land) { Combat = Profile, Attack = new(range, true), CaptureRanks = [BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle] }],
        [new(1, new(2, 7), new(1, 1)) { Combat = Profile, Attack = new(1, false) },
         new(2, new(37, 7), new(1, 1)) { Combat = Profile, Attack = new(1, false), DestroyedAtZeroDurability = destroyed },
         new(3, new(37, 2), new(1, 1)) { Combat = Profile, Attack = new(1, false) }]));

    private static WorldState World(uint defenders = 100, int durability = 100, bool controlled = false) => WorldState.Create(new(190, 1, 1), 42,
        [new(1, 100) { LeaderId = 11 }, new(2, 100) { LeaderId = 22 }],
        [new(1, 1, 100, 100, 100, 70, 500, 0, 100, 0, 100), new(2, 2, defenders, durability, 100, 70, 500, 0, 100, 0, 100)],
        [new(11, 1, null, new(90, 90, 90, 90), false) { UnitId = 0, Status = OfficerStatus.Ruler },
         new(12, 1, 1, new(70, 70, 70, 70), false), new(22, 2, 2, new(70, 70, 70, 70), false) { Status = OfficerStatus.Ruler }],
        units: [new(0, 1, new(35, 7), 10000) { FormationId = 7, Morale = 90, ServicePlaceId = 1,
            MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(11, BattleRank.Front),
            Order = new(MilitaryOrderKind.AttackSettlement, null, 2) }],
        controls: controlled ? [new("player", 1, ControllerKind.Player)] : [], corps: [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 22, new(10000, 10000)) { IsRoot = true }]);

    private static SimulationEngine Engine(WorldState world, MilitaryRulebook? rules = null) =>
        new(world, EconomyRulebook.Empty, PersonnelRulebook.Empty, TreasureRulebook.Empty, military: rules ?? Rules());

    [Fact]
    public void PlannedAttackRechecksOwnershipAndDuplicateSubmissionCannotRepeatCapture()
    {
        var world = World(controlled: true);
        var engine = Engine(world);
        var player = new CommandContext("player");
        var command = new OrderUnitCommand(Guid.NewGuid(), 1, 0, 0,
            new(MilitaryOrderKind.AttackSettlement, null, 2));
        Assert.True(engine.Validate(player, command).Accepted);
        var mutation = world.BeginMutation();
        mutation.AddSettlement(mutation.Settlements[2] with { Id = 3 });
        mutation.Replace(mutation.Officers[22] with { SettlementId = 3 });
        mutation.Replace(mutation.Settlements[2] with { OwnerId = 1 });
        world.Commit(mutation);
        var before = engine.Snapshot;
        var rejected = engine.Execute(player, command);
        Assert.False(rejected.Submission.Accepted);
        Assert.Empty(rejected.Events);
        StateAssert.Equal(before, engine.Snapshot);
        mutation = world.BeginMutation();
        mutation.Replace(mutation.Settlements[2] with { OwnerId = 2 });
        world.Commit(mutation);
        Assert.True(engine.Execute(player, command).Submission.Accepted);
        before = engine.Snapshot;
        rejected = engine.Execute(player, command);
        Assert.False(rejected.Submission.Accepted);
        Assert.Empty(rejected.Events);
        StateAssert.Equal(before, engine.Snapshot);
        engine.EndControl(player);
        Assert.Single(engine.AdvanceXun().Events.OfType<SettlementCapturedEvent>());
        engine.EndControl(player);
        Assert.Empty(engine.AdvanceXun().Events.OfType<SettlementCapturedEvent>());
    }

    [Theory]
    [InlineData(UnitCondition.Normal)]
    [InlineData(UnitCondition.HighSpirits)]
    public void NormalAndHighSpiritsAttackersBothDamageWalls(UnitCondition condition)
    {
        var state = Engine(World(100000)).CaptureStableState();
        var engine = Engine(WorldState.Restore(state with
        {
            Units = state.Units.Select(u => u with { Condition = condition,
                ConditionDaysRemaining = condition == UnitCondition.Normal ? 0 : 10 }).ToArray()
        }));
        var first = engine.AdvanceXun().Events.OfType<BattleDamageEvent>().First(e => e.AttackerId == 0 && !e.AttackerIsSettlement);
        Assert.True(first.DurabilityLoss > 0);
        Assert.Equal(2, first.SettlementId);
    }

    [Fact]
    public void PublicAttackApproachesCapturesEliminatesAndTerminalRestoreIsStable()
    {
        var engine = Engine(World());
        var restored = Engine(WorldState.Restore(engine.CaptureStableState()));
        var result = engine.AdvanceXun();
        EventAssert.Equal(result.Events, restored.AdvanceXun().Events);
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
        Assert.Single(result.Events.OfType<SettlementCapturedEvent>());
        Assert.Single(result.Events.OfType<ForceEliminatedEvent>());
        Assert.Single(result.Events.OfType<SimulationEndedEvent>());
        Assert.Equal(SimulationBoundary.Terminal, engine.Snapshot.Simulation.Boundary);
        Assert.Equal(1, engine.Snapshot.Settlements.Single(s => s.Id == 2).OwnerId);
        var prisoner = engine.Snapshot.Officers.Single(p => p.Id == 22);
        Assert.Equal(OfficerStatus.Captive, prisoner.Status);
        Assert.Null(prisoner.OwnerId);
        Assert.Null(prisoner.UnitId);
        Assert.Equal(2, prisoner.SettlementId);
        Assert.Contains(result.Events.OfType<UnitAbsorbedEvent>(), e => e.UnitId == 0 && e.SettlementId == 2);
        Assert.Empty(engine.AdvanceXun().Events);
        StateAssert.Equal(engine.Snapshot, Engine(WorldState.Restore(engine.CaptureStableState())).Snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicCaptureUsesCurrentBarbarianIdentityAndDoesNotRecaptureAbandonedCity(bool barbarian)
    {
        var world = World(defenders: 0);
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Forces[1] with { IsBarbarian = barbarian });
        mutation.AddSettlement(mutation.Settlements[2] with { Id = 3, Soldiers = 1000 });
        mutation.Replace(mutation.Officers[22] with { SettlementId = 3 });
        world.Commit(mutation);
        var engine = Engine(world);
        var restored = Engine(WorldState.Restore(engine.CaptureStableState()));

        var result = engine.AdvanceXun();
        EventAssert.Equal(result.Events, restored.AdvanceXun().Events);
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
        var city = engine.Snapshot.Settlements.Single(s => s.Id == 2);
        Assert.Equal(barbarian ? (int?)null : 1, city.OwnerId);
        Assert.False(city.IsDestroyed);
        Assert.Single(result.Events.OfType<SettlementCapturedEvent>());
        Assert.Empty(result.Events.OfType<ForceEliminatedEvent>());
        if (barbarian)
        {
            Assert.Contains(result.Events.OfType<UnitOrderChangedEvent>(), e => e.UnitId == 0 &&
                e.Reason == "settlement-abandoned" && e.Order == new MilitaryOrder(MilitaryOrderKind.Return, null, 1));
            Assert.DoesNotContain(result.Events.OfType<UnitAbsorbedEvent>(), e => e.SettlementId == 2);
            Assert.Equal(0u, city.Soldiers + city.Wounded);
        }
        restored = Engine(WorldState.Restore(engine.CaptureStableState()));
        var next = engine.AdvanceXun();
        EventAssert.Equal(next.Events, restored.AdvanceXun().Events);
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
        Assert.Empty(next.Events.OfType<SettlementCapturedEvent>());
    }

    [Fact]
    public void BattleEliminationReturnsEnvoyBeforeCurrentDatesCommandsAcrossRestore()
    {
        var initial = World(defenders: 0, controlled: true).CreateSnapshot();
        var world = WorldState.Create(initial.Clock, 42,
            initial.Forces.Append(new ForceState(3, 100) { LeaderId = 31 }),
            initial.Settlements.Append(initial.Settlements[0] with { Id = 3, OwnerId = 3 }),
            initial.Officers.Append(new OfficerState(31, 3, 3, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
            units: initial.Units.Select(u => u with { Order = null }), controls: initial.Simulation.Controls,
            naturalDeathsEnabled: false, annualAppearancesEnabled: false, corps: [.. initial.Corps, new San9.Core.Domain.CorpsState(3, 3, 31, new(10000, 10000)) { IsRoot = true }]);
        var rules = new SimulationRules
        {
            Military = Rules(),
            Personnel = new([new(1, new(0, 0)), new(2, new(600, 0)), new(3, new(900, 0))])
        };
        var engine = new SimulationEngine(world, rules);
        Assert.True(engine.Execute(new("player"), new GiftCommand(Guid.NewGuid(), 1, 0, 12, 2,
            new(GiftKind.Money, 1000, null))).Submission.Accepted);
        engine.EndControl(new("player"));
        engine.AdvanceXun();
        Assert.True(engine.Execute(new("player"), new OrderUnitCommand(Guid.NewGuid(), 1, 1, 0,
            new(MilitaryOrderKind.AttackSettlement, null, 2))).Submission.Accepted);
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), rules);
        List<DomainEvent> all = [];
        for (int i = 0; i < 2; i++)
        {
            engine.EndControl(new("player"));
            restored.EndControl(new("player"));
            var events = engine.AdvanceXun().Events;
            EventAssert.Equal(events, restored.AdvanceXun().Events);
            all.AddRange(events);
            if (i == 0)
            {
                Assert.Equal(new GameClock(190, 1, 12), Assert.Single(events.OfType<ForceEliminatedEvent>()).Clock);
                var returning = Assert.Single(engine.Snapshot.Simulation.Tasks.Where(t => t.OfficerId == 12));
                Assert.False(returning.Journey!.ReturnLeg!.AfterDailyCommands);
                Assert.True(returning.Journey.ReturnLeg.DiplomaticEnvoy);
                // Date 12 already includes eleven outbound ticks: 20 - (20 - 11).
                Assert.Equal(11, returning.Journey.ReturnLeg.CountdownDays);
                Assert.Equal(new GameClock(190, 1, 22), returning.Due);
                restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), rules);
            }
        }
        Assert.Equal(new GameClock(190, 1, 22), Assert.Single(all.OfType<PersonnelReturnedEvent>(), e => e.OfficerId == 12).Clock);
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedCityRequiresAnotherCityEvenWithRulerArmyAndSubcorpsHolding(bool remainingCity)
    {
        var world = World();
        var mutation = world.BeginMutation();
        var rootTreasury20 = ResourceAccounts.Root(mutation, 2).Treasury; mutation.RemoveCorps(ResourceAccounts.Root(mutation, 2).Id); mutation.AddCorps(new(20, 2, 22, rootTreasury20) { IsRoot = true });
        mutation.AddCorps(new(21, 2, 12, new(500, 500)));
        mutation.Replace(mutation.Settlements[2] with { CorpsId = 20 });
        mutation.AddSettlement(mutation.Settlements[2] with { Id = 3, CorpsId = 21,
            Kind = remainingCity ? SettlementKind.City : SettlementKind.Facility });
        mutation.Replace(mutation.Officers[12] with { OwnerId = 2, SettlementId = 3 });
        mutation.Replace(mutation.Officers[22] with { SettlementId = null, UnitId = 1 });
        mutation.AddUnit(new(1, 2, new(17, 2), 10000) { FormationId = 7, ServicePlaceId = 3,
            CorpsId = 20, CommanderId = 22,
            MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(22, BattleRank.Front) });
        mutation.SetSimulation(mutation.Simulation with { NextUnitId = 2 });
        world.Commit(mutation);
        var baseline = Rules();
        var military = new MilitaryRulebook(new(8, 3, Enumerable.Repeat(0, 24).ToImmutableArray(), [new(0, 1, null)],
            baseline.Formations.Values.ToImmutableArray(), baseline.Places.Values.ToImmutableArray())
            { RegionIds = Enumerable.Repeat<int?>(3, 24).ToImmutableArray() });
        var engine = Engine(world, military);
        var restored = Engine(WorldState.Restore(engine.CaptureStableState()), military);
        var result = engine.AdvanceXun();
        EventAssert.Equal(result.Events, restored.AdvanceXun().Events);
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
        var capture = Assert.Single(result.Events.OfType<SettlementCapturedEvent>());
        Assert.Equal(!remainingCity, engine.Snapshot.Forces.Single(f => f.Id == 2).IsEliminated);
        if (remainingCity)
        {
            Assert.DoesNotContain(result.Events, e => e is ForceEliminatedEvent);
            Assert.Equal(2, engine.Snapshot.Settlements.Single(s => s.Id == 3).OwnerId);
            Assert.Contains(engine.Snapshot.Units, u => u.Id == 1 && u.OwnerId == 2);
        }
        else
        {
            Assert.Equal(capture.Clock, Assert.Single(result.Events.OfType<ForceEliminatedEvent>()).Clock);
            Assert.DoesNotContain(engine.Snapshot.Units, u => u.OwnerId == 2);
            Assert.DoesNotContain(engine.Snapshot.Corps, c => c.OwnerId == 2);
            Assert.DoesNotContain(engine.Snapshot.Officers, p => p.OwnerId == 2);
            Assert.Null(engine.Snapshot.Settlements.Single(s => s.Id == 3).OwnerId);
            Assert.Equal(3, engine.Snapshot.Officers.Single(p => p.Id == 22).SettlementId);
        }
        Assert.NotNull(WorldState.Restore(engine.CaptureStableState()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ZeroWallsRebuildOrDestroyAccordingToDefinition(bool destroyed)
    {
        var engine = Engine(World(100000, 1), Rules(destroyed: destroyed));
        var result = engine.AdvanceXun();
        Assert.Equal(destroyed, Assert.Single(result.Events.OfType<SettlementCapturedEvent>()).Destroyed);
        var settlement = engine.Snapshot.Settlements.Single(s => s.Id == 2);
        Assert.Equal(destroyed ? 0 : 25, settlement.Durability);
        Assert.Equal(destroyed, settlement.IsDestroyed);
        Assert.Equal(destroyed ? null : 1, settlement.OwnerId);
    }

    [Fact]
    public void CompetingAttacksCannotCaptureTheSameSettlementTwice()
    {
        var world = World();
        var mutation = world.BeginMutation();
        mutation.AddUnit(new(1, 1, new(36, 6), 10000) { FormationId = 7, Morale = 90,
            Order = new(MilitaryOrderKind.AttackSettlement, null, 2) });
        mutation.SetSimulation(mutation.Simulation with { NextUnitId = 2 });
        world.Commit(mutation);
        var engine = Engine(world);
        var result = engine.AdvanceXun();
        var capture = Assert.Single(result.Events.OfType<SettlementCapturedEvent>());
        Assert.Equal(2, result.Events.OfType<UnitAbsorbedEvent>().Count(e => e.Clock == capture.Clock && e.SettlementId == 2));
        Assert.Empty(engine.Snapshot.Units);
    }

    [Fact]
    public void MissingCombatDefinitionAndFriendlyTargetsAreRejectedWithoutEffects()
    {
        var world = World();
        var mutation = world.BeginMutation();
        var events = new List<DomainEvent>();
        Assert.Equal(CommandRejection.InvalidTask, MilitaryMovement.Order(mutation,
            new(Guid.NewGuid(), 1, 0, 0, new(MilitaryOrderKind.AttackSettlement, null, 1)), Rules(), events));
        Assert.Empty(events);
        Assert.Equal(2, mutation.Units[0].Order!.SettlementId);
    }

    [Fact]
    public void CounterattackPreservesOwnAttackAndUsesPreBattleStrength()
    {
        var world = World();
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Units[0] with { Position = new(20, 7), Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 } });
        mutation.AddUnit(new(1, 2, new(21, 7), 10000) { FormationId = 7, Morale = 90,
            Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 0 } });
        mutation.SetSimulation(mutation.Simulation with { NextUnitId = 2 });
        var military = Rules();
        uint outgoing = MilitaryCombat.NormalDamage(mutation, new(0, false), new(1, false), military);
        uint incoming = MilitaryCombat.NormalDamage(mutation, new(1, false), new(0, false), military);
        var events = new List<DomainEvent>();
        MilitaryCombat.AdvanceDay(mutation, new() { Military = military }, events);
        var damage = events.OfType<BattleDamageEvent>().ToArray();
        Assert.Equal(outgoing, damage[0].SoldierLoss);
        Assert.Equal(incoming, damage[1].SoldierLoss);
        Assert.All(damage.GroupBy(e => e.TargetUnitId), target => Assert.Equal(2, target.Count()));
        Assert.Equal(4, damage.Length);
        Assert.All(damage, e => Assert.Equal(e.SoldierLoss, e.AddedWounded + e.Dead));
    }

    [Theory]
    [InlineData(0, UnitCondition.Normal)]
    [InlineData(2, UnitCondition.Normal)]
    [InlineData(0, UnitCondition.Confused)]
    [InlineData(2, UnitCondition.Routed)]
    public void SiegeAndMultipleFieldCounterattacksUseIndependentOpportunities(int siegeId, UnitCondition condition)
    {
        var baseline = World(defenders: 10000).CreateSnapshot();
        var world = WorldState.Create(baseline.Clock, 42, baseline.Forces, baseline.Settlements,
            baseline.Officers.Select(p => p.UnitId == 0 ? p with { UnitId = siegeId } : p),
            baseline.Units.Select(u => u with { Id = siegeId, Position = new(36, 7), Condition = condition }),
            corps: baseline.Corps);
        var mutation = world.BeginMutation();
        foreach (int id in new[] { 1, 3 })
            mutation.AddUnit(new(id, 2, id == 1 ? new(35, 7) : new(36, 6), 10000) { FormationId = 7, Morale = 90,
                Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = siegeId } });
        var events = new List<DomainEvent>();
        MilitaryCombat.AdvanceDay(mutation, new() { Military = Rules() }, events);
        var outgoing = events.OfType<BattleDamageEvent>()
            .Where(e => !e.AttackerIsSettlement && e.AttackerId == siegeId && e.SoldierLoss > 0).ToArray();
        if (condition != UnitCondition.Normal)
        {
            Assert.Empty(outgoing);
            Assert.Contains(events.OfType<BattleDamageEvent>(), e => e.TargetUnitId == siegeId && e.SoldierLoss > 0);
            return;
        }
        Assert.All(new[] { 1, 3 }, id => Assert.Single(outgoing, e => e.TargetUnitId == id));
        // One assault plus the reply to the city's own daily attack.
        Assert.Equal(2, outgoing.Count(e => e.SettlementId == 2));
        Assert.Equal(4, outgoing.Length);
        Assert.Equal(2, mutation.Units[siegeId].Order!.SettlementId);
        Assert.Null(mutation.Units[siegeId].EngagementTargetId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefeatedLeaderAndLostHomeLeaveNoDanglingUnitsOrForceReferences(bool loseHome)
    {
        var world = World();
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Units[0] with { Position = new(20, 7), Soldiers = 1, Morale = 100,
            Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 } });
        mutation.AddUnit(new(1, 2, new(21, 7), 10000) { FormationId = 7, Morale = 90 });
        mutation.SetSimulation(mutation.Simulation with { NextUnitId = 2 });
        var rootTreasury10 = ResourceAccounts.Root(mutation, 1).Treasury; mutation.RemoveCorps(ResourceAccounts.Root(mutation, 1).Id); mutation.AddCorps(new(10, 1, 11, rootTreasury10) { IsRoot = true });
        mutation.Replace(mutation.Settlements[1] with { CorpsId = 10 });
        mutation.Replace(mutation.Units[0] with { CorpsId = 10 });
        if (loseHome)
        {
            mutation.Replace(mutation.Settlements[1] with { OwnerId = 2, CorpsId = null });
            mutation.Replace(mutation.Officers[12] with { OwnerId = 2 });
        }
        world.Commit(mutation);
        var engine = Engine(world);
        var result = engine.AdvanceXun();
        Assert.DoesNotContain(engine.Snapshot.Units, u => u.Id == 0);
        Assert.Single(result.Events.OfType<UnitTerminatedEvent>());
        Assert.DoesNotContain(engine.Snapshot.Units, u => u.Order?.UnitId == 0);
        Assert.DoesNotContain(engine.Snapshot.Officers, p => p.UnitId == 0);
        Assert.NotNull(WorldState.Restore(engine.CaptureStableState()));
        if (loseHome)
        {
            Assert.True(engine.Snapshot.Forces.Single(f => f.Id == 1).IsEliminated);
            Assert.Equal(OfficerStatus.Captive, engine.Snapshot.Officers.Single(p => p.Id == 11).Status);
        }
        else
        {
            Assert.False(engine.Snapshot.Forces.Single(f => f.Id == 1).IsEliminated);
            Assert.Equal(11, engine.Snapshot.Forces.Single(f => f.Id == 1).LeaderId);
            Assert.Equal(11, engine.Snapshot.Corps.Single(c => c.Id == 10).LeaderId);
            Assert.DoesNotContain(result.Events.OfType<ForceLeaderChangedEvent>(), e => e.ForceId == 1);
        }
    }

    [Fact]
    public void CapturedRulerRetainsForceAndRootCorpsUntilRecovered()
    {
        var world = World();
        var engine = Engine(world);
        var mutation = world.BeginMutation();
        var rootTreasury10 = ResourceAccounts.Root(mutation, 1).Treasury; mutation.RemoveCorps(ResourceAccounts.Root(mutation, 1).Id); mutation.AddCorps(new(10, 1, 11, rootTreasury10) { IsRoot = true });
        mutation.Replace(mutation.Settlements[1] with { CorpsId = 10 });
        mutation.Replace(mutation.Units[0] with { CorpsId = 10 });
        var events = new List<DomainEvent>();
        PersonnelTransitions.Capture(mutation, 11, 2, null, PersonnelRulebook.Empty, events);
        UnitTerminationService.Complete(mutation, 0, null, 1, new([]), events);
        GovernanceLifecycle.Reconcile(mutation, new HashSet<int> { 1 }, new() { Military = Rules() }, events, true);
        world.Commit(mutation);
        Assert.Equal(11, engine.Snapshot.Forces.Single(f => f.Id == 1).LeaderId);
        Assert.Equal(11, engine.Snapshot.Corps.Single(c => c.OwnerId == 1 && c.IsRoot).LeaderId);
        Assert.Equal(OfficerStatus.Captive, engine.Snapshot.Officers.Single(p => p.Id == 11).Status);
        Assert.DoesNotContain(events, e => e is ForceLeaderChangedEvent);
        var restored = WorldState.Restore(engine.CaptureStableState());
        mutation = restored.BeginMutation();
        BattlePersonnel.Escape(mutation, 11, 1, PersonnelRulebook.Empty, events, new(37, 7), Rules());
        restored.Commit(mutation);
        Assert.Equal(OfficerStatus.Ruler, restored.CreateSnapshot().Officers.Single(p => p.Id == 11).Status);
        Assert.Equal(11, restored.CreateSnapshot().Forces.Single(f => f.Id == 1).LeaderId);
    }

    [Fact]
    public void MutualDestructionSettlesBothUnitsAndMembersOnce()
    {
        var world = World();
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Units[0] with { Position = new(20, 7), Soldiers = 1, Morale = 90,
            Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 } });
        mutation.AddUnit(new(1, 2, new(21, 7), 1) { FormationId = 7, Morale = 90, ServicePlaceId = 2,
            MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(22, BattleRank.Rear) });
        mutation.Replace(mutation.Officers[22] with { UnitId = 1, SettlementId = null });
        mutation.SetSimulation(mutation.Simulation with { NextUnitId = 2 });
        world.Commit(mutation);
        var engine = Engine(world);
        var result = engine.AdvanceXun();
        Assert.Empty(engine.Snapshot.Units);
        Assert.Equal(2, result.Events.OfType<UnitTerminatedEvent>().Count());
        Assert.All(engine.Snapshot.Officers, p => Assert.Null(p.UnitId));
        Assert.All(engine.Snapshot.Forces, f => Assert.False(f.IsEliminated));
        Assert.NotNull(WorldState.Restore(engine.CaptureStableState()));
    }

    [Fact]
    public void UnrelatedAssetlessForceIsNotEliminatedByAnotherBattle()
    {
        var baseline = World().CreateSnapshot();
        var world = WorldState.Create(baseline.Clock, 42, baseline.Forces.Append(new ForceState(3, 0)),
            baseline.Settlements, baseline.Officers, baseline.Units, corps: [..baseline.Corps, new San9.Core.Domain.CorpsState(3, 3, null, new(100, 100)) { IsRoot = true }]);
        var engine = Engine(world);
        engine.AdvanceXun();
        Assert.False(engine.Snapshot.Forces.Single(f => f.Id == 3).IsEliminated);
        Assert.NotEqual(SimulationBoundary.Terminal, engine.Snapshot.Simulation.Boundary);
    }

    [Fact]
    public void CapturedCorpsDissolvesWithoutRemovingSurvivingForceOrTreasuryAnchor()
    {
        var baseline = World().CreateSnapshot();
        var world = WorldState.Create(baseline.Clock, 42,
            baseline.Forces.Select(f => f.Id == 2 ? f with { LeaderId = 23 } : f),
            baseline.Settlements.Select(s => s with { CorpsId = s.Id == 1 ? 10 : 21 })
                .Append(new SettlementState(3, 2, 100, 100, 100, 70, 500, 0, 100, 0, 100) { CorpsId = 20 }),
            baseline.Officers.Select(p => p.Id == 22 ? p with { Status = OfficerStatus.Governor } : p)
                .Append(new OfficerState(23, 2, 3, new(70, 70, 70, 70), false) { Status = OfficerStatus.Ruler }),
            baseline.Units.Select(u => u with { CorpsId = 10 }),
            [new(10, 1, 11, new(10000, 10000)) { IsRoot = true }, new(20, 2, 23, new(10000, 10000)) { IsRoot = true }, new(21, 2, 22, new(1234, 2345))]);
        var engine = Engine(world);
        var result = engine.AdvanceXun();
        Assert.Single(result.Events.OfType<SettlementCapturedEvent>());
        Assert.Contains(result.Events, e => e is CorpsDissolvedEvent { CorpsId: 21 });
        Assert.DoesNotContain(engine.Snapshot.Corps, c => c.Id == 21);
        Assert.Contains(engine.Snapshot.Corps, c => c.Id == 20 && c.LeaderId == 23);
        Assert.All(engine.Snapshot.Forces, f => Assert.False(f.IsEliminated));
        Assert.Equal(10, engine.Snapshot.Settlements.Single(s => s.Id == 2).CorpsId);
        Assert.NotNull(WorldState.Restore(engine.CaptureStableState()));
    }

    [Fact]
    public void CapturePreservesUndiscoveredAndFuturePeople()
    {
        var baseline = World().CreateSnapshot();
        var world = WorldState.Create(baseline.Clock, 42, baseline.Forces, baseline.Settlements,
            baseline.Officers.Concat(new[] {
                new OfficerState(30, null, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Undiscovered },
                new OfficerState(31, null, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.NotAppeared, AppearanceYear = 200 }
            }), baseline.Units, corps: baseline.Corps);
        var engine = Engine(world);
        engine.AdvanceXun();
        Assert.Equal(OfficerStatus.Undiscovered, engine.Snapshot.Officers.Single(p => p.Id == 30).Status);
        Assert.Equal(OfficerStatus.NotAppeared, engine.Snapshot.Officers.Single(p => p.Id == 31).Status);
    }

    [Fact]
    public void ActualHeldEscapeMountProtectsDefeatedFieldMemberWithReachableHome()
    {
        var baseline = World().CreateSnapshot();
        var world = WorldState.Create(baseline.Clock, 42, baseline.Forces, baseline.Settlements, baseline.Officers,
            baseline.Units.Select(u => u with { Soldiers = 1, Position = new(20, 7),
                Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 } })
                .Append(new UnitState(1, 2, new(21, 7), 10000) { FormationId = 7, Morale = 90 }),
            treasures: [new(7, 11, TreasureDiscovery.Discovered, null)], corps: baseline.Corps);
        var engine = new SimulationEngine(world, EconomyRulebook.Empty, PersonnelRulebook.Empty,
            new TreasureRulebook([new(7, default, 0) { EscapeMount = true }]), military: Rules());
        var result = engine.AdvanceXun();
        Assert.Contains(result.Events, e => e is OfficerEscapedBattleEvent { OfficerId: 11, SettlementId: 1 });
        Assert.DoesNotContain(result.Events, e => e is OfficerCapturedEvent { OfficerId: 11 });
        Assert.Equal(11, Assert.Single(engine.Snapshot.Treasures).HolderId);
        Assert.Equal(OfficerStatus.Ruler, engine.Snapshot.Officers.Single(p => p.Id == 11).Status);
    }

    [Fact]
    public void TravelingOfficerContinuesHisSearchInsteadOfBeingCapturedWithAdministrativeHome()
    {
        var baseline = World().CreateSnapshot();
        var world = WorldState.Create(baseline.Clock, 42, baseline.Forces,
            baseline.Settlements.Append(new SettlementState(3, 2, 100, 100, 100, 70, 500, 0, 100, 0, 100)),
            baseline.Officers, baseline.Units, corps: baseline.Corps);
        var mutation = world.BeginMutation();
        Guid taskId = Guid.NewGuid();
        mutation.SetSimulation(mutation.Simulation with { NextSequence = 1,
            Tasks = [new(taskId, "personnel-search", "system", 2, 22, 2, new(190, 1, 1), new(190, 1, 10), 0, 0)
            { Journey = new(3, null, 99) { Route = new(new(37, 7), new(37, 2), false) } }] });
        world.Commit(mutation);
        var geography = new PersonnelRulebook([new(1, new(2, 7)), new(2, new(37, 7)), new(3, new(37, 2))]);
        var engine = new SimulationEngine(world, EconomyRulebook.Empty, geography, TreasureRulebook.Empty, military: Rules());
        var result = engine.AdvanceXun();
        Assert.DoesNotContain(result.Events, e => e is TaskCancelledEvent cancelled && cancelled.TaskId == taskId);
        Assert.Contains(result.Events, e => e is TaskCompletedEvent completed && completed.TaskId == taskId);
        Assert.Contains(result.Events, e => e is SearchResolvedEvent { OfficerId: 22 });
        Assert.DoesNotContain(result.Events, e => e is OfficerCapturedEvent { OfficerId: 22 });
        var ruler = engine.Snapshot.Officers.Single(p => p.Id == 22);
        Assert.Equal(OfficerStatus.Ruler, ruler.Status);
        Assert.Equal(3, ruler.SettlementId);
        Assert.False(engine.Snapshot.Forces.Single(f => f.Id == 2).IsEliminated);
    }

    [Fact]
    public void OrdinaryLossDoesNotCauseProportionalMoraleCapture()
    {
        var world = World(100000);
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Settlements[2] with { Morale = 1 });
        world.Commit(mutation);
        var engine = Engine(world);
        var result = engine.AdvanceXun();
        Assert.Empty(result.Events.OfType<SettlementCapturedEvent>());
        Assert.All(result.Events.OfType<BattleDamageEvent>(), e => Assert.Equal(0, e.MoraleLoss));
        var captured = engine.Snapshot.Settlements.Single(s => s.Id == 2);
        Assert.True(captured.Soldiers > 0);
        Assert.True(captured.Durability > 0);
    }

    [Fact]
    public void BarbarianEliminationKillsNonPrisonersAndClearsTheirTreasureHoldings()
    {
        var baseline = World().CreateSnapshot();
        var world = WorldState.Create(baseline.Clock, 42, baseline.Forces.Select(f => f.Id == 2 ? f with { IsBarbarian = true } : f),
            baseline.Settlements, baseline.Officers.Append(new OfficerState(30, 2, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special }),
            baseline.Units, treasures: [new(7, 30, TreasureDiscovery.Discovered, null)], corps: baseline.Corps);
        var engine = new SimulationEngine(world, EconomyRulebook.Empty, PersonnelRulebook.Empty, new TreasureRulebook([new(7, default, 0)]), military: Rules());
        var result = engine.AdvanceXun();
        Assert.Equal(OfficerStatus.Dead, engine.Snapshot.Officers.Single(p => p.Id == 30).Status);
        Assert.Null(Assert.Single(engine.Snapshot.Treasures).HolderId);
        Assert.Equal(OfficerStatus.Dead, engine.Snapshot.Officers.Single(p => p.Id == 22).Status);
        Assert.Null(engine.Snapshot.Officers.Single(p => p.Id == 22).OwnerId);
        Assert.Null(engine.Snapshot.Officers.Single(p => p.Id == 30).SettlementId);
        Assert.Null(engine.Snapshot.Officers.Single(p => p.Id == 22).SettlementId);
        Assert.Equal(OfficerDeathCause.ForceEliminated, Assert.Single(result.Events.OfType<OfficerDiedEvent>(), e => e.OfficerId == 30).Cause);
        Assert.Equal(OfficerDeathCause.NoRetreat, Assert.Single(result.Events.OfType<OfficerDiedEvent>(), e => e.OfficerId == 22).Cause);
        Assert.Single(result.Events.OfType<TreasureDroppedEvent>());
    }

    [Fact]
    public void RivalClaimantContinuesAttackingTheNewOwnerAndCanReply()
    {
        var baseline = World().CreateSnapshot();
        var world = WorldState.Create(baseline.Clock, 42, baseline.Forces.Append(new ForceState(3, 100) { LeaderId = 33 }),
            baseline.Settlements.Append(new SettlementState(3, 3, 100, 100, 100, 70, 500, 0, 100, 0, 100)),
            baseline.Officers.Append(new OfficerState(33, 3, null, new(90, 90, 90, 90), false) { UnitId = 1, Status = OfficerStatus.Ruler }),
            baseline.Units.Append(new UnitState(1, 3, new(36, 6), 10000) { FormationId = 7, Morale = 90,
                MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(33, BattleRank.Front),
                Order = new(MilitaryOrderKind.AttackSettlement, null, 2) }), corps: [.. baseline.Corps, new San9.Core.Domain.CorpsState(3, 3, 33, new(10000, 10000)) { IsRoot = true }]);
        var engine = Engine(world);
        var result = engine.AdvanceXun();
        Assert.Single(result.Events.OfType<SettlementCapturedEvent>());
        Assert.Equal(1, engine.Snapshot.Settlements.Single(s => s.Id == 2).OwnerId);
        var captureDay = Assert.Single(result.Events.OfType<SettlementCapturedEvent>()).Clock;
        var damage = result.Events.OfType<BattleDamageEvent>().Where(e => e.Clock == captureDay).ToArray();
        Assert.Contains(damage, e => e.AttackerId == 1 && !e.AttackerIsSettlement && e.SettlementId == 2 && e.Target?.ForceId == 1);
        Assert.Contains(damage, e => e.AttackerIsSettlement && e.AttackerId == 2 && e.TargetUnitId == 1 && e.Source?.ForceId == 1);
        Assert.Equal(new(MilitaryOrderKind.AttackSettlement, null, 2), engine.Snapshot.Units.Single(u => u.Id == 1).Order);
    }
}
