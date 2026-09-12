using San9.Application;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class StrategyOverviewTests
{
    private static ScenarioInitialState Fixture() => new(ScenarioInitialState.CurrentSchemaVersion,"overview",190,1,1,4,
        [new(0,"a",0,0,0,200),new(1,"b",1,0,0,200),new(2,"c",2,0,0,150),new(3,"d",3,0,0,100),new(4,"player",4,0,0,100),new(5,"tribe",5,0,0,999){IsBarbarian=true}],
        [new(0,"city",4,10,20,1000,100,100,50,[]),new(50,"port",4,11,20,200,100,100,50,[]){Kind=SettlementKind.Facility}],[],[])
        { Officers=[new("officer",new(0,4,0,new(50,50,50,50),false))],Units=[new(0,4,MapCoordinates.Center(new(12,20)),300)] };

    [Fact]
    public void RankingUsesCompetitionRanksAndAddsPlayerOnlyOutsideTopThree()
    {
        var view = GameSession.Start(Fixture(),1).View;
        var columns = StrategyOverview.Rankings(view);
        var trust = columns.Single(c => c.Metric == ForceRankingMetric.Trust);
        Assert.Equal(new[]{1,1,3},trust.Leaders.Select(r=>r.Rank));
        Assert.Equal(new[]{0,1,2},trust.Leaders.Select(r=>r.Force.Id));
        Assert.Equal(4,trust.Player!.Rank);
        Assert.Equal(100,trust.Player.Value);
        Assert.DoesNotContain(columns.SelectMany(c=>c.Leaders),r=>r.Force.Id==5);
        var soldiers=columns.Single(c=>c.Metric==ForceRankingMetric.Soldiers);
        Assert.Equal(1500,soldiers.Leaders[0].Value);
        Assert.Null(soldiers.Player);
        Assert.Equal(1,columns.Single(c=>c.Metric==ForceRankingMetric.Cities).Leaders[0].Value);
        Assert.Equal(1,columns.Single(c=>c.Metric==ForceRankingMetric.Officers).Leaders[0].Value);
    }

    [Fact]
    public void UnitsAreOwnedSnapshotsAndPlayerBarbarianRemainsEligible()
    {
        var initial=Fixture(); var session=GameSession.Start(initial,1);
        initial.Units[0]=initial.Units[0] with {Soldiers=9000};
        Assert.Equal(300u,Assert.Single(session.Snapshot.Units).Soldiers);
        Assert.Equal(1500,StrategyOverview.Rankings(session.View).Single(c=>c.Metric==ForceRankingMetric.Soldiers).Leaders[0].Value);
        Assert.Throws<InvalidDataException>(()=>(initial with {Units=[new(0,99,MapCoordinates.Center(new(10,20)),100)]}).Validate());
        Assert.Throws<InvalidDataException>(()=>(initial with {Units=[new(0,4,new(-1,20),100)]}).Validate());
        var barbarian=GameSession.Start(initial with {PlayerForceId=5},1);
        Assert.Equal(5,StrategyOverview.Rankings(barbarian.View)[0].Leaders[0].Force.Id);
    }

    [Fact]
    public void ClosingDoesNotResetIndependentLayers()
    {
        var state=new StrategyOverview(); state.Open();
        state.Toggle(OverviewLayer.Forces); state.Toggle(OverviewLayer.Units);
        state.Close(); state.Open();
        Assert.True(state.IsOpen); Assert.False(state.ShowForces); Assert.True(state.ShowFacilities); Assert.False(state.ShowUnits);
    }
}
