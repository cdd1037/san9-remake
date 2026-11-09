using System.Text.Json.Nodes;
using San9.Application.AI;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Application.Tests;

public sealed class PlayerDomesticAutomationTests
{
    private static readonly CompanionSettings Enabled = new() { PlayerCorpsAutomation = PlayerCorpsAutomationMode.DomesticAndSearch, PlayerAutoDomesticEnabled = true };

    private static ScenarioInitialState Content(DomesticActivity activity, uint money = 1000)
    {
        var content = G01ScenarioTests.Fixture();
        var city = content.Settlements[0] with
        {
            Morale = activity == DomesticActivity.Train ? 50 : 100,
            Durability = activity == DomesticActivity.Repair ? 50 : 100,
            Commerce = activity == DomesticActivity.Commerce ? 300 : 500,
            Cultivation = activity == DomesticActivity.Cultivation ? 200 : 500,
            PublicOrder = activity == DomesticActivity.Patrol ? 500 : 1000,
        };
        return content with
        {
            Settlements = [city, content.Settlements[1]],
            Officers = content.Officers.Select(p => p.State.OwnerId == 1
                ? p with { State = p.State with { BaseAbilities = new(100, 100, 100, 100) } } : p).ToArray(),
            Information = content.Information with { Corps = content.Information.Corps.Select(c => c.OwnerId == 1
                ? c with { Treasury = new(money, c.Treasury.Food) } : c).ToArray() },
        };
    }

    [Theory]
    [InlineData(DomesticActivity.Train)]
    [InlineData(DomesticActivity.Repair)]
    [InlineData(DomesticActivity.Commerce)]
    [InlineData(DomesticActivity.Cultivation)]
    [InlineData(DomesticActivity.Patrol)]
    public void EndControlRunsOnlyEnabledDomesticWorkThroughPublicSettlement(DomesticActivity activity)
    {
        var content = Content(activity);
        var disabled = GameSession.Start(content, 190);
        var enabled = GameSession.Start(content, 190, companionSettings: Enabled);
        var before = enabled.Snapshot;
        Assert.Empty(disabled.EndControl(new("player")).Events.OfType<DevelopmentCompletedEvent>());
        var result = enabled.EndControl(new("player"));
        var completed = Assert.Single(result.Events.OfType<DevelopmentCompletedEvent>());
        Assert.Equal(activity, completed.Command.Activity);
        Assert.Equal(1, completed.Command.ForceId);
        Assert.Equal(1, completed.Command.SettlementId);
        Assert.Equal(before.Corps.Single(c => c.Id == 1).Treasury.Money - completed.Cost,
            enabled.Snapshot.Corps.Single(c => c.Id == 1).Treasury.Money);
        Assert.All(completed.Command.OfficerIds, id => Assert.True(enabled.Snapshot.Officers.Single(p => p.Id == id).Acted));
        Assert.All(enabled.Snapshot.Officers.Where(p => p.OwnerId == 2), p => Assert.False(p.Acted));
        Assert.Single(enabled.Records);
        Assert.Empty(enabled.Snapshot.Simulation.Tasks);
        Assert.Equal(1, enabled.Snapshot.Simulation.NextSequence);
        Assert.Equal(1, enabled.Snapshot.Simulation.ActiveControl);
        Assert.Single(enabled.LastOriginalAiWork.Where(r => r.Submitted > 0));
    }

    [Theory]
    [InlineData(false, false, 0, 0)]
    [InlineData(false, true, 0, 3)]
    [InlineData(true, false, 1, 0)]
    [InlineData(true, true, 1, 2)]
    public void DomesticAndLocalSearchSwitchesAreIndependentAndDomesticRunsFirst(
        bool domestic, bool search, int developments, int searches)
    {
        var session = GameSession.Start(Content(DomesticActivity.Train), 190, companionSettings: new()
        {
            PlayerCorpsAutomation = PlayerCorpsAutomationMode.DomesticAndSearch,
            PlayerAutoDomesticEnabled = domestic,
            PlayerAutoSearchEnabled = search,
        });
        var result = session.EndControl(new("player"));
        Assert.Equal(developments, result.Events.OfType<DevelopmentCompletedEvent>().Count());
        var tasks = result.Events.OfType<TaskStartedEvent>().Select(e => e.Task).ToArray();
        Assert.Equal(searches, tasks.Length);
        Assert.All(tasks, task => Assert.Equal(session.Snapshot.Officers.Single(p => p.Id == task.OfficerId).SettlementId,
            task.Journey!.DestinationPlaceId));
        Assert.Equal(search, session.LastOriginalAiWork.Any(r => r.Work == OriginalAiWork.Search));
    }

