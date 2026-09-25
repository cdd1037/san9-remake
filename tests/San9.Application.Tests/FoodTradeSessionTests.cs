using System.Collections.Immutable;
using System.Text.Json;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class FoodTradeSessionTests
{
    private static ScenarioInitialState Definition(int chance = 100) => new(ScenarioInitialState.CurrentSchemaVersion, "food-trade", 190, 2, 1, 1,
        [new(1, "force", 1, 100) { LeaderId = 1, BaseTrust = 100 }],
        [new(1, "home", 1, 0, 0, 0, 100, 100, 0, []) { MerchantPresenceChance = chance, CorpsId = 1 },
         new(2, "corps", 1, 1, 0, 0, 100, 100, 0, []) { MerchantPresenceChance = 100, CorpsId = 2 }],
        new() { [1] = "home", [2] = "corps" }, new() { [1] = 1, [2] = 2 })
    {
        Officers = [new("ruler", new(1, 1, 1, new(80, 80, 80, 80), false) { Status = OfficerStatus.Ruler }),
            new("officer", new(2, 1, 2, new(80, 80, 80, 80), false)), new("reserve", new(3, 1, 2, new(80, 80, 80, 80), false))],
        Information = InformationDefinitions.Empty with { Corps = [new(1, 1, 1, new(50000, 10000)) { IsRoot = true }, new(2, 1, 2, new(10000, 10000))] },
        FoodExchangeRate = 7
    };

    [Theory]
    [InlineData(FoodTradeDirection.Buy)]
    [InlineData(FoodTradeDirection.Sell)]
    public void PublicTradeUsesIndependentAccountAndSurvivesSaveReplayAndXunRecovery(FoodTradeDirection direction)
    {
        var d = Definition(); var session = GameSession.Start(d, 190); session.BeginRecording();
        Assert.Contains("merchant", session.View.Settlements.Single(s => s.Id == 2).Icons);
        var before = session.Snapshot; var quote = session.GetFoodTradeQuote(1, 2, [2], direction);
        Assert.True(quote.Available); Assert.Equal(new(ResourceOwnerKind.Corps, 2), quote.Account);
        Assert.Equal(6000u, quote.MaximumFoodAmount); Assert.Equal(before.StateHash, session.Snapshot.StateHash);
        var command = new FoodTradeCommand(Guid.NewGuid(), 1, 0, 2, [2], direction, 8);
        var request = session.Request(new CommandOperation(command), id: command.CommandId);
        var result = session.Apply(request); Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.Single(result.Events.OfType<FoodTradeCompletedEvent>());
        Assert.Equal(before.Corps.Single(c => c.IsRoot).Treasury.Money, session.Snapshot.Corps.Single(c => c.IsRoot).Treasury.Money);
        Assert.Equal(before.Corps.Single(c => c.IsRoot).Treasury.Food, session.Snapshot.Corps.Single(c => c.IsRoot).Treasury.Food);
        var treasury = session.Snapshot.Corps.Single(c => c.Id == 2).Treasury!;
        uint previous = before.Corps.Single(c => c.Id == 2).Treasury!.Money;
        Assert.Equal(direction == FoodTradeDirection.Buy ? previous - 2 : previous + 1, treasury.Money);
        Assert.Equal(direction == FoodTradeDirection.Buy ? 10008u : 9992u, treasury.Food);
        Assert.Equal(SessionDisposition.AlreadyProcessed, session.Apply(request).Disposition);
        Assert.Equal(FoodTradeRejection.AlreadyUsed, session.GetFoodTradeQuote(1, 2, [3], direction).Rejection);
        var restored = SaveService.Decode(SaveService.Encode(session), d);
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(restored));
        session.AdvanceXun(); restored.AdvanceXun();
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(restored));
        Assert.True(session.GetFoodTradeQuote(1, 2, [2], direction).Available);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), d, GameSession.EngineFactory(d));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(replay.Session));
    }

    [Fact]
    public void InvalidCommandsAndDelegatedCorpsCannotBypassAuthorization()
    {
        var session = GameSession.Start(Definition(), 190);
        var command = new FoodTradeCommand(Guid.NewGuid(), 1, 0, 2, [2], FoodTradeDirection.Buy, 7);
        foreach (var invalid in new[] { command with { FoodAmount = 0 }, command with { FoodAmount = 6001 }, command with { OfficerIds = [2, 2] },
            command with { OfficerIds = [1] }, command with { Direction = (FoodTradeDirection)99 }, command with { SettlementId = 99 } })
        {
            string hash = session.Snapshot.StateHash;
            Assert.False(session.Execute(new("player"), invalid).Submission.Accepted); Assert.Equal(hash, session.Snapshot.StateHash);
        }
        Assert.True(session.Execute(new("player"), new SetCorpsControlCommand(Guid.NewGuid(), 1, 0, 2, CorpsControlMode.Automatic)).Submission.Accepted);
        command = command with { Sequence = 1 };
        string delegated = session.Snapshot.StateHash;
        Assert.Equal(CommandRejection.NotActiveController, session.Execute(new("player"), command).Submission.Rejection);
        Assert.Equal(delegated, session.Snapshot.StateHash);
        Assert.True(session.Execute(new(CorpsControl.ControllerId(2)), command).Submission.Accepted);
    }

    [Fact]
    public void VisitingMerchantPreservesTrainingIconAndTradeStateWithoutReroll()
    {
        var d = Definition(20);
        d = d with { Settlements = d.Settlements.Select(s => s with { Icons = ["training"] }).ToArray() };
        var session = GameSession.Start(d, 190);
        for (int xun = 0; xun < 8; xun++)
        {
            bool present = session.Snapshot.Settlements.Single(s => s.Id == 1).MerchantPresent;
            Assert.Equal(["training"], session.View.Settlements.Single(s => s.Id == 1).Icons);
            Assert.Equal(["merchant"], session.View.Settlements.Single(s => s.Id == 2).Icons);
            Assert.Equal(present, session.GetFoodTradeQuote(1, 1, [1], FoodTradeDirection.Buy).Available);
            var restored = SaveService.Decode(SaveService.Encode(session), d);
            Assert.Equal(SaveService.Encode(session), SaveService.Encode(restored));
            Assert.Equal(["training"], restored.View.Settlements.Single(s => s.Id == 1).Icons);
            Assert.Equal(["merchant"], restored.View.Settlements.Single(s => s.Id == 2).Icons);
            session.AdvanceXun(); restored.AdvanceXun();
            Assert.Equal(SaveService.Encode(session), SaveService.Encode(restored));
        }
        var random = new StableRandom(StableRandom.DeriveSeed(190, "city-merchants"));
        for (int i = 0; i < 9; i++) random.NextUInt64();
        Assert.Equal(random.State, session.Snapshot.Simulation.RandomStreams.Single(s => s.Name == "city-merchants").State);
    }

    [Fact]
    public void MerchantContractIsRequiredValidatedAndFingerprintRelevant()
    {
        var d = Definition(); var changed = d with { Settlements = [d.Settlements[0] with { MerchantPresenceChance = 20 }, d.Settlements[1]] };
        Assert.NotEqual(GameSession.Start(d, 1).ContentFingerprint, GameSession.Start(changed, 1).ContentFingerprint);
        foreach (int chance in new[] { -1, 101 })
            Assert.Throws<InvalidDataException>(() => (d with { Settlements = [d.Settlements[0] with { MerchantPresenceChance = chance }, d.Settlements[1]] }).Validate());
        Assert.Throws<InvalidDataException>(() => (d with { Settlements = [d.Settlements[0] with { Kind = SettlementKind.Facility }, d.Settlements[1]] }).Validate());
        Assert.Throws<InvalidDataException>(() => (d with { Settlements = [d.Settlements[0] with { Icons = ["merchant"] }, d.Settlements[1]] }).Validate());
        var options = San9.Content.Packages.ScenarioContentFiles.DataJsonOptions;
        var json = JsonSerializer.SerializeToNode(d, options)!; json["settlements"]![0]!.AsObject().Remove("merchantPresenceChance");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ScenarioInitialState>(json.ToJsonString(), options));
    }
}
