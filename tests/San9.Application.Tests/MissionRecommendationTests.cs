using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class MissionRecommendationTests
{
    private static ScenarioInitialState Content()
    {
        var basis = MissionScenario.Create();
        return basis with
        {
            Forces = basis.Forces.Select(force => force.Id == 1 ? force with { AdviserId = 2 } : force).ToArray(),
            Officers = [.. basis.Officers.Select(person => person.State.Id == 2 ? person with
            {
                State = person.State with { Status = OfficerStatus.Strategist, BaseAbilities = new(100,100,100,100) }
            } : person), new("candidate", new(4,1,1,new(70,70,70,70),false))],
            CityConnections = [.. basis.CityConnections, new(1,3)]
        };
    }

    [Theory]
    [InlineData("alienate",22)]
    [InlineData("burn",2)]
    [InlineData("seize",2)]
    [InlineData("rumor",2)]
    [InlineData("false-report",20)]
    [InlineData("disorder",20)]
    [InlineData("inspire",10)]
    [InlineData("rescue",10)]
    public void RecommendationsUsePublicAdviceAndPreserveWorld(string kind, int target)
    {
        var session = GameSession.Start(Content(),42);
        var before = session.Capture();
        var draft = new MissionDraft(session,1,kind);
        Assert.True(draft.SetTarget(target));
        if (kind == "rumor") Assert.True(draft.SetTarget(3,secondary:true));
        var opinions = draft.Actors.ToDictionary(actor => actor, actor => kind switch
        {
            "alienate" => session.GetAlienateAdvice(1,actor,target),
            "burn" => session.GetBurnAdvice(1,actor,target),
            "seize" => session.GetSeizeAdvice(1,actor,target),
            "rumor" => session.GetGossipAdvice(1,actor,target,3),
            _ => session.GetUnitStrategyAdvice(1,actor,kind switch
            {
                "false-report" => UnitStrategyKind.FalseReport, "disorder" => UnitStrategyKind.Disorder,
                "inspire" => UnitStrategyKind.Inspire, _ => UnitStrategyKind.Reinforce
            },target)
        });
        foreach (int actor in draft.Actors) Assert.Equal(opinions[actor],draft.ActorAdvice(actor));
        int expected = draft.Actors.OrderByDescending(actor => MissionDraft.AdvicePriority(opinions[actor].Opinion)).First();
        Assert.Equal(expected,draft.ActorId);
        Assert.Equal(opinions.Values.Any(advice => advice.Opinion == AdviserOpinion.Favorable),
            draft.TargetOpinion(target) == AdviserOpinion.Favorable);
        var texts = MissionScenario.Texts(draft.View);
        var rows = MissionOfficerCatalog.Read(draft,texts,0);
        Assert.Equal(expected,rows[0].Id);
        Assert.Equal(MissionDraft.AdviceSymbol(opinions[expected].Opinion),rows[0].Cells[0].Text);
        Assert.Equal(rows.Select(row => row.Id),MissionOfficerCatalog.Read(draft,texts,1).Select(row => row.Id));
        Assert.Same(before,session.Capture());
    }

    [Fact]
    public void FavorableCandidateTakesPrecedenceOverHigherAbility()
    {
        var content = Content();
        for (ulong seed = 0; seed < 64; seed++)
        {
            var session = GameSession.Start(content,seed);
            if (session.GetBurnAdvice(1,2,2).Opinion != AdviserOpinion.Unfavorable ||
                session.GetBurnAdvice(1,4,2).Opinion != AdviserOpinion.Favorable) continue;
            var draft = new MissionDraft(session,1,"burn");
            Assert.Equal(2,draft.Actors[0]);
            Assert.True(draft.SetTarget(2));
            Assert.NotEqual(2,draft.ActorId);
            Assert.Equal(AdviserOpinion.Favorable,draft.Advice!.Opinion);
            Assert.Equal(AdviserOpinion.Favorable,draft.TargetOpinion(2));
            Assert.True(draft.SetActor(2));
            Assert.Equal(AdviserOpinion.Unfavorable,draft.TargetOpinion(2));
            return;
        }
        Assert.Fail("The fixture must include a favorable lower-ability candidate and an unfavorable higher-ability candidate.");
    }

    [Fact]
    public void ManualActorSelectionScopesTargetRecommendationsAndSurvivesTargetChanges()
    {
        var session = GameSession.Start(Content(),42);
        var draft = new MissionDraft(session,1,"burn");
        var before = session.Capture();
        _ = draft.TargetOpinion(2);
        Assert.True(draft.SetActor(4));
        foreach (int target in draft.Targets)
            Assert.Equal(session.GetBurnAdvice(1,4,target).Opinion,draft.TargetOpinion(target));
        Assert.True(draft.SetTarget(2));
        Assert.True(draft.SetTarget(3));
        Assert.Equal(4,draft.ActorId);
        var facilities = new CommandFacilityCatalog(draft.View,MissionScenario.Texts(draft.View),draft.Kind,draft.Targets);
        var catalog = new MissionTargetCatalog(draft,MissionScenario.Texts(draft.View),facilities.Pages,facilities.Read);
        Assert.Equal("advice",catalog.Pages[0].Columns[0].Id);
        Assert.Equal(catalog.Read(0).Select(row => row.Id),catalog.Read(1).Select(row => row.Id));
        foreach (var row in catalog.Read(0))
            Assert.Equal(MissionDraft.AdviceSymbol(session.GetBurnAdvice(1,4,row.Id).Opinion),row.Cells[0].Text);
        Assert.Same(before,session.Capture());
        Assert.True(session.Execute(new("player"),draft.CreateCommand()).Submission.Accepted);
        Assert.Null(draft.ActorAdvice(4));
        Assert.Equal(AdviserOpinion.Unavailable,draft.TargetOpinion(2));
    }

    [Fact]
    public void RumorRecommendationsRequireEligibleForcePairsAndFollowSecondTarget()
    {
        var session = GameSession.Start(Content(),42);
        var draft = new MissionDraft(session,1,"rumor");
        var before = session.Capture();
        Assert.Null(draft.ActorAdvice(1));
        Assert.Equal(AdviserOpinion.Unavailable,draft.TargetOpinion(3));
        var expected = draft.Actors.Any(actor => session.GetGossipAdvice(1,actor,2,3).Opinion == AdviserOpinion.Favorable)
            ? AdviserOpinion.Favorable : AdviserOpinion.Unfavorable;
        Assert.Equal(expected,draft.TargetOpinion(2));
        Assert.True(draft.SetTarget(2));
        Assert.Equal(expected,draft.TargetOpinion(3,secondary:true));
        Assert.Null(draft.ActorAdvice(1));
        Assert.True(draft.SetTarget(3,secondary:true));
        Assert.Equal(session.GetGossipAdvice(1,1,2,3),draft.ActorAdvice(1));
        Assert.Same(before,session.Capture());
    }

    [Fact]
    public void MissingAdviserLeavesRecommendationCellsEmptyAndPreservesAbilityOrder()
    {
        var session = GameSession.Start(MissionScenario.Create(),42);
        var draft = new MissionDraft(session,1,"burn");
        Assert.True(draft.SetTarget(2));
        Assert.Equal(draft.Actors,draft.RecommendedActors);
        Assert.All(MissionOfficerCatalog.Read(draft,MissionScenario.Texts(draft.View),0),row => Assert.Empty(row.Cells[0].Text));
        Assert.Equal(AdviserOpinion.Unavailable,draft.TargetOpinion(2));
        var map = new CommandTargetSelection(draft.View,CommandMapTargetKind.Settlement,[2],CommandTargetMarker.Hostile,[2,999]);
        Assert.Equal([2],map.Recommended);
        Assert.Equal(2,map.Resolve(new(2,false)));
        Assert.Null(map.Resolve(new(2,true)));
    }
}
