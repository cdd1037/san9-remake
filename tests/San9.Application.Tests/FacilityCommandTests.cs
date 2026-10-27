using San9.Tests;
using San9.Application.Presentation;
using San9.Application.Persistence;
using San9.Content.Interface;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class FacilityCommandTests
{
    private static ScenarioInitialState Definition()
    {
        var source = G01ScenarioTests.Fixture();
        return source with { Settlements = source.Settlements.Select(s => s with {
            Durability = 40, DraftPopulation = 10000, SoldierCapacity = 100000, MerchantPresenceChance = 100 }).ToArray(), FoodExchangeRate = 7 };
    }

    [Theory]
    [InlineData("commerce", DomesticActivity.Commerce)]
    [InlineData("cultivate", DomesticActivity.Cultivation)]
    [InlineData("repair", DomesticActivity.Repair)]
    [InlineData("recruit", DomesticActivity.Recruit)]
    [InlineData("train", DomesticActivity.Train)]
    public void PreparationIsReadOnlyAndCommitsTheSelectedActivityOnce(string kind, DomesticActivity activity)
    {
        var session = GameSession.Start(Definition(),7);
        var before = session.Snapshot;
        var draft = new FacilityCommandDraft(session,1,kind);
        Assert.False(draft.CanExecute);
        Assert.True(draft.SetMembers([1]));
        Assert.False(draft.SetMembers([1,1]));
        Assert.False(draft.SetMembers([4]));
        var canceled = new CatalogSelection(draft.Candidates,draft.Members,5); canceled.Clear();
        Assert.Equal(new[] { 1 },draft.Members.ToArray());
        Assert.Same(before,session.Snapshot);
        var command = Assert.IsType<DevelopCommand>(draft.CreateCommand(Guid.NewGuid(),0));
        Assert.Equal(activity,command.Development.Activity);
        var result = session.Execute(new("player"),command);
        Assert.True(result.Submission.Accepted);
        var completed = Assert.Single(result.Events.OfType<DevelopmentCompletedEvent>());
        Assert.Equal((int)draft.Cost,completed.Cost);
        Assert.Equal(draft.Money-draft.Cost,session.View.PlayerCorps!.Treasury.Money);
        Assert.True(session.Snapshot.Officers.Single(p => p.Id == 1).Acted);
        Assert.False(draft.CanExecute);
        Assert.Throws<InvalidOperationException>(() => draft.CreateCommand(Guid.NewGuid(),1));
        Assert.NotNull(FacilityCommandDraft.UnavailableReasonKey(session.View,1,kind));
        if (kind == "recruit") Assert.Equal(draft.RecruitCount,completed.Increment);
        if (kind == "train") Assert.Equal(0,completed.Cost);
    }

    [Theory]
    [InlineData("buy", 2u, 2008u)]
    [InlineData("sell", 1u, 1992u)]
    public void TradeSelectionUsesCoreQuoteAndCorrectRounding(string kind,uint money,uint food)
    {
        var session = GameSession.Start(Definition(),7);
        var before = session.Snapshot;
        var draft = new FacilityCommandDraft(session,1,kind);
        Assert.True(draft.SetMembers([1,2]));
        Assert.Equal(session.GetFoodTradeQuote(1,1,draft.Members,draft.Direction),draft.Quote);
        Assert.False(draft.SetFoodAmount(0));
        Assert.False(draft.SetFoodAmount(draft.Quote!.MaximumFoodAmount+1));
        Assert.True(draft.SetFoodAmount(8));
        Assert.Equal(money,draft.Cost);
        Assert.Same(before,session.Snapshot);
        var result = session.Execute(new("player"),draft.CreateCommand(Guid.NewGuid(),0));
        Assert.True(result.Submission.Accepted);
        var completed = Assert.Single(result.Events.OfType<FoodTradeCompletedEvent>());
        Assert.Equal(8u,completed.FoodAmount); Assert.Equal(money,completed.MoneyAmount);
        Assert.Equal(food,session.View.PlayerCorps!.Treasury.Food);
        Assert.All(new[] { "buy","sell" },k => Assert.Equal("ui.command-reason.trade-used",FacilityCommandDraft.UnavailableReasonKey(session.View,1,k)));
        Assert.False(draft.SetFoodAmount(1));
    }

    [Fact]
    public void ExplicitRecruitAmountIsValidatedAndSubmitted()
    {
        var definition = Definition(); var session = GameSession.Start(definition,7);
        var draft = new FacilityCommandDraft(session,1,"recruit");
        Assert.True(draft.SetMembers([1]));
        Assert.False(draft.SetRecruitAmount(0));
        Assert.False(draft.SetRecruitAmount((uint)draft.RecruitCount+1));
        Assert.True(draft.SetRecruitAmount(11)); Assert.Equal(2u,draft.Cost);
        var command = Assert.IsType<DevelopCommand>(draft.CreateCommand(Guid.NewGuid(),0));
        Assert.Equal(11u,command.Development.RecruitAmount);
        var hash = session.Snapshot;
        foreach (var invalid in new[] { command.Development with { RecruitAmount = 0 }, command.Development with { RecruitAmount = uint.MaxValue },
            command.Development with { Activity = DomesticActivity.Commerce } })
        {
            Assert.False(session.Execute(new("player"),command with { Development = invalid }).Submission.Accepted);
            StateAssert.Equal(hash, session.Snapshot);
        }
        var result = session.Execute(new("player"),command);
        Assert.True(result.Submission.Accepted);
        Assert.Equal(11,Assert.Single(result.Events.OfType<DevelopmentCompletedEvent>()).Increment);
    }

    [Fact]
    public void TrainingNeedsNoFundsAndTradeSelectionRecalculatesAmount()
    {
        var d = Definition();
        var poor = d with { Information = d.Information with { Corps = d.Information.Corps.Select(c => c with { Treasury = new(0,2000) }).ToArray() } };
        var session = GameSession.Start(poor,7);
        Assert.Null(FacilityCommandDraft.UnavailableReasonKey(session.View,1,"train"));
        Assert.Equal("ui.command-reason.funds",FacilityCommandDraft.UnavailableReasonKey(session.View,1,"commerce"));
        var draft = new FacilityCommandDraft(GameSession.Start(d,7),1,"buy");
        draft.SetMembers([1,2]); uint limit = draft.FoodAmount;
        draft.SetMembers([1]); Assert.True(draft.FoodAmount < limit);
        draft.SetMembers([]); Assert.Equal(0u,draft.FoodAmount); Assert.False(draft.CanExecute);
    }

    [Theory]
    [InlineData("train", "war", 2, 3)]
    [InlineData("repair", "leadership", 3, 2)]
    [InlineData("recruit", "leadership", 3, 2)]
    [InlineData("commerce", "politics", 2, 3)]
    [InlineData("buy", "politics", 2, 3)]
    public void PreparationAndSelectedParticipantsUseTheActivityAbility(string kind, string field, int first, int second)
    {
        var definition = Definition();
        definition = definition with { Officers = definition.Officers.Select(p => p.State.Id switch
        {
            2 => p with { State = p.State with { BaseAbilities = new(100, 50, 100, 20) } },
            3 => p with { State = p.State with { BaseAbilities = new(20, 50, 20, 100) } },
            _ => p
        }).ToArray() };
        var session = GameSession.Start(definition, 7);
        var before = session.Snapshot;
        var draft = new FacilityCommandDraft(session, 1, kind);
        Assert.Equal(field, draft.ActorPriorityField);
        Assert.Equal(first, draft.Candidates[0]);
        Assert.Equal(100, draft.ActorAbility(session.View.Officers.Single(p => p.Id == first).Abilities));
        Assert.True(draft.SetMembers([3, 2]));
        Assert.Equal(new[] { first, second }, draft.Members.ToArray());
        Assert.Same(before, session.Snapshot);
    }

    [Theory]
    [InlineData("commerce")]
    [InlineData("cultivate")]
    [InlineData("repair")]
    [InlineData("recruit")]
    [InlineData("train")]
    [InlineData("buy")]
    [InlineData("sell")]
    public void MenuAndPreparationAgreeAboutConfusionAndAutomaticControl(string kind)
    {
        var definition = Definition(); var world = GameSession.Start(definition,7).Snapshot;
        foreach (var altered in new[] {
            world with { Settlements = world.Settlements.Select(s => s with { ConfusionDaysRemaining = 1 }).ToArray() },
            world with { Corps = world.Corps.Select(c => c with { ControlMode = CorpsControlMode.Automatic }).ToArray() } })
        {
            var view = new StrategySnapshot(definition,altered);
            string? reason = FacilityCommandDraft.UnavailableReasonKey(view,1,kind);
            Assert.NotNull(reason);
            Assert.Equal(reason,MapCommandMenu.Settlement(view,1).Single(g => g.Id == "facilities").Children.Single(c => c.Id == kind).UnavailableReasonKey);
        }
    }

    [Theory]
    [InlineData(DomesticBonusKind.SpecialPair)]
    [InlineData(DomesticBonusKind.Affinity)]
    public void PresentationUsesCommittedBonusIdentityAndDoesNotRepeatRules(DomesticBonusKind kind)
    {
        var session = GameSession.Start(Definition(),7); var before = session.Snapshot;
        string[] roles = ["normal","cooperative","citizen","pair-first","pair-second","result","complete","complete-dialogue"];
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion,[]) { Messages = roles.ToDictionary(k => k,k => new MessageDefinition([new MessageLiteral(k)])) };
        var content = CommandPresentation.Empty with { Facilities = new() { ["repair"] = new(roles.ToDictionary(k => k),3) } };
        var completed = new DevelopmentCompletedEvent(before.Clock,new(1,1,[1,2],DomesticActivity.Repair),60,100)
            { Bonus = new(kind,2,kind == DomesticBonusKind.SpecialPair ? 1 : null) };
        var messages = FacilityCommandPresentation.Create(session.View,session.Messages(texts),content,"repair",completed,_ => 99);
        Assert.Equal(5,messages.Count);
        Assert.Equal(2,messages[0].SpeakerId);
        Assert.Equal(kind == DomesticBonusKind.SpecialPair ? 1 : 3,messages[1].SpeakerId);
        Assert.Null(messages[2].SpeakerId);
        Assert.Equal("repair-complete",messages[3].SoundRole);
        Assert.Same(before,session.Snapshot);
    }
}
