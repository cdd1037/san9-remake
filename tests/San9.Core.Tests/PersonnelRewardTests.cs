using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class PersonnelRewardTests
{
    private static SimulationEngine Engine(int loyalty = 80, int righteousness = 0, uint money = 1000,
        bool acted = false, ControllerKind control = ControllerKind.Player)
    {
        var world = WorldState.Create(new(190, 1, 1), 190,
            [new(1, 2000, 1000, 0) { LeaderId = 0 }, new(9000, 0, 0, 0)],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100) { CorpsId = 3 }],
            [new(0, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new(1, 1, 1, new(50, 50, 50, 50), acted)
             {
                 Righteousness = righteousness,
                 ForceLoyalties = ImmutableDictionary<int, int>.Empty.Add(1, loyalty).Add(9000, 254)
             }], corps: [new(3, 1, 0, new(1001, 1000))], controls: [new("controller", 1, control)]);
        var engine = new SimulationEngine(world);
        // Initial preparation recovers actions. Set the stable boundary used by the scenario explicitly.
        var state = engine.Snapshot;
        return new SimulationEngine(WorldState.Restore(state with
        {
            Officers = state.Officers.Select(p => p.Id == 1 ? p with { Acted = acted } : p).ToArray(),
            Forces = state.Forces.Select(f => f.Id == 1 ? f with { Money = money } : f).ToArray()
        }));
    }

    private static CommandExecutionResult Reward(SimulationEngine engine, string controller = "controller", int officer = 1) =>
        engine.Execute(new(controller), new RewardOfficerCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence, officer));

    [Theory]
    [InlineData(0, 80, 10)]
    [InlineData(1, 80, 9)]
    [InlineData(14, 80, 3)]
    [InlineData(15, 80, 2)]
    [InlineData(0, 99, 1)]
    public void RewardUsesForceAccountAndOnlyChangesIssuerLoyalty(int righteousness, int loyalty, int gain)
    {
        var engine = Engine(loyalty, righteousness, acted: true);
        var random = CanonicalJson.Text(engine.Snapshot.Simulation.RandomStreams);
        var result = Reward(engine);
        Assert.True(result.Submission.Accepted);
        var person = engine.Snapshot.Officers.Single(p => p.Id == 1);
        Assert.Equal(loyalty + gain, person.Loyalty);
        Assert.Equal(254, person.LoyaltyTo(9000));
        Assert.True(person.Acted);
        Assert.True(person.RewardedThisXun);
        Assert.Equal(1000u, engine.Snapshot.Corps.Single().Treasury!.Money);
        Assert.Equal(900u, engine.Snapshot.Forces.Single(f => f.Id == 1).Money);
        var debit = Assert.Single(result.Events.OfType<ResourceChangedEvent>());
        Assert.Equal(-100, debit.Actual);
        Assert.Equal(new ResourceAccount(ResourceOwnerKind.Force, 1), debit.Account);
        Assert.Equal(gain, Assert.Single(result.Events.OfType<OfficerRewardedEvent>()).Gain);
        Assert.Equal(random, CanonicalJson.Text(engine.Snapshot.Simulation.RandomStreams));
    }

    [Theory]
    [InlineData(100, 1000, "controller", 1, CommandRejection.LoyaltyAtMaximum)]
    [InlineData(255, 1000, "controller", 1, CommandRejection.LoyaltyAtMaximum)]
    [InlineData(80, 99, "controller", 1, CommandRejection.InsufficientFunds)]
    [InlineData(80, 1000, "outsider", 1, CommandRejection.NotActiveController)]
    [InlineData(80, 1000, "controller", 0, CommandRejection.PersonUnavailable)]
    [InlineData(80, 1000, "controller", 99, CommandRejection.UnknownPerson)]
    public void RejectionsHaveNoEffects(int loyalty, uint money, string controller, int officer, CommandRejection rejection)
    {
        var engine = Engine(loyalty, money: money);
        string before = engine.Snapshot.StateHash;
        var result = Reward(engine, controller, officer);
        Assert.Equal(rejection, result.Submission.Rejection);
        Assert.Empty(result.Events);
        Assert.Equal(before, engine.Snapshot.StateHash);
    }

    [Theory]
    [InlineData(ControllerKind.Player)]
    [InlineData(ControllerKind.Automatic)]
    public void RewardFlagSurvivesRestoreAndResetsNextXun(ControllerKind control)
    {
        var engine = Engine(control: control);
        Assert.True(Reward(engine).Submission.Accepted);
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()));
        string before = restored.Snapshot.StateHash;
        Assert.Equal(CommandRejection.AlreadyRewarded, Reward(restored).Submission.Rejection);
        Assert.Equal(before, restored.Snapshot.StateHash);
        engine.EndControl(new("controller"));
        restored.EndControl(new("controller"));
        Assert.Equal(CanonicalJson.Text(engine.AdvanceXun().Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
        Assert.False(restored.Snapshot.Officers.Single(p => p.Id == 1).RewardedThisXun);
        Assert.True(Reward(restored).Submission.Accepted);
    }

    [Fact]
    public void LoyaltyLedgerIsValidatedAndFollowsCurrentOwner()
    {
        var engine = Engine();
        var person = engine.Snapshot.Officers.Single(p => p.Id == 1);
        Assert.Equal(254, (person with { OwnerId = 9000 }).Loyalty);
        Assert.Equal(0, (person with { OwnerId = null }).Loyalty);
        Assert.Equal(80, person.Loyalty);
        foreach (var invalid in new[]
        {
            person with { Righteousness = 16 },
            person with { ForceLoyalties = person.ForceLoyalties.SetItem(1, 256) },
            person with { ForceLoyalties = person.ForceLoyalties.SetItem(1, -1) },
            person.WithLoyalty(7777, 80)
        })
            Assert.Throws<ArgumentException>(() => WorldState.Restore(engine.Snapshot with
            {
                Officers = engine.Snapshot.Officers.Select(p => p.Id == 1 ? invalid : p).ToArray()
            }));
    }

    [Fact]
    public void EmptyCorpsTreasuryDoesNotBlockRewardFromForceFunds()
    {
        var state = Engine(money: 100).Snapshot;
        var engine = new SimulationEngine(WorldState.Restore(state with
        {
            Corps = state.Corps.Select(c => c with { Treasury = c.Treasury! with { Money = 0 } }).ToArray()
        }));
        Assert.True(Reward(engine).Submission.Accepted);
        Assert.Equal(0u, engine.Snapshot.Forces.Single(f => f.Id == 1).Money);
        Assert.Equal(0u, engine.Snapshot.Corps.Single().Treasury!.Money);
    }

    [Fact]
    public void ValidationDoesNotSpendMoneyOrReserveReward()
    {
        var engine = Engine(money: 100);
        var command = new RewardOfficerCommand(Guid.NewGuid(), 1, 0, 1);
        string before = engine.Snapshot.StateHash;
        Assert.True(engine.Validate(new("controller"), command).Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
        Assert.True(engine.Execute(new("controller"), command).Submission.Accepted);
        Assert.Equal(0u, engine.Snapshot.Forces.Single(f => f.Id == 1).Money);
    }
}
