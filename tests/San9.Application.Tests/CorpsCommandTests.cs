using San9.Tests;
using San9.Application.Persistence;
using San9.Application.Presentation;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class CorpsCommandTests
{
    private static GameSession ManagementSession()
    {
        var basis = G01ScenarioTests.Fixture();
        return GameSession.Start(basis with
        {
            Settlements = [basis.Settlements[0] with { CorpsId = 10 }, basis.Settlements[1],
                basis.Settlements[0] with { Id = 3, CorpsId = 11 }],
            Officers = basis.Officers.Select(p => p.State.Id == 2 ? p with { State = p.State with { SettlementId = 3 } } : p).ToArray(),
            Information = basis.Information with { Corps = [.. basis.Information.Corps.Where(c => c.OwnerId != 1),
                new(10,1,1,new(1000,2000)) { IsRoot = true }, new(11,1,2,new(100,200))] }
        },42);
    }

    [Fact]
    public void ControlDraftRevokesOrdersAndKeepsManagementReachableUntilControlIsRestored()
    {
        var session = ManagementSession();
        var draft = new CorpsDraft(session); draft.SetOperation(CorpsOperation.Control);
        Assert.True(draft.SelectCorps(10)); Assert.False(draft.CanExecute);
        var before = session.Capture();
        draft.SetControl(CorpsControlMode.Automatic);
        Assert.True(draft.CanExecute); Assert.Same(before,session.Capture());
        Assert.True(session.Execute(new("player"),draft.CreateCommand()).Submission.Accepted);
        Assert.False(draft.IsCurrent);
        var entries = MapCommandMenu.Settlement(session.View,1).SelectMany(g => g.Children).ToArray();
        Assert.Null(entries.Single(e => e.Id == "delegate").UnavailableReasonKey);
        Assert.NotNull(entries.Single(e => e.Id == "cultivate").UnavailableReasonKey);
        draft = new(session); draft.SetOperation(CorpsOperation.Control); Assert.True(draft.SelectCorps(10));
        draft.SetControl(CorpsControlMode.Direct); Assert.True(draft.CanExecute);
        Assert.True(session.Execute(new("player"),draft.CreateCommand()).Submission.Accepted);
        Assert.Equal(before.World.Corps,session.Capture().World.Corps);
        Assert.Null(MapCommandMenu.Settlement(session.View,1).SelectMany(g => g.Children).Single(e => e.Id == "cultivate").UnavailableReasonKey);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void IndependentPolicyUsesPublicTargetsEvenWithDelegatedRootAndDoesNotReorganizeCorps(int kind)
    {
        var session = ManagementSession();
        Assert.True(session.Execute(new("player"),new SetCorpsDelegationCommand(Guid.NewGuid(),1,0,11,new DomesticDelegation())).Submission.Accepted);
        Assert.True(session.Execute(new("player"),new SetCorpsControlCommand(Guid.NewGuid(),1,1,10,CorpsControlMode.Automatic)).Submission.Accepted);
        var draft = new CorpsDraft(session); draft.SetOperation(CorpsOperation.Policy);
        Assert.False(draft.SelectCorps(10)); Assert.False(draft.SelectCorps(2)); Assert.True(draft.SelectCorps(11));
        Assert.False(draft.CanExecute);
        var before = session.Capture();
        CorpsDelegation policy = kind switch { 0 => new GeneralDelegation(), 1 => new DomesticDelegation(),
            2 => new DefeatForceDelegation(2), _ => new CaptureSettlementDelegation(2) };
        Assert.Contains(2,draft.PolicyTargets(true)); Assert.Contains(2,draft.PolicyTargets(false));
        draft.SetPolicy(policy);
        Assert.Equal(kind != 1,draft.CanExecute); Assert.Same(before,session.Capture());
        if (kind == 1) return;
        Assert.True(session.Execute(new("player"),draft.CreateCommand()).Submission.Accepted);
        var previous = before.World.Corps.Single(c => c.Id == 11);
        Assert.Equal(previous with { Delegation = policy },session.Snapshot.Corps.Single(c => c.Id == 11));
        Assert.Equal(before.World.Settlements,session.Snapshot.Settlements);
        Assert.Equal(before.World.Units,session.Snapshot.Units);
    }

    [Fact]
    public void InvalidPolicyAndStaleAdministrationDraftCannotPublishChanges()
    {
        var session = ManagementSession();
        var draft = new CorpsDraft(session); draft.SetOperation(CorpsOperation.Policy); draft.SelectCorps(11);
        draft.SetPolicy(new DefeatForceDelegation(1));
        var before = session.Capture(); Assert.False(draft.CanExecute);
        Assert.False(session.Execute(new("player"),draft.CreateCommand()).Submission.Accepted);
        Assert.Same(before,session.Capture());
        draft.SetPolicy(new DefeatForceDelegation(2)); Assert.True(draft.CanExecute);
        Assert.True(session.Execute(new("player"),new SetCorpsControlCommand(Guid.NewGuid(),1,0,11,CorpsControlMode.Automatic)).Submission.Accepted);
        Assert.Equal(CommandRejection.NotStable,draft.Rejection); Assert.False(draft.CanExecute);
        Assert.Empty(draft.PolicyTargets(true));
    }

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
        Assert.DoesNotContain(draft.View.RequirePlayerForce(),targets);
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
            var command = new SetCorpsDelegationCommand(Guid.NewGuid(),draft.View.RequirePlayerForce(),
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
