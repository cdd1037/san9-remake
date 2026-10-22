using San9.Tests;
using San9.Application.Persistence;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class BarbarianSelectionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SelectedBarbarianUsesOrdinaryEconomyAndKeepsThatIdentityAcrossRestore(int playerId)
    {
        var baseline = G01ScenarioTests.Fixture();
        var definition = baseline with
        {
            PlayerForceId = playerId,
            Forces = baseline.Forces.Select(f => f with { IsBarbarian = true }).ToArray()
        };
        var session = GameSession.Start(definition, 190);
        var ordinary = GameSession.Start(definition with
        {
            Forces = definition.Forces.Select(f => f.Id == playerId ? f with { IsBarbarian = false } : f).ToArray()
        }, 190);
        Assert.All(definition.Forces, f => Assert.True(f.IsBarbarian));
        Assert.False(session.Snapshot.Forces.Single(f => f.Id == playerId).IsBarbarian);
        Assert.True(session.Snapshot.Forces.Single(f => f.Id != playerId).IsBarbarian);
        Assert.False(session.View.Forces.Single(f => f.Id == playerId).IsBarbarian);
        List<ResourceChangedEvent> resources = [];
        for (int xun = 0; xun < 18; xun++)
        {
            resources.AddRange(session.AdvanceXun().Events.OfType<ResourceChangedEvent>());
            ordinary.AdvanceXun();
            Assert.Equal(CanonicalJson.Text(ordinary.Snapshot.Forces), CanonicalJson.Text(session.Snapshot.Forces));
            if (xun == 8)
            {
                var restored = SaveService.Decode(SaveService.Encode(session), definition);
                Assert.False(restored.Snapshot.Forces.Single(f => f.Id == playerId).IsBarbarian);
                StateAssert.Equal(session.Capture(), restored.Capture());
                ordinary = restored;
            }
        }
        Assert.DoesNotContain(resources, e => e.Account.Id == playerId && e.Reason.StartsWith("barbarian-"));
        Assert.Contains(resources, e => e.Account.Id == playerId && e.Reason == "quarterly-revenue");
        Assert.Contains(resources, e => e.Account.Id == playerId && e.Reason == "annual-harvest");
        Assert.Contains(resources, e => e.Account.Id != playerId && e.Reason == "barbarian-quarterly-funds");
        Assert.Contains(resources, e => e.Account.Id != playerId && e.Reason == "barbarian-annual-food");
    }
}
