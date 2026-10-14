using San9.Tests;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class WorldQueryTests
{
    private static readonly TreasureRulebook Treasures = new([
        new(10, new(0, 0, 0, 20), 0) { EscapeMount = true, TacticId = 7 },
        new(11, new(0, 0, 0, 10), 0)]);
    private static readonly OfficerAbilityRules Abilities = new(Treasures,
        new([new(1, 10000, 0) { AbilityBonuses = new(0, 0, 0, 5) }]));

    private static WorldState Create() => WorldState.Create(new(190, 1, 1), 42,
        [new(1, 0)], [new(1, 1, 1000, 100, 100, 80, 0, 0, 100, 0, 100)],
        [new(1, 1, 1, new(50, 50, 50, 60), false), new(2, 1, 1, new(50, 50, 50, 90), false),
            new(3, 1, 1, new(50, 50, 50, 100), false) { Status = OfficerStatus.Captive },
            new(4, 1, 1, new(50, 50, 50, 100), false) { IsDormant = true }],
        [new(10, 1, new(10, 10), 1000), new(20, 1, new(20, 20), 1000)],
        corps: [new(1, 1, null, new(0, 0)) { IsRoot = true }],
        treasures: [new(10, 1, TreasureDiscovery.Discovered, null), new(11, 2, TreasureDiscovery.Discovered, null)]);

    private static PersistentTask Task(int officer, bool traveling) => new(Guid.NewGuid(), "test", "player", 1,
        officer, 1, new(190, 1, 1), new(190, 1, 2), 0, 0)
    {
        Journey = traveling ? new(1, null, null) { Route = new(new(10, 10), new(20, 20), false) } : null
    };

    [Fact]
    public void MemberQueriesFollowMovesStatusAndActivationWithoutPublishingChanges()
    {
        var state = Create();
        var hash = state.CreateSnapshot();
        var world = state.BeginMutation(Abilities);
        Assert.Equal([1, 2, 3], world.SettlementMembers(1).Select(p => p.Id).ToArray());
        Assert.Empty(world.UnitMembers(10));
        world.Replace(world.Officers[1] with { SettlementId = null, UnitId = 10 });
        world.Replace(world.Officers[3] with { SettlementId = null, UnitId = 10 });
        Assert.Equal([1, 3], world.UnitMembers(10).Select(p => p.Id).ToArray());
        Assert.Equal(1, Assert.Single(world.ServingUnitMembers(10)).Id);
        world.Replace(world.Officers[1] with { Injury = 4, OwnerId = null });
        Assert.Equal(world.Officers[1], Assert.Single(world.ServingUnitMembers(10)));
        world.Replace(world.Officers[1] with { UnitId = 20 });
        Assert.Equal(3, Assert.Single(world.UnitMembers(10)).Id);
        Assert.Equal(1, Assert.Single(world.UnitMembers(20)).Id);
        world.Replace(world.AllOfficers[4] with { IsDormant = false });
        Assert.Equal([2, 4], world.ServingSettlementMembers(1).Select(p => p.Id).ToArray());
        world.Replace(world.Officers[4] with { IsDormant = true });
        Assert.Equal(2, Assert.Single(world.ServingSettlementMembers(1)).Id);
        StateAssert.Equal(hash, state.CreateSnapshot());
    }

    [Fact]
    public void CombatLeadershipReadsCurrentInjuryOfficeMembershipAndJourney()
    {
        var world = Create().BeginMutation(Abilities);
        world.Replace(world.Officers[1] with { SettlementId = null, UnitId = 10 });
        world.Replace(world.Officers[3] with { SettlementId = null, UnitId = 10 });
        Assert.Equal(80, MilitaryCombat.Strength(world, world.Units[10]).Leadership);
        Assert.Equal(100, MilitaryCombat.GarrisonStrength(world, world.Settlements[1]).Leadership);
        world.Replace(world.Officers[1] with { Injury = 5, OfficeId = 1 });
        Assert.Equal(55, MilitaryCombat.Strength(world, world.Units[10]).Leadership);
        var journey = Task(1, true);
        var work = Task(2, false);
        world.SetSimulation(world.Simulation with { Tasks = [journey, work] });
        Assert.Equal(0, MilitaryCombat.Strength(world, world.Units[10]).Leadership);
        Assert.Equal(100, MilitaryCombat.GarrisonStrength(world, world.Settlements[1]).Leadership);
        world.SetSimulation(world.Simulation with { Tasks = [work with { Journey = journey.Journey }] });
        Assert.Equal(55, MilitaryCombat.Strength(world, world.Units[10]).Leadership);
        Assert.Equal(0, MilitaryCombat.GarrisonStrength(world, world.Settlements[1]).Leadership);
        world.SetSimulation(world.Simulation with { Tasks = [] });
        world.Replace(world.Officers[1] with { Injury = 0 });
        Assert.Equal(85, MilitaryCombat.Strength(world, world.Units[10]).Leadership);
        Assert.Equal(100, MilitaryCombat.GarrisonStrength(world, world.Settlements[1]).Leadership);
    }

    [Fact]
    public void TaskQueriesDistinguishBusyFromTravelingAndFollowReplacementAndRemoval()
    {
        var world = Create().BeginMutation(Abilities);
        Assert.False(world.IsBusy(1));
        var work = Task(1, false);
        var journey = Task(1, true);
        world.SetSimulation(world.Simulation with { Tasks = [work, journey] });
        Assert.True(world.IsBusy(1));
        Assert.True(world.IsTraveling(1));
        Assert.Equal([work, journey], world.TasksFor(1).ToArray());
        world.SetSimulation(world.Simulation with { Tasks = [work with { OfficerId = 2 }] });
        Assert.False(world.IsBusy(1));
        Assert.False(world.IsTraveling(1));
        Assert.True(world.IsBusy(2));
        Assert.False(world.IsTraveling(2));
        world.SetSimulation(world.Simulation with { Tasks = [] });
        Assert.False(world.IsBusy(2));
        Assert.Empty(world.TasksFor(2));
    }

    [Fact]
    public void TreasureEffectsRefreshBothHoldersAndKeepDiscoveryConditionsDistinct()
    {
        var world = Create().BeginMutation(Abilities);
        void Check(int id, int leadership, bool mount, bool tactic)
        {
            Assert.Equal(leadership, world.Abilities(world.AllOfficers[id]).Leadership);
            Assert.Equal(mount, world.HasEscapeMount(id, Treasures));
            Assert.Equal(tactic, world.HasTreasureTactic(id, 7, Treasures));
        }
        Check(1, 80, true, true);
        Check(2, 100, false, false);
        Assert.True(MilitaryTactics.HasTactic(world, 1, [1], 7, Treasures));
        world.Replace(world.Treasures[10] with { HolderId = 2 });
        Check(1, 60, false, false);
        Check(2, 110, true, true);
        Assert.False(MilitaryTactics.HasTactic(world, 1, [1], 7, Treasures));
        Assert.True(MilitaryTactics.HasTactic(world, 1, [2], 7, Treasures));
        world.Replace(world.Treasures[10] with { Discovery = TreasureDiscovery.Buried, DiscoveryRegionId = 1 });
        Check(2, 110, true, false);
        world.Replace(world.Treasures[10] with { Discovery = TreasureDiscovery.Disabled });
        Check(2, 100, false, false);
        world.Replace(world.Treasures[10] with { Discovery = TreasureDiscovery.Discovered });
        world.Replace(world.Officers[2] with { IsDormant = true });
        Check(2, 90, false, false);
        world.Replace(world.AllOfficers[2] with { IsDormant = false });
        Check(2, 110, true, true);
        world.Replace(world.Treasures[10] with { HolderId = null });
        Check(2, 100, false, false);
    }
}
