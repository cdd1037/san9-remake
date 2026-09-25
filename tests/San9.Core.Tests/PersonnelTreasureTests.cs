using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class PersonnelTreasureTests
{
    private static readonly PersonnelRulebook Places = new([new(1, new(0, 0)), new(2, new(300, 0)), new(3, new(0, 300))]);
    private static TreasureRulebook Rules(int loyalty = 8) => new([new(10, new(10, 0, 0, 0), loyalty),
        new(11, new(6, 0, 0, 0), 0), new(12, new(0, 7, 0, 0), 8)]);
    private static readonly GovernanceRulebook Governance = new([new(1, 10000, 0) { AbilityBonuses = new(2, 3, 4, 5) }]);
    private static SimulationEngine Engine(int loyalty = 98, int bonus = 8, bool captive = false, int location = 1)
    {
        var world = WorldState.Create(new(190, 1, 1), 190,
            [new(1, 1000) { LeaderId = 1, BaseTrust = 950 }, new(2, 0) { LeaderId = 3 }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new(2, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100),
             new(3, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new(2, captive ? null : 1, location, new(50, 50, 50, 50), false)
             { Status = captive ? OfficerStatus.Captive : OfficerStatus.Serving, Injury = 2, OfficeId = 1, ForceLoyalties = ImmutableDictionary<int, int>.Empty.Add(1, loyalty).Add(2, 177) },
             new(3, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }],
            treasures: [new(10, 1, TreasureDiscovery.Discovered, null), new(11, 1, TreasureDiscovery.Discovered, null),
                new(12, 2, TreasureDiscovery.Discovered, null)], controls: [new("player", 1, ControllerKind.Player)], corps: [new San9.Core.Domain.CorpsState(1, 1, 1, new(1000, 1000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 3, new(1000, 1000)) { IsRoot = true }]);
        var initialized = new SimulationEngine(world, new([new(1, 0)], [], 10), Places, Rules(bonus), Governance);
        // Set the command boundary input after opening salary and injury recovery.
        var state = initialized.CaptureStableState();
        state = state with { Officers = state.Officers.Select(p => p.Id == 2 ? p.WithLoyalty(1, loyalty) with { Injury = 2 } : p).ToArray() };
        return new(WorldState.Restore(state), new([new(1, 0)], [], 10), Places, Rules(bonus), Governance);
    }

    private static CommandExecutionResult Grant(SimulationEngine engine, int item = 10, int person = 2) =>
        engine.Execute(new("player"), new GrantTreasureCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence, item, person));
    private static CommandExecutionResult Confiscate(SimulationEngine engine, int item = 10) =>
        engine.Execute(new("player"), new ConfiscateTreasureCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence, item));

    [Theory]
    [InlineData(98, 8, 106)]
    [InlineData(250, 8, 255)]
    [InlineData(255, 8, 255)]
    [InlineData(98, 0, 98)]
    public void GrantTransfersThenChangesLoyaltyAndRecomputesBothHolders(int loyalty, int bonus, int expected)
    {
        var engine = Engine(loyalty, bonus);
        var before = engine.Snapshot;
        var result = Grant(engine);
        Assert.True(result.Submission.Accepted);
        Assert.Equal(2, engine.Snapshot.Treasures.Single(t => t.Id == 10).HolderId);
        var ruler = engine.Snapshot.Officers.Single(p => p.Id == 1);
        var person = engine.Snapshot.Officers.Single(p => p.Id == 2);
        Assert.Equal(new OfficerAbilities(56, 50, 50, 50), engine.GetAbilities(engine.Snapshot)[ruler.Id]);
        Assert.Equal(new OfficerAbilities(52, 50, 44, 45), engine.GetAbilities(engine.Snapshot)[person.Id]);
        Assert.Equal(expected, person.Loyalty);
        Assert.Equal(177, person.LoyaltyTo(2));
        Assert.Equal(before.Officers.Single(p => p.Id == 2).Acted, person.Acted);
        Assert.False(person.RewardedThisXun);
        Assert.Equal(before.Corps.Single(f => f.IsRoot && f.OwnerId == 1).Treasury.Money, engine.Snapshot.Corps.Single(f => f.IsRoot && f.OwnerId == 1).Treasury.Money);
        Assert.Equal(CanonicalJson.Text(before.Simulation.RandomStreams), CanonicalJson.Text(engine.Snapshot.Simulation.RandomStreams));
        Assert.Equal(950, engine.Snapshot.Forces.Single(f => f.Id == 1).Trust);
        Assert.True(Array.FindIndex(result.Events.ToArray(), e => e is TreasureTransferredEvent) <
            Array.FindIndex(result.Events.ToArray(), e => e is OfficerLoyaltyChangedEvent));
        Assert.True(Confiscate(engine).Submission.Accepted);
        Assert.Equal(1000, engine.Snapshot.Forces.Single(f => f.Id == 1).Trust);
        Assert.Equal(new OfficerAbilities(42, 50, 44, 45), engine.GetAbilities(engine.Snapshot)[2]);
    }

    [Theory]
    [InlineData(0, 20, 20)]
    [InlineData(5, 20, 13)]
    [InlineData(8, 20, 8)]
    [InlineData(8, 3, 0)]
    public void ConfiscationUsesFloorAndOnlyChangesTheIssuingForceLedger(int bonus, int loyalty, int expected)
    {
        var engine = Engine(loyalty, bonus);
        var state = engine.Snapshot;
        state = state with { Treasures = state.Treasures.Select(t => t.Id == 10 ? t with { HolderId = 2 } : t).ToArray() };
        engine = new(WorldState.Restore(state), new([new(1, 0)], [], 10), Places, Rules(bonus), Governance);
        Assert.True(Confiscate(engine).Submission.Accepted);
        Assert.Equal(expected, engine.Snapshot.Officers.Single(p => p.Id == 2).LoyaltyTo(1));
        Assert.Equal(177, engine.Snapshot.Officers.Single(p => p.Id == 2).LoyaltyTo(2));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void CaptiveConfiscationUsesDetentionLocationRatherThanOriginalAffiliation(int location, bool accepted)
    {
        var engine = Engine(20, captive: true, location: location);
        string before = engine.Snapshot.StateHash;
        var result = Confiscate(engine, 12);
        Assert.Equal(accepted, result.Submission.Accepted);
        if (!accepted) Assert.Equal(before, engine.Snapshot.StateHash);
        else
        {
            Assert.Equal(1, engine.Snapshot.Treasures.Single(t => t.Id == 12).HolderId);
            Assert.Equal(8, engine.Snapshot.Officers.Single(p => p.Id == 2).LoyaltyTo(1));
            Assert.Equal(177, engine.Snapshot.Officers.Single(p => p.Id == 2).LoyaltyTo(2));
        }
    }

    [Fact]
    public void ValidationRechecksHolderAndTravellingRecipientWithoutSideEffects()
    {
        var engine = Engine();
        var command = new GrantTreasureCommand(Guid.NewGuid(), 1, 0, 10, 2);
        string before = engine.Snapshot.StateHash;
        Assert.True(engine.Validate(new("player"), command).Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
        Assert.True(Grant(engine).Submission.Accepted);
        before = engine.Snapshot.StateHash;
        Assert.Equal(CommandRejection.TreasureUnavailable, Grant(engine).Submission.Rejection);
        Assert.Equal(before, engine.Snapshot.StateHash);
        Assert.True(engine.Execute(new("player"), new MoveOfficersCommand(Guid.NewGuid(), 1,
            engine.Snapshot.Simulation.NextSequence, [2], 3)).Submission.Accepted);
        before = engine.Snapshot.StateHash;
        Assert.Equal(CommandRejection.PersonUnavailable, Confiscate(engine).Submission.Rejection);
        Assert.Equal(CommandRejection.PersonUnavailable, Grant(engine, 11).Submission.Rejection);
        Assert.Equal(before, engine.Snapshot.StateHash);
    }

    [Theory]
    [InlineData(10, 1, CommandRejection.PersonUnavailable)]
    [InlineData(10, 3, CommandRejection.PersonUnavailable)]
    [InlineData(10, 999, CommandRejection.UnknownPerson)]
    [InlineData(999, 2, CommandRejection.UnknownTreasure)]
    [InlineData(12, 2, CommandRejection.TreasureUnavailable)]
    public void InvalidGrantDoesNotMutate(int item, int recipient, CommandRejection reason)
    {
        var engine = Engine();
        string before = engine.Snapshot.StateHash;
        Assert.Equal(reason, Grant(engine, item, recipient).Submission.Rejection);
        Assert.Equal(before, engine.Snapshot.StateHash);
    }
}
