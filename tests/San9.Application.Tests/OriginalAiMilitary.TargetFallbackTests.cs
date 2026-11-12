using System.Collections.Immutable;
using San9.Application.AI;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed partial class OriginalAiMilitaryTests
{
    [Fact]
    public void UnreachablePreferredTargetDoesNotBlockTheNextTarget()
    {
        var basis = Fixture();
        var content = basis with
        {
            Settlements = [.. basis.Settlements, basis.Settlements[1] with { Id = 3, X = 1, Y = 13 }],
            MilitaryCityConnections = [new(1, 2), new(2, 1), new(1, 3), new(3, 1)],
            CityConnections = [new(1, 2), new(2, 1), new(1, 3), new(3, 1)],
            RegionCityIds = new(basis.RegionCityIds) { [3] = 3 },
            RegionNameKeys = new(basis.RegionNameKeys) { [3] = "reachable" },
            Military = basis.Military! with
            {
                Height = 20,
                TerrainIds = Enumerable.Range(0, 400).Select(i => i % 20 == 10 ? 1 : 0).ToImmutableArray(),
                Terrains = [new(0, 1, null), new(1, null, null)],

                Places = basis.Military.Places.Add(new(3, new(7, 67), new(1, 1)) { Combat = basis.Military.Places[1].Combat }),
                RegionIds = Enumerable.Range(0, 400).Select(i => (int?)(i % 20 >= 10 ? 2 : i / 20 >= 10 ? 3 : 1)).ToImmutableArray()
            }
        };
        var h = new Harness(content);
        var navigation = new MilitaryRouteQueryContext(h.Engine.Snapshot, new(content.Military!));
        var source = h.Engine.Snapshot.Settlements.Single(s => s.Id == 1);
        var position = navigation.DeploymentPosition(source, 400, null, 1, new());
        Assert.NotNull(position);
        var blocked = new UnitState(900, 1, position.Value, 10000)
        {
            FormationId = 400,
            Order = new(MilitaryOrderKind.AttackSettlement, null, 2)
        };
        Assert.Equal(RouteReachability.Unreachable, navigation.Estimate(blocked, null).Status);
        AssertAttackAlignmentCommandAvailable(content, h.Engine, 20000, 3);

        Assert.Equal(1, h.Military.Run(OriginalAiWork.Attack, 1, h.State,
            new(1, OriginalAiMode.TargetSettlement, null, 2)).Submitted);
        Assert.Equal(3, Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).Order.SettlementId);
    }

    [Fact]
    public void InterveningTargetWithoutSourcesDoesNotBlockTheNextTarget()
    {
        var basis = Fixture();
        var content = basis with
        {
            Settlements = [.. basis.Settlements,
                basis.Settlements[0] with { Id = 3, Kind = SettlementKind.Facility, X = 5, Soldiers = 0, Morale = 0 },
                basis.Settlements[1] with { Id = 4, Kind = SettlementKind.Facility, X = 9 },
                basis.Settlements[1] with { Id = 5, X = 13, Y = 18, Soldiers = 0, Morale = 0 },
                basis.Settlements[1] with { Id = 6, X = 1, Y = 13 }],
            MilitaryCityConnections = [new(1, 2), new(2, 1), new(1, 6), new(6, 1)],
            CityConnections = [new(1, 2), new(2, 1), new(1, 6), new(6, 1)],
            RegionCityIds = new(basis.RegionCityIds) { [3] = 1, [4] = 5, [5] = 5, [6] = 6 },
            RegionNameKeys = new(basis.RegionNameKeys) { [3] = "transit", [4] = "interception", [5] = "other-city", [6] = "alternative" },
            Military = basis.Military! with
            {
                Height = 20,
                TerrainIds = Enumerable.Repeat(0, 400).ToImmutableArray(),

                Places = basis.Military.Places
                    .Add(new(3, new(27, 7), new(1, 1)) { Combat = basis.Military.Places[0].Combat })
                    .Add(new(4, new(47, 7), new(1, 1)) { Combat = basis.Military.Places[1].Combat })
                    .Add(new(5, new(67, 92), new(1, 1)) { Combat = basis.Military.Places[1].Combat })
                    .Add(new(6, new(7, 67), new(1, 1)) { Combat = basis.Military.Places[1].Combat }),
                RegionIds = Enumerable.Range(0, 400).Select(i => (int?)(i / 20 >= 17 && i % 20 >= 12 ? 5 :
                    i / 20 >= 10 && i % 20 < 8 ? 6 : i % 20 < 3 ? 1 : i % 20 < 8 ? 3 : i % 20 < 12 ? 4 : 2)).ToImmutableArray()
            }
        };
        var h = new Harness(content);
        var geography = new OriginalAiGeography(content, new(content.Military!), h.Engine.Snapshot);
        Assert.DoesNotContain(geography.Neighbors(1), p => p.Id == 4);
        AssertAttackAlignmentCommandAvailable(content, h.Engine, 20000, 6);

        Assert.Equal(1, h.Military.Run(OriginalAiWork.Attack, 1, h.State,
            new(1, OriginalAiMode.TargetSettlement, null, 2)).Submitted);
        var command = Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands));
        Assert.Equal(1, command.SettlementId);
        Assert.Equal(6, command.Order.SettlementId);
    }
}
