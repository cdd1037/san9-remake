using San9.Application;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class RuntimeSessionTests
{
    [Fact]
    public void SessionOwnsStateAndMetadataAndProjectsCommittedChanges()
    {
        var initial = new ScenarioInitialState(15,"fixture",190,1,1,5,[new(5,"force",5,100,200,100)],
            [new(15,"city",5,89,64,1000,500,1000,50,[]) { CorpsId=5, IsFacilityListEntry=true, Commerce=300,MaxCommerce=1000,Cultivation=100,MaxCultivation=400 }],[],[])
            { Officers=[new("officer.1",new(1,5,15,new(0,0,100,0),false))], Information = new([], [], [], [new(5,5,1,new(100,200))], []) };
        var first=GameSession.Start(initial,1); var second=GameSession.Start(initial,1);
        var old=first.View;
        var roster = new StrategyRoster(); roster.Refresh(old, 5); roster.SelectPage(RosterPage.Officers);
        Assert.True(Assert.Single(roster.Current.Ordinary).IsAvailable);
        initial.Officers[0] = new("changed", initial.Officers[0].State with { BaseAbilities = new(1,1,1,1) });
        initial.Forces[0]=initial.Forces[0] with { Money=1,ColorCode=49 };
        var development = first.Develop(new(5, 15, [1], DomesticActivity.Commerce));
        Assert.True(development.Accepted);
        Assert.Equal(100u,first.View.Forces.Single().Money);
        Assert.Equal(5,first.View.Forces.Single().ColorCode);
        Assert.False(first.View.Settlements.Single().HasAvailableOfficer);
        Assert.True(old.Settlements.Single().HasAvailableOfficer);
        Assert.False(first.View.Officers.Single().IsAvailable);
        Assert.True(old.Officers.Single().IsAvailable);
        Assert.True(second.View.Officers.Single().IsAvailable);
        Assert.Equal(100, first.View.Officers.Single().Abilities.Politics);
        Assert.Equal(100u,old.Forces.Single().Money);
        Assert.Equal(100u,second.View.Forces.Single().Money);
        Assert.Equal(300,second.View.Settlements.Single().Commerce);
        Assert.Equal(new SessionRecord(old.World.Clock, 5, 15, 1, DomesticActivity.Commerce, development.Increment, development.Cost),
            Assert.Single(first.Records));
        Assert.False(first.Develop(new(5, 15, [1], DomesticActivity.Commerce)).Accepted);
        Assert.Single(first.Records);
        Assert.Empty(second.Records);
        Assert.NotEqual(first.Snapshot.StateHash,second.Snapshot.StateHash);
        Assert.Same(first.InitialView.Information, first.View.Information);
        Assert.Same(first.InitialView.RegionNameKeys, first.View.RegionNameKeys);
        Assert.Same(first.InitialView.RegionCityIds, first.View.RegionCityIds);
        Assert.Same(first.InitialView.Settlements.Single().Icons, first.View.Settlements.Single().Icons);
        roster.Refresh(first.View, 5);
        Assert.False(Assert.Single(roster.Current.Ordinary).IsAvailable);
        Assert.Equal("officer.1", first.View.Officers.Single().NameKey);
        first.AdvanceXun(); roster.Refresh(first.View, 5);
        Assert.True(Assert.Single(roster.Current.Ordinary).IsAvailable);
    }
}
