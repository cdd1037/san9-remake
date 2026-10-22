using San9.Tests;
using San9.Application.Persistence;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Content.Interface;
using San9.Core.Domain;
using San9.Core.Simulation;
using System.Collections.Immutable;

namespace San9.Application.Tests;

public sealed class MissionCommandTests
{
    [Theory]
    [InlineData("burn")]
    [InlineData("gift")]
    [InlineData("request")]
    public void PreparationAndDepartureDialogueDoNotPublishExecutionRecords(string kind)
    {
        var content = MissionScenario.Create();
        content = content with
        {
            Forces = content.Forces.Select(f => f.Id == 1 ? f with { AdviserId = 2 } : f).ToArray(),
            Officers = content.Officers.Select(p => p.State.Id == 2 ? p with { State = p.State with
                { Status = OfficerStatus.Strategist, BaseAbilities = kind == "request" ? new(100, 100, 100, 100) : p.State.BaseAbilities } } : p).ToArray()
        };
        var session = GameSession.Start(content,42);
        var draft = new MissionDraft(session,1,kind);
        Assert.True(draft.SetActor(2));
        Assert.True(draft.SetTarget(2));
        if (kind == "gift") Assert.True(draft.SetGift(new(GiftKind.Money,1000,null)));
        if (kind == "request") Assert.True(draft.SetTarget(3,true));
        string prefix = draft.IsDiplomacy ? kind : "strategy";
        string[] roles = [prefix + "-favorable",prefix + "-unfavorable",kind + "-start", "request-uncertain"];
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion,[]) { Messages = roles.ToDictionary(
            role => role,role => new MessageDefinition([new MessageLiteral(role)])) };
        var messages = roles.ToDictionary(role => role,
            role => new DecisionMessageDefinition(role,[],role.EndsWith("-start") ? "actor" : "adviser"));
        var context = session.Messages(texts);

        var advice = Assert.Single(MissionPresentation.Advice(draft,messages,context,texts));
        if (kind == "request")
        {
            Assert.Equal(AdviserOpinion.Uncertain, draft.Advice!.Opinion);
            Assert.Equal("request-uncertain", advice.Message.Key);
        }
        Assert.Equal(2,advice.SpeakerId);
        Assert.Null(advice.MapSpeech);

