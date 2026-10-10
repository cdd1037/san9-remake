using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class PatrolTests
{
    [Fact]
    public void DormantCitiesDoNotAlterPatrolPopulationForecast()
    {
        var definition = G01ScenarioTests.Fixture();
        var session = GameSession.Start(definition, 7);
        var active = session.Snapshot with { Settlements = session.Snapshot.Settlements.Select(s => s with { Population = 200399, DraftPopulation = 0, Soldiers = 0, PublicOrder = 800 }).ToArray() };
        var home = active.Settlements.Single(s => s.Id == 1);
        var dormant = home with { Id = 99, OwnerId = null, CorpsId = null, IsDormant = true, Population = 900000, Soldiers = 500000 };
        var complete = active with { Settlements = [.. active.Settlements, dormant] };
        string before = complete.StateHash;
        long expected = new PopulationOutlook(active).DraftGrowth(home.Id);
        Assert.True(expected > 0);
        var view = new StrategySnapshot(definition, complete);
        Assert.Equal(expected, new PatrolDraft(view, home.Id).DraftGrowth);
        var catalog = new AdministrativeCatalog(view, new TextCatalog(TextCatalog.CurrentSchemaVersion, []));
        Assert.Equal(expected, catalog.Cell(InformationCategory.Cities, home.Id, "draft-growth").Number);
        Assert.Equal(before, complete.StateHash);
    }

    [Fact]
    public void MenuAndPreparationShareDisabledReasonsWithoutChangingWorld()
    {
        var definition = G01ScenarioTests.Fixture();
        var session = GameSession.Start(definition,7);
        var world = session.Snapshot;
        var home = world.Settlements.Single(s => s.Id == 1);
        foreach (var (city,reason) in new[] {
            (home with { PatrolUsed = true },"already-used"),
            (home with { PublicOrder = 1000 },"public-order-max"),
            (home with { ConfusionDaysRemaining = 1 },"confused") })
        {
            var view = new StrategySnapshot(definition,world with { Settlements = world.Settlements.Select(s => s.Id == 1 ? city : s).ToArray() });
            var entry = MapCommandMenu.Settlement(view,1).Single(g => g.Id == "facilities").Children.Single(c => c.Id == "patrol");
            Assert.False(entry.Enabled);
            Assert.Equal("ui.command-reason."+reason,entry.UnavailableReasonKey);
            Assert.Equal(PatrolDraft.UnavailableReasonKey(view,1),entry.UnavailableReasonKey);
            Assert.Throws<ArgumentException>(() => new PatrolDraft(view,1));
        }
        Assert.True(MapCommandMenu.Settlement(session.View,1).Single(g => g.Id == "facilities").Children.Single(c => c.Id == "patrol").Enabled);
        Assert.Same(world,session.Snapshot);
    }

    [Fact]
    public void DialogueCanIdentifyNpcWithoutAddingItToOfficerBrowser()
    {
        var definition = G01ScenarioTests.Fixture();
        var session = GameSession.Start(definition,7);
        var world = session.Snapshot with { Officers = session.Snapshot.Officers.Select(p => p.Id == 3
            ? p with { Status = OfficerStatus.Special, OwnerId = null, SettlementId = null } : p).ToArray() };
        var view = new StrategySnapshot(definition,world);
        Assert.DoesNotContain(view.Officers,p => p.Id == 3);
        Assert.Equal((definition.Officers.Single(p => p.State.Id == 3).NameKey,(int?)null),view.MessageSpeaker(3));
    }

    [Fact]
    public void PreparationCancelAndNestedSelectionDoNotSpendOrConsumeRandom()
    {
        var session = GameSession.Start(G01ScenarioTests.Fixture(),7);
        var before = session.Snapshot;
        var draft = new PatrolDraft(session.View,1);
        Assert.Empty(draft.Members);
        Assert.False(draft.CanExecute);
        Assert.True(draft.SetMembers([2,1]));
        Assert.Equal(100,draft.Cost);
        var selector = new CatalogSelection(draft.Candidates,draft.Members,draft.SelectionLimit);
        selector.Clear(); // Cancel the nested selector: the draft still owns the confirmed list.
        Assert.Equal(2,draft.Members.Length);
        Assert.False(draft.SetMembers([2,2]));
        Assert.False(draft.SetMembers([4]));
        Assert.Same(before,session.Snapshot);
    }

    [Fact]
    public void SingleCandidateIsPreselectedAndFundsLimitTheSelection()
    {
        var definition = G01ScenarioTests.Fixture();
        var session = GameSession.Start(definition,7);
        var single = session.Snapshot with { Officers = session.Snapshot.Officers.Select(o => o with { Acted = o.Id is 2 or 3 }).ToArray() };
        var draft = new PatrolDraft(new(definition,single),1);
        Assert.Equal(new[] { 1 },draft.Members.ToArray());
        Assert.True(draft.CanExecute);
        var poor = session.View.World with { Corps = session.Snapshot.Corps.Select(c => c with { Treasury = new(49,0) }).ToArray() };
        Assert.NotNull(PatrolDraft.UnavailableReasonKey(new(definition,poor),1));
        var limited = new PatrolDraft(new(definition,poor with { Corps = poor.Corps.Select(c => c with { Treasury = new(50,0) }).ToArray() }),1);
        Assert.Equal(5,limited.SelectionLimit);
        Assert.True(limited.SetMembers([1]));
        Assert.False(limited.SetMembers([1,2]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CommandCommitsOnceAndPresentationRetainsTheActualBranch(int branch)
    {
        var definition = G01ScenarioTests.Fixture();
        if (branch == 2) definition = definition with { DomesticPairs = [new(DomesticActivity.Patrol,2,3,false,false)] };
        var session = GameSession.Start(definition,7);
        var before = session.View;
        var draft = new PatrolDraft(before,1);
        Assert.True(draft.SetMembers(branch == 2 ? [2,3] : branch == 1 ? [1,2] : [1]));
        var result = session.Execute(new("player"),draft.CreateCommand(Guid.NewGuid(),0));
        Assert.True(result.Submission.Accepted);
        var completed = Assert.Single(result.Events.OfType<DevelopmentCompletedEvent>());
        if (branch > 0) Assert.Equal(branch == 1 ? DomesticBonusKind.Affinity : DomesticBonusKind.SpecialPair,completed.Bonus!.Kind);
        else Assert.Null(completed.Bonus);
        Assert.Equal((int)draft.Money-draft.Cost,(int)session.View.PlayerCorps!.Treasury.Money);
        Assert.All(session.Snapshot.Officers.Where(p => draft.Members.Contains(p.Id)),p => Assert.True(p.Acted));
        Assert.True(session.Snapshot.Settlements.Single(s=>s.Id==1).PatrolUsed);
        string hash = session.Snapshot.StateHash;
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion,definition.Officers.ToDictionary(o=>o.NameKey,o=>o.NameKey))
        { Messages = new[] { "normal","cooperative","citizen","pair-first","pair-second","result","advice" }
            .ToDictionary(k=>k,k=>new MessageDefinition([new MessageLiteral(k)])) };
        var content = new PatrolPresentationDefinition(new[] { "normal","cooperative","citizen","pair-first","pair-second","result" }
            .ToDictionary(k=>k,k=>k),3,[new("advice",3,[],AdviceMetric.PublicOrder,0)]);
        var messages = PatrolPresentation.Create(before,new(definition,before.World,texts),content,completed,_=>99);
        Assert.Equal(branch == 2 ? new[] { "pair-first","pair-second","result" } : branch == 1 ? ["cooperative","citizen","result"] : ["normal","result"],messages.Select(m=>m.Message.Key));
        Assert.Equal("domestic-result",messages[^1].SoundRole);
        Assert.Null(messages[^1].SpeakerId);
        if (branch == 1)
        {
            Assert.Equal(completed.Bonus!.SpeakerId,messages[0].SpeakerId);
            Assert.Equal(content.CitizenId,messages[1].SpeakerId);
            Assert.Equal("patrol-citizen",messages[1].SoundRole);
        }
        if (branch == 2)
            Assert.Equal(new int?[] { completed.Bonus!.SpeakerId,completed.Bonus.PartnerId },messages.Take(2).Select(m=>m.SpeakerId));
        var restoredEvent = System.Text.Json.JsonSerializer.Deserialize<DevelopmentCompletedEvent>(
            System.Text.Json.JsonSerializer.Serialize(completed))!;
        Assert.Equal(completed.Bonus,restoredEvent.Bonus);
        int rolls = 0;
        var advice = content with { Advice = [
            new("advice",3,[2],AdviceMetric.PublicOrder,int.MaxValue), // No owned city: no draw.
            new("advice",3,[1,2],AdviceMetric.PublicOrder,draft.Home.PublicOrder+1), // Enemy city counts toward the threshold.
            new("advice",3,[],AdviceMetric.PublicOrder,0),
            new("advice",3,[],AdviceMetric.PublicOrder,0)] };
        var withAdvice = PatrolPresentation.Create(before,new(definition,before.World,texts),advice,completed,_=>{ rolls++; return 0; });
        Assert.Equal(1,rolls);
        Assert.Equal(messages.Count+1,withAdvice.Count);
        Assert.Equal("advice",withAdvice[^1].Message.Key);
        Assert.Null(withAdvice[^1].SoundRole);
        Assert.Equal(hash,session.Snapshot.StateHash);
        var repeat = session.Execute(new("player"),draft.CreateCommand(Guid.NewGuid(),session.Snapshot.Simulation.NextSequence));
        Assert.False(repeat.Submission.Accepted);
        Assert.Equal(hash,session.Snapshot.StateHash);
    }
}
