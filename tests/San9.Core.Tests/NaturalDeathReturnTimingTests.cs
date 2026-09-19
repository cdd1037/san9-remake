using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class NaturalDeathReturnTimingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void QuarterlyDeathCancelsRecruitmentBeforeTodaysCommands(bool useQuarterlyEntry)
    {
        var places = new PersonnelRulebook([new(10, new(0, 0)), new(20, new(1000, 0))]);
        var world = WorldState.Create(new(190, 1, 1), 42,
            [new(1, 1000, 1000, 0) { LeaderId = 1 }, new(2, 1000, 1000, 0) { LeaderId = 2 }],
            [new(10, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new(20, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(1, 1, 10, new(70, 70, 70, 70), false) { Status = OfficerStatus.Ruler },
             new(2, 2, 20, new(70, 70, 70, 70), false) { Status = OfficerStatus.Ruler },
             new(3, 1, 10, new(70, 70, 70, 70), false),
             new(4, null, 20, new(70, 70, 70, 70), false) { Status = OfficerStatus.Free, BirthYear = 90 }]);
        var mutation = world.BeginMutation();
        mutation.SetClock(new(191, 1, 1));
        var mission = new PersistentTask(Guid.NewGuid(), PersonnelMissions.RecruitKind, "system", 1, 3, 10,
            new(190, 12, 26), new(191, 3, 1), 5, 0)
        { Journey = new(20, 4, null) { Route = new(new(0, 0), new(1000, 0), false) } };
        mutation.SetSimulation(mutation.Simulation with { Tasks = [mission] });
        if (useQuarterlyEntry) NaturalMortality.Apply(mutation, new() { Personnel = places }, []);
        else OfficerDeath.Resolve(mutation, 4, OfficerDeathCause.Natural, new() { Personnel = places }, [], afterDailyCommands: false);
        var returned = Assert.Single(mutation.Simulation.Tasks);
        Assert.Equal(PersonnelReturns.Kind, returned.Kind);
        Assert.Equal(new GameClock(191, 1, 5), returned.Due);
        Assert.False(returned.Journey!.ReturnLeg!.AfterDailyCommands);
    }
}
