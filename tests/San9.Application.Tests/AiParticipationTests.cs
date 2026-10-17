using San9.Tests;
using San9.Application.AI;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class AiParticipationTests
{
    [Fact]
    public void DormantOfficersCannotExhaustDomesticParticipantSelection()
    {
        var initial = G01ScenarioTests.Fixture();
        var template = initial.Officers.Single(p => p.State.Id == 2);
        initial = initial with { Officers = [.. initial.Officers.Select(p => p with { State = p.State with { Acted = true } }),
            .. Enumerable.Range(10, 5).Select(id => template with { NameKey = $"officer.{id}", State = template.State with { Id = id, IsDormant = true } }),
            template with { NameKey = "officer.99", State = template.State with { Id = 99 } }] };
        var session = GameSession.Start(initial, 7);
        // Initial preparation resets action flags, so retain the complete authored registry while making
        // the existing active personnel unavailable for this pure planning query.
        var snapshot = session.Snapshot with { Officers = session.Snapshot.Officers.Select(p => p.Id < 10 ? p with { Acted = true } : p).ToArray() };
        var engine = new SimulationEngine(WorldState.Restore(snapshot));
        var command = DomesticCommandSelection.Choose(snapshot, engine.GetAbilities(snapshot), engine.Validate, snapshot.Simulation.Controls[0], new("player"), Guid.NewGuid(), 0);
        var development = Assert.IsType<DevelopCommand>(command);
        Assert.Equal(new[] { 99 }, development.Development.OfficerIds);
        Assert.True(engine.Validate(new("player"), command).Accepted);
        StateAssert.Equal(snapshot, engine.Snapshot);
    }
}
