using System.Collections.Immutable;
using System.Text.Json;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Core.Tests;

public sealed class NavalTacticOpportunityTests
{
    private static readonly MilitaryOrder Target = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 2 };

    private static MilitaryDefinition Definition(bool water = true)
    {
        var combat = new CombatProfile(0, 0, 0, 1000, 50);
        return new(6, 1, Enumerable.Repeat(water ? 1 : 0, 6).ToImmutableArray(),
            [new(0, 1, 1), new(1, 1, 1) { IsWater = true }],
            [new(100, 5, 0, 200000, 2, new(1, 1), TravelMode.Water)
                { Combat = combat, Attack = new(10, false), CaptureRanks = [BattleRank.Middle, BattleRank.Middle],
                    Tactics = new(15, 2, [], [5000]) { AutomaticOpportunityChance = 10 } },
             new(200, 5, 0, 200000, 1, new(1, 1), TravelMode.Water)
                { Combat = combat, CaptureRanks = [BattleRank.Middle] },
             new(300, 5, 0, 200000, 2, new(1, 1), TravelMode.Land)
                { Combat = combat, Attack = new(10, false), CaptureRanks = [BattleRank.Front, BattleRank.Middle],
                    Tactics = new(15, 2, [], []) }],
            [new(1, new(2, 2), new(1, 1)) { Combat = combat }, new(2, new(11, 2), new(1, 1)) { Combat = combat }])
        {
            Tactics = [new(5000, 4, TacticEffect.Martial, 1, 9, 0, false, false, [100]),
                new(6000, 7, TacticEffect.Confusion, 10, 9, 0, true, true, [100, 300]),
                new(6100, 8, TacticEffect.Taunt, 0, 9, 0, true, true, [100, 300]),
                new(6200, 8, TacticEffect.Inspire, 10, 9, 0, true, false, [100, 300])]
        };
    }

    private static WorldState World(bool secondMember = false, bool settlement = false) => WorldState.Create(new(190, 1, 1), 42,
        [new(1, 100) { LeaderId = 11 }, new(2, 100) { LeaderId = 21 }],
        [new(1, 1, 100000, 100, 100, 100, 0, 0, 100, 0, 100),
         new(2, 2, 100000, 100, 100, 100, 0, 0, 100, 0, 100)],
        [new(11, 1, null, new(70, 200, 70, 70), false)
            { UnitId = 1, LearnedTactics = [6000, 6100, 6200], EquippedTacticId = 6100 },
         .. secondMember ? new OfficerState[] { new(12, 1, null, new(70, 70, 70, 70), false) { UnitId = 1 } } : [],
         new(21, 2, settlement ? 2 : null, new(70, 0, 70, 70), false) { UnitId = settlement ? null : 2 }],
        units: [new(1, 1, new(6, 2), 100000) { FormationId = 300, NavalFormationId = 100, Morale = 100, ServicePlaceId = 1,
            CommanderId = 11, MemberRanks = secondMember
                ? new Dictionary<int, BattleRank> { [11] = BattleRank.Front, [12] = BattleRank.Middle }.ToImmutableDictionary()
                : ImmutableDictionary<int, BattleRank>.Empty.Add(11, BattleRank.Front) },
            .. settlement ? Array.Empty<UnitState>() : [new(2, 2, new(11, 2), 100000)
                { FormationId = 200, Morale = 100, ServicePlaceId = 2,
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(21, BattleRank.Middle) }]],
        controls: [new("player", 1, ControllerKind.Player)],
        corps: [new(1, 1, 11, new(10000, 1000000)) { IsRoot = true }, new(2, 2, 21, new(10000, 1000000)) { IsRoot = true }]);

    private static void Rolls(WorldMutation world, int automatic = 10, int personal = 14) =>
        world.SetSimulation(world.Simulation with { RandomStreams =
            [new("military-tactic-automatic-opportunity", TestSeeds.StateForRoll(automatic)),
             new("military-tactic-opportunity", TestSeeds.StateForRoll(personal)),
             new("military-tactic-member", TestSeeds.StateForRoll(0))] });

    [Theory]
    [InlineData(9, 14, 5000)]
    [InlineData(10, 14, 6100)]
    [InlineData(10, 15, null)]
    public void AutomaticAndPersonalPhasesHaveIndependentBoundaries(int automatic, int personal, int? expected)
    {
        var m = World().BeginMutation();
        Rolls(m, automatic, personal);
        List<DomainEvent> events = [];
        TacticCombat.TryUnit(m, 1, Target, new() { Military = new(Definition()) }, events);
        Assert.Equal(expected is int id ? [id] : Array.Empty<int>(), events.OfType<TacticActivatedEvent>().Select(e => e.TacticId));
        Assert.Equal(expected == 5000, m.Simulation.RandomStreams.Single(s => s.Name == "military-tactic-opportunity").State == TestSeeds.StateForRoll(personal));
        Assert.Equal(expected is null ? 0 : 9, m.Officers[11].TacticCooldown);
    }

    [Theory]
    [InlineData(19, true)]
    [InlineData(20, false)]
    public void HighSpiritsBonusAppliesToPersonalPhaseRatherThanTheShipRoll(int personal, bool expected)
    {
        var m = World().BeginMutation();
        m.Replace(m.Units[1] with { Condition = UnitCondition.HighSpirits, ConditionDaysRemaining = 3 });
        Rolls(m, 10, personal);
        List<DomainEvent> events = [];
        TacticCombat.TryUnit(m, 1, Target, new() { Military = new(Definition()) }, events);
        Assert.DoesNotContain(events.OfType<TacticActivatedEvent>(), e => e.TacticId == 5000);
        Assert.Equal(expected, events.OfType<TacticActivatedEvent>().Any(e => e.TacticId == 6100));
    }

    [Theory]
    [InlineData(49, true)]
    [InlineData(50, false)]
    public void DefensiveGateIsConsumedOnlyOnceBeforeBothPhases(int roll, bool expected)
    {
        var m = World().BeginMutation();
        Rolls(m);
        ulong seed = TestSeeds.StateForRoll(roll);
        m.SetSimulation(m.Simulation with { RandomStreams = m.Simulation.RandomStreams.Add(new("military-tactic-counter", seed)) });
        List<DomainEvent> events = [];
        TacticCombat.TryUnit(m, 1, Target, new() { Military = new(Definition() with { DefensiveTacticChance = 50 }) }, events, counter: true);
        Assert.Equal(expected, events.OfType<TacticActivatedEvent>().Any(e => e.TacticId == 6100));
        var random = new StableRandom(seed);
        random.NextUInt64();
        Assert.Equal(random.State, m.Simulation.RandomStreams.Single(s => s.Name == "military-tactic-counter").State);
        Assert.Equal(!expected, m.Simulation.RandomStreams.Single(s => s.Name == "military-tactic-automatic-opportunity").State == TestSeeds.StateForRoll(10));
    }

    [Fact]
    public void ResistedAutomaticResultStillSpendsTheOpportunityAndSharedSupportCooldown()
    {
        var m = World().BeginMutation();
        m.Replace(m.Officers[11] with { EquippedTacticId = 6200 });
        m.Replace(m.Officers[21] with { TacticProficiencies = [0, 0, 0, 0, 10000, 0, 0, 0, 0] });
        m.Replace(m.Units[1] with { Morale = 79 });
        Rolls(m, 0);
        var rules = new SimulationRules { Military = new(Definition()) };
        List<DomainEvent> events = [];
        TacticCombat.TryUnit(m, 1, Target, rules, events);
        var activation = Assert.Single(events.OfType<TacticActivatedEvent>());
        Assert.Equal(5000, activation.TacticId);
        Assert.Equal(1, activation.Effect);
        TacticSupport.AdvanceDay(m, rules, events);
        TacticCombat.TryUnit(m, 1, Target, rules, events);
        Assert.Single(events.OfType<TacticActivatedEvent>());
        Assert.Equal(79, m.Units[1].Morale);
        Assert.Equal(TestSeeds.StateForRoll(14), m.Simulation.RandomStreams.Single(s => s.Name == "military-tactic-opportunity").State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrIneligibleShipTacticDoesNotSuppressPersonalEquipment(bool missing)
    {
        var definition = Definition();
        if (missing) definition = definition with { Formations = definition.Formations.Select(f => f.Id == 100
            ? f with { Tactics = f.Tactics! with { AutomaticTactics = [] } } : f).ToImmutableArray() };
        var m = World(settlement: true).BeginMutation();
        Rolls(m, 0);
        List<DomainEvent> events = [];
        TacticCombat.TryUnit(m, 1, new(MilitaryOrderKind.AttackSettlement, null, 2), new() { Military = new(definition) }, events);
        Assert.Equal(6100, Assert.Single(events.OfType<TacticActivatedEvent>()).TacticId);
    }

    [Theory]
    [InlineData(false, 6100)]
    [InlineData(true, 5000)]
    public void ShoreChangesSelectTheCurrentFormationPhases(bool water, int expected)
    {
        var m = World().BeginMutation();
        Rolls(m, 0);
        List<DomainEvent> events = [];
        TacticCombat.TryUnit(m, 1, Target, new() { Military = new(Definition(water)) }, events);
        Assert.Equal(expected, Assert.Single(events.OfType<TacticActivatedEvent>()).TacticId);
        Assert.Equal(!water, m.Simulation.RandomStreams.Single(s => s.Name == "military-tactic-automatic-opportunity").State == TestSeeds.StateForRoll(0));
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(7, 0, true)]
    [InlineData(0, 1, false)]
    public void IntelligenceCanStartOneNavalFollowupExcludingItsActor(int cooldown, int injury, bool expected)
    {
        var world = World(secondMember: true);
        var m = world.BeginMutation();
        m.Replace(m.Officers[11] with { EquippedTacticId = 6000 });
        m.Replace(m.Officers[12] with { TacticCooldown = cooldown, Injury = injury });
        Rolls(m);
        List<DomainEvent> events = [];
        TacticCombat.TryUnit(m, 1, Target, new() { Military = new(Definition()) }, events);
        var activations = events.OfType<TacticActivatedEvent>().ToArray();
        Assert.Equal(6000, activations[0].TacticId);
        Assert.Equal(11, Assert.Single(activations[0].Participants).OfficerId);
        Assert.Equal(expected ? 2 : 1, activations.Length);
        if (expected)
        {
            Assert.True(activations[1].IsChain);
            Assert.Equal(5000, activations[1].TacticId);
            Assert.Equal(12, Assert.Single(activations[1].Participants).OfficerId);
            Assert.Equal(9, m.Officers[12].TacticCooldown);
        }
        else Assert.DoesNotContain(m.Simulation.RandomStreams, s => s.Name == "military-tactic-chain-member");
        Assert.Equal(9, m.Officers[11].TacticCooldown);
    }

    [Fact]
    public void DailyCombatAndStableRestoreUseTheSameNavalPhases()
    {
        var world = World(secondMember: true);
        var m = world.BeginMutation();
        m.Replace(m.Officers[11] with { EquippedTacticId = 6000 });
        Rolls(m);
        world.Commit(m);
        var military = new MilitaryRulebook(Definition());
        SimulationEngine Engine(WorldState state) => new(state, EconomyRulebook.Empty, PersonnelRulebook.Empty, TreasureRulebook.Empty, military: military);
        var engine = Engine(world);
        var player = new CommandContext("player");
        Assert.True(engine.Execute(player, new OrderUnitCommand(Guid.NewGuid(), 1, 0, 1, Target)).Submission.Accepted);
        var restored = Engine(WorldState.Restore(engine.CaptureStableState()));
        engine.EndControl(player); restored.EndControl(player);
        var actual = engine.AdvanceXun().Events;
        EventAssert.Equal(actual, restored.AdvanceXun().Events);
        Assert.Contains(actual, e => e is TacticActivatedEvent { TacticId: 6000 });
        Assert.Contains(actual, e => e is TacticActivatedEvent { TacticId: 5000, IsChain: true });
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void AutomaticChanceMustBeAValidPercentage(int chance)
    {
        var definition = Definition();
        definition = definition with { Formations = definition.Formations.Select(f => f.Id == 100
            ? f with { Tactics = f.Tactics! with { AutomaticOpportunityChance = chance } } : f).ToImmutableArray() };
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition));
    }

    [Fact]
    public void AutomaticPhaseMustBeExplicitInSerializedContent()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FormationTactics>(
            "{\"OpportunityChance\":15,\"LinkMultiplier\":2,\"FavoredCategories\":[],\"AutomaticTactics\":[]}"));
    }
}
