using San9.Tests;
using San9.Content.Scenarios;
using San9.Application.Persistence;
using San9.Application.Presentation;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class CorpsCommandTests
{
    [Fact]
    public void ChangeDraftIncludesAnotherCorpsFacilityInTheAddedCityPreviewAndSubmission()
    {
        var content = ManagementContent();
        content = content with
        {
            RegionNameKeys = new() { [1] = "city.1", [3] = "city.3", [4] = "city.4", [5] = "facility.5", [6] = "city.6" },
            RegionCityIds = new() { [1] = 1, [3] = 3, [4] = 4, [5] = 4, [6] = 6 },
            Settlements = [.. content.Settlements, content.Settlements[0] with { Id = 4, CorpsId = 10 },
                content.Settlements[0] with { Id = 5, Kind = SettlementKind.Facility, CorpsId = 12, Soldiers = 123, Wounded = 7 },
                content.Settlements[0] with { Id = 6, CorpsId = 12 }],
            Officers = content.Officers.Select(p => p.State.Id == 3
                ? p with { State = p.State with { SettlementId = 6 } } : p).ToArray(),
            Information = content.Information with { Corps = [.. content.Information.Corps, new(12, 1, 3, new(100, 100))] }
        };
        var session = GameSession.Start(content, 42);
        var draft = new CorpsDraft(session);
        draft.SetOperation(CorpsOperation.Change);
        Assert.True(draft.SelectCorps(11));
        Assert.True(draft.SetCities([3, 4]));
        Assert.Equal((ulong)session.Snapshot.Settlements.Where(s => s.Id is 3 or 4 or 5).Sum(s => (long)s.Soldiers + s.Wounded),
            draft.Counts.Soldiers);
        Assert.True(draft.CanExecute);
        Assert.True(session.Execute(new("player"), draft.CreateCommand()).Submission.Accepted);
        Assert.Equal(11, session.Snapshot.Settlements.Single(s => s.Id == 5).CorpsId);
        Assert.Equal(12, session.Snapshot.Settlements.Single(s => s.Id == 6).CorpsId);
    }

    [Theory]
    [InlineData("move", 3, 1, 3)]
    [InlineData("summon", 3, 3, 1)]
    public void RulerCanTransferPeopleAcrossAutomaticCorpsThroughBothPersonnelMenus(string kind, int home, int destination, int origin)
    {
        var content = ManagementContent();
        content = content with
        {
            Officers = content.Officers.Select(p => p.State.Id == 3
                ? p with { State = p.State with { SettlementId = origin } } : p).ToArray(),
            Information = content.Information with { Corps = content.Information.Corps.ToArray() }
        };
        var session = GameSession.Start(content, 42);
        Assert.Null(PersonnelDraft.UnavailableReasonKey(session.View, home, kind));
        Assert.NotNull(PersonnelDraft.UnavailableReasonKey(session.View, 3, "search"));
        var draft = new PersonnelDraft(session, home, kind);
        Assert.Contains(3, draft.Candidates);
        Assert.True(draft.SetTarget(destination));
        Assert.True(draft.SetMembers([3]));
        var command = draft.CreateCommand(Guid.NewGuid(), session.Snapshot.Simulation.NextSequence);
        Assert.Equal(CommandRejection.NotActiveController, session.Validate(new(CorpsControl.ControllerId(11)), command).Rejection);
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        Assert.Contains(session.Snapshot.Simulation.Tasks, t => t.OfficerId == 3 && t.Journey?.DestinationPlaceId == destination);
    }

    [Fact]
    public void AssistanceChangesDoNotReorganizeCorpsAndRefreshMenuAuthority()
    {
        var session = ManagementSession();
        var before = session.Capture();
        Assert.NotNull(MapCommandMenu.Settlement(session.View, 3).SelectMany(g => g.Children).Single(e => e.Id == "cultivate").UnavailableReasonKey);
        session.ConfigureCompanion(new() { AllowDelegatedCorpsOrders = true });
        Assert.Null(MapCommandMenu.Settlement(session.View, 3).SelectMany(g => g.Children).Single(e => e.Id == "cultivate").UnavailableReasonKey);
        StateAssert.Equal(before, session.Capture());
        session.ConfigureCompanion(CompanionSettings.Default);
        Assert.NotNull(MapCommandMenu.Settlement(session.View, 3).SelectMany(g => g.Children).Single(e => e.Id == "cultivate").UnavailableReasonKey);
    }

    [Theory]
    [InlineData(10, 1, 1)]
    [InlineData(11, 3, 2)]
    public void AssistanceAllowsPlayerWorkAndUsesOnlyRemainingPeople(int corpsId, int cityId, int officerId)
    {
        var content = ManagementContent();
        content = content with { Officers = content.Officers.Select(p => p.State.Id == 3
            ? p with { State = p.State with { SettlementId = cityId } } : p).ToArray() };
        var session = GameSession.Start(content,42);
        session.ConfigureCompanion(new() { AllowDelegatedCorpsOrders = true, PlayerCorpsAutomation = PlayerCorpsAutomationMode.Full });
        Assert.Null(MapCommandMenu.Settlement(session.View, cityId).SelectMany(g => g.Children)
            .Single(e => e.Id == "cultivate").UnavailableReasonKey);
        var command = new DevelopCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence,
            new(1, cityId, [officerId], DomesticActivity.Commerce));
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        var occupied = session.Snapshot;
        Assert.False(session.Execute(new(CorpsControl.ControllerId(corpsId)), command with
            { CommandId = Guid.NewGuid(), Sequence = occupied.Simulation.NextSequence }).Submission.Accepted);
        StateAssert.Equal(occupied, session.Snapshot);
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        restored.ConfigureCompanion(session.Companion);
        var result = session.Apply(session.Request(new EndControlOperation()));
        var replay = restored.Apply(restored.Request(new EndControlOperation()));
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        EventAssert.Equal(result.Events, replay.Events);
        StateAssert.Equal(session.Capture(), restored.Capture());
        Assert.All(result.Events.OfType<DevelopmentCompletedEvent>(), e => Assert.DoesNotContain(officerId, e.Command.OfficerIds));
        Assert.Contains(result.Events, e => e is DevelopmentCompletedEvent { Command: var development } && development.SettlementId == cityId);
    }

    private static GameSession ManagementSession() => GameSession.Start(ManagementContent(),42);

    private static ScenarioInitialState ManagementContent()
    {
        var basis = G01ScenarioTests.Fixture();
        return basis with
        {
            Settlements = [basis.Settlements[0] with { CorpsId = 10 }, basis.Settlements[1],
                basis.Settlements[0] with { Id = 3, CorpsId = 11 }],
            Officers = basis.Officers.Select(p => p.State.Id == 2 ? p with { State = p.State with { SettlementId = 3 } } : p).ToArray(),
            Information = basis.Information with { Corps = [.. basis.Information.Corps.Where(c => c.OwnerId != 1),
                new(10,1,1,new(1000,2000)) { IsRoot = true }, new(11,1,2,new(100,200))] }
        };
    }

    [Theory]
    [InlineData(0)] [InlineData(2)] [InlineData(3)]
    public void IndependentPolicyUsesPublicTargetsWithAssistanceAndDoesNotReorganizeCorps(int kind)
    {
        var session = ManagementSession();
        Assert.True(session.Execute(new("player"),new SetCorpsDelegationCommand(Guid.NewGuid(),1,0,11,new DomesticDelegation())).Submission.Accepted);
        session.ConfigureCompanion(new() { AllowDelegatedCorpsOrders = true, PlayerCorpsAutomation = PlayerCorpsAutomationMode.Full });
        var draft = new CorpsDraft(session); draft.SetOperation(CorpsOperation.Change);
        Assert.False(draft.SelectCorps(10)); Assert.False(draft.SelectCorps(2)); Assert.True(draft.SelectCorps(11));
        Assert.False(draft.CanExecute);
        var before = session.Capture();
        CorpsDelegation policy = kind switch { 0 => new GeneralDelegation(), 1 => new DomesticDelegation(),
            2 => new DefeatForceDelegation(2), _ => new CaptureSettlementDelegation(2) };
        Assert.Contains(2,draft.PolicyTargets(true)); Assert.Contains(2,draft.PolicyTargets(false));
        Assert.Same(before,session.Capture());
        Assert.True(session.Execute(new("player"), new SetCorpsDelegationCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 11, policy)).Submission.Accepted);
        var previous = before.World.Corps.Single(c => c.Id == 11);
        Assert.Equal(previous with { Delegation = policy },session.Snapshot.Corps.Single(c => c.Id == 11));
        Assert.Equal(before.World.Settlements,session.Snapshot.Settlements);
        Assert.Equal(before.World.Units,session.Snapshot.Units);
    }

    [Fact]
    public void InvalidPolicyAndStaleChangeDraftCannotPublishChanges()
    {
        var session = ManagementSession();
        var draft = new CorpsDraft(session); draft.SetOperation(CorpsOperation.Change); draft.SelectCorps(11);
        draft.SetPolicy(new DefeatForceDelegation(1));
        var before = session.Capture(); Assert.False(draft.CanExecute);
        Assert.False(session.Execute(new("player"),draft.CreateCommand()).Submission.Accepted);
        Assert.Same(before,session.Capture());
        draft.SetPolicy(new DefeatForceDelegation(2)); Assert.True(draft.CanExecute);
        Assert.True(session.Execute(new("player"), new SetCorpsDelegationCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 11, new DomesticDelegation())).Submission.Accepted);
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
        Assert.Equal(2,draft.GovernorId); Assert.True(draft.CanExecute);
        Assert.IsType<GeneralDelegation>(draft.Policy);
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
