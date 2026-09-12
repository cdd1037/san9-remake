using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class TacticCombatTests
{
    private static readonly CommandContext Player = new("player");
    private static SimulationEngine Engine(int members = 1, bool water = false, bool settlement = false,
        int power = 13, bool equipped = true, int injury = 0, int cooldown = 0, bool allowedFacility = true, bool counter = false)
    {
        var combat = new CombatProfile(0, 0, 0, 1000, 50);
        var mode = water ? TravelMode.Water : TravelMode.Land;
        var rules = new MilitaryRulebook(new(4, 1, [0, 0, 0, 0], [new(0, 1, 1)],
            [new(100, 5, 0, 100000, 2, new(1, 1), mode) { Combat = combat, Attack = new(1, false),
                CaptureRanks = [BattleRank.Front, BattleRank.Front], Tactics = new(100, 0, 2, [0], water ? [9000] : []) },
             new(200, 5, 0, 100000, 1, new(1, 1), mode) { Combat = combat, CaptureRanks = [BattleRank.Middle],
                Attack = counter ? new(1, false) : null, Tactics = counter ? new(100, 100, 2, [0], []) : null }],
            [new(1, new(2, 2), new(1, 1)) { Combat = combat }, new(2, new(17, 2), new(1, 1)) { Combat = combat }])
        { Tactics = [new(9000, 0, TacticEffect.Martial, power, 9, 0, !water, allowedFacility, [100, 200])] });
        List<OfficerState> officers = [new(11, 1, null, new(80, 70, 70, 70), false)
            { UnitId = 0, LearnedTactics = equipped ? [9000] : [], EquippedTacticId = equipped ? 9000 : null,
                Injury = injury, TacticCooldown = cooldown }];
        if (members == 2) officers.Add(new(12, 1, null, new(80, 70, 70, 70), false)
            { UnitId = 0, LearnedTactics = [9000], EquippedTacticId = 9000 });
        officers.Add(new(21, 2, settlement ? 2 : null, new(70, 70, 70, 70), false) { UnitId = settlement ? null : 1,
            LearnedTactics = counter ? [9000] : [], EquippedTacticId = counter ? 9000 : null });
        List<UnitState> units = [new(0, 1, new(settlement ? 16 : 6, 2), 100000) { FormationId = 100, Morale = 100, ServicePlaceId = 1,
            MemberRanks = officers.Where(p => p.UnitId == 0).ToImmutableDictionary(p => p.Id, _ => BattleRank.Front) }];
        if (!settlement) units.Add(new(1, 2, new(7, 2), 100000) { FormationId = 200, Morale = 100, ServicePlaceId = 2,
            MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(21, BattleRank.Middle) });
        var world = WorldState.Create(new(190, 1, 1), 42,
            [new(1, 10000, 10000, 100) { LeaderId = 11 }, new(2, 10000, 10000, 100) { LeaderId = 21 }],
            [new(1, 1, 100, 100, 100, 100, 0, 0, 100, 0, 100), new(2, 2, 1000, 100, 100, 100, 0, 0, 100, 0, 100)],
            officers, units: units, controls: [new("player", 1, ControllerKind.Player)]);
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
    public void LinkedMembersRespectFullAndThirdCooldownAcrossConsecutiveXuns()
    {
        var engine = Engine(members: 2);
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
    public void TacticCaptureUsesSharedForceAndTargetCleanupExactlyOnce()
    {
        var engine = Engine(settlement: true, power: 10000);
        var result = engine.AdvanceXun();
        Assert.Single(result.Events.OfType<TacticActivatedEvent>());
        Assert.Single(result.Events.OfType<SettlementCapturedEvent>());
        Assert.Single(result.Events.OfType<ForceEliminatedEvent>());
        Assert.Equal(1, engine.Snapshot.Settlements.Single(s => s.Id == 2).OwnerId);
        Assert.Null(engine.Snapshot.Units.Single(u => u.Id == 0).Order);
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
