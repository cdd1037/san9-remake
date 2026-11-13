using San9.Application.Persistence;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class ServiceMeritTests
{
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void SuccessfulDevelopmentCreditsOnlyWhenEnabledAndSurvivesSave(bool enabled, int expected)
    {
        var scenario = G01ScenarioTests.Fixture();
        var session = GameSession.Start(scenario, 190, settings: new() { PersonalPeerageEnabled = enabled });
        var outcome = session.Develop(new(1, 1, [2], DomesticActivity.Commerce));
        Assert.True(outcome.Accepted);
        Assert.True(outcome.Increment > 0);
        Assert.Equal(expected, session.Snapshot.Officers.Single(p => p.Id == 2).ServiceMeritFor(1));
        var restored = SaveService.Decode(SaveService.Encode(session), scenario);
        Assert.Equal(expected, restored.Snapshot.Officers.Single(p => p.Id == 2).ServiceMeritFor(1));
        var before = restored.Snapshot.Officers.Single(p => p.Id == 2).ServiceMeritFor(1);
        Assert.False(restored.Develop(new(2, 2, [2], DomesticActivity.Commerce)).Accepted);
        Assert.Equal(before, restored.Snapshot.Officers.Single(p => p.Id == 2).ServiceMeritFor(1));
    }
}
