using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class UnitDefectionTests
{
    private static readonly SimulationRules Rules = new()
    {
        Personnel = new([new(1, new(2, 2)), new(2, new(902, 2))], new Dictionary<int, int> { [1] = 1, [2] = 2 }, [new(1, 2), new(2, 1)]),
        Military = new(new(200, 2, Enumerable.Repeat(0, 400).ToImmutableArray(), [new(0, 1, null)],
            [new(1, 1, 0, 1000000, 5, new(1, 1), TravelMode.Land) { CaptureRanks = [BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle], Attack = new(10, true), Combat = new(10, 10, 10, 10, 50) }],
            [new(1, new(2, 2), new(1, 1)), new(2, new(902, 2), new(1, 1))])
            { RegionIds = Enumerable.Repeat<int?>(2, 400).ToImmutableArray() })
    };

    private static WorldMutation World()
    {
        var world = FieldRecruitmentTests.World().BeginMutation(Rules.Abilities);
        world.SetClock(new(190, 2, 1));
        world.SetRelation(new(1, 2, 0));
        foreach (int id in new[] { 101, 102 })
            world.AddUnit(new(id, 1, new(id == 101 ? 327 : 337, 2), 5000)
            { FormationId = 1, CorpsId = 1, ServicePlaceId = 1,
                Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 100 } });
        world.SetSimulation(world.Simulation with { NextUnitId = 104, SettlementDefectionsEnabled = false });
        world.MaintainLeadership([]);
        return world;
    }

    private static List<DomainEvent> Apply(WorldMutation world)
    {
        List<DomainEvent> events = [];
        UnitDefections.Apply(world, Rules, events);
        ResolutionPipeline.CheckTerminal(world, events);
        WorldInvariants.Validate(world);
        return events;
    }

    [Fact]
    public void BesiegedArmyDefectsWithoutCountingItselfAsReinforcement()
    {
        var world = World(); var before = world.Units[100]; var home = world.Settlements[2];
        var result = Assert.Single(Apply(world).OfType<UnitDefectedEvent>());
        Assert.Equal(new[] { 20, 22 }, result.FollowerIds);
        var army = world.Units[100];
        Assert.Equal((1, 1, 1), (army.OwnerId, army.CorpsId, army.ServicePlaceId));
        Assert.Equal((before.Position, before.Soldiers, before.Wounded, before.CargoMoney, before.CargoFood, before.Morale),
            (army.Position, army.Soldiers, army.Wounded, army.CargoMoney, army.CargoFood, army.Morale));
        Assert.Equal(home, world.Settlements[2]);
        Assert.Equal(0, world.Officers[20].LoyaltyTo(2));
        var returning = Assert.Single(world.Simulation.Tasks);
        Assert.Equal(21, returning.OfficerId);
        Assert.False(returning.Journey!.ReturnLeg!.AfterDailyCommands);
        Assert.Equal(before.Position, returning.Journey.Route.Origin);
        Assert.Null(world.Units[101].Order);
        Assert.Null(world.Units[102].Order);
        Assert.Empty(Apply(world).OfType<UnitDefectedEvent>());
    }

    [Theory]
    [InlineData("disabled")] [InlineData("early")] [InlineData("one-attacker")]
    [InlineData("far")] [InlineData("confused")] [InlineData("other-target")]
    [InlineData("ambition")] [InlineData("friendly")] [InlineData("reinforcement")]
    public void IneligibleArmyDoesNotConsumeDefectionRandom(string gate)
    {
        var world = World();
        switch (gate)
        {
            case "disabled": world.SetSimulation(world.Simulation with { UnitDefectionsEnabled = false }); break;
            case "early": world.SetClock(new(190, 1, 21)); break;
            case "one-attacker": world.RemoveUnit(102); break;
            case "far": world.Replace(world.Units[102] with { Position = new(352, 2) }); break;
            case "confused": world.Replace(world.Units[102] with { Condition = UnitCondition.Confused }); break;
            case "other-target": world.Replace(world.Units[102] with { Order = null }); break;
            case "ambition": world.Replace(world.Officers[20] with { Ambition = 15 }); break;
            case "friendly": world.SetRelation(new(1, 2, 40)); break;
            case "reinforcement": world.AddUnit(new(103, 2, new(382, 2), 1) { FormationId = 1, CorpsId = 2, ServicePlaceId = 2 }); break;
        }
        Assert.Empty(Apply(world).OfType<UnitDefectedEvent>());
        Assert.Equal(2, world.Units[100].OwnerId);
        Assert.Empty(world.Simulation.RandomStreams);
    }

    [Theory]
    [InlineData(49, true)] [InlineData(50, false)]
    public void SiegeUsesStrictProbabilityBoundary(int roll, bool defects)
    {
        var world = World();
        world.Replace(world.Forces[1] with { Trust = 0 });
        world.Replace(world.Units[100] with { Soldiers = 20000 });
        world.Replace(world.Officers[20].WithLoyalty(1, 100));
        for (ulong seed = 0; ; seed++)
        {
            var random = new StableRandom(seed);
            if (random.NextUInt64() % 100 != (ulong)roll) continue;
            world.SetSimulation(world.Simulation with { RandomStreams = [new(UnitDefections.RandomStream, seed)] });
            break;
        }
        Assert.Equal(defects, Apply(world).OfType<UnitDefectedEvent>().Any());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OnlyAnAiRulersLastAssetCanDefect(bool player)
    {
        var world = World();
        world.Replace(world.Officers[2] with { SettlementId = null, UnitId = 100, Ambition = 0 });
        world.Replace(world.Units[100] with { Soldiers = 1 });
        world.Replace(world.Units[101] with { Soldiers = 500000 });
        world.MaintainLeadership([]);
        Assert.Empty(Apply(world).OfType<UnitDefectedEvent>()); // A home still belongs to the ruler.
        world.Replace(world.Settlements[2] with { OwnerId = 1, CorpsId = 1 });
        if (player) world.SetSimulation(world.Simulation with
            { Controls = world.Simulation.Controls.Add(new("other", 2, ControllerKind.Player)) });
        var events = Apply(world);
        Assert.Equal(!player, events.OfType<UnitDefectedEvent>().Any());
        Assert.Equal(!player, world.Forces[2].IsEliminated);
    }
}
