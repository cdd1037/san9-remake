using San9.Tests;
using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class UnitReformationTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 4)]
    [InlineData(3, 3)]
    public void EntrancesCoverEveryNonOverlappingContactIncludingOffCenterFootprints(int width, int height)
    {
        var place = new BattlefieldPlace(1, new(10, 10), new(2, 3));
        var footprint = new Footprint(width, height);
        var area = new MapOccupancy(place.Position, place.Footprint);
        var expected = from x in Enumerable.Range(0, 20) from y in Enumerable.Range(0, 20)
            let candidate = new MapOccupancy(new(x, y), footprint)
            where !candidate.Overlaps(area) && candidate.DistanceTo(area) == 1
            select candidate.Position;
        Assert.Equal(expected.OrderBy(p => p.X).ThenBy(p => p.Y), area.Entrances(footprint).OrderBy(p => p.X).ThenBy(p => p.Y));
    }

    private static readonly CommandContext Player = new("player");
    private static MilitaryRulebook Rules() => new(new(6, 1, [0, 2, 1, 1, 2, 0],
        [new(0, 1, null), new(1, null, 1) { IsWater = true }, new(2, 1, 1)],
        [new(10, 5, 0, 10000, 2, new(1, 1), TravelMode.Land) { Positions = San9.Tests.FormationTestData.Layout([BattleRank.Front, BattleRank.Rear]) },
         new(20, 5, 50, 10000, 2, new(1, 1), TravelMode.Water) { Positions = San9.Tests.FormationTestData.Layout([BattleRank.Middle, BattleRank.Middle]) },
         new(70, 5, 0, 10000, 2, new(1, 1), TravelMode.Land) { Positions = San9.Tests.FormationTestData.Layout([BattleRank.Front, BattleRank.Rear]) },
         new(71, 5, 50, 10000, 2, new(1, 1), TravelMode.Water) { Positions = San9.Tests.FormationTestData.Layout([BattleRank.Middle, BattleRank.Middle]) }],
        [new(1, new(4, 2), new(1, 1)), new(2, new(25, 2), new(1, 1))]) { TransportFormations = new(70, 71) });

    private static WorldState World(int x = 7, bool deployed = true, ControllerKind controller = ControllerKind.Player,
        bool automaticCorps = false) => WorldState.Create(new(190, 1, 1), 42,
        [new(1, 0)],
        [new(1, 1, 1000, 100, 100, 70, 0, 0, 100, 0, 100), new(2, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
        [new(11, 1, deployed ? null : 1, new(70, 70, 70, 70), false) { UnitId = deployed ? 0 : null },
         new(12, 1, deployed ? null : 1, new(70, 70, 70, 70), false) { UnitId = deployed ? 0 : null }],
        units: deployed ? [new(0, 1, new(x, 2), 500) { FormationId = 10, Morale = 70, Wounded = 80, CargoFood = 100,
            CorpsId = automaticCorps ? 1 : null,
            MemberPositions = San9.Tests.FormationTestData.Members(ImmutableDictionary<int, BattleRank>.Empty.Add(11, BattleRank.Front).Add(12, BattleRank.Rear)) }] : [],
        controls: [new("player", 1, controller)], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(1000, 10000)) { IsRoot = true,
                }]);

    private static SimulationEngine Engine(WorldState world) => new(world, EconomyRulebook.Empty,
        PersonnelRulebook.Empty, TreasureRulebook.Empty, military: Rules());
    private static ReformUnitCommand Reform(SimulationEngine engine, int formation, int[]? people = null, MilitaryOrder? order = null) =>
        new(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence, 0, formation, (people ?? [12, 11]).ToImmutableArray(), order);

    [Theory]
    [InlineData(ControllerKind.Player, false)]
    [InlineData(ControllerKind.Automatic, false)]
    [InlineData(ControllerKind.Player, true)]
    [InlineData(ControllerKind.Automatic, true)]
    public void ReformationIsGloballyDisabled(ControllerKind controller, bool automaticCorps)
    {
        var engine = Engine(World(controller: controller, automaticCorps: automaticCorps));
        if (automaticCorps) engine.ConfigureCompanion(new() { PlayerCorpsAutomation = PlayerCorpsAutomationMode.Full });
        var context = automaticCorps ? new CommandContext(CorpsControl.ControllerId(1)) : Player;
        var command = Reform(engine, 20);
        var before = engine.Snapshot;

        var validation = engine.Validate(context, command);
        Assert.Equal(CommandRejection.UnsupportedCommand, validation.Rejection);
        StateAssert.Equal(before, engine.Snapshot);
        var result = engine.Execute(context, command);
        Assert.Equal(validation, result.Submission);
        Assert.Empty(result.Events);
        StateAssert.Equal(before, engine.Snapshot);
    }

    [Theory]
    [InlineData(2, 20, false)]
    [InlineData(7, 999, false)]
    [InlineData(7, 20, true)]
    public void TerrainUnknownFormationAndMembershipRejectionsAreAtomic(int x, int formation, bool invalidMembers)
    {
        var engine = Engine(World(x));
        var before = engine.Snapshot;
        Assert.False(engine.Execute(Player, Reform(engine, formation, invalidMembers ? [11, 11] : null)).Submission.Accepted);
        StateAssert.Equal(before, engine.Snapshot);
    }

    [Fact]
    public void WaterTransportUsesShoreEntrancesWithoutEnteringLandBuildingCenter()
    {
        var engine = Engine(World(deployed: false));
        var command = new TransportUnitCommand(Guid.NewGuid(), 1, 0, 1, [11], 500, 2, 10, 100);
        Assert.True(engine.Execute(Player, command).Submission.Accepted);
        Assert.Equal(new WorldPosition(4, 2), Assert.Single(engine.Snapshot.Units).Position);
        engine.EndControl(Player);
        var result = engine.AdvanceXun();
        Assert.Single(result.Events.OfType<UnitAbsorbedEvent>());
        Assert.Equal(new WorldPosition(25, 2), result.Events.OfType<UnitMovedEvent>().Last().To);
        Assert.Equal(500u, engine.Snapshot.Settlements.Single(s => s.Id == 2).Soldiers);
    }
}
