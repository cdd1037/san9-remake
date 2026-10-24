using San9.Tests;
using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class DuelCombatIntegrationTests
{
    private static ulong Seed()
    {
        const ulong seed = 16UL;
        Assert.True(new StableRandom(StableRandom.DeriveSeed(seed, "duel-trigger")).NextUInt64() % 100 < 3);
        Assert.True(new StableRandom(StableRandom.DeriveSeed(seed, "duel-death")).NextUInt64() % 100 < 30);
        return seed;
    }

    private static SimulationEngine Engine(bool deaths = false, int opportunity = 0, int defensiveChance = 0, bool counterOnly = false, bool laterBattle = false, int cooldown = 0)
    {
        var combat = new CombatProfile(0, 0, 0, 1000, 50);
        var military = new MilitaryRulebook(new(4, 1, [0, 0, 0, 0], [new(0, 1, null)],
            [new(1, 5, 0, 100000, 3, new(1, 1), TravelMode.Land)
                { CaptureRanks = [BattleRank.Front, BattleRank.Front, BattleRank.Rear], Combat = combat, Attack = new(1, false), Tactics = new(opportunity, 2, [], []) }],
            [new(1, new(2, 2), new(1, 1)) { Combat = combat }, new(2, new(17, 2), new(1, 1)) { Combat = combat }])
        {
            DefensiveTacticChance = defensiveChance, BattleDeathsAllowed = true, DuelCapturesAllowed = false,
            Tactics = [new(9000, 7, TacticEffect.CounterConfusion, 0, 0, 0, false, false, [])]
        });
        var people = new[]
        {
            DuelContestTests.Person(11, 1, 79) with { Status = OfficerStatus.Ruler, TacticCooldown = cooldown, LearnedTactics = [9000], EquippedTacticId = 9000 },
            DuelContestTests.Person(21, 2, 20),
            DuelContestTests.Person(22, 2, 50) with { Status = OfficerStatus.Ruler },
            DuelContestTests.Person(23, 2, 30)
        };
        var world = DuelContestTests.World(people, Seed());
        var change = world.BeginMutation();
        if (counterOnly) change.Replace(change.Officers[11] with { Personality = 3, BaseAbilities = new(40, 50, 50, 50) });
        change.Replace(change.Forces[1] with { LeaderId = 11 }); change.Replace(ResourceAccounts.Root(change, 1) with { LeaderId = 11 });
        change.Replace(change.Forces[2] with { LeaderId = 22 }); change.Replace(ResourceAccounts.Root(change, 2) with { LeaderId = 22 });
        change.Replace(change.Units[1] with { Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 2 } });
        change.Replace(change.Units[2] with { MemberRanks = ImmutableDictionary<int, BattleRank>.Empty
            .Add(21, BattleRank.Rear).Add(22, BattleRank.Front).Add(23, BattleRank.Rear) });
        change.SetSimulation(change.Simulation with { BattleDeathsEnabled = deaths, Controls = [new("player", 2, ControllerKind.Player)] });
        world.Commit(change);
        var engine = new SimulationEngine(world, EconomyRulebook.Empty, PersonnelRulebook.Empty, TreasureRulebook.Empty, military: military);
        if (laterBattle)
        {
            var snapshot = engine.Snapshot;
            var laterAttacker = snapshot.Units.Single(u => u.Id == 1) with { Id = 3, Position = new(11, 2),
                CommanderId = 31, MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(31, BattleRank.Rear),
                Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 4 } };
            var laterDefender = laterAttacker with { Id = 4, OwnerId = 2, Position = new(12, 2), CommanderId = 41,
                MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(41, BattleRank.Rear), Order = null };
            engine = new SimulationEngine(WorldState.Restore(snapshot with
            {
                Simulation = snapshot.Simulation with { NextUnitId = 5 },
                Officers = [.. snapshot.Officers, DuelContestTests.Person(31, 1) with { UnitId = 3 },
                    DuelContestTests.Person(41, 2) with { UnitId = 4 }],
                Units = [.. snapshot.Units, laterAttacker, laterDefender]
            }), EconomyRulebook.Empty, PersonnelRulebook.Empty, TreasureRulebook.Empty, military: military);
        }
        engine.EndControl(new("player"));
        return engine;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void PassiveEquipmentAndFailedTacticOpportunityDoNotPreventDuel(int opportunity)
    {
        var engine = Engine(opportunity: opportunity);
        var result = engine.AdvanceXun();
        Assert.Contains(result.Events.OfType<DuelStartedEvent>(), e => e.Clock.Day == 2);
        Assert.Contains(result.Events.OfType<DuelCompletedEvent>(), e => e.Clock.Day == 2);
        Assert.Empty(result.Events.OfType<TacticActivatedEvent>());
        Assert.Equal(SimulationBoundary.Stable, engine.Snapshot.Simulation.Boundary);
        Assert.Equal(new(190, 1, 11), engine.Snapshot.Clock);
        Assert.Equal(10, result.Events.OfType<StageCompletedEvent>().Count(e => e.Stage == ResolutionStage.DailyMapUpdate));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(100, true)]
    public void CounterOpportunityGateAlsoControlsCounterDuel(int defensiveChance, bool expected)
    {
        var engine = Engine(defensiveChance: defensiveChance, counterOnly: true);
        var result = engine.AdvanceXun();
        var duels = result.Events.OfType<DuelStartedEvent>().ToArray();
        Assert.Equal(expected, duels.Length > 0);
        Assert.All(duels, duel => Assert.Equal(2, duel.Roster.AttackingUnitId));
        if (!expected) Assert.DoesNotContain(engine.Snapshot.Simulation.RandomStreams, s => s.Name == "duel-trigger");
    }

    [Fact]
    public void SuccessionContinuationRetainsRemainingBattlesAndUsedAttackers()
    {
        var engine = Engine(deaths: true, laterBattle: true);
        var paused = engine.AdvanceXun();
        Assert.NotNull(engine.GetDuelSuccessionPrompt());
        Assert.Equal(new[] { 1, 2 }, paused.Events.OfType<BattleDamageEvent>().Where(e => e.Clock.Day == 2).Select(e => e.AttackerId).Order());
        var response = engine.Execute(new("player"), new ChooseDuelSuccessorCommand(Guid.NewGuid(), 2,
            engine.Snapshot.Simulation.NextSequence, 22, 21));
        Assert.True(response.Submission.Accepted);
        Assert.Equal(new[] { 3, 4 }, response.Events.OfType<BattleDamageEvent>().Where(e => e.Clock.Day == 2).Select(e => e.AttackerId).Order());
        Assert.Single(response.Events.OfType<StageCompletedEvent>(), e => e.Clock.Day == 2 && e.Stage == ResolutionStage.DailyMapUpdate);
    }

    [Fact]
    public void ChoosingAnotherDefeatedParticipantRechecksHisNewRulerAndCommanderRole()
    {
        var engine = Engine(deaths: true);
        engine.AdvanceXun();
        var prompt = Assert.IsType<DuelSuccessionPrompt>(engine.GetDuelSuccessionPrompt());
        Assert.Contains(23, prompt.CandidateIds);
        var result = engine.Execute(new("player"), new ChooseDuelSuccessorCommand(Guid.NewGuid(), 2,
            engine.Snapshot.Simulation.NextSequence, 22, 23));
        Assert.True(result.Submission.Accepted);
        Assert.Equal(23, engine.Snapshot.Forces.Single(f => f.Id == 2).LeaderId);
        Assert.Equal(23, engine.Snapshot.Units.Single(u => u.Id == 2).CommanderId);
        Assert.Equal(OfficerStatus.Ruler, engine.Snapshot.Officers.Single(p => p.Id == 23).Status);
        Assert.Single(result.Events.OfType<OfficerDiedEvent>());
        Assert.Single(result.Events.OfType<DuelCompletedEvent>());
        Assert.Null(engine.GetDuelSuccessionPrompt());
        Assert.Equal(SimulationBoundary.Stable, engine.CaptureStableState().Simulation.Boundary);
    }

    [Fact]
    public void LastDaySuccessionCompletesTheXunOnlyAfterTheAnswer()
    {
        var engine = Engine(deaths: true, cooldown: 10);
        var paused = engine.AdvanceXun();
        Assert.NotNull(engine.GetDuelSuccessionPrompt());
        Assert.Equal(new(190, 1, 11), engine.Snapshot.Clock);
        Assert.DoesNotContain(paused.Events, e => e is XunAdvancedEvent || e is StageCompletedEvent { Stage: ResolutionStage.XunUpdate });
        var result = engine.Execute(new("player"), new ChooseDuelSuccessorCommand(Guid.NewGuid(), 2,
            engine.Snapshot.Simulation.NextSequence, 22, 21));
        Assert.True(result.Submission.Accepted);
        Assert.Single(result.Events.OfType<XunAdvancedEvent>());
        Assert.Single(result.Events.OfType<StageCompletedEvent>(), e => e.Stage == ResolutionStage.DailyMapUpdate);
        Assert.Single(result.Events.OfType<StageCompletedEvent>(), e => e.Stage == ResolutionStage.XunUpdate);
        Assert.Equal(new(190, 1, 11), engine.CaptureStableState().Clock);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlayerSuccessionResumesSameDayWithoutRepeatingDamageOrDuelRandom(bool automatic)
    {
        var engine = Engine(deaths: true);
        var arrival = engine.AdvanceXun();
        var prompt = Assert.IsType<DuelSuccessionPrompt>(engine.GetDuelSuccessionPrompt());
        Assert.Equal(22, prompt.RulerId);
        Assert.Equal(new(190, 1, 2), engine.Snapshot.Clock);
        Assert.Throws<InvalidOperationException>(() => engine.CaptureStableState());
        Assert.DoesNotContain(arrival.Events, e => e is StageCompletedEvent { Stage: ResolutionStage.DailyMapUpdate });
        Assert.DoesNotContain(arrival.Events, e => e is OfficerDiedEvent);
        var before = engine.Snapshot;
        Assert.Throws<InvalidOperationException>(() => engine.AdvanceXun());
        Assert.Equal(CommandRejection.DecisionPending, engine.Execute(new("player"),
            new WaitCommand(Guid.NewGuid(), 2, engine.Snapshot.Simulation.NextSequence)).Submission.Rejection);
        var answer = new ChooseDuelSuccessorCommand(Guid.NewGuid(), 2, engine.Snapshot.Simulation.NextSequence, 22, automatic ? null : 21);
        Assert.Equal(CommandRejection.NotActiveController, engine.Execute(new("other"), answer).Submission.Rejection);
        Assert.Equal(CommandRejection.InvalidDecision, engine.Execute(new("player"), answer with { SuccessorId = 11 }).Submission.Rejection);
        StateAssert.Equal(before, engine.Snapshot);
        var deathRandom = engine.Snapshot.Simulation.RandomStreams.Single(s => s.Name == "duel-death");
        var result = engine.Execute(new("player"), answer);
        Assert.True(result.Submission.Accepted);
        Assert.Null(engine.GetDuelSuccessionPrompt());
        Assert.Equal(automatic ? 23 : 21, engine.Snapshot.Forces.Single(f => f.Id == 2).LeaderId);
        Assert.Equal(OfficerStatus.Dead, engine.Snapshot.Officers.Single(p => p.Id == 22).Status);
        Assert.Equal(deathRandom, engine.Snapshot.Simulation.RandomStreams.Single(s => s.Name == "duel-death"));
        Assert.DoesNotContain(result.Events.OfType<BattleDamageEvent>(), e => e.Clock.Day == 2);
        Assert.DoesNotContain(result.Events.OfType<DuelStartedEvent>(), e => e.Clock.Day == 2);
        Assert.Single(result.Events.OfType<DuelCompletedEvent>(), e => e.Clock.Day == 2);
        Assert.Equal(10, arrival.Events.Concat(result.Events).OfType<StageCompletedEvent>().Count(e => e.Stage == ResolutionStage.DailyMapUpdate));
        Assert.Single(result.Events.OfType<XunAdvancedEvent>());
        Assert.Equal(SimulationBoundary.Stable, engine.CaptureStableState().Simulation.Boundary);
    }
}
