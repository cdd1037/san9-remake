using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class FacilityConstructionTests
{
    private static readonly CommandContext Player = new("player");
    private static readonly PersonnelRulebook Geography = new([new(1, new(2, 2)), new(2, new(27, 2)), new(8000, new(17, 2))],
        new Dictionary<int, int> { [1] = 1, [2] = 2, [8000] = 1 });
    private static readonly MilitaryRulebook Military = CreateMilitary();
    private static MilitaryRulebook CreateMilitary(bool hostsPeople = true, bool needsTechnology = false, bool attacksWalls = false, int members = 1) => new(new(6, 1, [0, 0, 0, 0, 0, 0], [new(0, 1, null)],
        [new(1, 5, 50, 100000, members, new(1, 1), TravelMode.Land)
            { Combat = new(10, 10, 1000, 10, 50), Attack = new(1, attacksWalls), CaptureRanks = [.. Enumerable.Repeat(BattleRank.Front, members)] }],
        [new(1, new(2, 2), new(1, 1)) { FacilityTypeId = 0 },
         new(2, new(27, 2), new(1, 1)) { FacilityTypeId = 0 }])
    {
        Facilities = [new(0, 0, 600, new(1, 1), 15, false, true, true, 500000, null, null, null, []),
            new(9000, 1000, 600, new(3, 3), 15, true, hostsPeople, hostsPeople, 250000,
                needsTechnology ? 20 : null, needsTechnology ? 19 : null, null, [])
                { Combat = new(10, 10, 0, 10, 50), DestroyedAtZeroDurability = true,
                    ConstructionGrowthTacticId = needsTechnology ? 20 : 19 }],
        Tactics = [new(19, 6, TacticEffect.Construction, 0, 0, 0, false, false, []),
            new(20, 6, TacticEffect.StoneFormation, 0, 0, 0, false, false, [])],
        ConstructionSites = [new(8000, new(17, 2), [9000])]
    });
    private static SimulationEngine Engine(WorldState? world = null, MilitaryRulebook? military = null) => new(world ?? WorldState.Create(new(190, 1, 1), 42,
        [new(1, 10000, 1000000, 100) { LeaderId = 11 }], [new(1, 1, 20000, 600, 600, 100, 0, 0, 100, 0, 100)],
        [new(11, 1, 1, new(100, 100, 100, 100), false) { Status = OfficerStatus.Ruler }],
        controls: [new("player", 1, ControllerKind.Player)]), EconomyRulebook.Empty, Geography, TreasureRulebook.Empty, military: military ?? Military);
    private static ConstructFacilityCommand Command(long sequence = 0) => new(Guid.NewGuid(), 1, sequence, 1, [11], 10000, 1, 8000, 9000, new(17, 2));
    [Theory]
    [InlineData("controller")]
    [InlineData("issuer")]
    [InlineData("sequence")]
    public void ConstructionRejectsUnauthorizedOrStaleEnvelopesWithoutSideEffects(string invalid)
    {
        var engine = Engine();
        var command = Command();
        Assert.True(engine.Validate(Player, command).Accepted);
        var before = engine.Snapshot.StateHash;
        var result = engine.Execute(invalid == "controller" ? new("outsider") : Player,
            invalid == "issuer" ? command with { IssuerId = 2 } : invalid == "sequence" ? command with { Sequence = 1 } : command);
        Assert.False(result.Submission.Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
        Assert.Empty(result.Events);
    }

    [Fact]
    public void PreviouslyValidConstructionRechecksCurrentFundsAndCannotReserveATargetTwice()
    {
        var engine = Engine();
        var command = Command();
        Assert.True(engine.Validate(Player, command).Accepted);
        var initial = engine.CaptureStableState();
        var poorer = Engine(WorldState.Restore(initial with { Forces = initial.Forces.Select(f => f with { Money = 999 }).ToArray() }));
        var before = poorer.Snapshot.StateHash;
        Assert.False(poorer.Execute(Player, command).Submission.Accepted);
        Assert.Equal(before, poorer.Snapshot.StateHash);
        Assert.True(engine.Execute(Player, command).Submission.Accepted);
        before = engine.Snapshot.StateHash;
        foreach (var duplicate in new[] { command, command with { CommandId = Guid.NewGuid(), Sequence = engine.Snapshot.Simulation.NextSequence } })
        {
            var rejected = engine.Execute(Player, duplicate);
            Assert.False(rejected.Submission.Accepted);
            Assert.Empty(rejected.Events);
            Assert.Equal(before, engine.Snapshot.StateHash);
        }
        Assert.Single(engine.Snapshot.Units);
        Assert.Equal(9000u, engine.Snapshot.Forces.Single().Money);
    }

    [Fact]
    public void ConfusedConstructionSourceRejectsWithoutReservingOrSpending()
    {
        var engine = Engine(); var command = Command();
        Assert.True(engine.Validate(Player, command).Accepted);
        var initial = engine.CaptureStableState();
        var confused = Engine(WorldState.Restore(initial with { Settlements = initial.Settlements
            .Select(s => s.Id == 1 ? s with { ConfusionDaysRemaining = 12 } : s).ToArray() }));
        string before = confused.Snapshot.StateHash;
        Assert.Equal(CommandRejection.SettlementConfused, confused.Validate(Player, command).Rejection);
        var rejected = confused.Execute(Player, command);
        Assert.Equal(CommandRejection.SettlementConfused, rejected.Submission.Rejection);
        Assert.Empty(rejected.Events); Assert.Equal(before, confused.Snapshot.StateHash);
        Advance(confused); Advance(confused);
        Assert.True(confused.Execute(Player, command with { Sequence = confused.Snapshot.Simulation.NextSequence }).Submission.Accepted);
        Assert.Single(confused.Snapshot.Units);
    }

    private static AdvanceXunResult Advance(SimulationEngine engine)
    {
        engine.EndControl(Player);
        return engine.AdvanceXun();
    }

    [Fact]
    public void ConstructionGrowthFollowsWorkMilestonesAndStopsAfterCompletion()
    {
        var engine = Engine();
        Assert.True(engine.Execute(Player, Command()).Submission.Accepted);
        Assert.Equal(0, engine.Snapshot.Officers.Single().TacticProficiencies[6]);
        List<DomainEvent> events = [];
        for (int i = 0; i < 4; i++) events.AddRange(Advance(engine).Events);
        var phases = events.OfType<FacilityConstructionChangedEvent>().ToArray();
        Assert.Single(phases, e => e.Phase == "started");
        Assert.Single(phases, e => e.Phase == "completed");
        int expected = 6 + phases.Count(e => e.Phase == "progress");
        Assert.Equal(expected, engine.Snapshot.Officers.Single().TacticProficiencies[6]);
        Assert.Equal(expected, events.OfType<TacticProficiencyGrownEvent>().Sum(e => e.Current - e.Previous));
        Assert.DoesNotContain(Advance(engine).Events, e => e is TacticProficiencyGrownEvent or TacticLearnedEvent);
    }

    [Fact]
    public void PausedAndCancelledConstructionDoNotGrantGrowth()
    {
        var engine = Engine();
        Assert.True(engine.Execute(Player, Command()).Submission.Accepted);
        Advance(engine);
        var saved = engine.CaptureStableState();
        var paused = Engine(WorldState.Restore(saved with { Units = saved.Units.Select(u => u with
            { Condition = UnitCondition.Confused, ConditionDaysRemaining = 100 }).ToImmutableArray() }));
        Assert.DoesNotContain(Advance(paused).Events, e => e is TacticProficiencyGrownEvent or TacticLearnedEvent or OfficerAbilityGrownEvent);
        Assert.True(engine.Execute(Player, new OrderUnitCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence, 0,
            new(MilitaryOrderKind.Return, null, 1))).Submission.Accepted);
        Assert.DoesNotContain(Advance(engine).Events, e => e is TacticProficiencyGrownEvent or TacticLearnedEvent or OfficerAbilityGrownEvent);
    }

    [Theory]
    [InlineData(false, 100)]
    [InlineData(true, 100)]
    [InlineData(true, 999)]
    public void ConstructionGrowthUsesCurrentMembersMasteryAndSaturation(bool mastered, int initial)
    {
        var original = Engine().CaptureStableState();
        var engine = Engine(WorldState.Restore(original with { Officers = original.Officers.Select(p => p with
        {
            TacticProficiencies = p.TacticProficiencies.SetItem(6, initial),
            MasteredCategories = mastered ? [6] : []
        }).ToImmutableArray() }));
        Assert.True(engine.Execute(Player, Command()).Submission.Accepted);
        List<DomainEvent> events = [];
        for (int i = 0; i < 4; i++) events.AddRange(Advance(engine).Events);
        int after = engine.Snapshot.Officers.Single().TacticProficiencies[6];
        int progress = events.OfType<FacilityConstructionChangedEvent>().Count(e => e.Phase == "progress");
        Assert.Equal(Math.Min(1000, initial + (6 + progress) * (mastered ? 3 : 1)), after);
    }

    [Fact]
    public void ConstructionMilestonesCanTeachTheUnselectedConstructionTactic()
    {
        var initial = Engine().CaptureStableState();
        var engine = Engine(WorldState.Restore(initial with { Officers = initial.Officers.Select(p => p with
            { TacticProficiencies = p.TacticProficiencies.SetItem(6, 1000) }).ToImmutableArray() }));
        Assert.True(engine.Execute(Player, Command()).Submission.Accepted);
        List<DomainEvent> events = [];
        for (int i = 0; i < 4; i++) events.AddRange(Advance(engine).Events);
        var learned = Assert.Single(events.OfType<TacticLearnedEvent>());
        Assert.Equal(19, learned.TacticId);
        Assert.Equal(11, learned.OfficerId);
        Assert.Contains(events.OfType<FacilityConstructionChangedEvent>(),
            e => e.Clock == learned.Clock && e.Phase is "started" or "completed");
        Assert.Contains(19, engine.Snapshot.Officers.Single().LearnedTactics);
        Assert.Null(engine.Snapshot.Officers.Single().EquippedTacticId);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(100)]
    public void ConstructionGrowthUsesIdentityOrderWithoutReselectingCappedMember(int leadership)
    {
        var world = WorldState.Create(new(190, 1, 1), 42,
            [new(1, 10000, 1000000, 100) { LeaderId = 22 }],
            [new(1, 1, 20000, 600, 600, 100, 0, 0, 100, 0, 100)],
            [new(11, 1, 1, new(80, 80, 80, leadership), false),
             new(22, 1, 1, new(80, 80, 80, 99), false) { Status = OfficerStatus.Ruler }],
            controls: [new("player", 1, ControllerKind.Player)]);
        var military = CreateMilitary(members: 2);
        var engine = Engine(world, military);
        Assert.True(engine.Execute(Player, Command() with { OfficerIds = [22, 11] }).Submission.Accepted);
        var before = engine.CaptureStableState();
        Assert.Equal(22, Assert.Single(before.Units).CommanderId);
        before = before with { Simulation = before.Simulation with { RandomStreams =
        [
            new("military-construction-growth", 52),
            new("military-construction-growth-member", 52),
            new("military-construction-learning", 52)
        ] } };
        engine = Engine(WorldState.Restore(before), military);
        var restored = Engine(WorldState.Restore(before), military);

        var result = Advance(engine);
        var replayed = Advance(restored);

        Assert.Single(result.Events.OfType<FacilityConstructionChangedEvent>(), e => e.Phase == "started");
        Assert.DoesNotContain(result.Events.OfType<FacilityConstructionChangedEvent>(), e => e.Phase == "completed");
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(result.Events), System.Text.Json.JsonSerializer.Serialize(replayed.Events));
        Assert.Equal(11, Assert.Single(result.Events.OfType<TacticLearnedEvent>()).OfficerId);
        var growth = result.Events.OfType<OfficerAbilityGrownEvent>().ToArray();
        if (leadership == 99) Assert.Equal(11, Assert.Single(growth).OfficerId);
        else Assert.Empty(growth);
        Assert.Equal(99, engine.Snapshot.Officers.Single(p => p.Id == 22).BaseAbilities.Leadership);
        var random = new StableRandom(52);
        random.NextUInt64();
        Assert.Equal(leadership == 100 ? 52UL : random.State,
            engine.Snapshot.Simulation.RandomStreams.Single(s => s.Name == "military-construction-growth").State);
        random.NextUInt64();
        Assert.Equal(random.State, engine.Snapshot.Simulation.RandomStreams.Single(s => s.Name == "military-construction-learning").State);
    }

    [Theory]
    [InlineData("member", true)]
    [InlineData("force", true)]
    [InlineData("treasure", true)]
    [InlineData("nonmember", false)]
    [InlineData("buried", false)]
    public void ConstructionTechnologyAndDiscountUseOnlyEligibleProviders(string grant, bool accepted)
    {
        var world = WorldState.Create(new(190, 1, 1), 42,
            [new(1, 10000, 1000000, 100) { ProvidedTactics = grant == "force" ? [19, 20] : [] }],
            [new(1, 1, 20000, 600, 600, 100, 0, 0, 100, 0, 100)],
            [new(11, 1, 1, new(70, 70, 70, 70), false) { LearnedTactics = grant == "member" ? [19, 20] : [] },
                new(12, 1, 1, new(70, 70, 70, 70), false) { LearnedTactics = grant == "nonmember" ? [19, 20] : [] }],
            treasures: [new(1, grant == "nonmember" ? 12 : 11,
                grant == "buried" ? TreasureDiscovery.Buried : TreasureDiscovery.Discovered, null),
                new(2, grant == "nonmember" ? 12 : 11,
                    grant == "buried" ? TreasureDiscovery.Buried : TreasureDiscovery.Discovered, null)],
            controls: [new("player", 1, ControllerKind.Player)]);
        var treasures = new TreasureRulebook([new(1, default, 0) { TacticId = grant is "treasure" or "buried" or "nonmember" ? 19 : null },
            new(2, default, 0) { TacticId = grant is "treasure" or "buried" or "nonmember" ? 20 : null }]);
        var engine = new SimulationEngine(world, EconomyRulebook.Empty, Geography, treasures, military: CreateMilitary(needsTechnology: true));
        string before = engine.Snapshot.StateHash;
        uint money = engine.Snapshot.Forces.Single().Money;
        var result = engine.Execute(Player, Command());
        Assert.Equal(accepted, result.Submission.Accepted);
        if (accepted)
        {
            Assert.Equal(money - 666, engine.Snapshot.Forces.Single().Money);
            Advance(engine);
            Assert.NotNull(engine.Snapshot.Settlements.Single(s => s.Id == 8000).Facility!.BuilderUnitId);
            Assert.True(engine.Snapshot.Officers.Single(p => p.Id == 11).TacticProficiencies[6] > 0);
            Assert.Equal(0, engine.Snapshot.Officers.Single(p => p.Id == 12).TacticProficiencies[6]);
        }
        else
        {
            Assert.Equal(CommandRejection.TacticUnavailable, result.Submission.Rejection);
            Assert.Equal(before, engine.Snapshot.StateHash);
        }
    }

    [Fact]
    public void ConstructionMovesStartsAndCompletesOnceAndRestorePreservesUnfinishedWork()
    {
        var engine = Engine();
        var command = Command();
        var submitted = engine.Execute(Player, command);
        Assert.True(submitted.Submission.Accepted);
        Assert.Equal(9000u, engine.Snapshot.Forces.Single().Money);
        Assert.DoesNotContain(engine.Snapshot.Settlements, s => s.Id == 8000);
        Assert.False(engine.Execute(Player, command).Submission.Accepted);
        var first = Advance(engine);
        Assert.Single(first.Events.OfType<FacilityConstructionChangedEvent>(), e => e.Phase == "started");
        var unfinished = engine.Snapshot.Settlements.Single(s => s.Id == 8000);
        Assert.NotNull(unfinished.Facility!.BuilderUnitId);
        Assert.False(unfinished.CanHostPersons);
        Assert.InRange(unfinished.Durability, 1, 599);
        var restored = Engine(WorldState.Restore(engine.CaptureStableState()));
        int completed = 0;
        for (int i = 0; i < 3; i++)
        {
            var actual = Advance(engine);
            Assert.Equal(CanonicalJson.Text(actual.Events), CanonicalJson.Text(Advance(restored).Events));
            completed += actual.Events.OfType<FacilityConstructionChangedEvent>().Count(e => e.Phase == "completed");
        }
        Assert.Equal(1, completed);
        Assert.Empty(engine.Snapshot.Units);
        var facility = engine.Snapshot.Settlements.Single(s => s.Id == 8000);
        Assert.Equal(600, facility.Durability);
        Assert.Null(facility.Facility!.BuilderUnitId);
        Assert.True(facility.CanHostPersons);
        Assert.Equal(10000u, facility.Soldiers);
        Assert.Equal(8000, engine.Snapshot.Officers.Single().SettlementId);
        Assert.Equal(new WorldPosition(17, 2), Military.Place(facility).Position);
    }

    [Fact]
    public void NewOrdersCancelUnfinishedFacilityWithoutRefundAndAllowReconstruction()
    {
        var engine = Engine();
        Assert.True(engine.Execute(Player, Command()).Submission.Accepted);
        Advance(engine);
        var result = engine.Execute(Player, new OrderUnitCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence, 0,
            new(MilitaryOrderKind.Return, null, 1)));
        Assert.True(result.Submission.Accepted);
        Assert.Single(result.Events.OfType<FacilityConstructionChangedEvent>(), e => e.Phase == "cancelled");
        Assert.True(engine.Snapshot.Settlements.Single(s => s.Id == 8000).IsDestroyed);
        Assert.Equal(9000u, engine.Snapshot.Forces.Single().Money);
        Advance(engine);
        Assert.Empty(engine.Snapshot.Units);
        Assert.True(engine.Execute(Player, Command(engine.Snapshot.Simulation.NextSequence)).Submission.Accepted);
        Advance(engine);
        Assert.False(engine.Snapshot.Settlements.Single(s => s.Id == 8000).IsDestroyed);
        Assert.Equal(8000u, engine.Snapshot.Forces.Single().Money);
    }

    [Theory]
    [InlineData(UnitCondition.HighSpirits, true)]
    [InlineData(UnitCondition.Confused, false)]
    public void HighSpiritsAllowsWorkButConfusionPausesIt(UnitCondition condition, bool progresses)
    {
        var engine = Engine();
        Assert.True(engine.Execute(Player, Command()).Submission.Accepted);
        Advance(engine);
        var saved = engine.CaptureStableState();
        int before = saved.Settlements.Single(s => s.Id == 8000).Durability;
        var changed = Engine(WorldState.Restore(saved with { Units = saved.Units.Select(u => u with
            { Condition = condition, ConditionDaysRemaining = 100 }).ToImmutableArray() }));
        Advance(changed);
        Assert.Equal(progresses, changed.Snapshot.Settlements.Single(s => s.Id == 8000).Durability > before);
    }

    [Fact]
    public void DemolishingUnfinishedFacilityClearsItsBuilderOrder()
    {
        var engine = Engine();
        Assert.True(engine.Execute(Player, Command()).Submission.Accepted);
        Advance(engine);
        Assert.True(engine.Execute(Player, new DemolishFacilityCommand(Guid.NewGuid(), 1,
            engine.Snapshot.Simulation.NextSequence, 8000)).Submission.Accepted);
        Assert.Null(engine.Snapshot.Units.Single().Order);
        Assert.Null(engine.Snapshot.Settlements.Single(s => s.Id == 8000).Facility!.BuilderUnitId);
        Assert.True(engine.Snapshot.Settlements.Single(s => s.Id == 8000).IsDestroyed);
        Assert.DoesNotContain(Advance(engine).Events.OfType<FacilityConstructionChangedEvent>(), e => e.Phase == "completed");
    }

    [Fact]
    public void LossOfConstructionUnitToStarvationRemovesItsUnfinishedFacility()
    {
        var engine = Engine();
        Assert.True(engine.Execute(Player, Command()).Submission.Accepted);
        var saved = engine.CaptureStableState();
        var starving = Engine(WorldState.Restore(saved with
        {
            Forces = saved.Forces.Select(f => f with { Food = 0 }).ToImmutableArray(),
            Units = saved.Units.Select(u => u with { Morale = 30 }).ToImmutableArray()
        }));
        var result = Advance(starving);
        Assert.Empty(starving.Snapshot.Units);
        Assert.Single(result.Events.OfType<FacilityConstructionChangedEvent>(), e => e.Phase == "cancelled");
        Assert.True(starving.Snapshot.Settlements.Single(s => s.Id == 8000).IsDestroyed);
    }

    [Fact]
    public void DomesticRepairCannotCompleteUnfinishedWorkButRepairsTheFinishedFacilityNormally()
    {
        var engine = Engine();
        Assert.True(engine.Execute(Player, Command()).Submission.Accepted);
        Advance(engine);
        string before = engine.Snapshot.StateHash;
        var repair = new DevelopCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence,
            new(1, 8000, [11], DomesticActivity.Repair));
        var rejected = engine.Execute(Player, repair);
        Assert.False(rejected.Submission.Accepted);
        Assert.Equal(DevelopmentRejection.UnsupportedActivity, rejected.Development!.Value.Rejection);
        Assert.Equal(before, engine.Snapshot.StateHash);
        for (int i = 0; i < 3; i++) Advance(engine);
        var saved = engine.CaptureStableState();
        var damaged = Engine(WorldState.Restore(saved with
        {
            Settlements = saved.Settlements.Select(s => s.Id == 8000 ? s with { Durability = 599 } : s).ToImmutableArray()
        }));
        var result = damaged.Execute(Player, repair with { CommandId = Guid.NewGuid(), Sequence = damaged.Snapshot.Simulation.NextSequence });
        Assert.True(result.Submission.Accepted);
        Assert.Equal(600, damaged.Snapshot.Settlements.Single(s => s.Id == 8000).Durability);
        Assert.DoesNotContain(result.Events, e => e is FacilityConstructionChangedEvent or UnitAbsorbedEvent);
    }

    [Fact]
    public void NonHostingStructureReturnsItsWorkforceToTheServiceCity()
    {
        var engine = Engine(military: CreateMilitary(hostsPeople: false));
        Assert.True(engine.Execute(Player, Command()).Submission.Accepted);
        var events = new List<DomainEvent>();
        for (int i = 0; i < 4; i++) events.AddRange(Advance(engine).Events);
        Assert.Single(events.OfType<FacilityConstructionChangedEvent>(), e => e.Phase == "completed");
        Assert.DoesNotContain(events.OfType<UnitAbsorbedEvent>(), e => e.SettlementId == 8000);
        Assert.Single(events.OfType<UnitAbsorbedEvent>(), e => e.SettlementId == 1);
        Assert.Equal(20000u, engine.Snapshot.Settlements.Single(s => s.Id == 1).Soldiers);
        var facility = engine.Snapshot.Settlements.Single(s => s.Id == 8000);
        Assert.Equal(600, facility.Durability);
        Assert.False(facility.CanHostPersons);
        Assert.Equal(0u, facility.Soldiers);
        Assert.Empty(engine.Snapshot.Units);
    }

    [Fact]
    public void TechnologyIsRequiredDiscountUsesParticipantsAndLossCancelsUnfinishedWork()
    {
        var rules = CreateMilitary(needsTechnology: true);
        var engine = Engine(military: rules);
        string hash = engine.Snapshot.StateHash;
        Assert.Equal(CommandRejection.TacticUnavailable, engine.Execute(Player, Command()).Submission.Rejection);
        Assert.Equal(hash, engine.Snapshot.StateHash);
        var initial = engine.CaptureStableState();
        engine = Engine(WorldState.Restore(initial with { Forces = initial.Forces.Select(f => f with { ProvidedTactics = [19, 20] }).ToImmutableArray() }), rules);
        Assert.True(engine.Execute(Player, Command()).Submission.Accepted);
        Assert.Equal(9334u, engine.Snapshot.Forces.Single().Money);
        Advance(engine);
        var saved = engine.CaptureStableState();
        engine = Engine(WorldState.Restore(saved with { Forces = saved.Forces.Select(f => f with { ProvidedTactics = [19] }).ToImmutableArray() }), rules);
        var result = Advance(engine);
        Assert.Single(result.Events.OfType<FacilityConstructionChangedEvent>(), e => e.Phase == "cancelled");
        Assert.Null(engine.Snapshot.Units.Single().Order);
        Assert.True(engine.Snapshot.Settlements.Single(s => s.Id == 8000).IsDestroyed);
        Assert.Equal(9334u, engine.Snapshot.Forces.Single().Money);
    }

    [Theory]
    [InlineData("position")]
    [InlineData("type")]
    [InlineData("region")]
    [InlineData("members")]
    [InlineData("soldiers")]
    [InlineData("funds")]
    [InlineData("acted")]
    public void InvalidConstructionLeavesResourcesPeopleAndSequenceUnchanged(string invalid)
    {
        var engine = Engine();
        var saved = engine.CaptureStableState();
        if (invalid == "funds")
            engine = Engine(WorldState.Restore(saved with { Forces = saved.Forces.Select(f => f with { Money = 999 }).ToImmutableArray() }));
        if (invalid == "acted")
            engine = Engine(WorldState.Restore(saved with { Officers = saved.Officers.Select(p => p with { Acted = true }).ToImmutableArray() }));
        var command = invalid switch
        {
            "position" => Command() with { Position = new(16, 2) },
            "type" => Command() with { FacilityTypeId = 0 },
            "region" => Command() with { RegionId = 1 },
            "members" => Command() with { OfficerIds = [11, 11] },
            "soldiers" => Command() with { Soldiers = 20001 },
            _ => Command()
        };
        string before = engine.Snapshot.StateHash;
        var result = engine.Execute(Player, command);
        Assert.False(result.Submission.Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
        Assert.DoesNotContain(result.Events, e => e is UnitDeployedEvent or FacilityConstructionChangedEvent or ResourceChangedEvent);
    }

    [Fact]
    public void LossOfParentCityCancelsWorkWithoutCompletionOrRefund()
    {
        var engine = Engine();
        Assert.True(engine.Execute(Player, Command()).Submission.Accepted);
        Advance(engine);
        var saved = engine.CaptureStableState();
        engine = Engine(WorldState.Restore(saved with
        {
            Settlements = saved.Settlements.Select(s => s.Id == 1 ? s with { Durability = 0 } : s).ToImmutableArray()
        }));
        var result = Advance(engine);
        Assert.Single(result.Events.OfType<FacilityConstructionChangedEvent>(), e => e.Phase == "cancelled");
        Assert.DoesNotContain(result.Events.OfType<FacilityConstructionChangedEvent>(), e => e.Phase == "completed");
        Assert.True(engine.Snapshot.Settlements.Single(s => s.Id == 8000).IsDestroyed);
        Assert.Null(engine.Snapshot.Units.Single().Order);
        Assert.Equal(9000u, engine.Snapshot.Forces.Single().Money);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnfinishedFacilityRejectsAttackAndStaleOrdersWithoutTakingDamage(bool attacksWalls)
    {
        var rules = CreateMilitary(attacksWalls: attacksWalls);
        var engine = Engine(military: rules);
        Assert.True(engine.Execute(Player, Command()).Submission.Accepted);
        Advance(engine);
        var saved = engine.CaptureStableState();
        engine = Engine(WorldState.Restore(saved with
        {
            Forces = saved.Forces.ToImmutableArray().Add(new(2, 10000, 1000000, 100) { LeaderId = 21 }),
            Settlements = saved.Settlements.ToImmutableArray().Add(new(2, 2, 0, 600, 600, 100, 0, 0, 100, 0, 100)),
            Officers = saved.Officers.ToImmutableArray().Add(new(21, 2, null, new(100, 100, 100, 100), false)
                { UnitId = 1, Status = OfficerStatus.Ruler }),
            Units = saved.Units.Select(u => u with { Condition = UnitCondition.Confused, ConditionDaysRemaining = 100 }).ToImmutableArray()
                .Add(new(1, 2, new(19, 2), 10000) { FormationId = 1, Morale = 100,
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(21, BattleRank.Front),
                    Order = new(MilitaryOrderKind.AttackSettlement, null, 8000) }),
            Simulation = saved.Simulation with { NextUnitId = 2 }
        }), rules);
        var events = Advance(engine).Events;
        var facility = engine.Snapshot.Settlements.Single(s => s.Id == 8000);
        Assert.False(facility.IsDestroyed);
        Assert.Equal(saved.Settlements.Single(s => s.Id == 8000).Durability, facility.Durability);
        Assert.DoesNotContain(events.OfType<SettlementCapturedEvent>(), e => e.SettlementId == 8000);
        Assert.DoesNotContain(events.OfType<BattleDamageEvent>(), e => e.SettlementId == 8000);
        Assert.NotNull(facility.Facility!.BuilderUnitId);
        Assert.Null(engine.Snapshot.Units.Single(u => u.Id == 1).Order);
        var controlled = engine.CaptureStableState();
        engine = Engine(WorldState.Restore(controlled with
        {
            Simulation = controlled.Simulation with { Controls = [new("player", 2, ControllerKind.Player)] }
        }), rules);
        string before = engine.Snapshot.StateHash;
        var rejected = engine.Execute(Player, new OrderUnitCommand(Guid.NewGuid(), 2,
            engine.Snapshot.Simulation.NextSequence, 1, new(MilitaryOrderKind.AttackSettlement, null, 8000)));
        Assert.False(rejected.Submission.Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletedWorkOnlyDisappearsAtZeroDurabilityWithoutCaptureBenefits(bool attacksWalls)
    {
        var rules = CreateMilitary(hostsPeople: false, attacksWalls: attacksWalls);
        var engine = Engine(military: rules);
        Assert.True(engine.Execute(Player, Command()).Submission.Accepted);
        for (int i = 0; i < 4; i++) Advance(engine);
        Assert.True(engine.Execute(Player, new DemolishFacilityCommand(Guid.NewGuid(), 1,
            engine.Snapshot.Simulation.NextSequence, 8000)).Submission.Accepted);
        Assert.True(engine.Snapshot.Settlements.Single(s => s.Id == 8000).IsDestroyed);
        Assert.True(engine.Execute(Player, Command(engine.Snapshot.Simulation.NextSequence)).Submission.Accepted);
        for (int i = 0; i < 4; i++) Advance(engine);
        Assert.False(engine.Snapshot.Settlements.Single(s => s.Id == 8000).IsDestroyed);
        var saved = engine.CaptureStableState();
        engine = Engine(WorldState.Restore(saved with
        {
            Forces = saved.Forces.ToImmutableArray().Add(new(2, 10000, 1000000, 100) { LeaderId = 21 }),
            Settlements = saved.Settlements.ToImmutableArray().Add(new(2, 2, 0, 600, 600, 100, 0, 0, 100, 0, 100)),
            Officers = saved.Officers.ToImmutableArray().Add(new(21, 2, null, new(100, 100, 100, 100), false)
                { UnitId = 1, Status = OfficerStatus.Ruler }),
            Units = [new(1, 2, new(19, 2), 10000) { FormationId = 1, Morale = 50,
                MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(21, BattleRank.Front),
                Order = new(MilitaryOrderKind.AttackSettlement, null, 8000) }],
            Simulation = saved.Simulation with { NextUnitId = 2 }
        }), rules);
        var events = new List<DomainEvent>();
        for (int i = 0; i < 3; i++) events.AddRange(Advance(engine).Events);
        var facility = engine.Snapshot.Settlements.Single(s => s.Id == 8000);
        Assert.Equal(attacksWalls, facility.IsDestroyed);
        Assert.Equal(attacksWalls ? 0 : 600, facility.Durability);
        Assert.Equal(attacksWalls ? null : (int?)1, facility.OwnerId);
        Assert.Equal(attacksWalls ? 1 : 0, events.OfType<FacilityDestroyedEvent>().Count());
        Assert.Empty(events.OfType<SettlementCapturedEvent>());
        Assert.DoesNotContain(events.OfType<ResourceChangedEvent>(), e => e.Reason == "settlement-captured");
        Assert.Equal(50 - events.OfType<PeriodicMoraleLostEvent>().Sum(e => e.PreviousMorale - e.Morale), engine.Snapshot.Units.Single().Morale);
        Assert.Equal(10000u, engine.Snapshot.Forces.Single(f => f.Id == 2).Money);
        Assert.Equal(8000u, engine.Snapshot.Forces.Single(f => f.Id == 1).Money);
        var restored = Engine(WorldState.Restore(engine.CaptureStableState()), rules);
        Assert.Equal(CanonicalJson.Text(Advance(engine).Events), CanonicalJson.Text(Advance(restored).Events));
    }

    [Fact]
    public void DefeatingBuilderRemovesItsSiteOnce()
    {
        var engine = Engine();
        Assert.True(engine.Execute(Player, Command()).Submission.Accepted);
        Advance(engine);
        var saved = engine.CaptureStableState();
        engine = Engine(WorldState.Restore(saved with
        {
            Forces = saved.Forces.ToImmutableArray().Add(new(2, 10000, 1000000, 100) { LeaderId = 21 }),
            Settlements = saved.Settlements.ToImmutableArray().Add(new(2, 2, 0, 600, 600, 100, 0, 0, 100, 0, 100)),
            Officers = saved.Officers.ToImmutableArray().Add(new(21, 2, null, new(100, 100, 100, 100), false)
                { UnitId = 1, Status = OfficerStatus.Ruler }),
            Units = saved.Units.Select(u => u with { Soldiers = 1, Condition = UnitCondition.Confused, ConditionDaysRemaining = 100 }).ToImmutableArray()
                .Add(new(1, 2, new(12, 2), 10000) { FormationId = 1, Morale = 100,
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(21, BattleRank.Front),
                    Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 0 } }),
            Simulation = saved.Simulation with { NextUnitId = 2 }
        }));
        var events = Advance(engine).Events;
        Assert.DoesNotContain(engine.Snapshot.Units, u => u.Id == 0);
        Assert.Single(events.OfType<FacilityConstructionChangedEvent>(), e => e.Phase == "cancelled");
        Assert.DoesNotContain(events.OfType<FacilityConstructionChangedEvent>(), e => e.Phase == "completed");
        Assert.Empty(events.OfType<SettlementCapturedEvent>());
        var site = engine.Snapshot.Settlements.Single(s => s.Id == 8000);
        Assert.True(site.IsDestroyed);
        Assert.Null(site.Facility!.BuilderUnitId);
        Assert.DoesNotContain(Advance(engine).Events.OfType<FacilityConstructionChangedEvent>(), e => e.Phase == "cancelled");
    }
}
