using San9.Application.AI;
using System.Collections.Immutable;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class StrategyPlannerTests
{
    [Fact]
    public void DelegatedCorpsUsesItsOwnOfficerAndPublicStrategyDuringPlayerControl()
    {
        var basis = Fixture(4);
        var content = basis with
        {
            Settlements = [.. basis.Settlements.Select(s => s.Id == 1 ? s with { CorpsId = 11, Soldiers = 1000 } : s),
                basis.Settlements[0] with { Id = 4, X = 25, CorpsId = 10 }],
            Officers = basis.Officers.Select(p => p.State.Id == 1 ? p with { State = p.State with { SettlementId = 4 } } :
                p.State.Id == 2 ? p with { State = p.State with { Status = OfficerStatus.Governor } } : p).ToArray(),
            Information = basis.Information with { Corps = [new(10, 1, 1, null),
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

    internal static ScenarioInitialState Fixture(int kind = 0)
    {
        // 190-01-01 starts at category 5 for force 1; select each category through real calendar input.
        int xun = (kind + 3) % 8;
        return new(ScenarioInitialState.CurrentSchemaVersion, "g08-strategy-public", 190, 1 + xun / 3, 1 + xun % 3 * 10, 1,
            [new(1, "home", 1, 10000, 10000, 100) { LeaderId = 1 },
             new(2, "enemy", 2, 10000, 10000, 100) { LeaderId = 21 },
             new(3, "third", 3, 10000, 10000, 100) { LeaderId = 31 }],
            [new(1, "home", 1, 0, 1, 10000, 100, 100, 70, []) { Durability = 500, MaxDurability = 500 },
             new(2, "enemy", 2, 10, 1, 10000, 100, 100, 70, []) { Durability = 500, MaxDurability = 500 },
             new(3, "third", 3, 20, 1, 10000, 100, 100, 70, []) { Durability = 500, MaxDurability = 500 }], [], [])
        {
            RegionNameKeys = new() { [1] = "home", [2] = "enemy", [3] = "third" },
            RegionCityIds = new() { [1] = 1, [2] = 2, [3] = 3 },
            CityConnections = [new(1, 2), new(2, 1)],
            Officers = [new("ruler", new(1, 1, 1, new(90, 90, 90, 90), false) { Status = OfficerStatus.Ruler }),
                new("actor", new(2, 1, 1, new(90, 90, 90, 90), false)),
                new("commander", new(3, 1, null, new(50, 50, 50, 50), false) { UnitId = 10 }),
                new("enemy-ruler", new(21, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
                new("enemy-officer", new OfficerState(22, 2, 2, new(50, 50, 50, 50), false).WithLoyalty(2, 80)),
                new("enemy-commander", new(23, 2, null, new(50, 50, 50, 50), false) { UnitId = 20 }),
                new("third-ruler", new(31, 3, 3, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler })],
            Units = [new(10, 1, new(12, 7), 5000) { FormationId = 7, Morale = 70, CommanderId = 3,
                    Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 20 } },
                new(20, 2, new(42, 7), 5000) { FormationId = 7, Morale = 70, CommanderId = 23, }],
            Military = new(30, 3, Enumerable.Repeat(0, 90).ToImmutableArray(), [new(0, 1, null)],
                [new(7, 1, 0, 10000, 5, new(1, 1), TravelMode.Land)],
                [new(1, new(2, 7), new(1, 1)), new(2, new(52, 7), new(1, 1)), new(3, new(102, 7), new(1, 1))])
                { RegionIds = Enumerable.Repeat<int?>(1, 90).ToImmutableArray() }
        };
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void EveryCategoryUsesPublicCommandAndResolvesIdenticallyAfterSaveAndReplay(int kind)
    {
        var content = Fixture(kind);
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
        var session = GameSession.Start(Fixture(), 42);
        byte[] before = SaveService.Encode(session);
        Assert.IsType<WaitCommand>(StrategyPlanner.Choose(session, new("unknown", 1, ControllerKind.Automatic), Guid.NewGuid()));
        Assert.Equal(before, SaveService.Encode(session));
        var content = Fixture() with { Officers = Fixture().Officers.Where(p => p.State.OwnerId != 1 || p.State.UnitId is not null).ToArray(),
            Forces = Fixture().Forces.Select(f => f.Id == 1 ? f with { LeaderId = null } : f).ToArray() };
        session = GameSession.Start(content, 42);
        Assert.IsType<WaitCommand>(StrategyPlanner.Choose(session, session.Snapshot.Simulation.Controls[0], Guid.NewGuid()));
    }

    [Fact]
    public void ReinforcementKeepsGarrisonReserveAndDoesNotSendForTinyDeficit()
    {
        foreach (bool tiny in new[] { false, true })
        {
            var content = Fixture(3);
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
        var content = Fixture();
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
