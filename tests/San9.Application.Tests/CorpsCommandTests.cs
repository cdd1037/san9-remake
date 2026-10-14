using San9.Tests;
using San9.Application.Persistence;
using San9.Application.Presentation;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class CorpsCommandTests
{
    [Fact]
    public void ManagementPreparesAndSubmitsAllOperationsThroughPublicRules()
    {
        var basis = G01ScenarioTests.Fixture();
        var content = basis with
        {
            RegionNameKeys = new() { [1] = "city.1", [3] = "city.3" }, RegionCityIds = new() { [1] = 1, [3] = 3 },
            Settlements = [basis.Settlements[0] with { CorpsId = 1000 },basis.Settlements[1],basis.Settlements[0] with { Id = 3, CorpsId = 1000 }],
            Officers = basis.Officers.Select(p => p.State.Id == 2 ? p with { State = p.State with { SettlementId = 3 } } : p).ToArray(),
            Information = basis.Information with { Corps = [.. basis.Information.Corps.Where(c => c.OwnerId != 1),new(1000,1,1,new(1000,2000)) { IsRoot = true }] }
        };
        var session = GameSession.Start(content,42);
        var treasury = session.Snapshot.Corps.Single(c => c.OwnerId == 1).Treasury;
        var draft = new CorpsDraft(session);
        var before = session.Capture();
        Assert.DoesNotContain(1,draft.CityCandidates);
        Assert.False(draft.SetCities([1])); Assert.False(draft.SetCities([3,3]));
        Assert.True(draft.SetCities([3]));
        Assert.Equal((uint)((ulong)draft.MaximumMoney * draft.Counts.People / draft.Counts.TotalPeople), draft.Money);
        Assert.Equal(1000U,draft.Food);
        var targets = draft.PolicyTargets(true);
        Assert.DoesNotContain(draft.View.PlayerForceId,targets);
        Assert.NotEmpty(targets);
        draft.SetMoney(100); draft.SetFood(200);
        Assert.Equal(2,draft.GovernorId); Assert.False(draft.CanExecute);
        var counts = draft.Counts;
        Assert.Equal(1UL,counts.People); Assert.Equal(3UL,counts.TotalPeople);
        draft.SetPolicy(new DomesticDelegation()); Assert.True(draft.CanExecute);
        Assert.Same(before, session.Capture());
        Assert.True(session.Execute(new("player"),draft.CreateCommand()).Submission.Accepted);
        Assert.Equal(CommandRejection.NotStable,draft.Rejection);
        int corps = session.Snapshot.Corps.Single(c => c.OwnerId == 1 && !c.IsRoot).Id;
        Assert.IsType<DomesticDelegation>(session.Snapshot.Corps.Single(c => c.Id == corps).Delegation);
        draft = new(session); draft.SetOperation(CorpsOperation.Change); Assert.True(draft.SelectCorps(corps)); Assert.False(draft.CanExecute);
        Assert.Equal(treasury.Money,draft.MaximumMoney); Assert.Equal(treasury.Food,draft.MaximumFood);
        draft.SetMoney(draft.MaximumMoney+1); Assert.Equal(CommandRejection.InsufficientFunds,draft.Rejection);
        draft.SetMoney(50); draft.SetFood(300); Assert.True(session.Execute(new("player"),draft.CreateCommand()).Submission.Accepted);
        draft = new(session); draft.SetOperation(CorpsOperation.Change);
        before = session.Capture(); var policy = draft.Policy;
        _ = draft.PolicyTargets(true); _ = draft.PolicyTargets(false);
        foreach (var delegation in draft.View.Forces.Select(f => (CorpsDelegation)new DefeatForceDelegation(f.Id))
            .Concat(draft.View.Settlements.Select(s => new CaptureSettlementDelegation(s.Id))))
        {
            var command = new SetCorpsDelegationCommand(Guid.NewGuid(),draft.View.PlayerForceId,
                session.Snapshot.Simulation.NextSequence,corps,delegation);
            Assert.Equal(session.Validate(new("player"),command).Rejection,session.GetPreparationEligibility(new("player"),command));
        }
        Assert.Equal(policy,draft.Policy); Assert.Same(before, session.Capture());
        draft = new(session); draft.SetOperation(CorpsOperation.Dissolve); Assert.True(draft.SelectCorps(corps));
        Assert.True(session.Execute(new("player"),draft.CreateCommand()).Submission.Accepted);
        Assert.Single(session.Snapshot.Corps,c => c.OwnerId == 1);
        Assert.Equal(treasury,session.Snapshot.Corps.Single(c => c.OwnerId == 1).Treasury);
    }
}
