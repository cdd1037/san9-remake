using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class TacticCombatTests
{

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComputerRetaliationScalesTheCombinedDamageExactlyOnce(bool tactics)
    {
        var baseline = Engine(counter: true, normalPower: true, opportunityChance: tactics ? 100 : 0).AdvanceXun().Events.OfType<BattleDamageEvent>().ToArray();
        var harder = Engine(counter: true, normalPower: true, opportunityChance: tactics ? 100 : 0, computerBonus: 100).AdvanceXun().Events.OfType<BattleDamageEvent>().ToArray();
        var outgoing = baseline.First(e => e.AttackerId == 0);
        var incoming = baseline.First(e => e.AttackerId == 1);
        Assert.True(incoming.SoldierLoss > 0);
        Assert.Equal(outgoing.SoldierLoss, harder.First(e => e.AttackerId == 0).SoldierLoss);
        var scaled = harder.First(e => e.AttackerId == 1);
        Assert.Equal(2 * incoming.SoldierLoss, scaled.SoldierLoss);
        Assert.Equal(scaled.SoldierLoss, scaled.AddedWounded + scaled.Dead);
    }

    private static readonly CommandContext Player = new("player");

    [Theory]
    [InlineData(2, 22UL, 2)] // Link stream rolls 49: below the ordinary 50% threshold.
    [InlineData(2, 29UL, 1)] // 50 is outside that threshold.
    [InlineData(3, 70UL, 2)] // Wedge threshold is 75%.
    [InlineData(3, 200UL, 1)]
    public void SameAffinityLinkUsesFormationHalfMultiplier(int multiplier, ulong seed, int participants)
    {
        var first = Engine(members: 2, seed: seed, linkMultiplier: multiplier).AdvanceXun()
            .Events.OfType<TacticActivatedEvent>().First();
        Assert.Equal(participants, first.OfficerIds.Length);
    }

    [Theory]
    [InlineData(false, 217UL, true)] // Opportunity roll 24 is below 15+10.
    [InlineData(false, 33UL, false)] // Roll 25 is outside that threshold.
    [InlineData(true, 217UL, false)] // Water formations do not receive the land bonus.
    public void OwnCityInfluenceAddsTenPointsToLandOpportunityOnly(bool water, ulong seed, bool expected)
    {
        bool FirstDay(SimulationEngine engine) => engine.AdvanceXun().Events.OfType<TacticActivatedEvent>().Any(e => e.Clock.Day == 2);
        Assert.Equal(expected, FirstDay(Engine(water: water, seed: seed, opportunityChance: 15, influence: new(8, 10))));
        Assert.False(FirstDay(Engine(water: water, seed: seed, opportunityChance: 15)));
    }

    private static SimulationEngine Engine(int members = 1, bool water = false, bool settlement = false,
        int power = 13, bool equipped = true, int injury = 0, int cooldown = 0, bool allowedFacility = true, bool counter = false,
        int primaryProficiency = 0, int linkedProficiency = 0, ulong seed = 42, int linkMultiplier = 2, TacticLinkRules? links = null,
        int? vulnerableCategory = null, int defenderProficiency = 0, CityInfluenceRule? influence = null, int opportunityChance = 100, int computerBonus = 0, bool normalPower = false, bool waterLearning = false, bool differentCommander = false, WorldSnapshot? restoredState = null)
    {
        var combat = normalPower ? new CombatProfile(10, 10, 0, 10, 50) : new CombatProfile(0, 0, 0, 1000, 50);
        var mode = water ? TravelMode.Water : TravelMode.Land;
        var rules = new MilitaryRulebook(new(4, 1, [0, 0, 0, 0], [new(0, 1, 1)],
            [new(100, 5, 0, 100000, 2, new(1, 1), mode) { Combat = combat, Attack = new(1, false),
                AutomaticTacticLearningId = waterLearning ? 9020 : null,
                CaptureRanks = [BattleRank.Front, BattleRank.Front], Tactics = new(opportunityChance, 0, linkMultiplier, [0], water ? [9000] : []) },
             new(200, 5, 0, 100000, 1, new(1, 1), mode) { Combat = combat, CaptureRanks = [BattleRank.Middle],
                MartialTacticVulnerabilities = vulnerableCategory is int weak ? [weak] : [],
                Attack = counter ? new(1, false) : null, Tactics = counter ? new(opportunityChance, 100, 2, [0], []) : null }],
            [new(1, new(2, 2), new(1, 1)) { Combat = combat }, new(2, new(17, 2), new(1, 1)) { Combat = combat }])
        { LinkRules = links ?? TacticLinkRules.Empty, CityInfluence = influence,
            Tactics = [new(9000, 0, TacticEffect.Martial, power, 9, 0, !water, allowedFacility, [100, 200]),
                new(9020, 4, TacticEffect.FormationTechnology, 0, 0, 0, false, false, [100])] });
        List<OfficerState> officers = [new(11, 1, null, new(80, 70, 70, 70), false)
            { UnitId = 0, LearnedTactics = equipped ? [9000] : [], EquippedTacticId = equipped ? 9000 : null,
                Injury = injury, TacticCooldown = cooldown,
                TacticProficiencies = [primaryProficiency, 0, 0, 0, waterLearning ? 1000 : 0, 0, 0, 0, 0] }];
        if (members == 2) officers.Add(new(12, 1, null, new(80, 70, 70, 70), false)
            { UnitId = 0, LearnedTactics = [9000], EquippedTacticId = 9000,
                TacticProficiencies = [linkedProficiency, 0, 0, 0, waterLearning ? 1000 : 0, 0, 0, 0, 0] });
        officers.Add(new(21, 2, settlement ? 2 : null, new(70, 70, 70, 70), false) { UnitId = settlement ? null : 1,
            TacticProficiencies = [defenderProficiency, 0, 0, 0, waterLearning ? 1000 : 0, 0, 0, 0, 0],
            LearnedTactics = counter ? [9000] : [], EquippedTacticId = counter ? 9000 : null });
        List<UnitState> units = [new(0, 1, new(settlement ? 16 : 6, 2), 100000) { FormationId = 100, Morale = 100, ServicePlaceId = 1,
            MemberRanks = officers.Where(p => p.UnitId == 0).ToImmutableDictionary(p => p.Id, _ => BattleRank.Front) }];
        if (!settlement) units.Add(new(1, 2, new(7, 2), 100000) { FormationId = 200, Morale = 100, ServicePlaceId = 2,
            MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(21, BattleRank.Middle) });
        if (differentCommander)
            units[0] = units[0] with { CommanderId = 12, };
        var world = WorldState.Create(new(190, 1, 1), seed,
            [new(1, 100) { LeaderId = 11 }, new(2, 100) { LeaderId = 21 }],
            [new(1, 1, 100, 100, 100, 100, 0, 0, 100, 0, 100), new(2, 2, 1000, 100, 100, 100, 0, 0, 100, 0, 100)],
            officers, units: units, controls: [new("player", 1, ControllerKind.Player)], computerDamageBonusPercent: computerBonus, corps: [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(10000, 10000)) { IsRoot = true }]);
        if (restoredState is not null) return new SimulationEngine(WorldState.Restore(restoredState), EconomyRulebook.Empty, PersonnelRulebook.Empty, TreasureRulebook.Empty, military: rules);
        var engine = new SimulationEngine(world, EconomyRulebook.Empty, PersonnelRulebook.Empty, TreasureRulebook.Empty, military: rules);
        var order = settlement ? new MilitaryOrder(MilitaryOrderKind.AttackSettlement, null, 2) :
            new MilitaryOrder(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 };
        Assert.True(engine.Execute(Player, new OrderUnitCommand(Guid.NewGuid(), 1, 0, 0, order)).Submission.Accepted);
        engine.EndControl(Player);
        return engine;
    }

    [Fact]
    public void RealAttackCreatesTacticsAtNineDayIntervalsAndGrowthUsesLiveState()
    {
        var engine = Engine();
        var result = engine.AdvanceXun();
        var activations = result.Events.OfType<TacticActivatedEvent>().ToArray();
        Assert.Equal(2, activations.Length);
        Assert.Equal(9, activations[1].Clock.DayNumber - activations[0].Clock.DayNumber);
        Assert.All(activations, e => Assert.Equal(new[] { 11 }, e.OfficerIds));
        Assert.Equal(6, engine.Snapshot.Officers.Single(p => p.Id == 11).TacticProficiencies[0]);
        Assert.Equal(9, engine.Snapshot.Officers.Single(p => p.Id == 11).TacticCooldown);
        Assert.True(engine.Snapshot.Units.Single(u => u.Id == 1).Wounded > 0);
        Assert.All(result.Events.OfType<BattleDamageEvent>(), e => Assert.Equal(e.SoldierLoss, e.AddedWounded + e.Dead));
        Assert.NotNull(System.Text.Json.JsonSerializer.Deserialize<DomainEvent>(System.Text.Json.JsonSerializer.Serialize<DomainEvent>(activations[0])));
    }

    [Fact]
    public void AuthoredRelationshipChangesActualLinkOutcome()
    {
        var unrelated = Engine(members: 2, seed: 29).AdvanceXun().Events.OfType<TacticActivatedEvent>().First();
        var related = Engine(members: 2, seed: 29, links: new(true, 20, [new(11, 12, 45, false)]))
            .AdvanceXun().Events.OfType<TacticActivatedEvent>().First();
        Assert.Single(unrelated.OfficerIds);
        Assert.Equal(2, related.OfficerIds.Length);
    }

    [Theory]
    [InlineData(null, 0, false, 10)]
    [InlineData(0, 0, false, 15)]
    [InlineData(null, 1000, false, 1)]
    [InlineData(0, 1000, false, 15)]
    [InlineData(1, 1000, false, 1)]
    [InlineData(0, 1000, true, 1)]
    public void TargetCategoryWeaknessPrecedesProficiencyResistance(int? category, int proficiency, bool settlement, int expected)
    {
        var engine = Engine(vulnerableCategory: category, defenderProficiency: proficiency, settlement: settlement);
        var first = engine.AdvanceXun().Events.OfType<TacticActivatedEvent>().First();
        Assert.Equal(expected, first.Effect);
        if (expected == 15)
            Assert.DoesNotContain(engine.Snapshot.Simulation.RandomStreams, s => s.Name == "military-tactic-resistance");
    }

    [Fact]
    public void LinkedMembersRespectFullAndThirdCooldownAcrossConsecutiveXuns()
    {
        var engine = Engine(members: 2, linkMultiplier: 4);
        var first = engine.AdvanceXun();
        engine.EndControl(Player);
        var second = engine.AdvanceXun();
        var events = first.Events.Concat(second.Events).OfType<TacticActivatedEvent>().ToArray();
        Assert.Contains(events, e => e.OfficerIds.Length == 2);
        var ready = new Dictionary<int, long>();
        foreach (var e in events)
            for (int i = 0; i < e.OfficerIds.Length; i++)
            {
                int id = e.OfficerIds[i];
                Assert.True(e.Clock.DayNumber >= ready.GetValueOrDefault(id));
                ready[id] = e.Clock.DayNumber + (i == 0 ? 9 : 3);
            }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public void LinkedProficiencyDoesNotChangePrimaryDoubleRoll(int primaryProficiency)
    {
        int compared = 0;
        for (ulong seed = 0; seed < 64; seed++)
        {
            var solo = Engine(primaryProficiency: primaryProficiency, seed: seed).AdvanceXun()
                .Events.OfType<TacticActivatedEvent>().First();
            Assert.Equal(new[] { 11 }, solo.OfficerIds);
            foreach (int linkedProficiency in new[] { 0, 1000 })
            {
                var linked = Engine(members: 2, primaryProficiency: primaryProficiency,
                    linkedProficiency: linkedProficiency, seed: seed, linkMultiplier: 4).AdvanceXun()
                    .Events.OfType<TacticActivatedEvent>().First();
                Assert.Equal(2, linked.OfficerIds.Length);
                if (linked.OfficerIds[0] != 11) continue;
                Assert.Equal(new[] { 11, 12 }, linked.OfficerIds);
                Assert.Equal(solo.Doubled, linked.Doubled);
                compared++;
            }
        }
        Assert.True(compared >= 64, "Compare at least 64 activations with officer 11 as the primary.");
    }

    [Theory]
    [InlineData(false, 0, 0)]
    [InlineData(true, 4, 0)]
    [InlineData(true, 0, 50)]
    public void UnavailableMembersNeverProduceTactics(bool equipped, int injury, int cooldown)
    {
        Assert.Empty(Engine(equipped: equipped, injury: injury, cooldown: cooldown).AdvanceXun().Events.OfType<TacticActivatedEvent>());
    }

    [Fact]
    public void WaterFormationProvidesItsTacticWithoutAnEquippedLandTactic()
    {
        var engine = Engine(water: true, equipped: false);
        Assert.All(engine.AdvanceXun().Events.OfType<TacticActivatedEvent>(), e => Assert.Equal(9000, e.TacticId));
        Assert.True(engine.Snapshot.Officers.Single(p => p.Id == 11).TacticCooldown > 0);
    }

    [Fact]
    public void AutomaticWaterResultTeachesItsFormationTechnologyToBothSides()
    {
        var engine = Engine(water: true, equipped: false, waterLearning: true, seed: 1);
        var result = engine.AdvanceXun();
        Assert.Contains(result.Events.OfType<TacticLearnedEvent>(), e => e.OfficerId == 11 && e.TacticId == 9020);
        Assert.Contains(result.Events.OfType<TacticLearnedEvent>(), e => e.OfficerId == 21 && e.TacticId == 9020);
        Assert.DoesNotContain(result.Events.OfType<TacticLearnedEvent>(), e => e.TacticId == 9000);
    }

    [Fact]
    public void WaterLearningUsesIdentityOrderOnBothSidesAcrossRestore()
    {
        var engine = Engine(members: 2, water: true, waterLearning: true, differentCommander: true, seed: 1);
        var restored = Engine(members: 2, water: true, waterLearning: true, differentCommander: true, seed: 1, restoredState: engine.CaptureStableState());
        var result = engine.AdvanceXun();
        var repeated = restored.AdvanceXun();
        var firstDay = result.Events;
        Assert.Equal(new[] { 21, 12 }, firstDay.OfType<TacticLearnedEvent>().Where(e => e.Clock.Day == 2).Select(e => e.OfficerId));
        Assert.Equal(new[] { 11, 12 }, firstDay.OfType<TacticProficiencyGrownEvent>().Where(e => e.Clock.Day == 2).Select(e => e.OfficerId));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(result.Events), System.Text.Json.JsonSerializer.Serialize(repeated.Events));
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
    }

    [Fact]
    public void TacticCaptureUsesSharedForceAndTargetCleanupExactlyOnce()
    {
        var engine = Engine(settlement: true, power: 10000);
        var result = engine.AdvanceXun();
        Assert.Single(result.Events.OfType<TacticActivatedEvent>());
        Assert.Single(result.Events.OfType<SettlementCapturedEvent>());
        Assert.Single(result.Events.OfType<ForceEliminatedEvent>());
        Assert.Equal(1, engine.Snapshot.Settlements.Single(s => s.Id == 2).OwnerId);
        Assert.DoesNotContain(engine.Snapshot.Units, u => u.Id == 0);
        Assert.Equal(Assert.Single(result.Events.OfType<SettlementCapturedEvent>()).Clock,
            Assert.Single(result.Events.OfType<UnitAbsorbedEvent>()).Clock);
        Assert.Contains(result.Events.OfType<TacticProficiencyGrownEvent>(), e => e.OfficerId == 11 && e.Category == 0 && e.Current - e.Previous == 3);
    }

    [Fact]
    public void DefenderOpportunityUsesItsExplicitCounterProfile()
    {
        var events = Engine(counter: true).AdvanceXun().Events.OfType<TacticActivatedEvent>().ToArray();
        Assert.Contains(events, e => e.SourceId == 0);
        Assert.Contains(events, e => e.SourceId == 1 && e.TargetId == 0);
        Assert.All(events.GroupBy(e => (e.Clock, e.SourceId)), group => Assert.Single(group));
    }

    [Fact]
    public void TacticWithoutFacilityQualificationDoesNotAffectGarrison()
    {
        Assert.Empty(Engine(settlement: true, power: 10000, allowedFacility: false).AdvanceXun().Events.OfType<TacticActivatedEvent>());
    }
}
