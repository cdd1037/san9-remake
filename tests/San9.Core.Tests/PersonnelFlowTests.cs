using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class PersonnelFlowTests
{
    private static readonly PersonnelRulebook Places = new(
        [new(10, new(0, 0)), new(20, new(30, 0)), new(30, new(60, 0))],
        new Dictionary<int, int> { [10] = 10, [20] = 20, [30] = 30 }, [new(20, 30)]);

    private static WorldMutation World()
    {
        var state = WorldState.Create(new(190, 1, 1), 17,
            [new(1, 100) { LeaderId = 1 }, new(2, 100) { LeaderId = 2 },
             new(3, 0) { IsDormant = true }, new(4, 0) { IsEliminated = true }],
            [new(10, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100) { CorpsId = 10 },
             new(20, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100) { CorpsId = 20 },
             new(30, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100) { CorpsId = 10 }],
            [new(1, 1, 10, new(60, 60, 60, 60), false) { Status = OfficerStatus.Ruler },
             new(2, 2, 20, new(60, 60, 60, 60), false) { Status = OfficerStatus.Ruler },
             new OfficerState(11, 2, 20, new(60, 60, 60, 60), false) { TacticCooldown = 8 }
                 .WithLoyalty(2, 90).WithLoyalty(3, 10)],
            corps: [new(10, 1, 1, new(100, 100)) { IsRoot = true },
                    new(20, 2, 2, new(100, 100)) { IsRoot = true },
                    new(30, 3, null, new(0, 0)) { IsRoot = true }]);
        var world = state.BeginMutation();
        world.SetClock(new(190, 4, 1));
        return world;
    }

    [Fact]
    public void RecruitmentClearsTacticCooldown()
    {
        var world = World();
        PersonnelTransitions.Recruit(world, 11, 1, 10, [], Places, false);
        WorldInvariants.Validate(world);
        Assert.Equal(0, world.Officers[11].TacticCooldown);
    }

    [Fact]
    public void ExileClearsTacticCooldown()
    {
        var world = World();
        PersonnelTransitions.Exile(world, 11, 30, [], Places, false);
        WorldInvariants.Validate(world);
        Assert.Equal(0, world.Officers[11].TacticCooldown);
    }

    [Fact]
    public void PrisonerReleaseClearsTacticCooldown()
    {
        var world = World();
        world.Replace(world.Officers[11] with { Status = OfficerStatus.Captive, SettlementId = 10 });
        Assert.Equal(CommandRejection.None, PrisonerRelease.Depart(world, 11, 10, 0,
            PrisonerDepartureKind.Released, 1, Places, EconomyRulebook.Empty, []));
        world.SetSimulation(world.Simulation with { NextSequence = 1 });
        WorldInvariants.Validate(world);
        Assert.Equal(0, world.Officers[11].TacticCooldown);
    }

    [Fact]
    public void OfficeRequestRefusalClearsTacticCooldown()
    {
        var world = World();
        world.Replace(world.Officers[11].WithLoyalty(1, 255) with
            { Status = OfficerStatus.Free, OwnerId = null, SettlementId = 10 });
        var rules = new SimulationRules { Personnel = Places };
        List<DomainEvent> events = [];
        OfficeRequests.Begin(world, rules, events);
        Assert.IsType<OfficeRequestEventSession>(world.Simulation.PendingEvent);
        Assert.Equal(CommandRejection.None, OfficeRequests.Respond(world, rules,
            new(Guid.NewGuid(), 1, 0, 11, EventChoice.Reject), events));
        WorldInvariants.Validate(world);
        Assert.Equal(0, world.Officers[11].TacticCooldown);
    }

    [Fact]
    public void MissingLiveForceLoyaltyStillRecoversTowardFifty()
    {
        var world = World();
        PersonnelLoyaltyCycle.Apply(world, []);
        Assert.Equal(1, world.Officers[11].LoyaltyTo(1));
    }

    [Theory]
    [InlineData(3, 10)]
    [InlineData(4, 0)]
    public void InactiveForceLoyaltiesDoNotRecover(int forceId, int loyalty)
    {
        var world = World();
        PersonnelLoyaltyCycle.Apply(world, []);
        Assert.Equal(loyalty, world.Officers[11].LoyaltyTo(forceId));
        if (forceId == 4) Assert.False(world.Officers[11].ForceLoyalties.ContainsKey(forceId));
    }

    [Fact]
    public void LostServiceWithoutHomeUsesSharedPlacementBeforeForceRetirement()
    {
        var world = World();
        world.Replace(world.Settlements[20] with { OwnerId = 1, CorpsId = 10 });
        world.Replace(world.Officers[2] with { Status = OfficerStatus.Captive, SettlementId = 10 });
        List<DomainEvent> events = [];
        PersonnelReturns.Repatriate(world, 11, 20, new("system", 2, 0), Places, events, false);
        GovernanceLifecycle.Reconcile(world, new HashSet<int> { 2 }, new() { Personnel = Places }, events, false);
        WorldInvariants.Validate(world);
        Assert.Equal(OfficerStatus.Free, world.Officers[11].Status);
        Assert.Equal(30, world.Officers[11].SettlementId);
    }

    [Fact]
    public void StrandedReinforcementTaskDemobilizesOnceAndUsesSharedPlacement()
    {
        var world = World();
        world.Replace(world.Settlements[20] with { OwnerId = 1, CorpsId = 10 });
        world.Replace(world.Officers[2] with { Status = OfficerStatus.Captive, SettlementId = 10 });
        // The resolution pipeline removes the completed task before dispatching its return.
        var previous = new PersistentTask(Guid.NewGuid(), UnitStrategies.Kind, "system", 2, 11, 20,
            world.Clock, world.Clock, 0, 0)
        {
            Journey = new(20, null, null) { Route = new(new(60, 0), new(30, 0), false) },
            Reinforcements = new(100, 70, 30) { Wounded = 20 }
        };
        List<DomainEvent> events = [];
        PersonnelReturns.Start(world, previous, Places, events, false);
        Assert.Equal(120u, world.Settlements[30].DraftPopulation);
        Assert.Single(events.OfType<ResourceChangedEvent>(), e => e.Resource == ResourceKind.DraftPopulation);
        Assert.Empty(world.Simulation.Tasks);
        Assert.Equal(30, world.Officers[11].SettlementId);
        Assert.Equal(45, world.Officers[11].LoyaltyTo(2));
        Assert.Equal(0, world.Officers[11].TacticCooldown);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void HistoricalLoyaltyToRetiredForcesIsAcceptedAndIgnoredByQuarterlyRecovery(int loyalty)
    {
        var world = World();
        world.Replace(world.Officers[11].WithLoyalty(4, loyalty));
        world.SetSimulation(world.Simulation with { Boundary = SimulationBoundary.Stable });
        WorldInvariants.Validate(world);
        world = WorldState.Restore(world.CapturePresentation()).BeginMutation();
        List<DomainEvent> events = [];
        PersonnelLoyaltyCycle.Apply(world, events);
        Assert.Equal(loyalty, world.Officers[11].LoyaltyTo(4));
        Assert.DoesNotContain(events.OfType<OfficerLoyaltyChangedEvent>(), e => e.ForceId == 4);
        WorldInvariants.Validate(world);
    }
}
