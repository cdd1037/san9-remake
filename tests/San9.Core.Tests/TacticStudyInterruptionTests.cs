using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class TacticStudyInterruptionTests
{
    [Theory]
    [InlineData("death")]
    [InlineData("capture")]
    [InlineData("city-lost")]
    public void InterruptedStudyCannotGrantAndReleasesOccupation(string interruption)
    {
        var places = new PersonnelRulebook([new(1, new(2, 2)), new(2, new(12, 2)), new(3, new(22, 2))],
            tacticCourses: [new(1, 9000)]);
        var military = new MilitaryRulebook(new(6, 1, [0, 0, 0, 0, 0, 0], [new(0, 1, null)], [],
            [new(1, new(2, 2), new(1, 1)), new(2, new(12, 2), new(1, 1)), new(3, new(22, 2), new(1, 1))])
        { Tactics = [new(9000, 0, TacticEffect.CounterConfusion, 0, 0, 0, false, false, [])] });
        var rules = new SimulationRules { Personnel = places, Military = military };
        var world = WorldState.Create(new(190, 1, 1), 10,
            [new(1, 10000, 100000, 100) { LeaderId = 1 }, new(2, 10000, 100000, 100) { LeaderId = 3 }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new(2, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100),
             new(3, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(1, 1, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new(2, 1, 1, new(50, 50, 50, 50), false),
             new(3, 2, 3, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }],
            controls: [new("player", 1, ControllerKind.Player)]);
        var engine = new SimulationEngine(world, rules);
        for (int i = 0; i < 9; i++) { engine.EndControl(new("player")); engine.AdvanceXun(); }
        Assert.True(engine.Execute(new("player"), new ChooseTacticStudentCommand(Guid.NewGuid(), 1, 0, 1, 2)).Submission.Accepted);
        var mutation = world.BeginMutation();
        List<DomainEvent> changes = [];
        if (interruption == "death") OfficerDeath.Resolve(mutation, 2, OfficerDeathCause.Natural, rules, changes);
        else if (interruption == "capture") PersonnelTransitions.Capture(mutation, 2, 3, null, places, changes);
        else
        {
            mutation.Replace(mutation.Settlements[1] with { OwnerId = 2 });
            PersonnelReturns.Repatriate(mutation, 2, 1, mutation.Simulation.Tasks.Single(), places, changes);
        }
        world.Commit(mutation);
        Assert.Single(changes.OfType<TaskCancelledEvent>());
        Assert.DoesNotContain(engine.Snapshot.Simulation.Tasks, t => t.Kind == "tactic-study");
        for (int i = 0; i < 9; i++) { engine.EndControl(new("player")); engine.AdvanceXun(); }
        Assert.DoesNotContain(9000, engine.Snapshot.Officers.Single(p => p.Id == 2).LearnedTactics);
        Assert.All(engine.Snapshot.Simulation.Tasks, t => Assert.Equal("personnel-return", t.Kind));
    }
}
