using System.Collections.Immutable;
using San9.Application.AI;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class ImmediatePlanningTests
{
    private static AllocationCandidate Departure() => new("ai:1", 1,
        new DeployUnitCommand(Guid.Empty, 1, 0, 1, [1], 5000, 7, new(MilitaryOrderKind.AttackSettlement, null, 2)),
        [1], 2, 5000, 10, 0, 1, 2);

    [Fact]
    public void TrainingSeparatesImmediateEffectFromActorRecovery()
    {
        var abilities = new Dictionary<int, OfficerAbilities> { [1] = new(80, 50, 30, 90), [2] = new(30, 30, 30, 70) }.ToImmutableDictionary();
        var options = TrainingPlans.Enumerate(10000, 50, 100, [1, 2], [1], abilities).ToArray();
        var other = options.Single(p => p.Trainers.SequenceEqual([2]));
        var same = options.Single(p => p.Trainers.SequenceEqual([1]));
        Assert.Equal(0, other.DepartureDays);
        Assert.Equal(10, same.DepartureDays);
        Assert.Equal(50 + DomesticDevelopment.TrainingIncrement(10000, 50, 100, [abilities[2]]), other.Morale);
        var schedule = TrainingPlans.Schedule(Departure(), other);
        Assert.IsType<DevelopCommand>(schedule.Intent);
        Assert.Equal(0, schedule.Continuation!.DelayDays);
        Assert.IsType<DeployUnitCommand>(schedule.Continuation.Action.Intent);
        Assert.Equal(new[] { 1, 2 }, schedule.Participants.Order());
        Assert.Equal(2, schedule.ImmediateSteps);
        Assert.Equal(1, TrainingPlans.Schedule(Departure(), same).ImmediateSteps);
    }

    [Fact]
    public void DelayedTrainingPlanLeavesFurtherPreparationToTheNextObservation()
    {
        var schedule = TrainingPlans.Schedule(Departure(), new([2], 80, 10));
        Assert.Equal(10, schedule.Continuation!.DelayDays);
        Assert.IsType<DeployUnitCommand>(schedule.Continuation.Action.Intent);
        Assert.Null(schedule.Continuation.Action.Continuation);
    }

    [Fact]
    public void TrainingIncludesHighIdSpecialistsAndValuesSaturationAndActorCost()
    {
        var world = GameSession.Start(G01ScenarioTests.Fixture(), 42).Snapshot;
        var abilities = Enumerable.Range(1, 6).ToImmutableDictionary(id => id,
            id => id == 6 ? new OfficerAbilities(10, 10, 10, 100) : new(90, 90, 90, 20));
        var home = new DomesticSettlementObservation(1, 1, 1000, 10000, 10000, 99, 1000, 100, 100, [1, 2, 3, 4, 5, 6]);
        var observation = new DomesticObservation(AiObservation.FromSnapshot(1, world), [home]) { Abilities = abilities, Trust = 100 };
        var one = new DevelopmentCommand(1, 1, [6], DomesticActivity.Train);
        var many = new DevelopmentCommand(1, 1, [1, 2, 3, 4, 6], DomesticActivity.Train);
        Assert.Contains(DomesticPlanner.Plan(observation, new(1000, 0), 0).Candidates,
            c => c.Activity == DomesticActivity.Train && c.OfficerIds.SequenceEqual([6]));
        Assert.True(DomesticPlanner.Value(one, observation) > DomesticPlanner.Value(many, observation));
    }

    [Fact]
    public void OptionalRewardKeepsTheSelectedMissionAndItsFundingInOneSequence()
    {
        var world = GameSession.Start(G01ScenarioTests.Fixture(), 42).Snapshot;
        var domestic = new DomesticObservation(AiObservation.FromSnapshot(1, world),
            [new(1, 1, 110, 10000, 10000, 100, 1000, 100, 100, [1])]);
        var economy = new EconomicObservation(domestic, [new(1, 110, 10000, 100)], [], 0);
        var mission = Departure();
        var reward = new AllocationCandidate("ai:1", 1, new RewardOfficerCommand(Guid.Empty, 1, 0, 1), [], 2, 200, 100, 0)
            { BeforeDeparture = [1], Configuration = true };
        var result = SharedAllocator.Decide([("ai:1", economy)], [reward, mission], [], 1000, new HashSet<string>(), new HashSet<string>());
        Assert.IsType<RewardOfficerCommand>(result.Candidate!.Intent);
        Assert.Equal(mission, result.Candidate.Continuation!.Action);
        var poor = economy with { Accounts = [economy.Accounts[0] with { Money = 100 }] };
        Assert.Equal(mission, SharedAllocator.Decide([("ai:1", poor)], [reward, mission], [], 1000,
            new HashSet<string>(), new HashSet<string>()).Candidate);
    }
}
