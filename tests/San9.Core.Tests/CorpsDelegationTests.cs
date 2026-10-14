using San9.Tests;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class CorpsDelegationTests
{
    private static WorldState World() => WorldState.Create(new(190, 1, 1), 42,
        [new ForceState(1, 100) { LeaderId = 11 },
         new ForceState(2, 100) { LeaderId = 21 },
         new ForceState(3, 100) { LeaderId = 31 }],
        [new(1, 1, 1000, 100, 100, 70, 0, 0, 0, 0, 0) { CorpsId = 10 },
         new(2, 1, 1000, 100, 100, 70, 0, 0, 0, 0, 0) { CorpsId = 20 },
         new(3, 2, 1000, 100, 100, 70, 0, 0, 0, 0, 0),
         new(4, 3, 1000, 100, 100, 70, 0, 0, 0, 0, 0)],
        [new OfficerState(11, 1, 1, new(50, 50, 100, 50), false) { Status = OfficerStatus.Ruler },
         new OfficerState(12, 1, 2, new(50, 50, 100, 50), false) { Status = OfficerStatus.Governor },
         new OfficerState(21, 2, 3, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new OfficerState(31, 3, 4, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }],
        corps: [new(10, 1, 11, new(100000, 100000)) { IsRoot = true }, new(20, 1, 12, new(1000, 1000)) { ControlMode = CorpsControlMode.Automatic }, new San9.Core.Domain.CorpsState(2, 2, 21, new(100000, 100000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(3, 3, 31, new(100000, 100000)) { IsRoot = true }],
        controls: [new("first", 1, ControllerKind.Player), new("second", 2, ControllerKind.Automatic), new("third", 3, ControllerKind.Automatic)]);

    private static SimulationRules Rules() => new() { Personnel = new([new(1, new(0, 0)), new(2, new(30, 0)), new(3, new(90, 0)), new(4, new(180, 0))]) };
    private static CorpsDelegation Policy(int kind) => kind switch
    {
        0 => new GeneralDelegation(), 1 => new DomesticDelegation(),
        2 => new DefeatForceDelegation(2), _ => new CaptureSettlementDelegation(3)
    };

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void PublicCommandPreservesTypedPolicyAcrossRestore(int kind)
    {
        var engine = new SimulationEngine(World(), Rules());
        var command = new SetCorpsDelegationCommand(Guid.NewGuid(), 1, 0, 20, Policy(kind));
        var before = engine.Snapshot;
        Assert.True(engine.Validate(new("first"), command).Accepted);
        StateAssert.Equal(before, engine.Snapshot);
        Assert.False(engine.Execute(new("corps:20"), command).Submission.Accepted);
        StateAssert.Equal(before, engine.Snapshot);
        var result = engine.Execute(new("first"), command);
        Assert.True(result.Submission.Accepted);
        Assert.Equal(Policy(kind), engine.Snapshot.Corps.Single(c => c.Id == 20).Delegation);
        var restored = new SimulationEngine(WorldState.Restore(JsonSerializer.Deserialize<WorldSnapshot>(
            CanonicalJson.Text(engine.Snapshot), CanonicalJson.Options)!), Rules());
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
        Assert.Equal(command, JsonSerializer.Deserialize<GameCommand>(JsonSerializer.Serialize<GameCommand>(command, CanonicalJson.Options), CanonicalJson.Options));
        foreach (var e in result.Events) Assert.NotNull(JsonSerializer.Deserialize<DomainEvent>(JsonSerializer.Serialize<DomainEvent>(e, CanonicalJson.Options), CanonicalJson.Options));
    }

    [Fact]
    public void InvalidTargetRootAndNullPolicyAreRejectedWithoutMutation()
    {
        var engine = new SimulationEngine(World(), Rules());
        var baseline = new SetCorpsDelegationCommand(Guid.NewGuid(), 1, 0, 20, new GeneralDelegation());
        var hash = engine.Snapshot;
        foreach (var c in new[] { baseline with { CorpsId = 10 }, baseline with { CorpsId = 99 },
            baseline with { Delegation = null! }, baseline with { Delegation = new DefeatForceDelegation(1) },
            baseline with { Delegation = new DefeatForceDelegation(99) }, baseline with { Delegation = new CaptureSettlementDelegation(1) },
            baseline with { Delegation = new CaptureSettlementDelegation(99) } })
        {
            Assert.Equal(CommandRejection.InvalidCorps, engine.Execute(new("first"), c).Submission.Rejection);
            StateAssert.Equal(hash, engine.Snapshot);
        }
    }

    [Theory]
    [InlineData(2)] [InlineData(3)]
    public void AcceptedGiftClearsNowFriendlyDelegationBeforeNextOrders(int kind)
    {
        var engine = new SimulationEngine(World(), Rules());
        Assert.True(engine.Execute(new("first"), new SetCorpsDelegationCommand(Guid.NewGuid(), 1, 0, 20, Policy(kind))).Submission.Accepted);
        Assert.True(engine.Execute(new("first"), new GiftCommand(Guid.NewGuid(), 1, 1, 11, 2,
            new(GiftKind.Money, 30000, null))).Submission.Accepted);
        foreach (var seat in engine.Snapshot.Simulation.Controls) engine.EndControl(new(seat.ControllerId));
        var result = engine.AdvanceXun();
        Assert.True(Assert.Single(result.Events.OfType<GiftResolvedEvent>()).Accepted);
        var cleared = Assert.Single(result.Events.OfType<CorpsDelegationChangedEvent>());
        Assert.Equal("target-invalidated", cleared.Reason);
        Assert.IsType<GeneralDelegation>(engine.Snapshot.Corps.Single(c => c.Id == 20).Delegation);
        Assert.Equal(new GameClock(190, 1, 11), cleared.Clock);
    }

    [Theory]
    [InlineData(2, "alliance")] [InlineData(3, "alliance")]
    [InlineData(2, "request")] [InlineData(3, "request")]
    [InlineData(2, "relation")] [InlineData(3, "relation")]
    [InlineData(2, "eliminated")] [InlineData(3, "destroyed")]
    [InlineData(2, "missing")] [InlineData(3, "missing")]
    [InlineData(3, "friendly-owner")]
    public void InvalidatingChangesClearPolicyAndTargetTogetherOnce(int kind, string change)
    {
        var world = World().BeginMutation();
        world.Replace(world.Corps[20] with { Delegation = Policy(kind) });
        switch (change)
        {
            case "alliance": world.SetSimulation(world.Simulation with { Alliance = new(3, 1, [1, 2], 10) }); break;
            case "request": AttackRequests.Establish(world, 2, 1, 4, []); break;
            case "relation": ForceRelations.Set(world, 1, 2, 40, []); break;
            case "eliminated": world.Replace(world.Forces[2] with { IsEliminated = true }); break;
            case "destroyed": world.Replace(world.Settlements[3] with { IsDestroyed = true }); break;
            case "missing": world.Replace(world.Corps[20] with { Delegation = kind == 2 ? new DefeatForceDelegation(99) : new CaptureSettlementDelegation(99) }); break;
            case "friendly-owner": world.Replace(world.Settlements[3] with { OwnerId = 1 }); break;
        }
        List<DomainEvent> events = [];
        CorpsDelegations.Cleanup(world, null, events);
        CorpsDelegations.Cleanup(world, null, events);
        Assert.Single(events.OfType<CorpsDelegationChangedEvent>());
        Assert.IsType<GeneralDelegation>(world.Corps[20].Delegation);
    }

    [Theory]
    [InlineData(null)] [InlineData(3)]
    public void ExistingSettlementRemainsTargetWhenUnownedOrCapturedByAnotherEnemy(int? owner)
    {
        var world = World().BeginMutation();
        var policy = new CaptureSettlementDelegation(3);
        world.Replace(world.Corps[20] with { Delegation = policy });
        world.Replace(world.Settlements[3] with { OwnerId = owner });
        List<DomainEvent> events = [];
        CorpsDelegations.Cleanup(world, null, events);
        Assert.Empty(events);
        Assert.Equal(policy, world.Corps[20].Delegation);
    }

    [Fact]
    public void WorkTargetDoesNotUseOwningForceDiplomacyGate()
    {
        var world = World().BeginMutation();
        var military = new MilitaryRulebook(new(3, 3, new int[9].ToImmutableArray(), [new(0, 1, null)], [],
            [new(3, new(7, 7), new(1, 1)) { FacilityTypeId = 9 }])
            { Facilities = [new(9, 0, 100, new(1, 1), 0, false, false, false, 100000, null, null, null, []) { IsWork = true }] });
        world.SetSimulation(world.Simulation with { Alliance = new(3, 1, [1, 2], 10) });
        world.Replace(world.Corps[20] with { Delegation = new CaptureSettlementDelegation(3) });
        List<DomainEvent> events = [];
        CorpsDelegations.Cleanup(world, military, events);
        Assert.Empty(events);
        world.Replace(world.Settlements[3] with { IsDestroyed = true });
        CorpsDelegations.Cleanup(world, military, events);
        Assert.Single(events);
        Assert.IsType<GeneralDelegation>(world.Corps[20].Delegation);
    }

    [Fact]
    public void MissingPolicyAndTargetCannotBeDefaultedAndInvalidRestoredTargetsAreRejected()
    {
        var corps = World().CreateSnapshot().Corps.Single(c => c.Id == 20) with { Delegation = new DefeatForceDelegation(2) };
        var json = JsonNode.Parse(CanonicalJson.Text(corps))!;
        Assert.True(json.AsObject().Remove("delegation"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CorpsState>(json.ToJsonString(), CanonicalJson.Options));
        json = JsonNode.Parse(CanonicalJson.Text(corps))!;
        Assert.True(json["delegation"]!.AsObject().Remove("forceId"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CorpsState>(json.ToJsonString(), CanonicalJson.Options));
        var state = World().CreateSnapshot();
        var changed = state with { Corps = state.Corps.Select(c => c.Id == 20 ? corps : c).ToArray(),
            Relations = [new(1, 2, 40)] };
        Assert.Throws<ArgumentException>(() => new SimulationEngine(WorldState.Restore(changed), Rules()));
    }
}
