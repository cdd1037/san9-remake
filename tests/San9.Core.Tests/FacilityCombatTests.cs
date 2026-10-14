using San9.Tests;
using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class FacilityCombatTests
{
    private static readonly CommandContext Player = new("player");

    private static SimulationEngine Engine(TacticEffect effect = TacticEffect.Martial, bool attacking = false,
        int injury = 0, int cooldown = 0, bool requiresEnabled = false, bool enabled = true, uint targetSoldiers = 10000,
        int confusion = 0, Action<MilitaryRulebook>? captureRules = null, bool area = false, int? vulnerableCategory = null)
    {
        bool support = effect is TacticEffect.Heal or TacticEffect.Inspire;
        var profile = new CombatProfile(0, 0, 0, 1000, 50);
        var military = new MilitaryRulebook(new(4, 1, [0, 0, 0, 0], [new(0, 1, null)],
            [new(700, 5, 0, 100000, 1, new(1, 1), TravelMode.Land)
                { Combat = profile, Attack = new(1, false), CaptureRanks = [BattleRank.Middle],
                    MartialTacticVulnerabilities = vulnerableCategory is int weak ? [weak] : [] }],
            [new(1, new(7, 2), new(1, 1)) { FacilityTypeId = 8000, Combat = profile, Attack = new(1, false), EnabledTacticIds = enabled ? [9000] : [] },
                new(2, new(17, 2), new(1, 1)) { FacilityTypeId = 8001, Combat = profile },
                ..(area ? new BattlefieldPlace[] { new(3, new(9, 2), new(1, 1)) { FacilityTypeId = 8001, Combat = profile } } : [])])
        {
            Facilities = [new(8000, 0, 100, new(1, 1), 0, false, true, true, 100000, null, null, null, [])
                { Combat = profile, Attack = new(1, false), Tactics = new(100, 3, requiresEnabled, [9000]) },
                new(8001, 0, 100, new(1, 1), 0, false, true, true, 100000, null, null, null, []) { Combat = profile }],
            Tactics = [new(9000, effect == TacticEffect.Martial ? 3 : 7, effect, effect == TacticEffect.Martial ? 1 : effect == TacticEffect.Taunt ? 0 : 10,
                15, 0, true, true, []) { EffectRadius = support || area ? 3 : 0 },
                new(9100, 7, TacticEffect.Trap, 10, 9, 0, false, false, [])],
            BlockedCells = [new(2, 0)],
            Elevations = [0, 0, 0, 0], TrapTerrains = [new(0, effect == TacticEffect.TrapSelection ? 9100 : null, false)]
        });
        var state = WorldState.Create(new(190, 1, 1), 42,
            [new(1, 100) { LeaderId = 11 }, new(2, 100) { LeaderId = 22 }],
            [new(1, 1, 10000, 100, 100, effect == TacticEffect.Inspire ? 79 : 100, 0, 0, 100, 0, 100) { ConfusionDaysRemaining = confusion },
                new(2, 2, 10000, 100, 100, 100, 0, 0, 100, 0, 100),
                ..(area ? new SettlementState[] { new(3, 2, 1, 100, 100, 100, 0, 0, 100, 0, 100) } : [])],
            [new(11, 1, 1, new(200, 200, 100, 100), false) { Status = OfficerStatus.Ruler, IsCommander = true, Injury = injury, TacticCooldown = cooldown },
                new(12, 1, 1, new(200, 200, 100, 200), false) { LearnedTactics = [9000], EquippedTacticId = 9000 },
                new(21, support ? 1 : 2, null, new(0, 0, 0, 0), false) { UnitId = 1, Injury = support ? 3 : 0 },
                new(22, 2, 2, new(70, 70, 70, 70), false) { Status = OfficerStatus.Ruler }],
            units: [new(1, support ? 1 : 2, new(8, 2), targetSoldiers) { FormationId = 700, Morale = support ? 79 : 100,
                CargoMoney = 50, CargoFood = 75,
                Wounded = support ? 100u : 40u, ServicePlaceId = 2, MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(21, BattleRank.Middle),
                Condition = support ? UnitCondition.Confused : UnitCondition.Normal, ConditionDaysRemaining = support ? 100 : 0 }],
            controls: [new("player", attacking ? 2 : 1, ControllerKind.Player)], corps: [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 100000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 22, new(10000, 100000)) { IsRoot = true }]);
        var engine = new SimulationEngine(state, EconomyRulebook.Empty, PersonnelRulebook.Empty, TreasureRulebook.Empty, military: military);
        if (attacking)
            Assert.True(engine.Execute(Player, new OrderUnitCommand(Guid.NewGuid(), 2, 0, 1,
                new(MilitaryOrderKind.AttackSettlement, null, 1))).Submission.Accepted);
        captureRules?.Invoke(military);
        return engine;
    }

    private static AdvanceXunResult Advance(SimulationEngine engine) { engine.EndControl(Player); return engine.AdvanceXun(); }

    [Theory]
    [InlineData(3, 15)]
    [InlineData(0, 10)]
    public void FacilityUsesTargetWeaknessForAuthoredCategory(int category, int expected)
    {
        var first = Advance(Engine(vulnerableCategory: category)).Events.OfType<TacticActivatedEvent>().First();
        Assert.True(first.SourceIsSettlement);
        Assert.Equal(expected, first.Effect);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CommanderFiresAloneWithThreeDayCooldownForAttackAndRetaliation(bool attacking)
    {
        var engine = Engine(attacking: attacking);
        var result = Advance(engine);
        var activations = result.Events.OfType<TacticActivatedEvent>().ToArray();
        Assert.Equal([2, 5, 8, 11], activations.Select(e => e.Clock.Day).ToArray());
        Assert.All(activations, e => { Assert.True(e.SourceIsSettlement); Assert.Equal(1, e.SourceId); Assert.Equal(11, Assert.Single(e.OfficerIds)); });
        Assert.Null(engine.Snapshot.Officers.Single(p => p.Id == 11).EquippedTacticId);
        Assert.True(engine.Snapshot.Units.Single().Soldiers < 10000);
        Assert.Equal(10, result.Events.OfType<BattleDamageEvent>().Count(e => e.AttackerIsSettlement && e.SoldierLoss == 0));
    }

    [Theory]
    [InlineData(10, 0, 0)]
    [InlineData(0, 100, 0)]
    [InlineData(0, 0, 100)]
    public void IneligibleCommanderOrConfusedSettlementCannotBeReplacedByReadyOfficer(int injury, int cooldown, int confusion)
    {
        var result = Advance(Engine(injury: injury, cooldown: cooldown, confusion: confusion));
        Assert.Empty(result.Events.OfType<TacticActivatedEvent>());
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    public void FixedAndEnabledProfilesHaveExplicitAvailability(bool required, bool enabled, bool fires)
    {
        var result = Advance(Engine(requiresEnabled: required, enabled: enabled));
        Assert.Equal(fires, result.Events.OfType<TacticActivatedEvent>().Any());
    }

    [Theory]
    [InlineData(TacticEffect.Confusion)]
    [InlineData(TacticEffect.Defection)]
    [InlineData(TacticEffect.Illusion)]
    [InlineData(TacticEffect.Sorcery)]
    [InlineData(TacticEffect.Taunt)]
    public void FacilityIntelligenceEffectsUseTheCommonStateAndSourceIdentity(TacticEffect effect)
    {
        var engine = Engine(effect);
        var result = Advance(engine);
        var resolved = Assert.Single(result.Events.OfType<IntelligenceEffectResolvedEvent>());
        Assert.Equal(new BattleSource(1, true), resolved.Source);
        Assert.Equal(10, resolved.Effect);
        if (effect == TacticEffect.Confusion) Assert.True(engine.Snapshot.Units.Single().ConditionDaysRemaining > 0);
        else if (effect == TacticEffect.Taunt) Assert.True(engine.Snapshot.Units.Single().Morale < 100);
        else Assert.True(engine.Snapshot.Units.Single().Soldiers < 10000);
        if (effect == TacticEffect.Defection) Assert.True(engine.Snapshot.Settlements.Single(s => s.Id == 1).Soldiers > 10000);
    }

    [Theory]
    [InlineData(TacticEffect.Heal)]
    [InlineData(TacticEffect.Inspire)]
    public void FacilitySupportNeedsNoEnemyAndPreservesSourceIdentity(TacticEffect effect)
    {
        var engine = Engine(effect);
        var result = Advance(engine);
        Assert.Single(result.Events.OfType<TacticActivatedEvent>());
        if (effect == TacticEffect.Heal)
        {
            Assert.Equal(new BattleSource(1, true), Assert.Single(result.Events.OfType<TacticHealingEvent>()).Source);
            Assert.Equal(UnitCondition.Normal, engine.Snapshot.Units.Single().Condition);
            Assert.Equal(0, engine.Snapshot.Officers.Single(p => p.Id == 21).Injury);
        }
        else
        {
            var change = Assert.Single(result.Events.OfType<TacticMoraleChangedEvent>());
            Assert.True(change.TargetIsSettlement);
            Assert.Equal(1, change.TargetId);
            Assert.Equal(100, engine.Snapshot.Settlements.Single(s => s.Id == 1).Morale);
            Assert.DoesNotContain(result.Events.OfType<TacticMoraleChangedEvent>(), e => !e.TargetIsSettlement);
        }
    }

    [Fact]
    public void FacilityVictoryWithMatchingUnitAndSettlementIdsReceivesPrisonerWoundedAndCargoOnce()
    {
        var engine = Engine(targetSoldiers: 1);
        var result = Advance(engine);
        var ended = Assert.Single(result.Events.OfType<UnitTerminatedEvent>());
        Assert.Equal(new BattleSource(1, true), ended.Winner);
        Assert.Empty(engine.Snapshot.Units);
        var prisoner = engine.Snapshot.Officers.Single(p => p.Id == 21);
        Assert.Equal(OfficerStatus.Captive, prisoner.Status);
        Assert.Equal(1, prisoner.SettlementId);
        Assert.Null(prisoner.UnitId);
        Assert.True(engine.Snapshot.Settlements.Single(s => s.Id == 1).Wounded > 0);
        Assert.Contains(result.Events.OfType<ResourceChangedEvent>(), e => e.Reason == "defeated-cargo" &&
            e.Account == new ResourceAccount(ResourceOwnerKind.Corps, 1) && e.Resource == ResourceKind.Money && e.Actual == 50);
        Assert.Contains(result.Events.OfType<ResourceChangedEvent>(), e => e.Reason == "defeated-cargo" &&
            e.Account == new ResourceAccount(ResourceOwnerKind.Corps, 1) && e.Resource == ResourceKind.Food && e.Actual == 75);
        Assert.DoesNotContain(Advance(engine).Events.OfType<UnitTerminatedEvent>(), e => e.UnitId == 1);
    }

    [Fact]
    public void FacilityTrapUsesConcreteTerrainEffectCooldownAndSelectionGrowth()
    {
        var engine = Engine(TacticEffect.TrapSelection);
        var result = Advance(engine);
        var activations = result.Events.OfType<TacticActivatedEvent>().ToArray();
        Assert.Equal([2, 11], activations.Select(e => e.Clock.Day).ToArray());
        Assert.All(activations, e => Assert.Equal(9100, e.TacticId));
        Assert.True(engine.Snapshot.Units.Single().Soldiers < 10000);
        Assert.DoesNotContain(9100, engine.Snapshot.Officers.Single(p => p.Id == 11).LearnedTactics);
        Assert.Equal(6, engine.Snapshot.Officers.Single(p => p.Id == 11).TacticProficiencies[7]);
    }

    [Theory]
    [InlineData(TacticEffect.Illusion)]
    [InlineData(TacticEffect.Sorcery)]
    public void FacilityAreaVictoryUsesTheSameSettlementOccupationAndCleanup(TacticEffect effect)
    {
        var engine = Engine(effect, area: true);
        var result = Advance(engine);
        var capture = Assert.Single(result.Events.OfType<SettlementCapturedEvent>());
        Assert.Equal(3, capture.SettlementId);
        Assert.Equal(new BattleSource(1, true), capture.Winner);
        Assert.Equal(1, engine.Snapshot.Settlements.Single(s => s.Id == 3).OwnerId);
        Assert.Contains(result.Events.OfType<IntelligenceEffectResolvedEvent>(), e => e.TargetIsSettlement && e.TargetId == 3 && e.Effect == 10);
    }

    [Fact]
    public void RestorePreservesFacilityCooldownAndAllSubsequentEffects()
    {
        MilitaryRulebook? rules = null;
        var engine = Engine(captureRules: r => rules = r);
        Advance(engine);
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), EconomyRulebook.Empty,
            PersonnelRulebook.Empty, TreasureRulebook.Empty, military: rules);
        EventAssert.Equal(Advance(engine).Events, Advance(restored).Events);
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
    }
    [Theory]
    [InlineData(12, 0, true)]
    [InlineData(12, 100, false)]
    [InlineData(null, 0, false)]
    public void TacticReadsAppointedCommanderWithoutSelectingTheHigherPriorityRuler(int? appointed, int cooldown, bool fires)
    {
        MilitaryRulebook military = null!;
        var engine = Engine(captureRules: rules => military = rules);
        var snapshot = engine.Snapshot with { Officers = engine.Snapshot.Officers.Select(p => p with
            { IsCommander = p.Id == appointed, TacticCooldown = p.Id == appointed ? cooldown : 0 }).ToArray() };
        var mutation = WorldState.Restore(snapshot).BeginMutation();
        List<DomainEvent> events = [];
        FacilityCombat.TryTactic(mutation, 1, new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 },
            new SimulationRules { Military = military }, events);
        var activations = events.OfType<TacticActivatedEvent>().ToArray();
        if (fires) Assert.Equal(appointed, Assert.Single(Assert.Single(activations).OfficerIds));
        else Assert.Empty(activations);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("removed")]
    [InlineData("range")]
    [InlineData("friendly")]
    [InlineData("disabled")]
    [InlineData("support-empty")]
    public void UnavailableTacticOrTargetDoesNotConsumeOpportunityOrCooldown(string reason)
    {
        MilitaryRulebook military = null!;
        var engine = Engine(effect: reason == "support-empty" ? TacticEffect.Inspire : TacticEffect.Martial,
            requiresEnabled: reason == "disabled", enabled: reason != "disabled", captureRules: rules => military = rules);
        var mutation = WorldState.Restore(engine.Snapshot).BeginMutation();
        if (reason == "removed") mutation.RemoveUnit(1);
        if (reason == "range") mutation.Replace(mutation.Units[1] with { Position = new(12, 2) });
        if (reason == "friendly") mutation.Replace(mutation.Units[1] with { OwnerId = 1 });
        if (reason == "support-empty") mutation.Replace(mutation.Settlements[1] with { Morale = 100 });
        var randomBefore = mutation.Simulation.RandomStreams;
        var officersBefore = mutation.Officers;
        List<DomainEvent> events = [];
        FacilityCombat.TryTactic(mutation, 1, reason is "missing" or "support-empty" ? null :
            new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 }, new() { Military = military }, events);
        Assert.Empty(events);
        Assert.Equal(randomBefore, mutation.Simulation.RandomStreams);
        Assert.Same(officersBefore, mutation.Officers);
    }
}
