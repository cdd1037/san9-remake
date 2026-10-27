using System.Text.Json;
using San9.Application.Persistence;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;
using Xunit.Abstractions;

namespace San9.Application.Tests;

public sealed partial class OriginalAiMilitaryTests
{
    [Fact]
    public void OrdinaryMilitaryUsesImportedLinksWithoutGeographicCityConnections()
    {
        var content = Fixture() with { CityConnections = [] };
        var harness = new Harness(content);
        var report = harness.Run(OriginalAiWork.Attack, 1);
        Assert.Equal(1, report.Submitted);
        Assert.Equal(2, Assert.IsType<DeployUnitCommand>(Assert.Single(harness.Commands)).Order.SettlementId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SessionLoadsAndRestoresOnlyTheImportedMilitaryLinks(bool empty)
    {
        var content = Fixture() with { MilitaryCityConnections = empty ? [] : [new(2, 1)] };
        var session = GameSession.Start(content, 190);
        var network = session.View.Rules.MilitaryCities;
        Assert.Equal(content.MilitaryCityConnections.ToArray(), network.Connections.ToArray());
        Assert.False(network.AreNeighbors(1, 2));
        Assert.NotEmpty(session.View.CityConnections);
        var changed = session.View.ForWorld(session.View.World with
        {
            Settlements = session.View.World.Settlements.Select(s => s with { OwnerId = 1, Soldiers = 1 }).ToArray()
        });
        Assert.Same(network, changed.Rules.MilitaryCities);
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        Assert.NotSame(network, restored.View.Rules.MilitaryCities);
        Assert.Equal(network.Connections.ToArray(), restored.View.Rules.MilitaryCities.Connections.ToArray());
    }
}

/// <summary>Uses the public fixture by default; an explicitly supplied converted map also verifies the research graph.</summary>
public sealed class MilitaryCityNetworkConvertedMapTests(ITestOutputHelper output)
{
    [Fact]
    public void SessionLoadsConvertedMilitaryLinksWithoutGeneratingThem()
    {
        string? path = Environment.GetEnvironmentVariable("SAN9_MILITARY_NEIGHBOR_SCENARIO");
        var content = string.IsNullOrWhiteSpace(path) ? G01ScenarioTests.Fixture() :
            JsonSerializer.Deserialize<ScenarioInitialState>(File.ReadAllText(path), ScenarioContentFiles.DataJsonOptions)!;
        var session = GameSession.Start(content, 190);
        var network = session.View.Rules.MilitaryCities;
        output.WriteLine($"ImportedDirectedLinks={network.Connections.Length}");
        Assert.Equal(content.MilitaryCityConnections.ToArray(), network.Connections.ToArray());
        Assert.Equal(new MilitaryCityNetworkDiagnostics(0, 0, 0, 0), network.Diagnostics);
        string? reference = Environment.GetEnvironmentVariable("SAN9_MILITARY_NEIGHBOR_REFERENCE");
        if (!string.IsNullOrWhiteSpace(reference))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reference));
            var expected = document.RootElement.GetProperty("graph").EnumerateArray()
                .Select(pair => new CityConnection(pair[0].GetInt32(), pair[1].GetInt32())).ToArray();
            Assert.Equal(expected, network.Connections.ToArray());
        }
    }
}