    [Fact]
    public void LocalSearchUsesConfiguredMinimumEffectivePolitics()
    {
        var content = Content(DomesticActivity.Train);
        content = content with { Officers = content.Officers.Select(p => p.State.OwnerId == 1
            ? p with { State = p.State with
            {
                BaseAbilities = p.State.BaseAbilities with { Politics = p.State.Id switch { 1 => 44, 2 => 50, _ => 70 } },
                // First-xun preparation heals one level; keep one level for effective politics 45.
                Injury = p.State.Id == 2 ? 2 : 0,
            } } : p).ToArray() };
        foreach (var (minimum, expected) in new (int Minimum, int[] Expected)[]
            { (45, [2, 3]), (46, [3]), (0, [1, 2, 3]), (71, []) })
        {
            var session = GameSession.Start(content, 190, companionSettings: new()
                { PlayerCorpsAutomation = PlayerCorpsAutomationMode.DomesticAndSearch, PlayerAutoSearchEnabled = true, PlayerAutoSearchMinimumPolitics = minimum });
            Assert.Equal(45, session.View.Officers.Single(p => p.Id == 2).Abilities.Politics);
            var result = session.EndControl(new("player"));
            Assert.Equal(expected, result.Events.OfType<TaskStartedEvent>().Select(e => e.Task.OfficerId));
            Assert.Empty(result.Events.OfType<DevelopmentCompletedEvent>());
        }
    }

    [Fact]
    public void ManualWorkAndItsParticipantsAreNotRepeatedAtEndControl()
    {
        var content = Content(DomesticActivity.Commerce);
        var session = GameSession.Start(content, 190, companionSettings: Enabled);
        Assert.True(session.Develop(new(1, 1, [1], DomesticActivity.Commerce)).Accepted);
        var before = session.Snapshot;
        Assert.Empty(session.EndControl(new("player")).Events.OfType<DevelopmentCompletedEvent>());
        Assert.Single(session.Records);
        Assert.Equal(before.Corps, session.Snapshot.Corps);
        Assert.Equal(before.Simulation.NextSequence, session.Snapshot.Simulation.NextSequence);
        Assert.False(session.Snapshot.Officers.Single(p => p.Id == 2).Acted);
    }

    [Fact]
    public void DevelopmentReserveDoesNotPreventAffordableTraining()
    {
        foreach (var activity in new[] { DomesticActivity.Commerce, DomesticActivity.Train })
        {
            var session = GameSession.Start(Content(activity, 200), 190, companionSettings: Enabled);
            uint before = session.Snapshot.Corps.Single(c => c.Id == 1).Treasury.Money;
            var result = session.EndControl(new("player"));
            if (activity == DomesticActivity.Commerce)
            {
                Assert.Empty(result.Events.OfType<DevelopmentCompletedEvent>());
                Assert.Contains(session.LastOriginalAiWork, r => r.Work == OriginalAiWork.Commerce && r.End == OriginalAiWorkEnd.NoCandidates);
            }
            else Assert.Single(result.Events.OfType<DevelopmentCompletedEvent>());
            Assert.Equal(before - result.Events.OfType<DevelopmentCompletedEvent>().Sum(e => (long)e.Cost),
                session.Snapshot.Corps.Single(c => c.Id == 1).Treasury.Money);
        }
    }

