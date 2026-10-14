using San9.Tests;
using System.Collections.Immutable;
using System.Text.Json;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class TacticSnipingTests
{
    private static readonly MilitaryOrder UnitTarget = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 2 };
    private static readonly MilitaryOrder PlaceTarget = new(MilitaryOrderKind.AttackSettlement, null, 2);
    internal static (WorldState World, SimulationRules Rules, TacticRule Tactic) Create(bool settlement = false, int bonus = 100, bool deathsAllowed = true, bool meleeRanks = true, int counterChance = 0)
    {
        var tactic = new TacticRule(9000, 3, TacticEffect.Martial, 1, 9, 0, true, true, [100]) { SnipingProficiencyCategory = 3 };
        var combat = new CombatProfile(0, 0, 0, 1000, 50);
        var military = new MilitaryRulebook(new(100, 3, [.. Enumerable.Repeat(0, 300)], [new(0, 1, 1)],
            [new(100, 5, 0, 100000, 2, new(1, 1), TravelMode.Land)
                { Combat = combat, Attack = new(10, false), SnipingChanceBonus = bonus, MeleeDeathUsesRanks = meleeRanks,
                    CaptureRanks = [BattleRank.Front, BattleRank.Middle], Tactics = new(100, counterChance, 2, [3], []) }],
            [new(1, new(2, 7), new(1, 1)) { Combat = combat, FacilityTypeId = 8000, Attack = new(10, false) },
                new(2, new(107, 7), new(1, 1)) { Combat = combat, FacilityTypeId = 8000 }, new(3, new(497, 7), new(1, 1)) { Combat = combat, FacilityTypeId = 8000 }])
            { BattleDeathsAllowed = deathsAllowed, Tactics = [tactic], Geography = new(5, []),
                Facilities = [new(8000, 0, 100, new(1, 1), 0, false, true, true, 100000, null, null, null, [])
                    { Combat = combat, Attack = new(10, false), Tactics = new(100, 3, false, [9000]) }] });
        var world = WorldState.Create(new(190, 1, 1), 42,
            [new(1, 100) { LeaderId = 11 }, new(2, 100) { LeaderId = 21 }],
            [new(1, 1, 10000, 100, 100, 100, 0, 0, 100, 0, 100),
                new(2, 2, 10000, 100, 100, 100, 0, 0, 100, 0, 100), new(3, 2, 10000, 100, 100, 100, 0, 0, 100, 0, 100)],
            [new(11, 1, null, new(100, 70, 70, 100), false) { UnitId = 1, Status = OfficerStatus.Ruler,
                LearnedTactics = [9000], EquippedTacticId = 9000, TacticProficiencies = [0, 0, 0, 1000, 0, 0, 0, 0, 0] },
                new(21, 2, settlement ? 2 : null, new(100, 70, 70, 100), false)
                    { UnitId = settlement ? null : 2, Status = OfficerStatus.Ruler, Injury = 1, IsCommander = settlement },
                new(22, 2, settlement ? 2 : null, new(70, 70, 70, 70), false) { UnitId = settlement ? null : 2 }],
            units: [new(1, 1, new(102, 7), 100000) { FormationId = 100, Morale = 100, ServicePlaceId = 1,
                    CommanderId = 11, Order = settlement ? PlaceTarget : UnitTarget,
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(11, BattleRank.Front) },
                ..(settlement ? Array.Empty<UnitState>() : [new(2, 2, new(106, 7), 100000) { FormationId = 100,
                    CommanderId = 21, Morale = 100, ServicePlaceId = 3,
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(21, BattleRank.Rear).Add(22, BattleRank.Middle) }])],
            controls: [new("player", 1, ControllerKind.Player)], corps: [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(10000, 10000)) { IsRoot = true }]);
        return (world, new() { Military = military }, tactic);
    }

    private static void Roll(WorldMutation world, string stream, uint value)
    {
        ulong seed = TestSeeds.StateForRoll((int)value);
        world.SetSimulation(world.Simulation with { RandomStreams = world.Simulation.RandomStreams
            .Where(s => s.Name != stream).Append(new(stream, seed)).ToImmutableArray() });
    }

    [Theory]
    [InlineData("proficiency")]
    [InlineData("effect")]
    [InlineData("unsupported")]
    [InlineData("rear")]
    [InlineData("injured")]
    [InlineData("absent-participant")]
    [InlineData("busy-facility")]
    public void IneligibleResultsDoNotConsumeSnipingRandom(string gate)
    {
        bool settlement = gate == "busy-facility";
        var (world, rules, tactic) = Create(settlement);
        var m = world.BeginMutation();
        if (gate == "proficiency") m.Replace(m.Officers[11] with { TacticProficiencies = [0, 0, 0, 300, 0, 0, 0, 0, 0] });
        if (gate == "rear") m.Replace(m.Units[2] with { MemberRanks = m.Units[2].MemberRanks.SetItem(22, BattleRank.Rear) });
        if (gate == "injured") m.Replace(m.Officers[22] with { Injury = 1 });
        if (settlement) m.Replace(m.Officers[22] with { Acted = true });
        Assert.Null(TacticSniping.Select(m, new(1, false), tactic with { SnipingProficiencyCategory = gate == "unsupported" ? null : 3 },
            settlement ? PlaceTarget : UnitTarget, gate == "absent-participant" ? [] : [11], gate == "effect" ? 9 : 10, rules.Military!));
        Assert.Empty(m.Simulation.RandomStreams);
    }

    [Theory]
    [InlineData(301, 0, 3, true)]
    [InlineData(301, 0, 4, false)]
    [InlineData(1000, 30, 79, true)]
    [InlineData(1000, 30, 80, false)]
    public void ProficiencyAndFormationDetermineTheActualProbability(int proficiency, int bonus, int roll, bool expected)
    {
        var (world, rules, tactic) = Create(bonus: bonus); var m = world.BeginMutation();
        m.Replace(m.Officers[11] with { TacticProficiencies = [0, 0, 0, proficiency, 0, 0, 0, 0, 0] });
        Roll(m, "military-sniping", (uint)roll);
        Assert.Equal(expected, TacticSniping.Select(m, new(1, false), tactic, UnitTarget, [11], 10, rules.Military!) is not null);
    }

    [Theory]
    [InlineData(0, 100, 99, 1)]
    [InlineData(60, 70, 0, 4)]
    [InlineData(70, 70, 0, 4)]
    [InlineData(100, 0, 0, 7)]
    public void InjuryUsesEffectiveMartialBranches(int attack, int defense, int roll, int injury)
    {
        var (world, rules, tactic) = Create(); var m = world.BeginMutation();
        m.Replace(m.Officers[11] with { BaseAbilities = new(attack, 0, 0, 0) });
        m.Replace(m.Officers[22] with { BaseAbilities = new(defense, 0, 0, 0) });
        Roll(m, "military-sniping-injury", (uint)roll);
        Assert.Equal(injury, TacticSniping.Select(m, new(1, false), tactic, UnitTarget, [11], 10, rules.Military!)!.Injury);
    }

    [Theory]
    [InlineData("none", true)]
    [InlineData("disabled", false)]
    [InlineData("mode", false)]
    [InlineData("commander", false)]
    [InlineData("ability", false)]
    [InlineData("barbarian", false)]
    [InlineData("defeated", false)]
    public void DeathUsesSharedLifecycleAndEligibility(string gate, bool dies)
    {
        var (world, rules, tactic) = Create(deathsAllowed: gate != "mode"); var m = world.BeginMutation(); List<DomainEvent> events = [];
        m.SetSimulation(m.Simulation with { BattleDeathsEnabled = gate != "disabled" });
        if (gate == "commander") m.Replace(m.Units[2] with { CommanderId = 22 });
        if (gate == "ability") m.Replace(m.Officers[22] with { BaseAbilities = new(70, 80, 70, 70) });
        if (gate == "barbarian") m.Replace(m.Forces[2] with { IsBarbarian = true });
        var outcome = TacticSniping.Select(m, new(1, false), tactic, UnitTarget, [11], 10, rules.Military!);
        Assert.NotNull(outcome); Roll(m, "military-sniping-death", 0);
        TacticSniping.Apply(m, outcome, gate == "defeated", rules, events);
        Assert.Equal(dies, m.Officers[22].Status == OfficerStatus.Dead);
        Assert.Single(events.OfType<TacticSnipingResolvedEvent>());
        if (dies) Assert.Equal(OfficerDeathCause.Sniping, Assert.Single(events.OfType<OfficerDiedEvent>()).Cause);
        else Assert.True(m.Officers[22].Injury > 0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RealMartialResultRetainsInjuryAfterDefeatAndCapture(bool settlement, bool lethal)
    {
        var (world, rules, _) = Create(settlement); var m = world.BeginMutation(); List<DomainEvent> events = [];
        if (lethal)
        {
            if (settlement) m.Replace(m.Settlements[2] with { Soldiers = 1 });
            else m.Replace(m.Units[2] with { Soldiers = 1 });
        }
        TacticCombat.TryUnit(m, 1, settlement ? PlaceTarget : UnitTarget, rules, events);
        Assert.Single(events.OfType<TacticActivatedEvent>());
        var hit = Assert.Single(events.OfType<TacticSnipingResolvedEvent>());
        Assert.Equal(22, hit.OfficerId); Assert.Equal(hit.Injury, m.Officers[22].Injury);
        if (lethal)
        {
            if (settlement) Assert.Equal(1, m.Settlements[2].OwnerId);
            else Assert.False(m.Units.ContainsKey(2));
            Assert.DoesNotContain(events.OfType<OfficerDiedEvent>(), e => e.Cause == OfficerDeathCause.Sniping);
        }
        world.Commit(m);
        var json = JsonSerializer.Serialize<DomainEvent[]>(events.ToArray(), CanonicalJson.Options);
        Assert.Equal(hit, Assert.Single(JsonSerializer.Deserialize<DomainEvent[]>(json, CanonicalJson.Options)!.OfType<TacticSnipingResolvedEvent>()));
    }

    [Fact]
    public void DailyBattleAndRestoredBoundaryProduceIdenticalSniping()
    {
        var (world, rules, _) = Create();
        SimulationEngine Engine(WorldState state) => new(state, EconomyRulebook.Empty, PersonnelRulebook.Empty,
            TreasureRulebook.Empty, military: rules.Military);
        var original = Engine(world);
        var restored = Engine(WorldState.Restore(original.Snapshot));
        foreach (var engine in new[] { original, restored }) engine.EndControl(new("player"));
        var first = original.AdvanceXun(); var second = restored.AdvanceXun();
        Assert.NotEmpty(first.Events.OfType<TacticSnipingResolvedEvent>());
        EventAssert.Equal(first.Events, second.Events);
        StateAssert.Equal(original.Snapshot, restored.Snapshot);
        var after = Engine(WorldState.Restore(original.Snapshot));
        StateAssert.Equal(original.Snapshot, after.Snapshot);
    }

    [Fact]
    public void FacilityUsesItsActualActorWithoutFormationBonus()
    {
        var (world, rules, _) = Create(); var m = world.BeginMutation(); List<DomainEvent> events = [];
        m.Replace(m.Officers[11] with { UnitId = null, SettlementId = 1, IsCommander = true });
        m.RemoveUnit(1);
        m.Replace(m.Units[2] with { Position = new(6, 7) });
        Roll(m, "military-sniping", 49);
        FacilityCombat.TryTactic(m, 1, UnitTarget, rules, events);
        Assert.Equal(11, Assert.Single(events.OfType<TacticSnipingResolvedEvent>()).ActorId);
        Assert.True(m.Officers[22].Injury > 0);
    }

    [Fact]
    public void LinkedParticipantCanSnipeWhenPrimaryCannot()
    {
        var (world, rules, _) = Create(); var m = world.BeginMutation(); List<DomainEvent> events = [];
        m.Replace(m.Officers[11] with { TacticProficiencies = [0, 0, 0, 300, 0, 0, 0, 0, 0] });
        m.Replace(m.Officers[21] with { OwnerId = 1, UnitId = 1, Status = OfficerStatus.Serving, Injury = 0,
            LearnedTactics = [9000], EquippedTacticId = 9000, TacticLinkBonus = 100,
            TacticProficiencies = [0, 0, 0, 1000, 0, 0, 0, 0, 0] });
        m.Replace(m.Units[1] with { MemberRanks = m.Units[1].MemberRanks.Add(21, BattleRank.Middle) });
        m.Replace(m.Units[2] with { CommanderId = 22, MemberRanks = m.Units[2].MemberRanks.Remove(21) });
        TacticCombat.TryUnit(m, 1, UnitTarget, rules, events);
        Assert.Equal(new[] { 11, 21 }, Assert.Single(events.OfType<TacticActivatedEvent>()).OfficerIds);
        Assert.Equal(21, Assert.Single(events.OfType<TacticSnipingResolvedEvent>()).ActorId);
    }

    [Fact]
    public void SnapshotDoesNotReviveDeadTargetsOrReduceSubsequentInjury()
    {
        var (world, rules, tactic) = Create(); var m = world.BeginMutation(); List<DomainEvent> events = [];
        var outcome = TacticSniping.Select(m, new(1, false), tactic, UnitTarget, [11], 10, rules.Military!);
        m.Replace(m.Officers[22] with { Injury = 14 });
        TacticSniping.Apply(m, outcome, true, rules, events);
        Assert.Equal(14, m.Officers[22].Injury);
        m.Replace(m.Officers[22] with { Status = OfficerStatus.Dead, OwnerId = null, UnitId = null });
        events.Clear(); TacticSniping.Apply(m, outcome, true, rules, events);
        Assert.Empty(events); Assert.Equal(OfficerStatus.Dead, m.Officers[22].Status);
    }

    [Fact]
    public void ContentRejectsInvalidSnipingSemantics()
    {
        var (_, _, tactic) = Create();
        Assert.Throws<ArgumentException>(() => (tactic with { SnipingProficiencyCategory = 9 }).Validate(new Dictionary<int, FormationRule>()));
        Assert.Throws<ArgumentException>(() => (tactic with { Effect = TacticEffect.Confusion }).Validate(new Dictionary<int, FormationRule>()));
        Assert.Throws<ArgumentException>(() => Create(bonus: -1));
        Assert.Throws<ArgumentException>(() => Create(bonus: 101));
    }
}
