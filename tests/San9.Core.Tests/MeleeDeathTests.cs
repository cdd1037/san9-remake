using San9.Tests;
using System.Collections.Immutable;
using System.Text.Json;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class MeleeDeathTests
{
    private static readonly MilitaryOrder Target = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 2 };

    private static void Roll(WorldMutation world, string stream, uint value)
    {
        ulong seed = TestSeeds.StateForRoll((int)value);
        world.SetSimulation(world.Simulation with { RandomStreams = world.Simulation.RandomStreams
            .Where(s => s.Name != stream).Append(new(stream, seed)).OrderBy(s => s.Name, StringComparer.Ordinal).ToImmutableArray() });
    }

    [Theory]
    [InlineData("none", true)]
    [InlineData("disabled", false)]
    [InlineData("mode", false)]
    [InlineData("not-doubled", false)]
    [InlineData("commander", false)]
    [InlineData("ruler", false)]
    [InlineData("ability", false)]
    [InlineData("barbarian", false)]
    [InlineData("failed-roll", false)]
    public void ActualMartialDefeatKillsEligibleMembersBeforeCapture(string gate, bool dies)
    {
        var (world, rules, _) = TacticSnipingTests.Create(deathsAllowed: gate != "mode");
        var m = world.BeginMutation(); List<DomainEvent> events = [];
        m.SetSimulation(m.Simulation with { BattleDeathsEnabled = gate != "disabled" });
        m.Replace(m.Units[2] with { Soldiers = 1 });
        m.Replace(m.Officers[22] with { BaseAbilities = new(40, 40, 40, 40) });
        if (gate == "commander") m.Replace(m.Units[2] with { CommanderId = 22 });
        if (gate == "ruler")
        {
            m.Replace(m.Officers[21] with { Status = OfficerStatus.Serving });
            m.Replace(m.Officers[22] with { Status = OfficerStatus.Ruler });
            m.Replace(m.Forces[2] with { LeaderId = 22 }); m.Replace(ResourceAccounts.Root(m, 2) with { LeaderId = 22 });
        }
        if (gate == "ability") m.Replace(m.Officers[22] with { BaseAbilities = new(40, 80, 40, 40) });
        if (gate == "barbarian") m.Replace(m.Forces[2] with { IsBarbarian = true });
        Roll(m, "military-tactic-double", gate == "not-doubled" ? 99u : 0u);
        Roll(m, "battle-melee-death", gate == "failed-roll" ? 25u : 0u);
        string before = CanonicalJson.Text(m.Simulation.RandomStreams.Single(s => s.Name == "battle-melee-death"));
        TacticCombat.TryUnit(m, 1, Target, rules, events);
        Assert.Single(events.OfType<TacticActivatedEvent>());
        Assert.False(m.Units.ContainsKey(2));
        Assert.Equal(dies, events.OfType<OfficerDiedEvent>().Any(e => e.OfficerId == 22 && e.Cause == OfficerDeathCause.Melee));
        if (dies)
        {
            Assert.Equal(OfficerStatus.Dead, m.Officers[22].Status);
            Assert.Null(m.Officers[22].UnitId);
            Assert.Null(m.Officers[22].OwnerId);
            Assert.DoesNotContain(events.OfType<OfficerEscapedBattleEvent>(), e => e.OfficerId == 22);
            var deaths = JsonSerializer.Deserialize<DomainEvent[]>(JsonSerializer.Serialize<DomainEvent[]>(events.ToArray(), CanonicalJson.Options), CanonicalJson.Options)!;
            Assert.Equal(OfficerDeathCause.Melee, Assert.Single(deaths.OfType<OfficerDiedEvent>()).Cause);
        }
        else if (gate != "failed-roll")
            Assert.Equal(before, CanonicalJson.Text(m.Simulation.RandomStreams.Single(s => s.Name == "battle-melee-death")));
        world.Commit(m);
    }

    [Theory]
    [InlineData(0, BattleRank.Front, 30)]
    [InlineData(60, BattleRank.Front, 20)]
    [InlineData(60, BattleRank.Middle, 5)]
    [InlineData(60, BattleRank.Rear, 0)]
    [InlineData(100, BattleRank.Front, 0)]
    public void ProbabilityUsesEffectiveWarAndRank(int war, BattleRank rank, int expected) =>
        Assert.Equal(expected, MeleeDeaths.Chance(war, rank));

    [Fact]
    public void FacilityMartialDefeatUsesTheSameDeathPath()
    {
        var (world, rules, _) = TacticSnipingTests.Create(); var m = world.BeginMutation(); List<DomainEvent> events = [];
        m.SetSimulation(m.Simulation with { BattleDeathsEnabled = true });
        m.Replace(m.Officers[11] with { UnitId = null, SettlementId = 1, IsCommander = true });
        m.RemoveUnit(1);
        m.Replace(m.Units[2] with { Position = new(6, 7), Soldiers = 1 });
        m.Replace(m.Officers[22] with { BaseAbilities = new(40, 40, 40, 40) });
        Roll(m, "military-tactic-double", 0); Roll(m, "battle-melee-death", 0);
        FacilityCombat.TryTactic(m, 1, Target, rules, events);
        Assert.Equal(OfficerDeathCause.Melee, Assert.Single(events.OfType<OfficerDiedEvent>()).Cause);
        world.Commit(m);
    }

    [Fact]
    public void DoubledMartialCaptureOfSettlementDoesNotKillGarrison()
    {
        var (world, rules, _) = TacticSnipingTests.Create(settlement: true); var m = world.BeginMutation(); List<DomainEvent> events = [];
        m.SetSimulation(m.Simulation with { BattleDeathsEnabled = true });
        m.Replace(m.Settlements[2] with { Soldiers = 1 });
        m.Replace(m.Officers[22] with { BaseAbilities = new(40, 40, 40, 40) });
        Roll(m, "military-tactic-double", 0);
        TacticCombat.TryUnit(m, 1, new(MilitaryOrderKind.AttackSettlement, null, 2), rules, events);
        Assert.Equal(1, m.Settlements[2].OwnerId);
        Assert.DoesNotContain(events.OfType<OfficerDiedEvent>(), e => e.Cause == OfficerDeathCause.Melee);
        Assert.DoesNotContain(m.Simulation.RandomStreams, s => s.Name == "battle-melee-death");
        world.Commit(m);
    }

    [Fact]
    public void OrdinaryDefeatNeverRunsMeleeDeath()
    {
        var (world, rules, _) = TacticSnipingTests.Create(); var m = world.BeginMutation(); List<DomainEvent> events = [];
        m.SetSimulation(m.Simulation with { BattleDeathsEnabled = true });
        m.Replace(m.Units[2] with { Soldiers = 0 });
        m.Replace(m.Officers[22] with { BaseAbilities = new(0, 0, 0, 0) });
        BattleAftermath.TerminateUnit(m, 2, new(1, false), rules, events);
        Assert.DoesNotContain(events.OfType<OfficerDiedEvent>(), e => e.Cause == OfficerDeathCause.Melee);
        Assert.DoesNotContain(m.Simulation.RandomStreams, s => s.Name == "battle-melee-death");
        world.Commit(m);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void FormationDeterminesWhetherRearRankProtectsTheOfficer(bool useRanks, bool dies)
    {
        var (world, rules, _) = TacticSnipingTests.Create(meleeRanks: useRanks); var m = world.BeginMutation(); List<DomainEvent> events = [];
        m.SetSimulation(m.Simulation with { BattleDeathsEnabled = true });
        m.Replace(m.Units[2] with { Soldiers = 1, MemberRanks = m.Units[2].MemberRanks.SetItem(22, BattleRank.Rear) });
        m.Replace(m.Officers[22] with { BaseAbilities = new(60, 40, 40, 40) });
        Roll(m, "military-tactic-double", 0); Roll(m, "battle-melee-death", 0);
        TacticCombat.TryUnit(m, 1, Target, rules, events);
        Assert.Equal(dies, events.OfType<OfficerDiedEvent>().Any(e => e.Cause == OfficerDeathCause.Melee));
        world.Commit(m);
    }

    [Fact]
    public void RestoredBattleHasIdenticalDeathsAndTransfersBelongingsToTheOldRuler()
    {
        var (fixture, baseRules, _) = TacticSnipingTests.Create();
        _ = new SimulationEngine(fixture, baseRules);
        var rules = new SimulationRules { Military = baseRules.Military, Treasures = new([new(99, default, 0)]) };
        var world = WorldState.Restore(fixture.CreateSnapshot() with { Treasures = [new(99, 22, TreasureDiscovery.Discovered, null)] });
        var setup = world.BeginMutation(rules.Abilities);
        setup.SetSimulation(setup.Simulation with { BattleDeathsEnabled = true });
        setup.Replace(setup.Units[2] with { Soldiers = 1 });
        setup.Replace(setup.Officers[22] with { BaseAbilities = new(40, 40, 40, 40) });
        Roll(setup, "military-tactic-double", 0); Roll(setup, "battle-melee-death", 0);
        world.Commit(setup);
        var restored = WorldState.Restore(JsonSerializer.Deserialize<WorldSnapshot>(CanonicalJson.Text(world.CreateSnapshot()), CanonicalJson.Options)!);
        List<DomainEvent> first = [], second = [];
        foreach (var (state, events) in new[] { (world, first), (restored, second) })
        {
            var m = state.BeginMutation(rules.Abilities);
            TacticCombat.TryUnit(m, 1, Target, rules, events);
            Assert.Equal(21, m.Treasures[99].HolderId);
            Assert.Equal(OfficerDeathCause.Melee, Assert.Single(events.OfType<OfficerDiedEvent>()).Cause);
            state.Commit(m);
        }
        Assert.Equal(CanonicalJson.Text(first), CanonicalJson.Text(second));
        StateAssert.Equal(world.CreateSnapshot(), restored.CreateSnapshot());
        StateAssert.Equal(world.CreateSnapshot(), WorldState.Restore(world.CreateSnapshot()).CreateSnapshot());
    }
}
