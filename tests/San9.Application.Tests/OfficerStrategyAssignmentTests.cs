using System.Collections.Immutable;
using San9.Application.Persistence;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Application.Tests;

public sealed class OfficerStrategyAssignmentTests
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
            } : person), .. Enumerable.Range(100,7).Select(id => new OfficerInitialState("candidate",
                new(id,1,1,new(70,70,70,70),false)))]
        };
    }

    private static OfficerStrategyCommand Command(string kind, Guid id, ImmutableArray<int> officers, int target) => kind switch
    {
        "alienate" => new AlienateOfficersCommand(id,1,0,officers,target),
        "burn" => new BurnOfficersCommand(id,1,0,officers,target),
        "seize" => new SeizeOfficersCommand(id,1,0,officers,target),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    [Theory]
    [InlineData("alienate",22)]
    [InlineData("burn",2)]
    [InlineData("seize",2)]
    public void MultiSelectionCreatesIndependentMissionsWithSaveAndCommandLogRoundTrips(string kind, int target)
    {
        var content = Content();
        var session = GameSession.Start(content,42);
        var header = CommandLogService.CreateHeader(session);
        var before = session.Capture();
        var draft = new MissionDraft(session,1,kind);
        Assert.Single(draft.Members);
        Assert.True(draft.SetTarget(target));
        Assert.True(draft.SetMembers(draft.Actors.Reverse()));
        Assert.True(draft.Members.Length > 5);
        Assert.Equal(draft.RecommendedActors,draft.Members);
        Assert.Equal(draft.Members.Count(id => draft.ActorAdvice(id)?.Opinion == AdviserOpinion.Favorable),draft.FavorableMemberCount);
        int representative = draft.ActorId!.Value;
        var texts = MissionScenario.Texts(draft.View);
        var pages = MissionOfficerCatalog.Pages(draft,texts);
        int arrivalColumn = Array.FindIndex(pages[0].Columns,column => column.Id == "estimate");
        foreach (var row in MissionOfficerCatalog.Read(draft,texts,0))
            Assert.Equal(draft.ArrivalDaysForActor(row.Id),row.Cells[arrivalColumn].Number);

        var command = Assert.IsAssignableFrom<OfficerStrategyCommand>(draft.CreateCommand());
        Assert.Equal(CommandRejection.None,session.GetPreparationEligibility(new("player"),command));
        Assert.True(session.Validate(new("player"),command).Accepted);
        Assert.Equal(draft.Preview,session.GetMissionPreparation(new("player"),command));
        var engine = session.View.Rules.CreateEngine(WorldState.Restore(session.Snapshot));
        Assert.Equal(draft.Advice!.Opinion,engine.GetMissionDecision(new("player"),command).Advice.Opinion);
        Assert.Same(before,session.Capture());

        var request = session.Request(new CommandOperation(command),id:command.CommandId);
        var result = session.Apply(request);
        Assert.Equal(SessionDisposition.Accepted,result.Disposition);
        var tasks = result.Events.OfType<TaskStartedEvent>().Select(item => item.Task).ToArray();
        Assert.Equal(draft.Members.Order(),tasks.Select(task => task.OfficerId));
        Assert.Equal(tasks.Length,tasks.Select(task => task.Id).Distinct().Count());
        Assert.Equal(1,session.Snapshot.Simulation.NextSequence);
        Assert.Equal(representative,draft.ActorId);
        Assert.All(tasks,task => Assert.True(session.Snapshot.Officers.Single(person => person.Id == task.OfficerId).Acted));

        var restored = SaveService.Decode(SaveService.Encode(session),content);
        StateAssert.Equal(session.Capture(),restored.Capture());
        var requests = new List<SessionRequest> { request };
        var resolved = new List<DomainEvent>();
        for (int xun = 0; xun < 3; xun++)
        {
            var advance = session.Request(new AdvanceOperation());
            requests.Add(advance);
            var originalResult = session.Apply(advance);
            var restoredResult = restored.Apply(advance);
            Assert.Equal(SessionDisposition.Accepted,originalResult.Disposition);
            EventAssert.Equal(originalResult.Events,restoredResult.Events);
            resolved.AddRange(originalResult.Events);
            StateAssert.Equal(session.Capture(),restored.Capture());
        }
        int[] resolvedOfficers = kind switch
        {
            "alienate" => resolved.OfType<AlienateResolvedEvent>().Select(item => item.OfficerId).ToArray(),
            "burn" => resolved.OfType<BurnResolvedEvent>().Select(item => item.OfficerId).ToArray(),
            _ => resolved.OfType<SeizeResolvedEvent>().Select(item => item.OfficerId).ToArray()
        };
        Assert.Equal(tasks.Select(task => task.OfficerId).Order(),resolvedOfficers.Order());

        string path = Path.Combine(Path.GetTempPath(),$"san9-strategy-{Guid.NewGuid():N}.jsonl");
        try
        {
            CommandLogService.Write(path,header,requests);
            var replay = GameSession.Start(content,42);
            var replayResult = CommandLogService.Replay(path,replay);
            Assert.Null(replayResult.FirstFailure);
            Assert.Equal(requests.Count,replayResult.Accepted);
            StateAssert.Equal(session.Capture(),replay.Capture());
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("alienate",22)]
    [InlineData("burn",2)]
    [InlineData("seize",2)]
    public void InvalidMembersRejectTheEntireAssignmentWithoutOccupyingOtherOfficers(string kind, int target)
    {
        var content = Content();
        var session = GameSession.Start(content,42);
        ImmutableArray<int>[] invalid = [default, [], [1,1], [1,999], [1,21], [1,3]];
        foreach (var officers in invalid)
        {
            var command = Command(kind,Guid.NewGuid(),officers,target);
            var before = session.Snapshot;
            Assert.NotEqual(CommandRejection.None,session.GetPreparationEligibility(new("player"),command));
            Assert.False(session.Validate(new("player"),command).Accepted);
            var result = session.Execute(new("player"),command);
            Assert.False(result.Submission.Accepted);
            Assert.Empty(result.Events);
            StateAssert.Equal(before,session.Snapshot);
        }
        var occupied = GameSession.Start(content,42);
        Assert.True(occupied.Execute(new("player"),Command(kind,Guid.NewGuid(),[2],target)).Submission.Accepted);
        var snapshot = occupied.Snapshot;
        var overlapping = Command(kind,Guid.NewGuid(),[1,2],target) with { Sequence = snapshot.Simulation.NextSequence };
        Assert.False(occupied.Execute(new("player"),overlapping).Submission.Accepted);
        StateAssert.Equal(snapshot,occupied.Snapshot);
    }

    [Theory]
    [InlineData("alienate",22)]
    [InlineData("burn",2)]
    [InlineData("seize",2)]
    public void MemberOrderDoesNotChangeTasksAndEachRollMatchesItsSingleOfficerCommand(string kind, int target)
    {
        var content = Content();
        var first = GameSession.Start(content,42);
        var reversed = GameSession.Start(content,42);
        var command = Command(kind,Guid.NewGuid(),[2,1],target);
        Assert.True(first.Execute(new("player"),command).Submission.Accepted);
        Assert.True(reversed.Execute(new("player"),command with { OfficerIds = [1,2] }).Submission.Accepted);
        StateAssert.Equal(first.Snapshot,reversed.Snapshot);
        foreach (var task in first.Snapshot.Simulation.Tasks)
        {
            var single = GameSession.Start(content,42);
            Guid commandId = Guid.NewGuid();
            GameCommand individual = kind switch
            {
                "alienate" => new AlienateCommand(commandId,1,0,task.OfficerId,target),
                "burn" => new BurnCommand(commandId,1,0,task.OfficerId,target),
                _ => new SeizeCommand(commandId,1,0,task.OfficerId,target)
            };
            Assert.True(single.Execute(new("player"),individual).Submission.Accepted);
            Assert.Equal(task with { Id = commandId },Assert.Single(single.Snapshot.Simulation.Tasks));
        }
    }

    [Fact]
    public void BatchAuthorizationChecksEveryMembersCorps()
    {
        var basis = Content();
        var content = basis with
        {
            Settlements = [.. basis.Settlements,basis.Settlements[0] with { Id = 4, CorpsId = 11 }],
            Officers = basis.Officers.Select(person => person.State.Id == 100
                ? person with { State = person.State with { SettlementId = 4 } } : person).ToArray(),
            Information = basis.Information with { Corps = [.. basis.Information.Corps,new(11,1,100,new(1000,1000))] },
            RegionNameKeys = new(basis.RegionNameKeys) { [4] = "delegated-home" },
            RegionCityIds = new(basis.RegionCityIds) { [4] = 1 },
            Military = basis.Military! with { Places = [.. basis.Military.Places,new(4,new(2,7),new(1,1))] }
        };
        var session = GameSession.Start(content,42);
        var mixed = Command("burn",Guid.NewGuid(),[1,100],2);
        Assert.Equal(CommandRejection.NotActiveController,session.GetPreparationEligibility(new("player"),mixed));
        Assert.Equal(CommandRejection.NotActiveController,session.GetPreparationEligibility(new("corps:11"),mixed));
        Assert.True(session.Validate(new("player"),mixed with { OfficerIds = [1] }).Accepted);
        Assert.True(session.Validate(new("corps:11"),mixed with { OfficerIds = [100] }).Accepted);
        var before = session.Snapshot;
        Assert.False(session.Execute(new("player"),mixed).Submission.Accepted);
        StateAssert.Equal(before,session.Snapshot);
        session.ConfigureCompanion(new() { AllowDelegatedCorpsOrders = true });
        Assert.True(session.Validate(new("player"),mixed).Accepted);
    }

    [Fact]
    public void ManualMembershipSurvivesTargetChangesAndEmptySelectionCanBeEditedAgain()
    {
        var content = Content();
        content = content with { CityConnections = [.. content.CityConnections,new(1,3)] };
        var session = GameSession.Start(content,42);
        var draft = new MissionDraft(session,1,"burn");
        Assert.True(draft.SetTarget(2));
        Assert.True(draft.SetMembers([1,100]));
        Assert.True(draft.SetTarget(3));
        Assert.Equal(new[] {1,100},draft.Members.Order());
        Assert.Equal(draft.Members.Any(id => draft.ActorAdvice(id)?.Opinion == AdviserOpinion.Favorable),
            draft.TargetOpinion(3) == AdviserOpinion.Favorable);
        Assert.False(draft.SetMembers([1,1]));
        Assert.False(draft.SetMembers([21]));
        Assert.True(draft.SetMembers([]));
        Assert.Null(draft.ActorId);
        Assert.False(draft.CanExecute);
        Assert.True(draft.CanSelectActor);
        Assert.True(draft.SetTarget(2));
        Assert.True(draft.SetMembers([100]));
        Assert.Equal(100,draft.ActorId);
        Assert.True(draft.CanExecute);
    }
}