        Assert.Single(MissionPresentation.Departure(draft,messages,context,texts));
    }

    [Theory]
    [InlineData("false-report",20)] [InlineData("disorder",20)]
    [InlineData("inspire",10)] [InlineData("rescue",10)]
    [InlineData("alienate",22)] [InlineData("burn",2)] [InlineData("seize",2)] [InlineData("rumor",2)]
    [InlineData("gift",2)] [InlineData("request",2)] [InlineData("return",2)] [InlineData("surrender",2)]
    public void ReadOnlyPreparationMatchesSubmissionAndRefreshesAfterWorldChanges(string kind,int target)
    {
        var content = MissionScenario.Create();
        if (kind == "return") content = content with { Officers = content.Officers.Select(p => p.State.Id == 3 ?
            p with { State = p.State with { UnitId = null, SettlementId = 2, Status = OfficerStatus.Captive } } : p).ToArray(),
            Units = content.Units.Where(u => u.Id != 10).ToArray() };
        var session = GameSession.Start(content,42);
        var before = session.Capture();
        var draft = new MissionDraft(session,1,kind);
        Assert.True(draft.SetActor(2));
        Assert.Contains(target,draft.Targets);
        Assert.True(draft.SetTarget(target));
        if (kind == "gift") Assert.True(draft.SetGift(new(GiftKind.Money,1000,null)));
        if (kind == "rumor") Assert.True(draft.SetTarget(3,true));
        if (kind == "request") Assert.True(draft.SetTarget(3,true));
        if (kind == "return") Assert.True(draft.SetPrisoner(3));
        Assert.True(draft.CanExecute);
        var preview = draft.Preview;
        var advice = draft.Advice;
        Assert.NotNull(advice);
        Assert.NotNull(preview.Arrival);
        Assert.Same(before, session.Capture());
        var command = draft.CreateCommand();
        Assert.Equal(command,draft.CreateCommand());
        Assert.Equal(session.Preview(new("player"),command),session.GetMissionPreparation(new("player"),command));
        Assert.Equal(CommandRejection.None,session.GetPreparationEligibility(new("player"),command));
        Assert.Same(before, session.Capture());
        var result = session.Execute(new("player"),command);
        Assert.True(result.Submission.Accepted);
        Assert.Equal(preview.Arrival,Assert.Single(result.Events.OfType<TaskStartedEvent>()).Task.Due);
        Assert.False(draft.IsCurrent);
        Assert.Equal(CommandRejection.NotStable,draft.Rejection);
        Assert.False(draft.SetActor(2));
        var acted = command with { CommandId = Guid.NewGuid(), Sequence = session.Snapshot.Simulation.NextSequence };
        Assert.Equal(session.Preview(new("player"),acted),session.GetMissionPreparation(new("player"),acted));
        Assert.Equal(session.Validate(new("player"),acted).Rejection,session.GetPreparationEligibility(new("player"),acted));
        Assert.NotEqual(CommandRejection.None,session.GetPreparationEligibility(new("player"),acted));
        if (!draft.IsDiplomacy)
            Assert.NotEmpty(MissionPresentation.Results(new(content,result.Snapshot),MissionScenario.Texts(new(content,result.Snapshot)),session.AdvanceXun().Events).Records);
    }

    [Fact]
    public void AttackRequestPreviewsRecipientJourneyAndRetainsOnlyEligibleFacilitySelection()
    {
        var session = GameSession.Start(MissionScenario.Create(),42);
        var before = session.Capture();
        var draft = new MissionDraft(session,1,"request");
        Assert.True(draft.SetActor(2));
        Assert.True(draft.SetTarget(2));
        var arrival = draft.Preview.Arrival;
        Assert.NotNull(arrival);
        Assert.Null(draft.SecondaryId);
        Assert.Null(draft.Advice);
        Assert.False(draft.CanExecute);
        Assert.Throws<InvalidOperationException>(() => draft.CreateCommand());
        Assert.True(draft.SetTarget(3,true));
        Assert.Equal(arrival,draft.Preview.Arrival);
        Assert.True(draft.CanExecute);
        Assert.True(draft.SetTarget(2));
        Assert.Equal(3,draft.SecondaryId);
        Assert.True(draft.SetTarget(3));
        Assert.Null(draft.SecondaryId);
        Assert.NotNull(draft.Preview.Arrival);
        Assert.False(draft.CanExecute);
        Assert.Same(before,session.Capture());
    }

    [Fact]
    public void PreparationSharesEnvelopeValidationWithoutChangingWorld()
    {
        var session = GameSession.Start(MissionScenario.Create(),42);
        var command = new BurnCommand(Guid.NewGuid(),1,session.Snapshot.Simulation.NextSequence,2,2);
        var before = session.Capture();
        foreach (var (context, candidate) in new (CommandContext, GameCommand)[]
        {
            (new("player"), command with { CommandId = Guid.Empty }),
            (new("player"), command with { Sequence = command.Sequence + 1 }),
            (new("other"), command)
        })
        {
            var trial = session.Preview(context,candidate);
            Assert.NotEqual(CommandRejection.None,trial.Rejection);
            Assert.Equal(trial,session.GetMissionPreparation(context,candidate));
            Assert.Equal(trial.Rejection,session.GetPreparationEligibility(context,candidate));
        }
        Assert.Same(before, session.Capture());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedBackgroundOperationDoesNotPoisonDraftCaches(bool primed)
    {
        var session = GameSession.Start(MissionScenario.Create(),42);
        var mission = new MissionDraft(session,1,"burn");
        var corps = new CorpsDraft(session);
        if (primed)
        {
            Assert.Contains(2,mission.Targets);
            Assert.Equal(CommandRejection.InvalidCorps,corps.Preview.Rejection);
        }
        var before = session.Snapshot;
        var pending = session.StartOperation(session.Request(new AdvanceOperation()) with { Sequence = session.NextOperationSequence + 1 });
        try
        {
            Assert.Empty(mission.Targets);
            Assert.Equal(CommandRejection.NotStable,corps.Preview.Rejection);
        }
        finally
        {
            Assert.True(SpinWait.SpinUntil(() => pending.IsCompleted,TimeSpan.FromSeconds(10)));
            Assert.Equal(SessionDisposition.Rejected,pending.Complete().Disposition);
        }
        Assert.Same(before,session.Snapshot);
        Assert.Contains(2,mission.Targets);
        Assert.Equal(CommandRejection.InvalidCorps,corps.Preview.Rejection);
    }

    [Fact]
    public void GiftSelectionAndSubmissionExcludeGrantedAndForeignTreasures()
    {
        var basis = MissionScenario.Create();
        var content = basis with { Information = basis.Information with
        {
            Treasures = [new(101,"own",0,0,5,null),new(102,"granted",0,0,5,null),new(103,"foreign",0,0,5,null)],
            TreasureStates = [new(101,1,TreasureDiscovery.Discovered,null),new(102,3,TreasureDiscovery.Discovered,null),
                new(103,21,TreasureDiscovery.Discovered,null)]
        } };
        var session = GameSession.Start(content,42);
        var draft = new MissionDraft(session,1,"gift");
        Assert.Equal(new[] {101},draft.Treasures);
        Assert.True(draft.SetActor(2));
        Assert.False(draft.SetGift(new(GiftKind.Treasure,0,102)));
        var invalid = new GiftCommand(Guid.NewGuid(),1,session.Snapshot.Simulation.NextSequence,2,2,new(GiftKind.Treasure,0,102));
        Assert.Equal(CommandRejection.TreasureUnavailable,session.Execute(new("player"),invalid).Submission.Rejection);
        Assert.True(draft.SetGift(new(GiftKind.Treasure,0,101)));
        Assert.True(draft.SetTarget(2));
        Assert.True(draft.CanExecute);
        Assert.True(session.Execute(new("player"),draft.CreateCommand()).Submission.Accepted);
        Assert.Equal(2,session.Snapshot.Treasures.Single(t => t.Id == 101).HolderId);
    }

    [Fact]
    public void DepartureKeepsRegionIdentityWhenItsIdAlsoNamesASettlement()
    {
        var basis = MissionScenario.Create();
        var content = basis with { RegionNameKeys = new() { [1] = "region",[2] = "enemy",[3] = "third" },
            Officers = basis.Officers.Select(p => p.State.Id == 23 ? p with { State = p.State.WithLoyalty(2,80) } : p).ToArray() };
        var session = GameSession.Start(content,42);
        var draft = new MissionDraft(session,1,"alienate");
        Assert.True(draft.SetTarget(23));
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion,new() { ["region"] = "野外地域",["home"] = "城内设施" })
        {
            Messages = new() { ["departure"] = new([new MessageValue(new("property","settlement.name",Arguments: [new("binding","building.0")]))]) }
        };
        var messages = MissionPresentation.Departure(draft,new Dictionary<string,DecisionMessageDefinition>
        { ["alienate-start"] = new("departure",new() { ["building.0"] = "place" },null) },session.Messages(texts),texts);
        Assert.Equal("野外地域",Assert.Single(messages).Message.BbCode);
    }

    [Fact]
    public void GiftModesAreExclusiveAndOverBudgetCannotSubmit()
    {
        var session = GameSession.Start(MissionScenario.Create(),42);
        var draft = new MissionDraft(session,1,"gift");
        var before = session.Capture();
        Assert.Null(draft.Gift);
        Assert.True(draft.SetActor(2)); Assert.True(draft.SetTarget(2));
        Assert.NotNull(draft.Preview.Arrival);
        Assert.Null(draft.Advice);
        Assert.False(draft.CanExecute);
        Assert.Throws<InvalidOperationException>(() => draft.CreateCommand());
        Assert.Same(before,session.Capture());
        Assert.False(draft.SetGift(new(GiftKind.Food,9999,null)));
        Assert.True(draft.SetGift(new(GiftKind.Food,10000,null)));
        Assert.True(draft.CanExecute);
        var command = Assert.IsType<GiftCommand>(draft.CreateCommand());
        Assert.Equal(GiftKind.Food,command.Offer.Kind);
        Assert.Null(command.Offer.TreasureId);
        Assert.False(draft.SetGift(new(GiftKind.Treasure,1200,999)));
        Assert.True(draft.SetGift(new(GiftKind.Money,draft.View.World.Corps.Single(c => c.OwnerId == 1).Treasury.Money+1,null)));
        Assert.False(draft.CanExecute);
        Assert.Equal(CommandRejection.InsufficientFunds,draft.Rejection);
    }

    [Theory]
    [InlineData(GiftKind.Money,true)]
    [InlineData(GiftKind.Food,true)]
    [InlineData(GiftKind.Money,false)]
    [InlineData(GiftKind.Food,false)]
    public void GiftAmountDefaultsRecomputeWithoutChangingTheSelectedGift(GiftKind kind, bool hasAdviser)
    {
        var content = MissionScenario.Create();
        content = content with
        {
            Forces = content.Forces.Select(f => f.Id == 1 ? f with { AdviserId = hasAdviser ? 2 : null } : f).ToArray(),
            Officers = content.Officers.Select(p => p.State.Id == 2 && hasAdviser ? p with { State = p.State with
                { Status = OfficerStatus.Strategist, BaseAbilities = new(90,90,100,90) } } : p).ToArray(),
            Information = content.Information with { Corps = content.Information.Corps.Select(c => c.OwnerId == 1
                ? c with { Treasury = new(1000000,1000000) } : c).ToArray() }
        };
        var session = GameSession.Start(content,42);
        var before = session.Capture();
        var draft = new MissionDraft(session,1,"gift");
        uint minimum = kind == GiftKind.Money ? 1000u : 10000u;
        uint maximum = 1000000;
        Assert.Equal(minimum,draft.GiftAmountInput(kind)!.Value);
        Assert.True(draft.SetActor(2));
        Assert.True(draft.SetTarget(2));
        var input = draft.GiftAmountInput(kind)!;
        Assert.Equal(minimum,input.Minimum);
        uint amount = input.Value;
        Assert.InRange(amount,minimum,maximum);
        if (hasAdviser)
        {
            Assert.Equal(AdviserOpinion.Favorable,session.GetGiftAdvice(1,2,2,new(kind,amount,null)).Opinion);
            if (amount > minimum)
                Assert.Equal(AdviserOpinion.Unfavorable,session.GetGiftAdvice(1,2,2,new(kind,amount-1,null)).Opinion);
        }
        else Assert.Equal(minimum,amount);
        Assert.Equal(amount,draft.GiftAmountInput(kind)!.Value);
        Assert.Null(draft.Gift);
        Assert.False(draft.CanExecute);
        uint manual = amount == maximum ? amount-1 : amount+1;
        Assert.True(draft.SetGift(new(kind,manual,null)));
        Assert.Equal(amount,draft.GiftAmountInput(kind)!.Value);
        Assert.Equal(new GiftOffer(kind,manual,null),draft.Gift);
        Assert.Same(before,session.Capture());
    }

    [Theory]
    [InlineData(GiftKind.Money,999u,false)]
    [InlineData(GiftKind.Money,1000u,true)]
    [InlineData(GiftKind.Food,9999u,false)]
    [InlineData(GiftKind.Food,10000u,true)]
    public void GiftInputAndCandidatesRespectResourceSpecificMinimums(GiftKind kind, uint balance, bool available)
    {
        var content = MissionScenario.Create();
        var session = GameSession.Start(content,42);
        // Set the exact balance after opening economic settlement, which may charge salaries.
        var opened = session.Capture();
        session = GameSession.Restore(content,opened with { World = opened.World with
        {
            Corps = opened.World.Corps.Select(c => c.OwnerId == 1
                ? c with { Treasury = kind == GiftKind.Money ? new(balance,0) : new(0,balance) } : c).ToImmutableArray()
        } });
        var before = session.Capture();
        var draft = new MissionDraft(session,1,"gift");
        uint minimum = kind == GiftKind.Money ? 1000u : 10000u;
        Assert.False(draft.SetGift(new(kind,minimum-1,null)));
        var range = draft.GiftAmountRange(kind);
        Assert.Equal(available,range.Maximum >= range.Minimum);
        var input = draft.GiftAmountInput(kind);
        if (available)
        {
            Assert.NotNull(input);
            Assert.Equal(minimum,input.Value);
            Assert.Equal(minimum,input.Minimum);
            Assert.Equal(balance,input.Maximum);
            Assert.True(input.CanConfirm);
            input.Clear();
            Assert.False(input.CanConfirm);
            Assert.NotEmpty(draft.Targets);
        }
        else
        {
            Assert.Null(input);
            Assert.Empty(draft.Targets);
        }
        Assert.Same(before,session.Capture());
    }

    [Fact]
    public void AutomaticCorpsCannotSubmitAndManagementRemainsReachable()
    {
        var content = MissionScenario.Create();
        content = content with { Information = content.Information with { Corps = content.Information.Corps.Select(c => c.OwnerId == 1 ?
            c with { ControlMode = CorpsControlMode.Automatic } : c).ToArray() } };
        var session = GameSession.Start(content,42);
        var draft = new MissionDraft(session,1,"burn");
        draft.SetActor(2); draft.SetTarget(2);
        Assert.False(draft.CanExecute);
        var menu = MapCommandMenu.Settlement(session.View,1).SelectMany(g => g.Children).ToArray();
        Assert.Null(menu.Single(e => e.Id == "delegate").UnavailableReasonKey);
        Assert.NotNull(menu.Single(e => e.Id == "burn").UnavailableReasonKey);
        var root = session.Snapshot.Corps.Single(c => c.OwnerId == 1 && c.IsRoot);
        Assert.True(session.Execute(new("player"),new SetCorpsControlCommand(Guid.NewGuid(),1,
            session.Snapshot.Simulation.NextSequence,root.Id,CorpsControlMode.Direct)).Submission.Accepted);
        draft = new(session,1,"burn"); draft.SetActor(2); draft.SetTarget(2);
        Assert.True(draft.CanExecute);
    }


    [Fact]
    public void ResultsTrackPlayerForcesAndDoNotRepeatAnsweredDiplomacy()
    {
        var session = GameSession.Start(MissionScenario.Create(),42);
        var draft = new MissionDraft(session,1,"gift"); draft.SetActor(2); draft.SetTarget(2);
        Assert.True(draft.SetGift(new(GiftKind.Money,1000,null)));
        var command = Assert.IsType<GiftCommand>(draft.CreateCommand());
        Assert.True(session.Execute(new("player"),command).Submission.Accepted);
        var before = session.View; var texts = MissionScenario.Texts(before);
        var clock = before.World.Clock.Advance();
        DomainEvent[] events = [new GiftResolvedEvent(clock,command.CommandId,2,2,command.Offer,true,1,0,0)];
        var messages = MissionPresentation.Results(before,texts,events);
        Assert.Equal(clock,Assert.Single(messages.Records).Message.Clock);
        var narrated = MissionPresentation.Results(before,texts,events,new HashSet<Guid> { command.CommandId });
        Assert.Equal(messages.Records,narrated.Records);
        Assert.Empty(narrated.Messages);
        var cancelled = MissionPresentation.Results(before,texts,[new TaskCancelledEvent(command.CommandId,clock,"target-invalidated")]);
        Assert.Contains("目标已失效",Assert.Single(cancelled.Records).Message.BbCode);
        var other = before.World with { Simulation = before.World.Simulation with { Tasks = before.World.Simulation.Tasks.Select(t => t with { ControllerId = "ai:1" }).ToImmutableArray() } };
        // Delegating the work does not change the player's reporting eligibility.
        Assert.Equal(messages.Records, MissionPresentation.Results(new(MissionScenario.Create(),other),texts,events).Records);
        other = other with { Simulation = other.Simulation with { Tasks = other.Simulation.Tasks.Select(t => t with { ForceId = 2 }).ToImmutableArray() } };
        Assert.Empty(MissionPresentation.Results(new(MissionScenario.Create(),other),texts,events).Records);
    }

    [Fact]
    public void AnsweredDiplomacyStillPublishesTheSameRecordWithoutReplayingDialogueOrMedia()
    {
        var session = GameSession.Start(MissionScenario.Create(),42);
        var draft = new MissionDraft(session,1,"gift"); draft.SetActor(2); draft.SetTarget(2);
        Assert.True(draft.SetGift(new(GiftKind.Money,1000,null)));
        var command = Assert.IsType<GiftCommand>(draft.CreateCommand());
        Assert.True(session.Execute(new("player"),command).Submission.Accepted);
        var before = session.View;
        string[] roles = ["diplomacy-arrival", "diplomacy-greeting", "diplomacy-welcome", "gift-proposal",
            "gift-actor-accepted", "gift-accepted", "gift-success"];
        var texts = MissionScenario.Texts(before) with { Messages = roles.ToDictionary(role => role,
            role => new MessageDefinition([new MessageLiteral(role), new MessageRandomChoice(
                [[new MessageLiteral("A")], [new MessageLiteral("B")]]), new MessageValue(new("query", "day"))])) };
        var content = CommandPresentation.Empty with { Missions = roles.ToDictionary(role => role,
            role => new DecisionMessageDefinition(role, [], Sound: role == "gift-success" ? "result-sound" : null)) };
        var clock = before.World.Clock.NextDay();
        DomainEvent[] facts = [new GiftResolvedEvent(clock,command.CommandId,2,2,command.Offer,true,1,0,0)];
        var context = session.Messages(texts,before.World);
        var fresh = ExecutionPublication.Read(before,before,context,texts,content,facts);
        var narrated = ExecutionPublication.Read(before,before,context,texts,content,facts,new HashSet<Guid> { command.CommandId });

        Assert.NotEmpty(fresh.Messages);
        var meeting = new DiplomacyMeeting(command.CommandId, 2, 2);
        Assert.Null(fresh.Messages[0].Diplomacy);
        Assert.All(fresh.Messages.Skip(1).Where(m => m.SoundRole != "result-sound"), message =>
        {
            Assert.Equal(meeting, message.Diplomacy);
            Assert.True(message.DiplomacyFarewell);
        });
        Assert.Null(fresh.Messages.Single(m => m.SoundRole == "result-sound").Diplomacy);
        Assert.Contains(fresh.Messages, message => message.SoundRole == "result-sound");
        Assert.All(fresh.Messages, message => Assert.Equal(clock,message.Message.Clock));
        Assert.Empty(narrated.Messages);
        Assert.Equal(Assert.Single(fresh.Records),Assert.Single(narrated.Records));
        Assert.Equal(clock,narrated.Records[0].Message.Clock);
        Assert.EndsWith(clock.Day.ToString(),narrated.Records[0].Message.BbCode);
    }
}
