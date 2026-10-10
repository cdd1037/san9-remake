using San9.Application.Persistence;
using San9.Application.Presentation;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class MissionSelectionTests
{
    [Theory]
    [InlineData("burn", false, CommandTargetMarker.Hostile)]
    [InlineData("seize", false, CommandTargetMarker.Hostile)]
    [InlineData("false-report", false, CommandTargetMarker.Hostile)]
    [InlineData("disorder", false, CommandTargetMarker.Hostile)]
    [InlineData("inspire", false, CommandTargetMarker.Friendly)]
    [InlineData("rescue", false, CommandTargetMarker.Friendly)]
    [InlineData("request", true, CommandTargetMarker.Attack)]
    public void TargetMarkersDistinguishHostileStrategiesFriendlySupportAndAttackRequests(
        string kind, bool secondary, CommandTargetMarker expected)
    {
        var draft = new MissionDraft(GameSession.Start(MissionScenario.Create(),42),1,kind);
        Assert.Equal(expected,draft.MapMarker(secondary));
    }

    [Theory]
    [InlineData("alienate",4)] [InlineData("burn",2)] [InlineData("seize",2)]
    [InlineData("rescue",4)] [InlineData("gift",5)] [InlineData("request",5)]
    public void CommandPrioritySelectsTheRelevantAbilityAndSurvivesPageSwitches(string kind, int first)
    {
        var basis = MissionScenario.Create();
        var content = basis with { Officers = [.. basis.Officers.Select(p => p.State.Id == 2 ? p with { State = p.State with { BaseAbilities = new(95,10,30,20) } } : p),
            new("clever",new(4,1,1,new(10,95,20,95),false)), new("politician",new(5,1,1,new(15,20,95,15),false))] };
        var session = GameSession.Start(content,42);
        var draft = new MissionDraft(session,1,kind);
        var texts = MissionScenario.Texts(draft.View);
        byte[] before = SaveService.Encode(session);
        Assert.Equal(first,draft.ActorId);
        var pages = MissionOfficerCatalog.Pages(draft);
        var primary = MissionOfficerCatalog.Read(draft,texts,0);
        Assert.Equal(first,primary[0].Id);
        Assert.Contains(draft.ActorPriorityField,pages[0].Columns.Select(c => c.Id));
        for (int page = 1; page < pages.Count; page++)
            Assert.Equal(primary.Select(r => r.Id),MissionOfficerCatalog.Read(draft,texts,page).Select(r => r.Id));
        Assert.Equal(before,SaveService.Encode(session));
    }

    [Fact]
    public void InspirationUsesIntelligenceBeforeKnowledgeAndKnowledgeBreaksTies()
    {
        var basis = MissionScenario.Create();
        var content = basis with { Officers = [.. basis.Officers,
            new("clever",new(4,1,1,new(10,95,20,20),false)),
            new("inspirer",new(5,1,1,new(10,90,20,20),false) { LearnedTactics = [101] })],
            Military = basis.Military! with { Tactics = [new(101,7,TacticEffect.Inspire,0,0,0,false,false,[])] } };
        var draft = new MissionDraft(GameSession.Start(content,42),1,"inspire");
        Assert.Equal(new[] {4,5,1,2},draft.Actors);
        var rows = MissionOfficerCatalog.Read(draft,MissionScenario.Texts(draft.View),0);
        int column = Array.FindIndex(MissionOfficerCatalog.Pages(draft)[0].Columns,c => c.Id == "inspiration");
        Assert.Equal("○",rows.Single(r => r.Id == 5).Cells[column].Text);
        Assert.Equal("×",rows.Single(r => r.Id == 4).Cells[column].Text);
    }

    [Fact]
    public void AlienateTargetPageProjectsTravelWithoutChangingDraftOrSimulation()
    {
        var session = GameSession.Start(MissionScenario.Create(),42);
        var draft = new MissionDraft(session,1,"alienate");
        byte[] before = SaveService.Encode(session);
        var pages = MissionOfficerCatalog.Pages(draft,targets:true);
        var rows = MissionOfficerCatalog.Read(draft,MissionScenario.Texts(draft.View),0,targets:true);
        var row = rows.Single(r => r.Id == 22);
        var arrival = row.Cells[Array.FindIndex(pages[0].Columns,c => c.Id == "estimate")];
        Assert.True(arrival.Number > 0);
        Assert.Null(draft.TargetId);
        Assert.Equal(before,SaveService.Encode(session));
    }

    [Fact]
    public void MapSelectionKeepsFacilityAndUnitIdentityDistinct()
    {
        var basis = MissionScenario.Create();
        var content = basis with { Units = basis.Units.Select(u => u.Id == 10 ? u with { Id = 2 } : u).ToArray(),
            Officers = basis.Officers.Select(p => p.State.UnitId == 10 ? p with { State = p.State with { UnitId = 2 } } : p).ToArray() };
        var session = GameSession.Start(content,42);
        byte[] before = SaveService.Encode(session);
        var facility = new CommandTargetSelection(session.View,CommandMapTargetKind.Settlement,[2],CommandTargetMarker.Hostile);
        var unit = new CommandTargetSelection(session.View,CommandMapTargetKind.Unit,[2],CommandTargetMarker.Friendly);
        Assert.Equal(2,facility.Resolve(new CommandMapObject(2,false)));
        Assert.Null(facility.Resolve(new CommandMapObject(2,true)));
        Assert.Equal(2,unit.Resolve(new CommandMapObject(2,true)));
        Assert.Null(unit.Resolve(new CommandMapObject(2,false)));
        Assert.Equal(before,SaveService.Encode(session));
    }

}