    [Fact]
    public void WorkOrderRereadsPeopleAndPreservesExistingSearchTasks()
    {
        var content = Content(DomesticActivity.Commerce);
        content = content with { Settlements = content.Settlements.Select(s => s.Id == 1
            ? s with { Morale = 50, Durability = 50 } : s).ToArray() };
        var session = GameSession.Start(content, 190, companionSettings: Enabled);
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, [3], 1)).Submission.Accepted);
        var search = Assert.Single(session.Snapshot.Simulation.Tasks);
        int commerce = session.Snapshot.Settlements.Single(s => s.Id == 1).Commerce;
        var completed = session.EndControl(new("player")).Events.OfType<DevelopmentCompletedEvent>().ToArray();
        Assert.Equal(new[] { DomesticActivity.Train, DomesticActivity.Repair }, completed.Select(e => e.Command.Activity));
        Assert.Equal(2, completed.SelectMany(e => e.Command.OfficerIds).Distinct().Count());
        Assert.DoesNotContain(completed, e => e.Command.OfficerIds.Contains(3));
        Assert.Equal(search, Assert.Single(session.Snapshot.Simulation.Tasks));
        Assert.Equal(commerce, session.Snapshot.Settlements.Single(s => s.Id == 1).Commerce);
    }

    [Fact]
    public void AutomationUsesOnlyTheRootAndLeavesDelegatedCorpsUntouched()
    {
        var content = Content(DomesticActivity.Train);
        content = content with
        {
            Settlements = [.. content.Settlements, content.Settlements[0] with { Id = 3, CorpsId = 3 },
                content.Settlements[0] with { Id = 4, CorpsId = 4 }],
            Officers = content.Officers.Select(p => p.State.Id is 2 or 3
                ? p with { State = p.State with { SettlementId = p.State.Id + 1 } } : p).ToArray(),
            Information = content.Information with { Corps = [.. content.Information.Corps,
                new(3, 1, 2, new(1000, 2000)), new(4, 1, 3, new(1000, 2000))] },
        };
        var definition = new ScenarioSessionDefinition(content);
        var engine = GameSession.EngineFactory(content)(definition.CreateWorld(190));
        engine.ConfigureCompanion(Enabled);
        var before = engine.Snapshot;
        List<DomainEvent> events = [];
        List<OriginalAiWorkReport> reports = [];
        PlayerDomesticAutomation.Run(engine, definition, new("player", 1, ControllerKind.Player),
            OriginalAiState.Create(content), events, reports);
        Assert.Equal(new[] { 1 }, events.OfType<DevelopmentCompletedEvent>().Select(e => e.Command.SettlementId));
        Assert.False(engine.Snapshot.Officers.Single(p => p.Id == 2).Acted);
        Assert.Equal(before.Corps.Single(c => c.Id == 4), engine.Snapshot.Corps.Single(c => c.Id == 4));
        Assert.Equal(before.Settlements.Single(s => s.Id == 4), engine.Snapshot.Settlements.Single(s => s.Id == 4));
        Assert.False(engine.Snapshot.Officers.Single(p => p.Id == 3).Acted);
        Assert.DoesNotContain(reports, r => r.CorpsId == 4);
    }

    [Theory]
    [InlineData(PlayerCorpsAutomationMode.Disabled)]
    [InlineData(PlayerCorpsAutomationMode.DomesticAndSearch)]
    [InlineData(PlayerCorpsAutomationMode.Full)]
    public void ModeSelectsExactlyOnePipelineAndFullIgnoresSpecializedOptions(PlayerCorpsAutomationMode mode)
    {
        var content = Content(DomesticActivity.Train);
        var settings = Enabled with { PlayerCorpsAutomation = mode, PlayerAutoSearchEnabled = true,
            PlayerAutoSearchMinimumPolitics = int.MaxValue };
        var a = GameSession.Start(content, 190, companionSettings: settings);
        var b = GameSession.Start(content, 190, companionSettings: mode == PlayerCorpsAutomationMode.Full
            ? settings with { PlayerAutoDomesticEnabled = false, PlayerAutoSearchEnabled = false, PlayerAutoSearchMinimumPolitics = 0 }
            : settings);
        var first = a.EndControl(new("player"));
        var second = b.EndControl(new("player"));
        EventAssert.Equal(first.Events, second.Events);
        StateAssert.Equal(a.Capture(), b.Capture());
        if (mode == PlayerCorpsAutomationMode.Disabled) Assert.Empty(a.LastOriginalAiWork);
        else if (mode == PlayerCorpsAutomationMode.DomesticAndSearch)
        {
            Assert.Single(first.Events.OfType<DevelopmentCompletedEvent>());
            Assert.Single(a.LastOriginalAiWork, r => r.Work == OriginalAiWork.Train);
            Assert.DoesNotContain(a.LastOriginalAiWork, r => r.Work == OriginalAiWork.Retreat);
        }
        else
        {
            Assert.Contains(a.LastOriginalAiWork, r => r.Work == OriginalAiWork.Retreat);
            Assert.Single(a.LastOriginalAiWork, r => r.Work == OriginalAiWork.Train);
        }
        Assert.All(first.Events.OfType<DevelopmentCompletedEvent>().SelectMany(e => e.Command.OfficerIds).GroupBy(id => id),
            group => Assert.Single(group));
    }

    [Fact]
    public void GameSavesExcludeHostConfiguration()
    {
        var content = Content(DomesticActivity.Train);
        var session = GameSession.Start(content, 190, companionSettings: Enabled);
        var save = JsonNode.Parse(SaveService.Encode(session))!;
        var simulation = save["session"]!["world"]!["simulation"]!.AsObject();
        Assert.False(simulation.ContainsKey("companion"));
        Assert.All(save["session"]!["world"]!["corps"]!.AsArray(), corps => Assert.False(corps!.AsObject().ContainsKey("controlMode")));
        Assert.DoesNotContain("playerCorpsAutomation", save.ToJsonString());
        Assert.DoesNotContain("allowDelegatedCorpsOrders", save.ToJsonString());
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        Assert.Equal(CompanionSettings.Default, restored.Companion);
    }

    [Fact]
    public void HostCanSupplyTheSameConfigurationAfterExactRestore()
    {
        var content = Content(DomesticActivity.Train);
        var session = GameSession.Start(content, 190);
        Assert.Equal(CompanionSettings.Default, session.Companion);
        session.ConfigureCompanion(Enabled);
        Assert.Equal(0, session.NextOperationSequence);
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        restored.ConfigureCompanion(Enabled);
        var expected = session.EndControl(new("player"));
        var actual = restored.EndControl(new("player"));
        Assert.Single(actual.Events.OfType<DevelopmentCompletedEvent>());
        EventAssert.Equal(expected.Events, actual.Events);
        StateAssert.Equal(session.Capture(), restored.Capture());
        restored.ConfigureCompanion(CompanionSettings.Default);
        Assert.False(restored.Companion.PlayerAutoDomesticEnabled);
        Assert.False(GameSession.Start(content, 190).Companion.PlayerAutoDomesticEnabled);
    }

    [Fact]
    public void HostConfigurationCannotConsumeSequenceOrChangeWorld()
    {
        var session = GameSession.Start(Content(DomesticActivity.Train), 190);
        var before = session.Capture();
        session.ConfigureCompanion(Enabled);
        Assert.Equal(Enabled, session.Companion);
        Assert.Throws<ArgumentNullException>(() => session.ConfigureCompanion(null!));
        StateAssert.Equal(before, session.Capture());
    }

    [Fact]
    public void LaterAdvanceFailureRollsBackAutomaticWorkAndSettingsRemainEffective()
    {
        var session = GameSession.Start(Content(DomesticActivity.Train), 190,
            world => new SimulationEngine(world, new FailingAdvanceRules()), companionSettings: Enabled);
        var before = session.Capture();
        Assert.Throws<InvalidOperationException>(() => session.AdvanceXun());
        StateAssert.Equal(before, session.Capture());
        Assert.Empty(session.Records);
        Assert.Empty(session.LastOriginalAiWork);
    }

    private sealed class FailingAdvanceRules : SimulationRules
    {
        internal override void ApplyStage(ResolutionStage stage, WorldMutation world, List<DomainEvent> events)
        {
            if (stage == ResolutionStage.BeforeDailyCommands) throw new InvalidOperationException("Advance failed after automatic work.");
        }
    }
}
