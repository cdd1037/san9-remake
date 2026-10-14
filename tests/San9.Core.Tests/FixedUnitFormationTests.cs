using San9.Tests;
using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class FixedUnitFormationTests
{
    private static readonly CommandContext Player = new("player");
    private static MilitaryDefinition Definition() => new(8, 2,
        [0, 0, 1, 1, 1, 1, 0, 0, 0, 0, 1, 1, 1, 1, 0, 0],
        [new(0, 10, null), new(1, null, 10) { IsWater = true }],
        [Formation(101, 50, TravelMode.Land), Formation(102, 50, TravelMode.Water),
         Formation(501, 90, TravelMode.Land), Formation(502, 70, TravelMode.Water),
         Formation(601, 130, TravelMode.Land), Formation(602, 85, TravelMode.Water)],
        [new(1, new(2, 2), new(1, 1)) { FacilityTypeId = 0 }, new(2, new(37, 2), new(1, 1)) { FacilityTypeId = 0 }])
    {
        BasicNavalFormationId = 102,
        ConstructionFormations = new(501, 502), TransportFormations = new(601, 602),
        Facilities = [new(0, 0, 600, new(1, 1), 0, false, true, true, 10000, null, null, null, []), new(9000, 100, 600, new(1, 1), 10, true, true, true, 10000, null, null, null, [])],
        ConstructionSites = [new(8000, new(32, 2), [9000])]
    };

    private static FormationRule Formation(int id, int budget, TravelMode mode) => new(id, budget, 0, 10000, 1, new(1, 1), mode)
    {
        IsCommon = id < 500, Combat = new(10, 10, 10, 10, 50), Attack = new(1, true), CaptureRanks = [BattleRank.Middle]
    };

    private static WorldState World() => WorldState.Create(new(190, 1, 1), 42,
        [new(1, 100) { AdditionalFormationIds = [501, 502, 601, 602] }],
        [new(1, 1, 5000, 600, 600, 100, 0, 0, 100, 0, 100), new(2, 1, 100, 600, 600, 100, 0, 0, 100, 0, 100)],
        [new(11, 1, 1, new(80, 80, 80, 80), false)], controls: [new("player", 1, ControllerKind.Player)],
        corps: [new(1, 1, null, new(10000, 100000)) { IsRoot = true }]);

    private static SimulationEngine Engine(WorldState world, MilitaryRulebook rules) =>
        new(world, EconomyRulebook.Empty, new([new(1, new(2, 2)), new(2, new(37, 2)), new(8000, new(32, 2))],
            new Dictionary<int, int> { [1] = 1, [2] = 2, [8000] = 1 }), TreasureRulebook.Empty, military: rules);

    private static GameCommand SpecialDeployment(bool construction) => construction
        ? new ConstructFacilityCommand(Guid.NewGuid(), 1, 0, 1, [11], 500, 8000, 9000, new(32, 2))
        : new TransportUnitCommand(Guid.NewGuid(), 1, 0, 1, [11], 500, 2, 100, 200);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedPurposeSurvivesReturnAndIdleAndCannotBeReformed(bool construction)
    {
        var rules = new MilitaryRulebook(Definition());
        var world = World(); var engine = Engine(world, rules);
        Assert.True(engine.Execute(Player, SpecialDeployment(construction)).Submission.Accepted);
        var unit = Assert.Single(engine.Snapshot.Units);
        Assert.Equal(construction ? 501 : 601, unit.FormationId);
        Assert.Equal(construction ? 502 : 602, unit.NavalFormationId);
        foreach (var order in new MilitaryOrder?[] { new(MilitaryOrderKind.Return, null, 1), null })
        {
            // Cancellation/arrival may leave the unit returning or idle; neither changes its purpose.
            var mutation = world.BeginMutation(); mutation.Replace(mutation.Units[unit.Id] with { Order = order }); world.Commit(mutation);
            engine = Engine(WorldState.Restore(engine.CaptureStableState()), rules);
            var before = engine.Snapshot;
            foreach (int formation in new[] { 101, construction ? 601 : 501, unit.FormationId!.Value })
            {
                var reform = new ReformUnitCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence,
                    unit.Id, formation, [11], new(MilitaryOrderKind.Move, new(7, 2), null));
                Assert.Equal(CommandRejection.UnsupportedCommand, engine.Execute(Player, reform).Submission.Rejection);
                StateAssert.Equal(before, engine.Snapshot);
            }
            Assert.False(engine.Execute(Player, new OrderUnitCommand(Guid.NewGuid(), 1,
                engine.Snapshot.Simulation.NextSequence, unit.Id, new(MilitaryOrderKind.AttackSettlement, null, 2))).Submission.Accepted);
            StateAssert.Equal(before, engine.Snapshot);
            world = WorldState.Restore(engine.CaptureStableState()); engine = Engine(world, rules);
        }
    }

    [Theory]
    [InlineData(501, 502)]
    [InlineData(601, 602)]
    [InlineData(101, 502)]
    [InlineData(101, 602)]
    public void OrdinaryDeploymentCannotSelectSpecialFormationsEvenWhenGranted(int land, int naval)
    {
        var engine = Engine(World(), new(Definition()));
        var before = engine.Snapshot;
        var command = new DeployUnitCommand(Guid.NewGuid(), 1, 0, 1, [11], 500, land,
            new(MilitaryOrderKind.Move, new(7, 2), null)) { NavalFormationId = naval };
        Assert.False(engine.Execute(Player, command).Submission.Accepted);
        StateAssert.Equal(before, engine.Snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedPairCrossesWaterAndReturnsToItsLandFormation(bool construction)
    {
        var rules = new MilitaryRulebook(Definition()); var world = World(); var engine = Engine(world, rules);
        Assert.True(engine.Execute(Player, SpecialDeployment(construction)).Submission.Accepted);
        var unit = Assert.Single(engine.Snapshot.Units);
        // Exercise the same daily navigator for both purposes without starting construction or unloading.
        Assert.True(engine.Execute(Player, new OrderUnitCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence,
            unit.Id, new(MilitaryOrderKind.Move, new(35, 2), null))).Submission.Accepted);
        var estimate = new MilitaryArrivalForecast(engine.Snapshot, rules).Estimate(unit.Id);
        bool visitedWater = false; int days = 0;
        while (world.CreateSnapshot().Units[0].Order is not null && days < 20)
        {
            var mutation = world.BeginMutation(); List<DomainEvent> events = [];
            MilitaryMovement.AdvanceDay(mutation, rules, new([]), PersonnelRulebook.Empty, events);
            world.Commit(mutation); days++;
            unit = world.CreateSnapshot().Units[0];
            bool water = rules.Map.IsWater(unit.Position); visitedWater |= water;
            Assert.Equal(water ? construction ? 502 : 602 : construction ? 501 : 601, rules.Formation(unit).Id);
            Assert.Equal(construction ? 501 : 601, unit.FormationId);
            Assert.Equal(construction ? 502 : 602, unit.NavalFormationId);
            // Restore every daily boundary so fractional budget and the fixed pair cannot be lost.
            world = WorldState.Restore(world.CreateSnapshot());
            _ = Engine(world, rules);
        }
        Assert.True(visitedWater);
        Assert.Null(unit.Order);
        Assert.Equal(new WorldPosition(35, 2), unit.Position);
        Assert.Equal(ArrivalEstimateStatus.Known, estimate.Status);
        Assert.Equal(days, estimate.Days);
    }

    [Fact]
    public void CombatUnitCannotBecomeTransportThroughOrdersOrReformation()
    {
        var engine = Engine(World(), new(Definition()));
        Assert.True(engine.Execute(Player, new DeployUnitCommand(Guid.NewGuid(), 1, 0, 1, [11], 500, 101,
            new(MilitaryOrderKind.Move, new(7, 2), null))).Submission.Accepted);
        var unit = Assert.Single(engine.Snapshot.Units);
        var before = engine.Snapshot;
        long sequence = engine.Snapshot.Simulation.NextSequence;
        Assert.Equal(CommandRejection.InvalidTask, engine.Execute(Player, new OrderUnitCommand(Guid.NewGuid(), 1,
            sequence, unit.Id, new(MilitaryOrderKind.Transport, null, 2))).Submission.Rejection);
        foreach (var pair in new[] { new FixedUnitFormations(501, 502), new FixedUnitFormations(601, 602) })
        {
            Assert.False(engine.Execute(Player, new ReformUnitCommand(Guid.NewGuid(), 1, sequence, unit.Id,
                pair.LandFormationId, [11], new(MilitaryOrderKind.Move, new(7, 2), null))
                { NavalFormationId = pair.NavalFormationId }).Submission.Accepted);
            StateAssert.Equal(before, engine.Snapshot);
        }
        StateAssert.Equal(before, engine.Snapshot);
    }

    [Fact]
    public void RestoreRejectsWrongVesselAndContentRejectsMissingOrSharedFixedReferences()
    {
        var definition = Definition(); var rules = new MilitaryRulebook(definition); var engine = Engine(World(), rules);
        Assert.True(engine.Execute(Player, SpecialDeployment(false)).Submission.Accepted);
        var snapshot = engine.CaptureStableState();
        foreach (int? naval in new int?[] { null, 102, 502 })
            Assert.Throws<ArgumentException>(() => Engine(WorldState.Restore(snapshot with
                { Units = [snapshot.Units[0] with { NavalFormationId = naval }] }), rules));
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with { TransportFormations = new(999, 602) }));
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with { TransportFormations = new(601, 502) }));
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with { TransportFormations = new(602, 601) }));
    }
}
