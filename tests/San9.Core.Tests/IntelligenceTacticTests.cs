using San9.Tests;
using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed partial class IntelligenceTacticTests
{
    private static readonly CommandContext Player = new("player");
    private static SimulationEngine Engine(TacticEffect effect, bool settlement = false, bool counter = false,
        bool bypass = false, int formationResistance = 0, bool area = false, uint wounded = 500, Action<MilitaryRulebook>? captureRules = null,
        int? trap = null, int existingConfusion = 0, int? localCity = 2, int? targetHome = 2,
        bool unstaffed = false, bool areaSettlement = false)
    {
        bool support = effect is TacticEffect.Heal or TacticEffect.Inspire;
        int targetOwner = support ? 1 : 2;
        var combat = new CombatProfile(0, 0, 0, 1000, 50);
        int power = effect switch { TacticEffect.Heal => 19, TacticEffect.Sorcery => 14, TacticEffect.Illusion => 13, TacticEffect.Taunt => 0, _ => 10 };
        var rules = new MilitaryRulebook(new(6, 1, [0, 0, 0, 0, 0, 0], [new(0, 1, null)],
            [new(100, 5, 0, 100000, 1, new(1, 1), TravelMode.Land) { Combat = combat, Attack = new(1, false),
                CaptureRanks = [BattleRank.Front], Tactics = new(100, 2, [], []) },
             new(200, 5, 0, 100000, 1, new(1, 1), TravelMode.Land) { Combat = combat, CaptureRanks = [BattleRank.Middle],
                Tactics = null, BypassesPassiveCounters = bypass, ConfusionResistanceChance = formationResistance }],
            [new(1, new(2, 2), new(1, 1)) { Combat = combat }, new(2, new(17, 2), new(1, 1)) { Combat = combat },
             new(3, new(areaSettlement ? 9 : 27, 2), new(1, 1)) { Combat = combat }])
        {
            RegionIds = Enumerable.Repeat(localCity is int city ? (int?)(city + 100) : null, 6).ToImmutableArray(),
            Tactics = [new(9000, 7, effect, power, 15, 0, true, true, [100]) { EffectRadius = support || area ? 3 : 0 },
            new(9100, 6, effect == TacticEffect.TrapSelection ? TacticEffect.CounterTrap : TacticEffect.CounterConfusion, 0, 0, 0, true, false, []),
            new(9200, 7, TacticEffect.Trap, 10, 9, 0, false, true, [100])],
            Elevations = [0, 0, 0, 0, 0, 0],
            TrapTerrains = [new(0, trap, false)]
        });
        List<OfficerState> officers = [new(11, 1, null, new(70, 200, 70, 70), false) { UnitId = 0, LearnedTactics = [9000],
                TacticProficiencies = [0, 0, 0, 0, 0, 0, 0, 500, 0] },
            new(21, targetOwner, settlement ? 2 : null, new(70, 0, 70, 70), false) { UnitId = settlement ? null : 1,
                LearnedTactics = counter ? [9100] : [], EquippedTacticId = counter ? 9100 : null, Injury = effect == TacticEffect.Heal ? 7 : 0 }];
        List<UnitState> units = [new(0, 1, new(settlement ? 16 : 6, 2), 99900) { FormationId = 100, Morale = effect == TacticEffect.Inspire ? 79 : 100, ServicePlaceId = 1,
            MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(11, BattleRank.Front) }];
        if (!settlement) units.Add(new(1, targetOwner, new(7, 2), support ? 99950u : 10000u)
        {
            FormationId = 200,
            Morale = effect == TacticEffect.Taunt ? 7 : effect == TacticEffect.Inspire ? 79 : 100,
            ServicePlaceId = targetHome,
            Wounded = effect == TacticEffect.Heal ? wounded : 0,
            Condition = effect == TacticEffect.Heal || existingConfusion > 0 ? UnitCondition.Confused : UnitCondition.Normal,
            ConditionDaysRemaining = effect == TacticEffect.Heal ? 100 : existingConfusion,
            MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(21, BattleRank.Middle)
        });
        List<ForceState> forces = [new(1, 100) { LeaderId = 11 }];
        if (!support) forces.Add(new(2, 100) { LeaderId = 21 });
        List<SettlementState> settlements = [new(1, 1, 1000, 100, 100, 100, 0, 0, 100, 0, 100),
            new(2, targetOwner, 10000, 100, 100, 100, 0, 0, 100, 0, 100) { ConfusionDaysRemaining = existingConfusion }];
        if (area)
        {
            forces.Add(new(3, 100) { LeaderId = 31 });
            officers.Add(new(31, 3, null, new(70, 0, 70, 70), false) { UnitId = 2 });
            units.Add(new(2, 3, new(9, 2), 10000)
            {
                FormationId = 200,
                Morale = 100,
                ServicePlaceId = 3,
                MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(31, BattleRank.Middle)
            });
            units.Add(new(3, 1, new(8, 3), 10000) { FormationId = 200, Morale = 100 });
            settlements.Add(new(3, 3, 1000, 100, 100, 100, 0, 0, 100, 0, 100));
        }
        if (unstaffed)
        {
            settlements.Add(new(3, targetOwner, 1000, 100, 100, 100, 0, 0, 100, 0, 100));
            officers[1] = officers[1] with { SettlementId = 3, UnitId = null };
        }
        if (areaSettlement)
        {
            units.RemoveAll(u => u.Id == 2);
            officers.RemoveAll(p => p.Id == 31);
            forces[2] = forces[2] with { LeaderId = null };
        }
        var engine = new SimulationEngine(WorldState.Create(new(190, 1, 1), 42, forces, settlements, officers, units: units,
            controls: [new("player", 1, ControllerKind.Player)], corps: forces.Select(f => new CorpsState(f.Id, f.Id, f.LeaderId, new(10000, 10000)) { IsRoot = true })), EconomyRulebook.Empty, new PersonnelRulebook([new(1, new(2, 2)), new(2, new(17, 2))],
                new Dictionary<int, int> { [101] = 1, [102] = 2 }), TreasureRulebook.Empty, military: rules);
        captureRules?.Invoke(rules);
        Assert.True(engine.Execute(Player, new SelectTacticCommand(Guid.NewGuid(), 1, 0, 11, 9000)).Submission.Accepted);
        if (!support)
        {
            var order = settlement ? new MilitaryOrder(MilitaryOrderKind.AttackSettlement, null, 2) :
                new MilitaryOrder(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 };
            Assert.True(engine.Execute(Player, new OrderUnitCommand(Guid.NewGuid(), 1, 1, 0, order)).Submission.Accepted);
        }
        engine.EndControl(Player);
        return engine;
    }

    [Theory]
    [InlineData(TacticEffect.Confusion)]
    [InlineData(TacticEffect.Defection)]
    [InlineData(TacticEffect.Illusion)]
    [InlineData(TacticEffect.Taunt)]
    [InlineData(TacticEffect.Sorcery)]
    public void UnstaffedGarrisonIsNotImmuneToIntelligenceTactics(TacticEffect effect)
    {
        var engine = Engine(effect, settlement: true, unstaffed: true, counter: true);
        Assert.DoesNotContain(engine.Snapshot.Officers, p => p.SettlementId == 2);
        var result = engine.AdvanceXun();
        Assert.Contains(result.Events.OfType<IntelligenceEffectResolvedEvent>(),
            e => e.TargetIsSettlement && e.TargetId == 2 && e.Effect == 10);
        var target = engine.Snapshot.Settlements.Single(s => s.Id == 2);
        if (effect == TacticEffect.Confusion) Assert.True(target.ConfusionDaysRemaining > 0);
        else if (effect == TacticEffect.Taunt) Assert.True(target.Morale < 100);
        else Assert.True(target.Soldiers < 10000);
    }

    [Theory]
    [InlineData(TacticEffect.Illusion)]
    [InlineData(TacticEffect.Sorcery)]
    public void UnstaffedGarrisonReceivesAreaEffectsFromAUnitPrimaryTarget(TacticEffect effect)
    {
        var engine = Engine(effect, area: true, areaSettlement: true);
        Assert.DoesNotContain(engine.Snapshot.Officers, p => p.SettlementId == 3);
        var result = engine.AdvanceXun();
        Assert.Contains(result.Events.OfType<IntelligenceEffectResolvedEvent>(),
            e => e.TargetIsSettlement && e.TargetId == 3 && e.Effect == 10);
        Assert.True(engine.Snapshot.Settlements.Single(s => s.Id == 3).Soldiers < 1000);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedConfusionCannotShortenTheExistingDuration(bool settlement)
    {
        var engine = Engine(TacticEffect.Confusion, settlement: settlement, existingConfusion: 100);
        var result = engine.AdvanceXun();
        Assert.Single(result.Events.OfType<TacticActivatedEvent>());
        Assert.Equal(90, settlement ? engine.Snapshot.Settlements.Single(s => s.Id == 2).ConfusionDaysRemaining :
            engine.Snapshot.Units.Single(u => u.Id == 1).ConditionDaysRemaining);
    }

    [Theory]
    [InlineData(false, false, 9200, true)]
    [InlineData(true, false, 9200, true)]
    [InlineData(false, true, 9200, false)]
    [InlineData(false, false, null, false)]
    public void TrapSelectionUsesTargetMappingAndConcreteCooldownWithoutLearningTheConcreteTrap(bool settlement, bool counter, int? trap, bool fires)
    {
        var engine = Engine(TacticEffect.TrapSelection, settlement: settlement, counter: counter, trap: trap);
        var result = engine.AdvanceXun();
        var activations = result.Events.OfType<TacticActivatedEvent>().ToArray();
        Assert.Equal(fires, activations.Length > 0);
        Assert.All(activations, e => Assert.Equal(9200, e.TacticId));
        var officer = engine.Snapshot.Officers.Single(p => p.Id == 11);
        Assert.Equal(9000, officer.EquippedTacticId);
        Assert.DoesNotContain(9200, officer.LearnedTactics);
        if (fires) Assert.Equal(Math.Max(0, 9 - (engine.Snapshot.Clock.Day - activations[^1].Clock.Day)), officer.TacticCooldown);
        else Assert.Equal(0, officer.TacticCooldown);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfusionHasAuthoritativeDurationAndExpiresWithoutRepeatingTheOrder(bool settlement)
    {
        var engine = Engine(TacticEffect.Confusion, settlement: settlement);
        var result = engine.AdvanceXun();
        Assert.Single(result.Events.OfType<TacticActivatedEvent>());
        Assert.Contains(result.Events.OfType<BattleConditionChangedEvent>(), e => e.Condition == UnitCondition.Confused);
        Assert.True(settlement ? engine.Snapshot.Settlements.Single(s => s.Id == 2).ConfusionDaysRemaining > 0 :
            engine.Snapshot.Units.Single(u => u.Id == 1).ConditionDaysRemaining > 0);
        Assert.True(engine.Execute(Player, new SelectTacticCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence, 11, null)).Submission.Accepted);
        for (int i = 0; i < 2; i++) { engine.EndControl(Player); engine.AdvanceXun(); }
        Assert.Equal(0, settlement ? engine.Snapshot.Settlements.Single(s => s.Id == 2).ConfusionDaysRemaining :
            engine.Snapshot.Units.Single(u => u.Id == 1).ConditionDaysRemaining);
        if (!settlement) Assert.Equal(UnitCondition.Normal, engine.Snapshot.Units.Single(u => u.Id == 1).Condition);
    }

    [Theory]
    [InlineData(true, false, 0, false)]
    [InlineData(true, true, 0, true)]
    [InlineData(false, false, 100, false)]
    public void EquippedCounterAndFormationResistanceHaveDistinctBypassRules(bool counter, bool bypass, int resistance, bool succeeds)
    {
        var engine = Engine(TacticEffect.Confusion, counter: counter, bypass: bypass, formationResistance: resistance);
        var result = engine.AdvanceXun();
        var events = result.Events.OfType<TacticActivatedEvent>().ToArray();
        Assert.Equal(succeeds, events.Length > 0);
        if (!succeeds)
        {
            Assert.Equal(0, engine.Snapshot.Officers.Single(p => p.Id == 11).TacticCooldown);
            var attempt = Assert.IsType<IntelligenceEffectResolvedEvent>(result.Events.First(e => e is IntelligenceEffectResolvedEvent));
            Assert.True(attempt.IsAttempt); Assert.Equal(0, attempt.Effect); Assert.Equal(11, attempt.ActorId);
            Assert.Equal(counter ? 21 : (int?)null, attempt.Occurrence!.CounterOfficerId);
            Assert.Equal(1, attempt.Occurrence.Target.Id);
        }
    }

    [Theory]
    [InlineData(false, 2, 1, 2)]
    [InlineData(false, 1, 2, 1)]
    [InlineData(false, null, 2, 2)]
    [InlineData(false, null, null, null)]
    [InlineData(true, 1, 1, 2)]
    public void DefectionAccountsForEveryTransferredSoldier(bool settlement, int? localCity, int? targetHome, int? receivingCity)
    {
        var engine = Engine(TacticEffect.Defection, settlement: settlement, localCity: localCity, targetHome: targetHome);
        var result = engine.AdvanceXun();
        var added = Assert.Single(result.Events.OfType<ResourceChangedEvent>(), e => e.Reason == "tactic-defection" && e.Actual > 0);
        var removed = Assert.Single(result.Events.OfType<ResourceChangedEvent>(), e => e.Reason == "tactic-defection" && e.Actual < 0);
        Assert.Equal(100, added.Actual);
        var returned = result.Events.OfType<ResourceChangedEvent>().Where(e => e.Reason == "tactic-defection-demobilized").ToArray();
        if (receivingCity is int city)
        {
            var returnedEvent = Assert.Single(returned);
            Assert.Equal(city, returnedEvent.Account.Id);
            Assert.True(returnedEvent.Actual > 0);
            Assert.Equal((uint)returnedEvent.Actual, engine.Snapshot.Settlements.Single(s => s.Id == city).DraftPopulation);
        }
        else Assert.Empty(returned);
        Assert.Equal(-removed.Actual, added.Actual + returned.Sum(e => e.Actual));
        Assert.Equal(0, added.SaturationLoss);
        Assert.Equal(100000u, engine.Snapshot.Units.Single(u => u.Id == 0).Soldiers);
        Assert.Equal(0u, engine.Snapshot.Units.Single(u => u.Id == 0).Wounded);
    }

    [Fact]
    public void TauntAtZeroMoraleUsesTheSameUnitTerminationChain()
    {
        var result = Engine(TacticEffect.Taunt).AdvanceXun();
        Assert.Contains(result.Events.OfType<TacticMoraleChangedEvent>(), e => e.Current == 0);
        Assert.Single(result.Events.OfType<UnitTerminatedEvent>());
    }

    [Theory]
    [InlineData(TacticEffect.Illusion)]
    [InlineData(TacticEffect.Sorcery)]
    public void AreaDamageIncludesThirdForceAndExcludesFriendlyUnits(TacticEffect effect)
    {
        var engine = Engine(effect, area: true);
        var result = engine.AdvanceXun();
        Assert.Contains(result.Events.OfType<IntelligenceEffectResolvedEvent>(), e => e.TargetId == 2 && !e.TargetIsSettlement && e.Effect == 10);
        Assert.True(engine.Snapshot.Units.Single(u => u.Id == 2).Soldiers < 10000);
        Assert.Equal(10000u, engine.Snapshot.Units.Single(u => u.Id == 3).Soldiers);
    }

    [Theory]
    [InlineData(500u, 50u)]
    [InlineData(0u, 0u)]
    public void HealingWithoutCombatRespectsCapacityAndClearsConditionAndInjury(uint wounded, uint expected)
    {
        var engine = Engine(TacticEffect.Heal, wounded: wounded);
        var result = engine.AdvanceXun();
        var healing = Assert.Single(result.Events.OfType<TacticHealingEvent>());
        Assert.Equal(expected, healing.RecoveredSoldiers);
        Assert.Contains(21, healing.HealedOfficers);
        var unit = engine.Snapshot.Units.Single(u => u.Id == 1);
        Assert.Equal(99950u + expected, unit.Soldiers);
        Assert.Equal(wounded - expected, unit.Wounded);
        Assert.Equal(UnitCondition.Normal, unit.Condition);
        Assert.Equal(0, unit.ConditionDaysRemaining);
        Assert.Equal(0, engine.Snapshot.Officers.Single(p => p.Id == 21).Injury);
    }

    [Fact]
    public void StableRestorePreservesTimersCooldownsAndSubsequentRandomEffects()
    {
        MilitaryRulebook? rules = null;
        var engine = Engine(TacticEffect.Confusion, captureRules: value => rules = value);
        engine.AdvanceXun();
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), EconomyRulebook.Empty,
            PersonnelRulebook.Empty, TreasureRulebook.Empty, military: rules);
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
        engine.EndControl(Player);
        restored.EndControl(Player);
        EventAssert.Equal(engine.AdvanceXun().Events, restored.AdvanceXun().Events);
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
    }

    [Fact]
    public void InspireRunsWithoutAnAttackAndStopsWhenMoraleIsNoLongerEligible()
    {
        var engine = Engine(TacticEffect.Inspire);
        var result = engine.AdvanceXun();
        Assert.Single(result.Events.OfType<TacticActivatedEvent>());
        Assert.Contains(result.Events.OfType<TacticMoraleChangedEvent>(), e => e.Previous == 78 && e.Current == 100);
        Assert.Equal(100 - result.Events.OfType<PeriodicMoraleLostEvent>().Where(e => e.UnitId == 0 && e.Clock.DayNumber > result.Events.OfType<TacticMoraleChangedEvent>().Single().Clock.DayNumber).Sum(e => e.PreviousMorale - e.Morale), engine.Snapshot.Units.Single(u => u.Id == 0).Morale);
    }
}
