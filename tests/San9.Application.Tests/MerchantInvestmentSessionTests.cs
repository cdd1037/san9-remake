using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class MerchantInvestmentSessionTests
{
    private static ScenarioInitialState Definition(ResourceKind resource = ResourceKind.Money) => new(ScenarioInitialState.CurrentSchemaVersion, "merchant", 190, 2, 1, 1,
        [new(1, "force", 1, 50000, 10000, 100) { LeaderId = 1, BaseTrust = 100 }],
        [new(1, "home", 1, 0, 0, 0, 100, 100, 0, [])], new() { [1] = "home" }, new() { [1] = 1 })
    {
        Officers = [new("ruler", new(1, 1, 1, new(0, 0, 80, 0), false) { Status = OfficerStatus.Ruler }),
            new("first", new(2, 1, 1, new(0, 0, 80, 0), false)), new("second", new(3, 1, 1, new(0, 0, 80, 0), false))],
        FoodExchangeRate = 3,
        FoodMarket = new(Enumerable.Range(1, 12).Select(m => ImmutableArray.Create(m + 5, m + 6)).ToImmutableArray()),
        SearchEvents = [new MerchantInvestmentDefinition([new("salt", [1], resource, new("salt.banned"))], 1, 50, 1000, 100, 5, 20, 3, 2, 4, 5)
            { Id = "merchant", Consumption = EventConsumption.None }]
    };
    private static ulong Seed(bool wins) => Enumerable.Range(1, 10000).Select(i => (ulong)i).First(seed =>
        new StableRandom(StableRandom.DeriveSeed(seed, "event-search-eligibility")).NextUInt64() % 100 < 80 &&
        (new StableRandom(StableRandom.DeriveSeed(seed, "event-merchant-investment")).NextUInt64() % 100 < 80) == wins);
    private static RespondDailyTaskCommand Answer(GameSession session, MerchantInvestmentPrompt p, MerchantInvestmentChoice choice, uint amount) =>
        new(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, p.TaskId, p.Step, choice == MerchantInvestmentChoice.Invest)
        { MerchantInvestment = new(choice, amount) };

    [Theory]
    [InlineData(MerchantInvestmentChoice.Invest, true, ResourceKind.Money)]
    [InlineData(MerchantInvestmentChoice.Invest, false, ResourceKind.Money)]
    [InlineData(MerchantInvestmentChoice.Invest, true, ResourceKind.Food)]
    [InlineData(MerchantInvestmentChoice.Decline, true, ResourceKind.Money)]
    [InlineData(MerchantInvestmentChoice.Execute, true, ResourceKind.Money)]
    public void AllOutcomesPreserveCurrentMarketSaveAndReplay(MerchantInvestmentChoice choice, bool wins, ResourceKind resource)
    {
        var d = Definition(resource); var session = GameSession.Start(d, Seed(wins)); session.BeginRecording();
        int price = session.Snapshot.Simulation.FoodExchangeRate; Assert.InRange(price, 7, 8);
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 2, 1)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), d);
        Assert.Equal(price, restored.Snapshot.Simulation.FoodExchangeRate);
        session.AdvanceXun(); restored.AdvanceXun();
        var prompt = Assert.IsType<MerchantInvestmentPrompt>(session.GetMerchantInvestmentPrompt());
        Assert.Equal(prompt, restored.GetMerchantInvestmentPrompt()); Assert.Equal(price, prompt.FoodExchangeRate);
        Assert.Equal(100u, prompt.MinimumInvestment); Assert.Equal(9999u, prompt.MaximumInvestment); Assert.Equal(80, prompt.SuccessChance);
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        Assert.Throws<InvalidOperationException>(() => session.EndRecording());
        string before = session.Snapshot.StateHash; Assert.Equal(prompt, session.GetMerchantInvestmentPrompt()); Assert.Equal(before, session.Snapshot.StateHash);
        uint amount = choice == MerchantInvestmentChoice.Invest ? 101u : 0u;
        var answer = Answer(session, prompt, choice, amount); var request = session.Request(new CommandOperation(answer), id: answer.CommandId);
        var first = session.Apply(request); var second = restored.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, first.Disposition); Assert.Equal(CanonicalJson.Text(first.Events), CanonicalJson.Text(second.Events));
        var result = Assert.Single(first.Events.OfType<MerchantInvestmentResolvedEvent>());
        bool success = choice == MerchantInvestmentChoice.Invest && wins;
        Assert.Equal(success, result.Succeeded); Assert.Equal(success ? 471L * (resource == ResourceKind.Food ? price : 1) : 0, result.GrossReturn);
        Assert.Equal(choice == MerchantInvestmentChoice.Execute ? 95 : 100, result.BaseTrust);
        Assert.Equal(choice == MerchantInvestmentChoice.Execute, session.Snapshot.Forces.Single().EventHistory.Contains(new("salt.banned")));
        Assert.Empty(session.Snapshot.Simulation.ConsumedEventKeys); Assert.DoesNotContain(first.Events, e => e is SearchResolvedEvent);
        Assert.Equal(SessionDisposition.AlreadyProcessed, session.Apply(request).Disposition);
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(restored));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(SaveService.Decode(SaveService.Encode(session), d)));
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), d, GameSession.EngineFactory(d));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(replay.Session));
        var rng = new StableRandom(StableRandom.DeriveSeed(Seed(wins), "food-market")); rng.NextUInt64(); rng.NextUInt64();
        Assert.Equal(rng.State, session.Snapshot.Simulation.RandomStreams.Single(s => s.Name == "food-market").State);
    }

    [Fact]
    public void InvalidAmountChoiceAndUnrelatedPayloadLeavePendingDecisionUnchanged()
    {
        var session = GameSession.Start(Definition(), Seed(true));
        session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 2, 1)); session.AdvanceXun();
        var p = session.GetMerchantInvestmentPrompt()!; string hash = session.Snapshot.StateHash;
        var valid = Answer(session, p, MerchantInvestmentChoice.Invest, 100);
        foreach (var invalid in new[]
        {
            valid with { MerchantInvestment = null }, valid with { MerchantInvestment = new(MerchantInvestmentChoice.Invest, 99) },
            valid with { MerchantInvestment = new(MerchantInvestmentChoice.Invest, p.MaximumInvestment + 1) },
            valid with { Accept = false }, valid with { CountryTitleId = 1 }, valid with { TaskId = Guid.NewGuid() }, valid with { Step = p.Step + 1 },
            valid with { MerchantInvestment = new((MerchantInvestmentChoice)99, 0) },
            valid with { Accept = false, MerchantInvestment = new(MerchantInvestmentChoice.Decline, 100) }
        })
        {
            Assert.False(session.Execute(new("player"), invalid).Submission.Accepted); Assert.Equal(hash, session.Snapshot.StateHash);
        }
        Assert.True(session.Execute(new("player"), valid).Submission.Accepted);
    }

    [Fact]
    public void MarketAndMerchantContentValidateAndContributeToFingerprint()
    {
        var d = Definition(); var merchant = Assert.IsType<MerchantInvestmentDefinition>(d.SearchEvents.Single());
        foreach (var invalid in new[]
        {
            merchant with { Categories = [merchant.Categories[0] with { PlaceIds = [999] }] },
            merchant with { Categories = [merchant.Categories[0], merchant.Categories[0]] },
            merchant with { InvestmentBalanceDivisor = 0 }, merchant with { ReturnDenominator = 0 }
        }) Assert.Throws<InvalidDataException>(() => (d with { SearchEvents = [invalid] }).Validate());
        Assert.Throws<InvalidDataException>(() => (d with { FoodExchangeRate = 0 }).Validate());
        Assert.Throws<InvalidDataException>(() => (d with { FoodMarket = new([]) }).Validate());
        Assert.NotEqual(GameSession.Fingerprint(d), GameSession.Fingerprint(d with { FoodExchangeRate = 4 }));
        var otherMarket = d with { FoodMarket = new(d.FoodMarket!.MonthlyRates.SetItem(0, [8, 8, 9])) };
        Assert.NotEqual(GameSession.Fingerprint(d), GameSession.Fingerprint(otherMarket));
        var json = JsonNode.Parse(CanonicalJson.Encode(d))!; json.AsObject().Remove("foodExchangeRate");
        Assert.Throws<JsonException>(() => StrictJson.Deserialize<ScenarioInitialState>(Encoding.UTF8.GetBytes(json.ToJsonString())));
    }
}
