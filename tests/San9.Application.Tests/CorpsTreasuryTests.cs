using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class CorpsTreasuryTests
{
    [Fact]
    public void HudRootAndForceTotalsStayDistinctAcrossSaveAndReplay()
    {
        var basis = G01ScenarioTests.Fixture();
        var content = basis with { Information = basis.Information with { Corps =
            [new(101, 1, 1, new(1000, 2000)) { IsRoot = true },
             new(202, 1, 3, new(3000, 4000)), ..basis.Information.Corps.Where(c => c.OwnerId == 2)] } };
        var session = GameSession.Start(content, 42);
        var root = session.View.PlayerCorps!;
        Assert.Equal(101, root.Id);
        Assert.Equal(3000u, session.Snapshot.Corps.Single(c => c.Id == 202).Treasury.Money);
        var force = session.View.Forces.Single(f => f.Id == 1);
        Assert.Equal(root.Treasury.Money + 3000L, force.Money);
        Assert.Equal(root.Treasury.Food + 4000L, force.Food);
        Assert.Equal(force.Money, EconomicOutlook.Read(session.Snapshot, new Dictionary<int, int>(), 1).Money.Balance);
        session.BeginRecording();
        var result = session.Execute(new("player"), new DevelopCommand(Guid.NewGuid(), 1, 0, new(1, 1, [2], DomesticActivity.Commerce)));
        Assert.True(result.Submission.Accepted);
        Assert.Contains(result.Events.OfType<ResourceChangedEvent>(), e => e.Account == new ResourceAccount(ResourceOwnerKind.Corps, 101) && e.Actual < 0);
        Assert.Equal(3000u, session.Snapshot.Corps.Single(c => c.Id == 202).Treasury.Money);
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        Assert.Equal(session.View.PlayerCorps, restored.View.PlayerCorps);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), content, GameSession.EngineFactory(content));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
        var serialized = JsonNode.Parse(CanonicalJson.Text(session.Snapshot.Forces[0]))!.AsObject();
        Assert.False(serialized.ContainsKey("money"));
        Assert.False(serialized.ContainsKey("food"));
    }

    [Theory]
    [InlineData("missing-root")]
    [InlineData("duplicate-root")]
    [InlineData("null-treasury")]
    public void InvalidAccountsAreRejectedByContentAndWorld(string defect)
    {
        var content = G01ScenarioTests.Fixture();
        var session = GameSession.Start(content, 42);
        var corps = content.Information.Corps;
        corps = defect switch
        {
            "missing-root" => corps.Where(c => c.OwnerId != 1).ToArray(),
            "duplicate-root" => [..corps, corps[0] with { Id = 999 }],
            _ => [corps[0] with { Treasury = null! }, ..corps.Skip(1)]
        };
        Assert.Throws<InvalidDataException>(() => (content with { Information = content.Information with { Corps = corps } }).Validate());
        Assert.Throws<ArgumentException>(() => WorldState.Restore(session.Capture().World with { Corps = corps }));
    }

    [Theory]
    [InlineData("treasury")]
    [InlineData("isRoot")]
    public void SavedCorpsRequireExplicitAccountAndIdentity(string field)
    {
        var corps = G01ScenarioTests.Fixture().Information.Corps[0];
        var json = JsonNode.Parse(CanonicalJson.Text(corps))!.AsObject();
        Assert.True(json.Remove(field));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CorpsState>(json.ToJsonString(), CanonicalJson.Options));
    }
}
