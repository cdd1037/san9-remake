using San9.Tests;
using San9.Application.AI;
using System.Collections.Immutable;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class AttackRequestPlannerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DistantAttackUsesCoarseRoutesAndOneBoundedDecisionBudget(bool blocked)
    {
        var basis = Fixture();
        var content = basis with
        {
            Settlements = basis.Settlements.Select(s => s.Id == 3 ? s with { X = 180, Y = 180 } :
                s.Id == 2 && blocked ? s with { X = 100, Y = 100 } : s).ToArray(),
            Military = basis.Military! with
            {
                Width = 200, Height = 200,
                TerrainIds = new int[40000].ToImmutableArray(),
                BlockedCells = blocked ? Enumerable.Range(0, 200).Select(y => new MapCell(100, y)).ToImmutableArray() : [],
                Places = basis.Military.Places.Select(p => p.Id == 3 ? p with { Position = new(902, 902) } :
                    p.Id == 2 && blocked ? p with { Position = new(502, 502), Footprint = new(1, 996) } : p).ToImmutableArray()
            }
        };
        var session = GameSession.Start(content, 42);
        var before = session.Capture();
        using var diagnostics = new PathSearchDiagnostics();
        var command = MilitaryPlanner.Choose(session, new("player", 1, ControllerKind.Player), Guid.NewGuid());
        if (blocked)
        {
            Assert.IsType<WaitCommand>(command);
            Assert.InRange(diagnostics.Expanded, 1, 100000);
        }
        else
        {
            Assert.Equal(3, Assert.IsType<DeployUnitCommand>(command).Order.SettlementId);
            Assert.InRange(diagnostics.Expanded, 1, 100000);
        }
        StateAssert.Equal(before, session.Capture());
    }

    [Theory]
    [InlineData("general", 2)]
    [InlineData("domestic", 0)]
    [InlineData("force", 4)]
    [InlineData("settlement", 3)]
    public void AutomaticCorpsUsesItsDelegationAndRestoresTheSamePlan(string policy, int expectedTarget)
    {
        var basis = Fixture();
        var home = basis.Settlements[0] with { Id = 5, NameKey = "root-home", X = 1, Y = 2, Soldiers = 100, CorpsId = 10 };
        var content = basis with
        {
            AttackRequests = [],
            Settlements = [.. basis.Settlements.Select(s => s.Id == 1 ? s with { CorpsId = 20 } : s), home],
            Officers = [.. basis.Officers.Select(p => p.State.Id == 11 ? p with { State = p.State with { SettlementId = 5 } } : p),
                new("helper", new(13, 1, 1, new(50, 50, 50, 50), false))],
            Information = basis.Information with { Corps = [..basis.Information.Corps.Where(c => c.OwnerId != 1), new(10, 1, 11, new(100000, 100000)) { IsRoot = true },
                new(20, 1, 12, new(10000, 10000)) { ControlMode = CorpsControlMode.Automatic }] },
            Military = basis.Military! with { Places = basis.Military.Places.Add(new(5, home.Position, new(1, 1))
                { Combat = new(10, 10, 0, 10, 50), Attack = new(1, false) }) }
        };
        CorpsDelegation delegation = policy switch
        {
            "domestic" => new DomesticDelegation(), "force" => new DefeatForceDelegation(2),
            "settlement" => new CaptureSettlementDelegation(3), _ => new GeneralDelegation()
        };
        var session = GameSession.Start(content, 42);
        session.BeginRecording();
        Assert.True(session.Execute(new("player"), new SetCorpsDelegationCommand(Guid.NewGuid(), 1, 0, 20, delegation)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        var request = session.Request(new EndControlOperation());
        var result = session.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        EventAssert.Equal(result.Events, restored.Apply(request).Events);
        StateAssert.Equal(session.Capture(), restored.Capture());
        if (expectedTarget == 0) Assert.Equal(MilitaryOrderKind.Transport, Assert.Single(session.Snapshot.Units).Order!.Kind);
        else if (policy == "general") Assert.Contains(session.Snapshot.Units, u => u.Order!.SettlementId == expectedTarget);
        else Assert.All(session.Snapshot.Units, u => Assert.Equal(expectedTarget, u.Order!.SettlementId));
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), content, GameSession.EngineFactory(content));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
        var authored = content with { Information = content.Information with { Corps = content.Information.Corps.Select(c =>
            c.Id == 20 ? c with { Delegation = delegation } : c).ToArray() } };
        authored.Validate();

        Assert.Equal(delegation, GameSession.Start(authored, 42).Snapshot.Corps.Single(c => c.Id == 20).Delegation);
    }

    private static ScenarioInitialState Fixture() => new(ScenarioInitialState.CurrentSchemaVersion, "request-planner", 190, 1, 1, 1,
        [new(1, "holder", 1, 100) { LeaderId = 11 },
         new(2, "requester", 2, 100) { LeaderId = 21 }, new(3, "enemy", 3, 100) { LeaderId = 31 }],
        [new(1, "origin", 1, 0, 1, 10000, 100, 100, 90, []),
         new(2, "near", 3, 3, 1, 100, 100, 100, 30, []),
         new(3, "requested", 3, 10, 1, 100, 100, 100, 30, []),
         new(4, "requester-home", 2, 11, 2, 100, 100, 100, 30, [])], [], [])
    {
        AttackRequests = [new(1, 2, 3, 19)],
        Officers = [new("first", new(11, 1, 1, new(90, 90, 90, 90), false) { Status = OfficerStatus.Ruler }),
            new("second", new(12, 1, 1, new(90, 90, 90, 90), false)),
            new("requester-ruler", new(21, 2, 4, new(40, 40, 40, 40), false) { Status = OfficerStatus.Ruler }),
            new("target-ruler", new(31, 3, 3, new(40, 40, 40, 40), false) { Status = OfficerStatus.Ruler })],
        Military = new(12, 3, Enumerable.Repeat(0, 36).ToImmutableArray(), [new(0, 1, null), new(1, null, null)],
            [new(7, 2, 10, 100000, 5, new(1, 1), TravelMode.Land)
                { Combat = new(10, 10, 10, 10, 50), Attack = new(1, true),
                    CaptureRanks = [BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle] },
             new(70, 2, 10, 100000, 5, new(1, 1), TravelMode.Land)],
            Enumerable.Range(1, 4).Select(id => new BattlefieldPlace(id, new(id == 1 ? 2 : id == 2 ? 17 : id == 3 ? 52 : 57, id == 4 ? 12 : 7), new(1, 1))
                { Combat = new(10, 10, 0, 10, 50), Attack = new(1, false) }).ToImmutableArray()) { TransportFormations = new(70, null) }
    , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 11, new(100000, 100000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(1000, 1000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(3, 3, 31, new(1000, 1000)) { IsRoot = true }], []) };

    [Fact]
    public void RequestedTargetPrecedesNearerTargetWithoutFulfillingOnSubmission()
    {
        var basis = Fixture();
        // A counterattack capture now absorbs the army immediately. Keep the transit city passive
        // so this scenario measures requested-target planning and fulfillment, not a new deployment.
        var content = basis with { Military = basis.Military! with
            { Places = basis.Military.Places.Select(p => p.Id == 2 ? p with { Attack = null } : p).ToImmutableArray() } };
        var session = GameSession.Start(content, 42);
        var before = session.Capture();
        var command = Assert.IsType<DeployUnitCommand>(MilitaryPlanner.Choose(session, new("player", 1, ControllerKind.Player), Guid.NewGuid()));
        Assert.Equal(3, command.Order.SettlementId);
        StateAssert.Equal(before, session.Capture());
        session.BeginRecording();
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        Assert.Single(session.Snapshot.Simulation.AttackRequests);
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        StateAssert.Equal(session.Capture(), restored.Capture());
        var events = new List<DomainEvent>();
        for (int i = 0; i < 8 && session.Snapshot.Simulation.AttackRequests.Length != 0; i++)
        {
            var result = session.AdvanceXun();
            events.AddRange(result.Events);
            EventAssert.Equal(result.Events, restored.AdvanceXun().Events);
            StateAssert.Equal(session.Capture(), restored.Capture());
        }
        Assert.Empty(session.Snapshot.Simulation.AttackRequests);
        Assert.Equal(AttackRequestEndReason.Fulfilled, Assert.Single(events.OfType<AttackRequestEndedEvent>()).Reason);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), content, GameSession.EngineFactory(content));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Fact]
    public void IdleFieldUnitAlsoPrioritizesTheRequestedTarget()
    {
        var content = Fixture();
        var session = GameSession.Start(content, 42);
        Assert.True(session.Execute(new("player"), new DeployUnitCommand(Guid.NewGuid(), 1, 0, 1, [12], 1000,
            7, new(MilitaryOrderKind.Move, new(7, 7), null))).Submission.Accepted);
        var snapshot = session.Capture();
        session = GameSession.Restore(content, snapshot with { World = snapshot.World with
            { Units = snapshot.World.Units.Select(u => u with { Order = null }).ToImmutableArray() } });
        var command = Assert.IsType<OrderUnitCommand>(MilitaryPlanner.Choose(session, new("player", 1, ControllerKind.Player), Guid.NewGuid()));
        Assert.Equal(3, command.Order.SettlementId);
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
    }

    [Fact]
    public void UnreachableRequestedTargetFallsBackToReachableOrdinaryTarget()
    {
        var content = Fixture();
        content = content with { Military = content.Military! with
            { TerrainIds = Enumerable.Range(0, 36).Select(i => i % 12 == 7 ? 1 : 0).ToImmutableArray() } };
        var session = GameSession.Start(content, 42);
        var command = Assert.IsType<DeployUnitCommand>(MilitaryPlanner.Choose(session, new("player", 1, ControllerKind.Player), Guid.NewGuid()));
        Assert.Equal(2, command.Order.SettlementId);
    }

    [Fact]
    public void ExpiredRequestDoesNotKeepPlanningPriority()
    {
        var content = Fixture() with { AttackRequests = [new(1, 2, 3, 1)] };
        var session = GameSession.Start(content, 42);
        Assert.Empty(session.Snapshot.Simulation.AttackRequests);
        var command = Assert.IsType<DeployUnitCommand>(MilitaryPlanner.Choose(session, new("player", 1, ControllerKind.Player), Guid.NewGuid()));
        Assert.Equal(2, command.Order.SettlementId);
    }

    [Fact]
    public void ObligationDoesNotOverrideAllianceProtection()
    {
        var session = GameSession.Start(Fixture() with { Alliance = new(2, 1, [1, 3], 19) }, 42);
        Assert.IsType<WaitCommand>(MilitaryPlanner.Choose(session, new("player", 1, ControllerKind.Player), Guid.NewGuid()));
        Assert.Single(session.Snapshot.Simulation.AttackRequests);
    }
}
