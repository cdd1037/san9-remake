using San9.Tests;
using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class TacticRescueTests
{
    private static readonly MilitaryOrder Target = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 2 };
    private static (WorldState World, SimulationRules Rules, TacticRule Tactic) Create(bool protectedFormation = false, bool own = true)
    {
        var tactic = new TacticRule(9000, 0, TacticEffect.Martial, 1, 9, 0, true, false, [100]) { CanRescuePrisoners = true };
        var military = new MilitaryRulebook(new(100, 3, [.. Enumerable.Repeat(0, 300)], [new(0, 1, 1)],
            [new(100, 5, 0, 100000, 1, new(1, 1), TravelMode.Land)
                { Combat = new(10, 10, 0, 1000, 50), Attack = new(10, false), CaptureRanks = [BattleRank.Front],
                    PreventsTacticRescue = protectedFormation, Tactics = new(100, 2, [0], []) }],
            [new(1, new(2, 7), new(1, 1)), new(2, new(497, 7), new(1, 1)), new(3, new(402, 7), new(1, 1))])
            { Tactics = [tactic], Geography = new(5, []) });
        var world = WorldState.Create(new(190, 1, 1), 42,
            [new(1, 100), new(2, 100), new(3, 100)],
            [new(1, 1, 10000, 100, 100, 100, 0, 0, 100, 0, 100),
                new(2, 2, 10000, 100, 100, 100, 0, 0, 100, 0, 100),
                new(3, 3, 10000, 100, 100, 100, 0, 0, 100, 0, 100)],
            [new(11, 1, null, new(70, 70, 70, 70), false) { UnitId = 1, LearnedTactics = [9000], EquippedTacticId = 9000 },
                new(21, 2, null, new(70, 70, 70, 70), false) { UnitId = 2 },
                new(31, 3, 3, new(70, 70, 70, 70), false),
                new(40, own ? 1 : 3, null, new(70, 70, 70, 70), false) { UnitId = 2, Status = OfficerStatus.Captive },
                new(41, 3, null, new(70, 70, 70, 70), false) { UnitId = 2, Status = OfficerStatus.Captive }],
            units: [new(1, 1, new(102, 7), 100000) { FormationId = 100, Morale = 100, ServicePlaceId = 1, Order = Target,
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(11, BattleRank.Front) },
                new(2, 2, new(106, 7), 100000) { FormationId = 100, Morale = 100, ServicePlaceId = 2,
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(21, BattleRank.Front) }], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, null, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(3, 3, null, new(10000, 10000)) { IsRoot = true }]);
        return (world, new() { Military = military }, tactic);
    }

    private static void RollValue(WorldMutation world, uint value)
    {
        ulong seed = TestSeeds.StateForRoll((int)value);
        world.SetSimulation(world.Simulation with { RandomStreams = [new("military-tactic-rescue", seed)] });
    }

    [Theory]
    [InlineData(true, 49, true)]
    [InlineData(true, 50, false)]
    [InlineData(false, 49, true)]
    public void MartialPipelineRescuesOneAndReturnsIndependently(bool own, int roll, bool rescued)
    {
        var (world, rules, _) = Create(own: own);
        var m = world.BeginMutation(); RollValue(m, (uint)roll); List<DomainEvent> events = [];
        TacticCombat.TryUnit(m, 1, Target, rules, events,
            CombatExchange.Calculate(m, new(1, false), CombatExchange.TargetOf(Target), rules.Military!));
        Assert.Single(events.OfType<TacticActivatedEvent>());
        Assert.Equal(rescued ? 1 : 0, events.OfType<TacticPrisonerRescuedEvent>().Count());
        Assert.Equal(rescued ? 1 : 2, m.Officers.Values.Count(p => p.Status == OfficerStatus.Captive));
        if (!rescued) return;
        var result = Assert.Single(events.OfType<TacticPrisonerRescuedEvent>());
        if (own) Assert.Equal(40, result.OfficerId);
        var person = m.Officers[result.OfficerId];
        Assert.Null(person.UnitId);
        Assert.Equal(own ? 1 : 3, person.OwnerId);
        Assert.Equal(OfficerStatus.Serving, person.Status);
        var journey = Assert.Single(m.Simulation.Tasks.Where(t => t.OfficerId == person.Id)).Journey!;
        Assert.Equal(new WorldPosition(106, 7), journey.Route.Origin);
        Assert.True(journey.ReturnLeg!.FieldTravel);
        Assert.Equal(new[] { 11 }, m.Units[1].MemberRanks.Keys);
    }

    [Theory]
    [InlineData("protected")]
    [InlineData("unsupported")]
    [InlineData("resisted")]
    [InlineData("empty")]
    [InlineData("settlement")]
    [InlineData("embedded")]
    public void IneligibleRescueConsumesNoRandom(string gate)
    {
        var (world, rules, tactic) = Create(protectedFormation: gate == "protected");
        var m = world.BeginMutation();
        if (gate == "embedded") m.Replace(m.Units[2] with { IsEmbedded = true });
        if (gate == "empty")
            foreach (int id in new[] { 40, 41 }) m.Replace(m.Officers[id] with { UnitId = null, SettlementId = 2 });
        Assert.Null(TacticRescue.Select(m, new(1, false), tactic with { CanRescuePrisoners = gate != "unsupported" },
            gate == "settlement" ? new(MilitaryOrderKind.AttackSettlement, null, 2) : Target,
            gate == "resisted" ? 1 : 10, rules.Military!));
        Assert.Empty(m.Simulation.RandomStreams);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefeatReleasesOwnAndThirdPartyCaptivesExactlyOnce(bool tacticKill)
    {
        var (world, rules, tactic) = Create();
        var m = world.BeginMutation(); RollValue(m, 49); List<DomainEvent> events = [];
        if (tacticKill)
        {
            m.Replace(m.Units[2] with { Soldiers = 1 });
            TacticCombat.ResolveMartial(m, new(1, false), 11, tactic, Target, [new(10000, 1000, 100, false)], false, 10, rules, events,
                CombatExchange.Calculate(m, new(1, false), CombatExchange.TargetOf(Target), rules.Military!));
        }
        else BattleAftermath.TerminateUnit(m, 2, new(1, false), rules, events);
        Assert.False(m.Units.ContainsKey(2));
        foreach (int id in new[] { 40, 41 })
        {
            Assert.Null(m.Officers[id].UnitId);
            Assert.Equal(OfficerStatus.Serving, m.Officers[id].Status);
            Assert.Single(m.Simulation.Tasks.Where(t => t.OfficerId == id));
            Assert.Single(events.OfType<OfficerEscapedBattleEvent>(), e => e.OfficerId == id);
        }
        Assert.Equal(3, m.Officers[41].OwnerId);
    }

    [Fact]
    public void RestoredBattleRepeatsSelectionEventsAndReturnState()
    {
        var (world, rules, _) = Create();
        _ = new SimulationEngine(world, EconomyRulebook.Empty, PersonnelRulebook.Empty, TreasureRulebook.Empty, military: rules.Military);
        var seed = world.BeginMutation(); RollValue(seed, 49); world.Commit(seed);
        var restored = WorldState.Restore(world.CreateSnapshot());
        string Run(WorldState state)
        {
            var m = state.BeginMutation(); List<DomainEvent> events = [];
            TacticCombat.TryUnit(m, 1, Target, rules, events,
                CombatExchange.Calculate(m, new(1, false), CombatExchange.TargetOf(Target), rules.Military!)); state.Commit(m);
            string json = System.Text.Json.JsonSerializer.Serialize(events, CanonicalJson.Options);
            Assert.Contains(System.Text.Json.JsonSerializer.Deserialize<DomainEvent[]>(json, CanonicalJson.Options)!, e => e is TacticPrisonerRescuedEvent);
            return json;
        }
        Assert.Equal(Run(world), Run(restored));
        StateAssert.Equal(world.CreateSnapshot(), restored.CreateSnapshot());
    }
}
