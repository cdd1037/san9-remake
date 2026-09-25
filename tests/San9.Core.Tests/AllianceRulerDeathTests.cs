using San9.Core.Domain;
using San9.Core.Simulation;
namespace San9.Core.Tests;
public sealed class AllianceRulerDeathTests
{
    [Theory]
    [InlineData(1, false)] [InlineData(2, false)] [InlineData(3, false)]
    [InlineData(1, true)] [InlineData(2, true)] [InlineData(3, true)]
    public void RulerDeathEndsLeaderOrTargetAllianceEvenWithSuccessor(int victimForce, bool execution)
    {
        int victim = victimForce * 10 + 1, captor = victimForce == 3 ? 2 : 3;
        var forces = Enumerable.Range(1, 3).Select(i => new ForceState(i, 100) { LeaderId = i * 10 + 1 });
        var cities = Enumerable.Range(1, 3).Select(i => new SettlementState(i, i, 0, 100, 100, 0, 0, 0, 100, 0, 100));
        var people = Enumerable.Range(1, 3).SelectMany(i => new[] {
            new OfficerState(i * 10 + 1, i, execution && i == victimForce ? captor : i, new(50,50,50,50), false)
                { Status = execution && i == victimForce ? OfficerStatus.Captive : OfficerStatus.Ruler, BirthYear = i == victimForce ? 90 : 170 },
            new OfficerState(i * 10 + 2, i, i, new(50,50,50,50), false) { BirthYear = 170 } });
        var world = WorldState.Create(new(190,1,1),42,forces,cities,people,alliance:new(3,1,[1,2],20), corps: Enumerable.Range(1, 3).Select(i => new San9.Core.Domain.CorpsState(i, i, i * 10 + 1, new(10000, 10000)) { IsRoot = true }).ToArray());
        var m = world.BeginMutation();
        m.SetSimulation(m.Simulation with { Boundary = SimulationBoundary.Stable });
        m.SetClock(new(191,1,1));
        List<DomainEvent> events=[];
        if (execution) Assert.Equal(CommandRejection.None, PrisonerExecution.Start(m,new(Guid.NewGuid(),captor,0,victim),new(),events));
        else NaturalMortality.Apply(m,new(),events);
        Assert.Equal(OfficerStatus.Dead,m.Officers[victim].Status);
        Assert.Equal(victimForce * 10 + 2,m.Forces[victimForce].LeaderId);
        Assert.False(m.Forces[victimForce].IsEliminated);
        Assert.Empty(events.OfType<AllianceVictoryEvent>());
        if (victimForce == 2)
        {
            Assert.NotNull(m.Simulation.Alliance);
            Assert.Empty(events.OfType<AllianceEndedEvent>());
        }
        else
        {
            Assert.Null(m.Simulation.Alliance);
            Assert.Equal(victimForce == 1 ? AllianceEndReason.LeaderDied : AllianceEndReason.TargetDied,
                Assert.Single(events.OfType<AllianceEndedEvent>()).Reason);
        }
        int count = events.Count;
        Assert.False(OfficerDeath.Remove(m, victim, OfficerDeathCause.Natural, PersonnelRulebook.Empty, events, false));
        Assert.Equal(count, events.Count);
        m.MaintainDerivedState(events);
        world.Commit(m);
    }

}
