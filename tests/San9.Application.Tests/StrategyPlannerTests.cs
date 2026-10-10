using San9.Application.AI;
using San9.Application.Persistence;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class StrategyPlannerTests
{
    [Fact]
    public void DelegatedCorpsUsesItsOwnOfficerAndPublicStrategyDuringPlayerControl()
    {
        var basis = MissionScenario.Create(4);
        var content = basis with
        {
            Settlements = [.. basis.Settlements.Select(s => s.Id == 1 ? s with { CorpsId = 11, Soldiers = 1000 } : s),
                basis.Settlements[0] with { Id = 4, X = 25, CorpsId = 10 }],
            Officers = basis.Officers.Select(p => p.State.Id == 1 ? p with { State = p.State with { SettlementId = 4 } } :
                p.State.Id == 2 ? p with { State = p.State with { Status = OfficerStatus.Governor } } : p).ToArray(),
            Information = basis.Information with { Corps = [.. basis.Information.Corps.Where(c => c.OwnerId != 1), new(10, 1, 1, new(10000, 10000)) { IsRoot = true },
                new(11, 1, 2, new(1000, 1000)) { ControlMode = CorpsControlMode.Automatic }] },
            Military = basis.Military! with { Places = [.. basis.Military.Places, new(4, new(127, 7), new(1, 1))] }
        };
        var session = GameSession.Start(content, 42);
        session.BeginRecording();
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        var result = session.AdvanceXun();
        Assert.Contains(result.Events.OfType<TaskStartedEvent>(), e => e.Task.ControllerId == "corps:11" &&
            e.Task.OfficerId == 2 && e.Task.Journey?.Burn is not null);
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), content, GameSession.EngineFactory(content));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(replay.Session));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void EveryCategoryUsesPublicCommandAndResolvesIdenticallyAfterSaveAndReplay(int kind)
    {
        var content = MissionScenario.Create(kind);
        var session = GameSession.Start(content, 42);
        var seat = session.Snapshot.Simulation.Controls.Single(c => c.ForceId == 1);
        session.BeginRecording();
        var id = Guid.NewGuid();
        byte[] before = SaveService.Encode(session);
        var command = StrategyPlanner.Choose(session, seat, id);
        Assert.Equal(kind, Category(command));
        Assert.Equal(command, StrategyPlanner.Choose(session, seat, id));
        Assert.Equal(before, SaveService.Encode(session));
        Assert.True(session.Execute(new(seat.ControllerId), command).Submission.Accepted);
        Assert.IsType<WaitCommand>(StrategyPlanner.Choose(session, seat, Guid.NewGuid()));
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        var result = session.AdvanceXun();
        Assert.Contains(result.Events, e => e switch
        {
            UnitStrategyResolvedEvent u => kind < 4 && (int)u.Strategy == kind && u.OfficerId == 2,
            BurnResolvedEvent b => kind == 4 && b.OfficerId == 2,
            SeizeResolvedEvent s => kind == 5 && s.OfficerId == 2,
            AlienateResolvedEvent a => kind == 6 && a.OfficerId == 2,
            GossipResolvedEvent g => kind == 7 && g.OfficerId == 2,
            _ => false
        });
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(restored));
        byte[] recording = RecordingService.Encode(session.EndRecording());
        var replay = RecordingService.Replay(recording, content, GameSession.EngineFactory(content));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(replay.Session));
        if (Environment.GetEnvironmentVariable("SAN9_G08_REPORT_DIR") is { Length: > 0 } output)
        {
            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, $"scenario-{kind}.json"), CanonicalJson.Encode(content));
            File.WriteAllBytes(Path.Combine(output, $"strategy-{kind}.recording.json"), recording);
        }
    }

    [Fact]
    public void NoAuthorityAndNoActorsProduceNoCommandAndDoNotChangeWorld()
    {
        var session = GameSession.Start(MissionScenario.Create(), 42);
        byte[] before = SaveService.Encode(session);
        Assert.IsType<WaitCommand>(StrategyPlanner.Choose(session, new("unknown", 1, ControllerKind.Automatic), Guid.NewGuid()));
        Assert.Equal(before, SaveService.Encode(session));
        var content = MissionScenario.Create() with { Officers = MissionScenario.Create().Officers.Where(p => p.State.OwnerId != 1 || p.State.UnitId is not null).ToArray(),
            Information = MissionScenario.Create().Information with { Corps = MissionScenario.Create().Information.Corps.Select(c => c.IsRoot && c.OwnerId == 1 ? c with { LeaderId = null } : c).ToArray() },
            Forces = MissionScenario.Create().Forces.Select(f => f.Id == 1 ? f with { LeaderId = null } : f).ToArray() };
        session = GameSession.Start(content, 42);
        Assert.IsType<WaitCommand>(StrategyPlanner.Choose(session, session.Snapshot.Simulation.Controls[0], Guid.NewGuid()));
    }

    [Fact]
    public void ReinforcementKeepsGarrisonReserveAndDoesNotSendForTinyDeficit()
    {
        foreach (bool tiny in new[] { false, true })
        {
            var content = MissionScenario.Create(3);
            content = content with
            {
                Settlements = content.Settlements.Select(s => s.Id == 1 ? s with { Soldiers = tiny ? 10000u : 5999u } : s).ToArray(),
                Units = content.Units.Select(u => u.Id == 10 ? u with { Soldiers = tiny ? 9500u : 5000u } : u).ToArray()
            };
            var session = GameSession.Start(content, 42);
            var command = StrategyPlanner.Choose(session, session.Snapshot.Simulation.Controls[0], Guid.NewGuid());
            Assert.NotEqual(3, Category(command));
        }
    }

    [Fact]
    public void UncontactedEnemyUnitsAreNotUsedAndAutomaticControlActuallyDispatches()
    {
        var content = MissionScenario.Create();
        content = content with { Units = content.Units.Select(u => u.Id == 10 ? u with { Order = null } : u).ToArray() };
        var session = GameSession.Start(content, 42);
        Assert.NotEqual(0, Category(StrategyPlanner.Choose(session, session.Snapshot.Simulation.Controls[0], Guid.NewGuid())));
        content = content with { PlayerForceId = 3 };
        session = GameSession.Start(content, 42);
        session.BeginRecording();
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        var result = session.AdvanceXun();
        Assert.Contains(result.Events.OfType<TaskStartedEvent>(), e => e.Task.ForceId == 1 &&
            (e.Task.Journey?.UnitStrategy is not null || e.Task.Journey?.Burn is not null || e.Task.Journey?.Seize is not null ||
             e.Task.Journey?.Gossip is not null || e.Task.Kind == "strategy-alienate"));
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), content, GameSession.EngineFactory(content));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(replay.Session));
    }

    private static int Category(GameCommand command) => command switch
    {
        UnitStrategyCommand u => (int)u.Strategy,
        BurnCommand => 4, SeizeCommand => 5, AlienateCommand => 6, GossipCommand => 7, _ => -1
    };
}
