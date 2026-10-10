using San9.Application.Persistence;
using San9.Application.Presentation;

namespace San9.Application.Tests;

public sealed class CommandFacilityCatalogTests
{
    [Theory]
    [InlineData("burn", "force,commander,commander-leadership,durability,state,estimate")]
    [InlineData("seize", "force,commander,commander-leadership,soldiers,captives,state,estimate")]
    [InlineData("request", "force,relation,commander,soldiers,durability,serving")]
    [InlineData("delegation", "force,relation,commander,soldiers,durability,serving")]
    public void FacilitySelectionUsesCommandPageAndSharedFacilityPages(string kind, string fields)
    {
        var session = GameSession.Start(MissionScenario.Create(),42);
        var catalog = new CommandFacilityCatalog(session.View,MissionScenario.Texts(session.View),kind,[2,3]);
        Assert.Equal(fields.Split(','),catalog.Pages[0].Columns.Skip(1).Select(c => c.Id));
        Assert.Equal(new[] { "所属", "战力" },catalog.Pages.Skip(1).Select(p => p.Title));
        Assert.Equal(kind == "delegation" ? "设施" : "工作物",catalog.Pages[0].Columns[0].Title);
        Assert.Equal(catalog.Read(0).Select(r => r.Id),catalog.Read(2).Select(r => r.Id));
    }

    [Fact]
    public void DefaultOrderingUsesCommanderLeadershipAndAllPagesKeepFacilityNames()
    {
        var definition = MissionScenario.Create() with { RegionNameKeys = new() { [1] = "home", [2] = "territory", [3] = "third" } };
        var session = GameSession.Start(definition,42);
        var view = new StrategySnapshot(definition,session.View.World with
        {
            Officers = session.View.World.Officers.Select(p => p.Id is 21 or 31 ? p with { IsCommander = true,
                BaseAbilities = new(50,50,50,p.Id == 21 ? 99 : 10) } : p).ToArray()
        });
        var catalog = new CommandFacilityCatalog(view,MissionScenario.Texts(view),"burn",[2,3]);
        for (int page = 0; page < catalog.Pages.Count; page++)
        {
            Assert.Equal(new[] {3,2},catalog.Read(page).Select(r => r.Id));
            Assert.Equal("enemy",catalog.Read(page).Single(r => r.Id == 2).Cells[0].Text);
        }
    }

    [Fact]
    public void ArrivalProjectionIsReadOnlyComputedOnceAndSurvivesPageChanges()
    {
        var session = GameSession.Start(MissionScenario.Create(),42);
        var draft = new MissionDraft(session,1,"burn");
        byte[] before = SaveService.Encode(session);
        int calls = 0;
        var catalog = new CommandFacilityCatalog(draft.View,MissionScenario.Texts(draft.View),"burn",draft.Targets,
            id => { calls++; return draft.ArrivalDaysToSettlement(id); });
        int column = Array.FindIndex(catalog.Pages[0].Columns,c => c.Id == "estimate");
        Assert.True(catalog.Read(0).Single(r => r.Id == 2).Cells[column].Number > 0);
        catalog.Read(1); catalog.Read(0);
        Assert.Equal(draft.Targets.Length,calls);
        Assert.Null(draft.TargetId);
        Assert.Equal(before,SaveService.Encode(session));
    }

    [Fact]
    public void NonServingCommanderFlagDoesNotCompeteWithTheFacilityCommander()
    {
        var definition = MissionScenario.Create();
        var session = GameSession.Start(definition,42);
        var view = new StrategySnapshot(definition,session.View.World with
        {
            Officers = session.View.World.Officers.Select(p => p.Id == 22 ? p with
            {
                OwnerId = null, Status = San9.Core.Domain.OfficerStatus.Free, IsCommander = true,
                BaseAbilities = new(100,100,100,100)
            } : p.Id == 21 ? p with { IsCommander = true } : p).ToArray()
        });
        var texts = MissionScenario.Texts(view);
        var administrative = new AdministrativeCatalog(view,texts);
        Assert.Equal(21,administrative.Commander(2)?.Id);
        Assert.Equal("enemy-ruler",administrative.Cell(InformationCategory.Regions,2,"commander").Text);
        var catalog = new CommandFacilityCatalog(view,texts,"burn",[2,3]);
        int commander = Array.FindIndex(catalog.Pages[0].Columns,c => c.Id == "commander");
        Assert.Equal("enemy-ruler",catalog.Read(0).Single(r => r.Id == 2).Cells[commander].Text);
        int leadership = Array.FindIndex(catalog.Pages[0].Columns,c => c.Id == "commander-leadership");
        Assert.Equal(view.Officers.Single(p => p.Id == 21).Abilities.Leadership,
            catalog.Read(0).Single(r => r.Id == 2).Cells[leadership].Number);
    }

}
