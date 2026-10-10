using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class PersonnelCommandTests
{
    private static TextCatalog CatalogTexts(StrategySnapshot view)
    {
        var keys = view.Officers.Select(o => o.NameKey).Concat(view.Forces.Select(f => f.NameKey))
            .Concat(view.Settlements.Select(s => s.NameKey)).Concat(view.Settlements.Select(s => s.NameSuffixKey).OfType<string>())
            .Concat(view.RegionNameKeys.Values).Concat(view.ProvinceNameKeys.Values)
            .Concat(view.Information.Offices.Select(o => o.NameKey));
        return new(TextCatalog.CurrentSchemaVersion,San9.Tests.CommandTextFixture.Entries(keys.Distinct().ToDictionary(k => k,k => k)));
    }
    [Theory]
    [InlineData("summon")]
    [InlineData("move")]
    [InlineData("search")]
    [InlineData("employ")]
    public void PersonnelCatalogPagesPreserveCandidatesAndDoNotChangeTheDraftOrWorld(string kind)
    {
        var session = GameSession.Start(Definition(),7);
        var draft = new PersonnelDraft(session,1,kind);
        var texts = CatalogTexts(session.View);
        var before = session.Snapshot;
        var target = draft.TargetId;
        var members = draft.Members;
        var pages = PersonnelCatalog.OfficerPages(draft,CatalogTexts(draft.View));
        var officerOrder = PersonnelCatalog.Officers(draft,texts,0).Select(r => r.Id).ToArray();
        Assert.Equal(draft.Candidates.Order(),officerOrder.Order());
        for (int page = 0; page < pages.Count; page++)
        {
            var rows = PersonnelCatalog.Officers(draft,texts,page);
            Assert.Equal(officerOrder,rows.Select(r => r.Id));
            Assert.All(rows,r => Assert.Equal(pages[page].Columns.Length,r.Cells.Length));
        }
        if (kind is "search" or "move")
        {
            var targetPages = PersonnelCatalog.TargetPages(draft,CatalogTexts(draft.View));
            var targetOrder = PersonnelCatalog.Targets(draft,texts,0).Select(r => r.Id).ToArray();
            Assert.Equal(draft.Targets.Order(),targetOrder.Order());
            for (int page = 0; page < targetPages.Count; page++)
            {
                var rows = PersonnelCatalog.Targets(draft,texts,page);
                Assert.Equal(targetOrder,rows.Select(r => r.Id));
                Assert.All(rows,r => Assert.Equal(targetPages[page].Columns.Length,r.Cells.Length));
            }
        }
        if (kind == "employ")
        {
            var targetPages = PersonnelCatalog.OfficerPages(draft,CatalogTexts(draft.View),targets:true);
            var targetOrder = PersonnelCatalog.Officers(draft,texts,0,targets:true).Select(r => r.Id).ToArray();
            Assert.Equal(draft.Targets.Order(),targetOrder.Order());
            for (int page = 0; page < targetPages.Count; page++)
                Assert.Equal(targetOrder,PersonnelCatalog.Officers(draft,texts,page,targets:true).Select(r => r.Id));
        }
        Assert.Equal(target,draft.TargetId);
        Assert.Equal(members,draft.Members);
        Assert.Same(before,session.Snapshot);
    }
    [Fact]
    public void SearchCatalogShowsLocalFreePeopleWithoutLeakingUndiscoveredPeopleAndEstimatesUnselectedTargets()
    {
        var definition = Definition();
        var hidden = definition.Officers.Single(p => p.State.Id == 100);
        var session = GameSession.Start(definition with { Officers = [.. definition.Officers,
            hidden with { State = hidden.State with { Id = 101, Status = OfficerStatus.Undiscovered } }] },7);
        var draft = new PersonnelDraft(session,1,"search");
        var texts = CatalogTexts(session.View);
        var page = PersonnelCatalog.TargetPages(draft,CatalogTexts(draft.View))[0];
        var rows = PersonnelCatalog.Targets(draft,texts,0);
        int free = Array.FindIndex(page.Columns,c => c.Id == "free");
        int estimate = Array.FindIndex(page.Columns,c => c.Id == "estimate");
        Assert.Equal(1,rows.Single(r => r.Id == 1).Cells[free].Number);
        Assert.All(rows,r => Assert.NotNull(r.Cells[estimate].Number));
        Assert.Equal(rows.Select(r => r.Cells[estimate].Number).Order(),rows.Select(r => r.Cells[estimate].Number));
        Assert.Equal("",rows.Single(r => r.Id == 300).Cells[Array.FindIndex(page.Columns,c => c.Id == "facility")].Text);
        Assert.Null(draft.TargetId);
        Assert.DoesNotContain(101,session.View.Officers.Select(o => o.Id));
    }
    [Fact]
    public void SearchDefaultOrderUsesDistanceBeforeRoundingToXun()
    {
        var definition = Definition();
        var session = GameSession.Start(definition with {
            UnbuiltPlaces = [.. definition.UnbuiltPlaces, new(301,MapCoordinates.Center(new(20,1))),new(302,MapCoordinates.Center(new(2,1)))],
            RegionNameKeys = new(definition.RegionNameKeys) { [301] = "far", [302] = "near" },
            RegionCityIds = new(definition.RegionCityIds) { [301] = 1, [302] = 1 }
        },7);
        var draft = new PersonnelDraft(session,1,"search");
        Assert.Equal(draft.ArrivalDaysTo(301),draft.ArrivalDaysTo(302));
        Assert.True(draft.DistanceDaysTo(302) < draft.DistanceDaysTo(301));
        var order = PersonnelCatalog.Targets(draft,CatalogTexts(session.View),0).Select(r => r.Id).ToList();
        Assert.True(order.IndexOf(302) < order.IndexOf(301));
    }
    [Fact]
    public void MoveDefaultOrderPrioritizesStatusThenOfficeThenAbilityTotal()
    {
        var definition = Definition();
        var template = definition.Officers.Single(p => p.State.Id == 3);
        var session = GameSession.Start(definition with {
            Information = definition.Information with { Offices = [new(200,"senior",1000,0) { GovernorPriority = 20 },new(100,"junior",1000,0) { GovernorPriority = 10 }] },
            Officers = [.. definition.Officers.Where(p => p.State.Id != 3).Select(p => p.State.Id == 1 ? p with { State = p.State with { Status = OfficerStatus.Ruler } } : p),
                template with { State = template.State with { BaseAbilities = new(1,1,1,1) } },
                template with { State = template.State with { Id = 10,Status = OfficerStatus.Serving,OfficeId = 200,BaseAbilities = new(1,1,1,1) } },
                template with { State = template.State with { Id = 11,Status = OfficerStatus.Serving,OfficeId = 100,BaseAbilities = new(100,100,100,100) } },
                template with { State = template.State with { Id = 12,Status = OfficerStatus.Serving,BaseAbilities = new(90,90,1,90) } },
                template with { State = template.State with { Id = 13,Status = OfficerStatus.Serving,BaseAbilities = new(1,1,100,1) } }]
        },7);
        var draft = new PersonnelDraft(session,1,"move");
        Assert.Equal(new[] { 1,3,10,11,12,13 },PersonnelCatalog.Officers(draft,CatalogTexts(session.View),0).Select(r => r.Id));
    }
    [Fact]
    public void RecruitDefaultOrderPrioritizesCaptivesAndLowerLoyaltyBeforeFreePeople()
    {
        var definition = Definition();
        var template = definition.Officers.Single(p => p.State.Id == 5);
        var free = definition.Officers.Single(p => p.State.Id == 100);
        var session = GameSession.Start(definition with { Information = definition.Information with { Relations = [new(1,2,0)] }, Officers = [.. definition.Officers.Select(p =>
            p.State.Id == 100 ? p with { State = p.State with { SettlementId = 30 } } :
            p.State.Id is 5 or 6 ? p with { State = p.State with { ForceLoyalties = ImmutableDictionary<int,int>.Empty.Add(2,p.State.Id == 5 ? 82 : 92) } } : p),
            template with { State = template.State with { Id = 101,SettlementId = 1,Status = OfficerStatus.Captive,ForceLoyalties = ImmutableDictionary<int,int>.Empty.Add(2,90) } },
            template with { State = template.State with { Id = 102,SettlementId = 1,Status = OfficerStatus.Captive,ForceLoyalties = ImmutableDictionary<int,int>.Empty.Add(2,20) } },
            free with { State = free.State with { Id = 103,BaseAbilities = new(100,100,100,100) } }]
        },7);
        var draft = new PersonnelDraft(session,1,"employ");
        var rows = PersonnelCatalog.Officers(draft,CatalogTexts(session.View),0,targets:true);
        Assert.Equal(new[] { 102,101,100,103,5,6 },rows.Select(r => r.Id));
        Assert.Equal(new[] { "武将","身分","忠诚","势力","所在","统率","武力","智力","政治","label.arrival" },
            PersonnelCatalog.OfficerPages(draft,CatalogTexts(draft.View),targets:true)[0].Columns.Select(c => c.Title));
        var far = rows.Single(r => r.Id == 100);
        var near = rows.Single(r => r.Id == 103);
        Assert.True(far.Cells[^1].Number > near.Cells[^1].Number);
        Assert.Equal(0,near.Cells[^1].Number);
        Assert.True(far.Cells[2].IsUnavailable);
        Assert.Equal("",far.Cells[3].Text);
        Assert.Equal(82,rows.Single(r => r.Id == 5).Cells[2].Number);
    }
    [Fact]
    public void RecallDefaultOrderUsesLocationBeforePolitics()
    {
        var definition = Definition();
        var template = definition.Officers.Single(p => p.State.Id == 2);
        var session = GameSession.Start(definition with {
            Settlements = [.. definition.Settlements,definition.Settlements.Single(s => s.Id == 30) with { Id = 20 }],
            Officers = [.. definition.Officers,template with { State = template.State with { Id = 101,SettlementId = 20,BaseAbilities = new(1,1,1,1) } }]
        },7);
        var draft = new PersonnelDraft(session,1,"summon");
        Assert.Equal(new[] { 101,2 },PersonnelCatalog.Officers(draft,CatalogTexts(session.View),0).Select(r => r.Id));
    }
    private static ScenarioInitialState Definition()
    {
        var source = G01ScenarioTests.Fixture();
        return source with
        {
            Forces = source.Forces.Select(f => f.Id == 1 ? f with { AdviserId = 3 } : f).ToArray(),
            Settlements = [.. source.Settlements.Select(s => s with { CorpsId = s.OwnerId }),
                source.Settlements[0] with { Id = 30, NameKey = "destination", X = 50, CorpsId = 1 }],
            RegionNameKeys = new() { [1] = "city.1", [2] = "city.2", [30] = "destination", [300] = "region" },
            RegionCityIds = new() { [1] = 1, [2] = 2, [30] = 30, [300] = 1 },
            UnbuiltPlaces = [new(300, MapCoordinates.Center(new(15,15)))],
            Officers = [.. source.Officers.Select(p => p.State.Id == 2 ? p with { State = p.State with { SettlementId = 30 } } :
                p.State.Id == 3 ? p with { State = p.State with { Status = OfficerStatus.Strategist } } : p),
                new("free", new(100, null, 1, new(50,50,50,50),false) { Status = OfficerStatus.Free, ForceLoyalties = ImmutableDictionary<int,int>.Empty.Add(1,100) })]
        };
    }
    [Theory]
    [InlineData("summon")]
    [InlineData("move")]
    [InlineData("search")]
    [InlineData("employ")]
    public void PreparationRemainsReadOnlyAndSubmitsOneAtomicCommand(string kind)
    {
        var session = GameSession.Start(Definition(),17);
        var before = session.Snapshot;
        var draft = new PersonnelDraft(session,1,kind);
        if (kind == "move") { Assert.True(draft.SetTarget(30)); Assert.True(draft.SetMembers([1,3])); }
        if (kind == "search") Assert.True(draft.SetTarget(300));
        if (kind == "employ") Assert.True(draft.SetTarget(100));
        if (kind == "summon") Assert.Equal(new[] { 2 },draft.Members);
        Assert.False(draft.SetMembers([100]));
        Assert.False(draft.SetTarget(9999));
        Assert.False(draft.SetMembers([draft.Candidates[0],draft.Candidates[0]]));
        var localSelection = new CatalogSelection(draft.Candidates,draft.Members,draft.SelectionLimit); localSelection.Clear();
        Assert.True(draft.CanExecute);
        var advice = draft.Advice;
        Assert.Equal(advice,draft.Advice);
        Assert.Same(before,session.Snapshot);
        var result = session.Execute(new("player"),draft.CreateCommand(Guid.NewGuid(),before.Simulation.NextSequence));
        Assert.True(result.Submission.Accepted,result.Submission.Rejection.ToString());
        Assert.Equal(draft.Members.Length,result.Events.OfType<TaskStartedEvent>().Count());
        Assert.All(draft.Members,id => Assert.True(session.Snapshot.Officers.Single(p => p.Id == id).Acted));
        Assert.False(draft.IsCurrent);
        Assert.False(draft.CanExecute);
        Assert.Throws<InvalidOperationException>(() => draft.CreateCommand(Guid.NewGuid(),1));
    }
    [Fact]
    public void SearchAndRecruitUseHighestEffectivePoliticsAndAdviceNeverChangesTheTaskRoll()
    {
        var queried = GameSession.Start(Definition(),3); var untouched = GameSession.Start(Definition(),3);
        foreach (string kind in new[] { "search","employ" })
        {
            var draft = new PersonnelDraft(queried,1,kind);
            Assert.Equal(1,Assert.Single(draft.Members));
            Assert.True(draft.SetTarget(kind == "search" ? 300 : 100));
            if (kind == "employ")
            {
                Assert.DoesNotContain(4,draft.Targets);
                Assert.All(draft.Targets,target => Assert.True(queried.GetPersonnelJourney(1,draft.Members[0],target,PersonnelJourneyKind.Recruit).Available));
            }
            var opinion = draft.Advice;
            for (int i = 0; i < 20; i++) Assert.Equal(opinion,draft.Advice);
        }
        var selected = new PersonnelDraft(queried,1,"search"); selected.SetTarget(300);
        var command = selected.CreateCommand(Guid.NewGuid(),0);
        Assert.True(queried.Execute(new("player"),command).Submission.Accepted);
        Assert.True(untouched.Execute(new("player"),command).Submission.Accepted);
        Assert.Equal(untouched.Capture().StateHash,queried.Capture().StateHash);
    }
    [Fact]
    public void RecallSpeaksOnceForAllOriginsUsingCommanderPriorityAndDoesNotMutateTheWorld()
    {
        var definition = Definition();
        var origin = definition.Settlements.Single(s => s.Id == 30);
        var person = definition.Officers.Single(p => p.State.Id == 2);
        var session = GameSession.Start(definition with {
            Settlements = [.. definition.Settlements, origin with { Id = 40, X = 60 }],
            Officers = [.. definition.Officers, person with { State = person.State with { Id = 11, SettlementId = 40,BaseAbilities = new(1,1,1,100) } },
                person with { State = person.State with { Id = 12 } }] }, 7);
        var draft = new PersonnelDraft(session,1,"summon");
        Assert.True(draft.SetMembers([12,11,2]));
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion,[]) { Messages = new() {
            ["departure"] = new([new MessageLiteral("depart")]) } };
        var content = new PersonnelPresentationDefinition(new() { ["move-start"] = new("departure",[],"actor") });
        var before = session.Snapshot;
        var messages = PersonnelPresentation.Departure(draft,content,session.Messages(texts),texts,_ => 0);
        Assert.Equal(11,Assert.Single(messages).SpeakerId);
        Assert.Same(before,session.Snapshot);
    }
    [Fact]
    public void MoveDepartureUsesRulerBeforeHigherPoliticsOrLeadership()
    {
        var definition = Definition();
        var session = GameSession.Start(definition with { Officers = definition.Officers.Select(p => p.State.Id == 1
            ? p with { State = p.State with { Status = OfficerStatus.Ruler,BaseAbilities = new(1,1,1,1) } } : p).ToArray() },7);
        var draft = new PersonnelDraft(session,1,"move");
        Assert.True(draft.SetMembers([1,3])); Assert.True(draft.SetTarget(30));
        Assert.Equal(3,draft.Members[0]);
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion,[]) { Messages = new() { ["departure"] = new([new MessageLiteral("depart")]) } };
        var content = new PersonnelPresentationDefinition(new() { ["move-start"] = new("departure",[],"actor") });
        var message = Assert.Single(PersonnelPresentation.Departure(draft,content,session.Messages(texts),texts,_ => 0));
        Assert.Equal(1,message.SpeakerId); Assert.Null(message.SoundRole);
    }
    [Fact]
    public void SuccessfulCaptiveRecruitmentUsesItsOwnResultAndRepliesHaveNoSound()
    {
        var definition = Definition();
        var session = GameSession.Start(definition with {
            Information = definition.Information with { Relations = [new(1,2,0)] },
            Officers = definition.Officers.Select(p => p.State.Id == 5 ? p with { State = p.State with { Status = OfficerStatus.Captive,SettlementId = 1 } } : p).ToArray()
        },7);
        Assert.True(session.Execute(new("player"),new RecruitOfficerCommand(Guid.NewGuid(),1,0,1,5)).Submission.Accepted);
        string[] roles = ["employ-meeting","captive-success","captive-result-success"];
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion,[]) { Messages = roles.ToDictionary(r => r,r => new MessageDefinition([new MessageLiteral(r)])) };
        var content = new PersonnelPresentationDefinition(roles.ToDictionary(r => r,r => new DecisionMessageDefinition(r,[],r == "captive-success" ? "target" : null)));
        var before = session.Snapshot;
        var messages = PersonnelPresentation.Results(session.View,session.Messages(texts),texts,content,
            [new RecruitmentResolvedEvent(before.Clock,1,5,100,0,true)],_ => 0);
        Assert.Equal(roles,messages.Select(m => m.Message.BbCode));
        Assert.Equal(5,messages[1].SpeakerId); Assert.All(messages,m => Assert.Null(m.SoundRole));
        Assert.Same(before,session.Snapshot);
    }
    [Fact]
    public void CityRecruitmentTransferPresentsTheIllustrationAndSoundAfterTheMapNotice()
    {
        var definition = Definition();
        var session = GameSession.Start(definition with { Information = definition.Information with { Relations = [new(1,2,0)] } },7);
        Assert.True(session.Execute(new("player"),new RecruitOfficerCommand(Guid.NewGuid(),1,0,1,5)).Submission.Accepted);
        string[] roles = ["employ-secret-meeting","employ-success","employ-result-success","employ-territory","employ-city"];
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion,[]) { Messages = roles.ToDictionary(r => r,r => new MessageDefinition([new MessageLiteral(r)])) };
        var content = new PersonnelPresentationDefinition(roles.ToDictionary(r => r,r => new DecisionMessageDefinition(r,[],
            r == "employ-success" ? "target" : null,r == "employ-city" ? "transfer" : null,r == "employ-city" ? "city-image" : null)));
        var before = session.Snapshot;
        var messages = PersonnelPresentation.Results(session.View,session.Messages(texts),texts,content,
            [new RecruitmentResolvedEvent(before.Clock,1,5,100,0,true),new PersonnelTerritoryTransferredEvent(before.Clock,2,2,1,5)],_ => 0);
        Assert.Equal(roles,messages.Select(m => m.Message.BbCode));
        Assert.Equal("transfer",messages[^1].SoundRole); Assert.Equal("city-image",messages[^1].IllustrationRole);
        Assert.Null(messages[^2].SoundRole);
        Assert.Same(before,session.Snapshot);
    }
    [Fact]
    public void RecallUsesEachOriginAndSearchDoesNotExposeHiddenCandidates()
    {
        var session = GameSession.Start(Definition(),7);
        var recall = new PersonnelDraft(session,1,"summon");
        Assert.True(recall.ArrivalDays(2) > 1);
        var search = new PersonnelDraft(session,1,"search"); search.SetTarget(1);
        Assert.Equal(10,search.ArrivalDays(1));
        Assert.Equal(AdviserOpinion.Unfavorable,search.Advice!.Opinion);
        Assert.Contains(300,search.Targets);
        Assert.DoesNotContain(2,search.Targets);
    }
    [Theory]
    [InlineData(SearchResultKind.None)]
    [InlineData(SearchResultKind.Money)]
    [InlineData(SearchResultKind.Treasure)]
    [InlineData(SearchResultKind.Person)]
    public void SearchResultsNarrateOnceFromEventsAndPersonDecisionsAreNotDuplicated(SearchResultKind kind)
    {
        var session = GameSession.Start(Definition(),3);
        session.Execute(new("player"),new SearchCommand(Guid.NewGuid(),1,0,1,1));
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion,[]) { Messages = new() {
            ["none"] = new([new MessageLiteral("none")]), ["empty"] = new([new MessageLiteral("empty")]),
            ["money"] = new([new MessageLiteral("money")]), ["treasure"] = new([new MessageLiteral("treasure")]) } };
        var content = new PersonnelPresentationDefinition(new() {
            ["search-none"] = new("none",[],"actor"), ["search-empty"] = new("empty",[],Sound:"decrease"),
            ["search-money"] = new("money",[],Sound:"increase"), ["search-treasure"] = new("treasure",[],Sound:"success") });
        var before = session.Snapshot;
        var result = PersonnelPresentation.Results(session.View,session.Messages(texts),texts,content,
            [new SearchResolvedEvent(before.Clock,1,1,null,false) { Result = kind, Money = 123, TreasureId = kind == SearchResultKind.Treasure ? 99 : null }],_ => 0);
        Assert.Equal(kind == SearchResultKind.None ? 2 : kind == SearchResultKind.Person ? 0 : 1,result.Count);
        if (kind == SearchResultKind.Treasure) Assert.Equal("information-treasure-99",result[0].IllustrationRole);
        if (kind == SearchResultKind.None) { Assert.Equal(1,result[0].SpeakerId); Assert.Equal("decrease",result[1].SoundRole); }
        Assert.Same(before,session.Snapshot);
    }
}
