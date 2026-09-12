using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class ShoreMilitarySessionTests
{
    [Fact]
    public void DemolishedFacilityDisappearsFromActiveListsAndSessionRestoresItsState()
    {
        var original = Fixture();
        var definition = original with
        {
            Settlements = original.Settlements.Select(s => s.Id == 2 ? s with { Kind = SettlementKind.Facility, IsFacilityListEntry = true } : s).ToArray(),
            RegionCityIds = new() { [1] = 1, [2] = 1 },
            RegionNameKeys = new() { [1] = "origin", [2] = "destination" },
            Military = original.Military! with
            {
                Facilities = [new(8000, 0, 100, new(1, 1), 0, false, true, true, 500000, null, null, null, []),
                    new(9000, 100, 100, new(1, 1), 0, true, true, true, 250000, null, null, null, [])],
                ConstructionSites = [new(2, new(27, 2), [9000])],
                Places = original.Military.Places.Select(p => p with { CanDemolish = p.Id == 2, FacilityTypeId = p.Id == 2 ? 9000 : 8000 }).ToImmutableArray()
            }
        };
        var session = GameSession.Start(definition, 42);
        Assert.True(session.View.Settlements.Single(s => s.Id == 2).IsFacilityListEntry);
        Assert.Empty(ConstructionLocations.Read(session, 1, 9000));
        var command = new DemolishFacilityCommand(Guid.NewGuid(), 1, 0, 2);
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        var facility = session.View.Settlements.Single(s => s.Id == 2);
        Assert.True(facility.IsDestroyed);
        Assert.False(facility.IsFacilityListEntry);
        Assert.Equal(10100u, session.View.Settlements.Single(s => s.Id == 1).Soldiers);
        var restored = GameSession.Restore(definition, session.Capture());
        Assert.Equal(session.Snapshot.StateHash, restored.Snapshot.StateHash);
        Assert.False(restored.View.Settlements.Single(s => s.Id == 2).IsFacilityListEntry);
        string hash = restored.Snapshot.StateHash;
        Assert.Equal(new WorldPosition(27, 2), Assert.Single(ConstructionLocations.Read(restored, 1, 9000)).Position);
        Assert.Empty(ConstructionLocations.Read(restored, 1, 8000));
        Assert.Equal(hash, restored.Snapshot.StateHash);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlannerHonorsFormationEligibilityIncludingTreasureProvidedByContent(bool hasTreasure)
    {
        var original = Fixture();
        var definition = original with
        {
            Military = original.Military! with { Tactics = [new(8000, 4, TacticEffect.FormationTechnology, 0, 0, 0, false, false, [20])],
                Formations = original.Military.Formations
                .Select(f => f.TravelMode == TravelMode.Water ? f with { RequiredTacticId = 8000 } : f).ToImmutableArray() },
            Information = new([], [], [new(99, "treasure", 0, 0, 0, 8000)], [],
                [new(99, hasTreasure ? 1 : null, TreasureDiscovery.Discovered, null)])
        };
        var session = GameSession.Start(definition, 42);
        var command = MilitaryPlanner.Choose(session, new("player", 1, ControllerKind.Player), Guid.NewGuid());
        if (hasTreasure)
        {
            Assert.Equal(20, Assert.IsType<DeployUnitCommand>(command).FormationId);
            Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        }
        else Assert.IsType<WaitCommand>(command);
    }

    private static ScenarioInitialState Fixture() => new(ScenarioInitialState.CurrentSchemaVersion, "shore-public", 190, 1, 1, 1,
        [new(1, "force", 1, 10000, 10000, 100)],
        [new(1, "origin", 1, 0, 0, 10000, 100, 100, 70, []), new(2, "destination", 1, 5, 0, 100, 100, 100, 30, [])], [], [])
    {
        Officers = [new("officer", new(1, 1, 1, new(70, 70, 70, 70), false))],
        Military = new(6, 1, [0, 2, 1, 1, 2, 0], [new(0, 1, null), new(1, null, 1), new(2, 1, 1)],
            [new(10, 5, 0, 10000, 1, new(1, 1), TravelMode.Land), new(20, 5, 50, 10000, 1, new(1, 1), TravelMode.Water)],
            [new(1, new(2, 2), new(5, 5)), new(2, new(27, 2), new(5, 5))])
    };

    [Fact]
    public void PlannerChoosesReachableWaterEntrancesAndActualCommandDeliversReinforcements()
    {
        var session = GameSession.Start(Fixture(), 42);
        var command = Assert.IsType<DeployUnitCommand>(MilitaryPlanner.Choose(session,
            new("player", 1, ControllerKind.Player), Guid.NewGuid()));
        Assert.Equal(20, command.FormationId);
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        var result = session.AdvanceXun();
        Assert.Single(result.Events.OfType<UnitAbsorbedEvent>());
        Assert.Equal(3000u, session.View.Settlements.Single(s => s.Id == 2).Soldiers);
    }

    [Fact]
    public void SessionDispatchesReformationAndQueryShowsItsCurrentFormationAndOrder()
    {
        var session = GameSession.Start(Fixture(), 42);
        Assert.True(session.Execute(new("player"), new DeployUnitCommand(Guid.NewGuid(), 1, 0, 1, [1], 500,
            10, new(MilitaryOrderKind.Move, new(7, 2), null))).Submission.Accepted);
        var unit = Assert.Single(session.View.World.Units);
        // The first land exit is on the shared shore, so embark before any date advance.
        var command = new ReformUnitCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence,
            unit.Id, 20, [1], new(MilitaryOrderKind.Transport, null, 2));
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        unit = Assert.Single(session.View.World.Units);
        Assert.Equal(20, unit.FormationId);
        Assert.Equal(2, unit.Order!.SettlementId);
        Assert.Single(session.AdvanceXun().Events.OfType<UnitAbsorbedEvent>());
    }
}
