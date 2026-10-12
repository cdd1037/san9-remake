using System.Collections.Immutable;
using System.Text.Json;
using San9.Application.AI;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class PlanningIntentEqualityTests
{
    public static IEnumerable<object[]> CollectionIntents()
    {
        yield return [new DeployUnitCommand(Guid.Empty, 1, 0, 2, [3, 4], 2000, 1,
            new(MilitaryOrderKind.AttackSettlement, null, 5))
            { Tactics = ImmutableDictionary<int, int?>.Empty.Add(3, 2).Add(4, null) }];
        yield return [new TransportUnitCommand(Guid.Empty, 1, 0, 2, [3, 4], 2000, 5, 100, 200)];
        yield return [new MoveOfficersCommand(Guid.Empty, 1, 0, [3, 4], 5)];
        yield return [new DevelopCommand(Guid.Empty, 1, 0, new(1, 2, [3, 4], DomesticActivity.Commerce))];
        yield return [new FoodTradeCommand(Guid.Empty, 1, 0, 2, [3, 4], FoodTradeDirection.Buy, 100)];
        yield return [new AssignOfficesCommand(Guid.Empty, 1, 0, [new(3, 1), new(4, null)])];
    }

    [Theory]
    [MemberData(nameof(CollectionIntents))]
    public void RecreatedPayloadsRemainRejectedWithoutDependingOnCollectionIdentity(GameCommand original)
    {
        var copy = JsonSerializer.Deserialize<GameCommand>(JsonSerializer.Serialize(original, CanonicalJson.Options), CanonicalJson.Options)!;
        var rejected = new RejectedPlanningIntents();
        rejected.Add(original);
        Assert.True(rejected.Contains(copy));
        Assert.False(rejected.Contains(copy with { IssuerId = 2 }));
        Assert.False(rejected.Contains(copy with { Sequence = 1 }));
        Assert.False(rejected.Contains(copy with { CommandId = Guid.NewGuid() }));
    }

    [Fact]
    public void DeploymentMemberOrderConfigurationAndRouteRemainDistinct()
    {
        var command = (DeployUnitCommand)CollectionIntents().First()[0];
        var rejected = new RejectedPlanningIntents();
        rejected.Add(command);
        Assert.False(rejected.Contains(command with { OfficerIds = [4, 3] }));
        Assert.False(rejected.Contains(command with { Tactics = command.Tactics.SetItem(4, 3) }));
        Assert.False(rejected.Contains(command with { Soldiers = 2001 }));
        Assert.False(rejected.Contains(command with { Waypoint = new(2, 2) }));
        Assert.False(rejected.Contains(command with { ConfirmAttack = true }));
        Assert.True(rejected.Contains(command with { Tactics = ImmutableDictionary<int, int?>.Empty.Add(4, null).Add(3, 2) }));
    }
}
