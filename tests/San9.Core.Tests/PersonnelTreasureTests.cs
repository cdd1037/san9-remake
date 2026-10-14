using San9.Tests;
using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class PersonnelTreasureTests
{
    [Theory]
    [InlineData(DomesticActivity.Commerce)]
    [InlineData(DomesticActivity.Train)]
    [InlineData(DomesticActivity.Recruit)]
    public void DomesticPreparationIsReadOnlyAndRejectsUsedActivities(DomesticActivity activity)
    {
        var snapshot = Engine().Snapshot;
        snapshot = snapshot with { Settlements = snapshot.Settlements.Select(s => s.Id == 1
            ? s with { Soldiers = 5000, Morale = 30, Commerce = 0, MaxCommerce = 1000, DraftPopulation = 10000 } : s).ToArray() };
        var engine = new SimulationEngine(WorldState.Restore(snapshot), new([new(1, 0)], [], 10), Places, Rules(), Governance);
        var command = new DevelopCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence, new(1, 1, [2], activity));
        var before = engine.Snapshot;
        Assert.Equal(CommandRejection.None, engine.GetPreparationEligibility(new("player"), command));
        StateAssert.Equal(before, engine.Snapshot);
        Assert.True(engine.Execute(new("player"), command).Submission.Accepted);
        var next = command with { CommandId = Guid.NewGuid(), Sequence = engine.Snapshot.Simulation.NextSequence };
        Assert.Equal(CommandRejection.DevelopmentFailed, engine.GetPreparationEligibility(new("player"), next));
        Assert.Equal(engine.Validate(new("player"), next).Rejection, engine.GetPreparationEligibility(new("player"), next));
    }

    [Theory]
    [InlineData("grant", true)]
    [InlineData("grant", false)]
    [InlineData("confiscate", true)]
    [InlineData("confiscate", false)]
    [InlineData("reward", true)]
    [InlineData("reward", false)]
    [InlineData("office", true)]
    [InlineData("office", false)]
    public void PreparationMatchesExecutionWithoutChangingWorld(string kind, bool valid)
    {
        var engine = Engine();
        var before = engine.Snapshot;
        int officer = valid ? 2 : 9999;
        GameCommand command = kind switch
        {
            "grant" => new GrantTreasureCommand(Guid.NewGuid(), 1, before.Simulation.NextSequence, 10, officer),
            "confiscate" => new ConfiscateTreasureCommand(Guid.NewGuid(), 1, before.Simulation.NextSequence, valid ? 12 : 9999),
            "reward" => new RewardOfficerCommand(Guid.NewGuid(), 1, before.Simulation.NextSequence, officer),
            _ => new AssignOfficesCommand(Guid.NewGuid(), 1, before.Simulation.NextSequence, [new(officer, null)])
        };
        var rejection = engine.GetPreparationEligibility(new("player"), command);
        Assert.Equal(valid, rejection == CommandRejection.None);
        StateAssert.Equal(before, engine.Snapshot);
        Assert.Equal(rejection, engine.Execute(new("player"), command).Submission.Rejection);
        var next = command with { CommandId = Guid.NewGuid(), Sequence = engine.Snapshot.Simulation.NextSequence };
        Assert.Equal(engine.Validate(new("player"), next).Rejection, engine.GetPreparationEligibility(new("player"), next));
    }

    [Fact]
    public void AbilityProjectionReusesUnchangedDependenciesAndRefreshesTreasureAndInjury()
    {
        var engine = Engine();
        var before = engine.Snapshot;
        var abilities = engine.GetAbilities(before);
        var acted = before with { Officers = before.Officers.Select(p => p with { Acted = !p.Acted }).ToArray() };
        Assert.Same(abilities, engine.GetAbilities(acted));
        Grant(engine);
        var granted = engine.GetAbilities(engine.Snapshot);
        Assert.NotEqual(abilities[2].War, granted[2].War);
        var injured = engine.Snapshot with { Officers = engine.Snapshot.Officers.Select(p => p.Id == 2 ? p with { Injury = 5 } : p).ToArray() };
        Assert.NotEqual(granted[2].War, engine.GetAbilities(injured)[2].War);
        Assert.Equal(abilities, engine.GetAbilities(before));
    }

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
        var before = engine.Snapshot;
        var result = Confiscate(engine, 12);
        Assert.Equal(accepted, result.Submission.Accepted);
        if (!accepted) StateAssert.Equal(before, engine.Snapshot);
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
        var before = engine.Snapshot;
        Assert.True(engine.Validate(new("player"), command).Accepted);
        StateAssert.Equal(before, engine.Snapshot);
        Assert.True(Grant(engine).Submission.Accepted);
        before = engine.Snapshot;
        Assert.Equal(CommandRejection.TreasureUnavailable, Grant(engine).Submission.Rejection);
        StateAssert.Equal(before, engine.Snapshot);
        Assert.True(engine.Execute(new("player"), new MoveOfficersCommand(Guid.NewGuid(), 1,
            engine.Snapshot.Simulation.NextSequence, [2], 3)).Submission.Accepted);
        before = engine.Snapshot;
        Assert.Equal(CommandRejection.PersonUnavailable, Confiscate(engine).Submission.Rejection);
        Assert.Equal(CommandRejection.PersonUnavailable, Grant(engine, 11).Submission.Rejection);
        StateAssert.Equal(before, engine.Snapshot);
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
        var before = engine.Snapshot;
        Assert.Equal(reason, Grant(engine, item, recipient).Submission.Rejection);
        StateAssert.Equal(before, engine.Snapshot);
    }
}
