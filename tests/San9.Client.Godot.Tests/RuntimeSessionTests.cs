using San9.Application;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Client.Godot.Tests;

public sealed class RuntimeSessionTests
{
    [Fact]
    public void SessionOwnsStateAndMetadataAndProjectsCommittedChanges()
    {
        var initial = new ScenarioInitialState(3,"fixture",190,1,1,5,[new(5,"force",5,100,200,100)],
            [new(15,"city",5,89,64,1000,500,1000,50,[]) { Commerce=300,MaxCommerce=1000,Cultivation=100,MaxCultivation=400 }],[],[])
            { Officers=[new(1,5,15,100,false)] };
        var first=GameSession.Start(initial,1); var second=GameSession.Start(initial,1);
        var old=first.View;
        initial.Forces[0]=initial.Forces[0] with { Money=1,ColorCode=49 };
        Assert.True(first.Develop(new(5,15,1,DomesticActivity.Commerce)).Accepted);
        Assert.Equal(50u,first.View.Forces.Single().Money);
        Assert.Equal(5,first.View.Forces.Single().ColorCode);
        Assert.False(first.View.Settlements.Single().HasAvailableOfficer);
        Assert.True(old.Settlements.Single().HasAvailableOfficer);
        Assert.Equal(100u,old.Forces.Single().Money);
        Assert.Equal(100u,second.View.Forces.Single().Money);
        Assert.Equal(300,second.View.Settlements.Single().Commerce);
        Assert.NotEqual(first.Snapshot.StateHash,second.Snapshot.StateHash);
    }
}
