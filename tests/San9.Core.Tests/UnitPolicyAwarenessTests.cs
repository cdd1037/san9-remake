using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class UnitPolicyAwarenessTests
{
    [Theory]
    [InlineData(0, -3, 0)] [InlineData(1, -3, 0)] [InlineData(1, -2, 1)]
    [InlineData(1, -1, 2)] [InlineData(2, -1, 3)] [InlineData(3, 0, 4)]
    [InlineData(2, 1, 5)] [InlineData(1, 1, 6)] [InlineData(1, 2, 7)]
    [InlineData(0, 3, 8)] [InlineData(-1, 3, 8)] [InlineData(-1, 2, 9)]
    [InlineData(-1, 1, 10)] [InlineData(-2, 1, 11)] [InlineData(-3, 0, 12)]
    [InlineData(-3, -1, 12)] [InlineData(-2, -1, 13)] [InlineData(-1, -1, 14)]
    [InlineData(-1, -2, 15)] [InlineData(0, 0, 0)]
    public void AwarenessBearingPreservesMapSectorBoundaries(int dx, int dy, int expected) =>
        Assert.Equal(expected, FormationVisionGeometry.Bearing(dx, dy));

    [Fact]
    public void VisionUsesDiscreteCenterDistanceAndFacingRatherThanFootprintDistance()
    {
        var origin = new WorldPosition(50, 50);
        var cone = new FormationVision(10, 90);
        Assert.True(FormationVisionGeometry.Contains(origin, WorldDirection.East, new(60, 54), cone, 0));
        Assert.False(FormationVisionGeometry.Contains(origin, WorldDirection.East, new(61, 50), cone, 0));
        Assert.True(FormationVisionGeometry.Contains(origin, WorldDirection.East, new(53, 47), cone, 0));
        Assert.False(FormationVisionGeometry.Contains(origin, WorldDirection.East, new(51, 47), cone, 0));
        Assert.False(FormationVisionGeometry.Contains(origin, WorldDirection.West, new(60, 50), cone, 0));
    }

    [Fact]
    public void ApproachChoosesCombatPowerAndRetainsAuthoredOrderAtEqualPower()
    {
        var source = Unit(1, 1, new(50, 50), 11);
        var first = Unit(30, 2, new(65, 50), 33);
        var second = Unit(2, 2, new(55, 50), 22);
        var state = World([source, first, second]).BeginMutation();
        MilitaryPolicies.ApproachDay(state, Rules(), []);
        Assert.Equal(30, state.Units[1].EngagementTargetId);
        state = World([source, first with { Soldiers = 9000 }, second]).BeginMutation();
        MilitaryPolicies.ApproachDay(state, Rules(), []);
        Assert.Equal(30, state.Units[1].EngagementTargetId);
    }

    [Fact]
    public void RulerPenaltyAffectsCandidateRankingRatherThanTheAttackDecisionStrength()
    {
        var source = Unit(1, 1, new(50, 50), 11);
        var ruler = Unit(2, 2, new(55, 50), 22);
        var other = Unit(30, 2, new(65, 50), 33);
        var snapshot = World([source, ruler, other]).CreateSnapshot();
        snapshot = snapshot with
        {
            Simulation = snapshot.Simulation with { Boundary = SimulationBoundary.Stable },
            Forces = snapshot.Forces.Select(f => f.Id == 2 ? f with { LeaderId = 22 } : f).ToArray(),
            Corps = snapshot.Corps.Select(c => c.Id == 2 ? c with { LeaderId = 22 } : c).ToArray(),
            Officers = snapshot.Officers.Select(o => o.Id == 22
                ? o with { Status = OfficerStatus.Ruler } : o).ToArray()
        };
        var state = WorldState.Restore(snapshot).BeginMutation();
        MilitaryPolicies.ApproachDay(state, Rules(), []);
        Assert.Equal(30, state.Units[1].EngagementTargetId);
    }

    [Fact]
    public void ApproachCanReplaceAnInterceptionThatLeftVisionAndCanSeeDelayedMilitaryTargets()
    {
        var source = Unit(1, 1, new(50, 50), 11) with { EngagementTargetId = 2, DepartureDaysRemaining = 3 };
        var old = Unit(2, 2, new(95, 50), 22);
        var replacement = Unit(30, 2, new(65, 50), 33) with { DepartureDaysRemaining = 4 };
        var state = World([source, old, replacement]).BeginMutation();
        MilitaryPolicies.ApproachDay(state, Rules(), []);
        Assert.Equal(30, state.Units[1].EngagementTargetId);
        Assert.Equal(source.Order, state.Units[1].Order);
    }

    [Fact]
    public void NonresponsiveStateStillMaintainsTheEncounterMemory()
    {
        var source = Unit(1, 1, new(50, 50), 11) with { Condition = UnitCondition.Confused };
        var target = Unit(2, 2, new(55, 50), 22);
        var state = World([source, target]).BeginMutation();
        MilitaryPolicies.ApproachDay(state, Rules(), []);
        Assert.False(state.Units[1].ApproachNotificationPending);
        Assert.Null(state.Units[1].EngagementTargetId);
        state.Replace(state.Units[2] with { Position = new(95, 50) });
        MilitaryPolicies.ApproachDay(state, Rules(), []);
        Assert.True(state.Units[1].ApproachNotificationPending);
    }

    [Fact]
    public void AVisiblePrimaryGarrisonPreventsInterceptionOfAnotherEnemy()
    {
        var source = Unit(1, 1, new(75, 5), 11) with
            { Order = new(MilitaryOrderKind.AttackSettlement, null, 2) };
        var other = Unit(2, 2, new(75, 15), 22);
        var state = World([source, other]).BeginMutation();
        MilitaryPolicies.ApproachDay(state, Rules(garrisons: true), []);
        Assert.False(state.Units[1].ApproachNotificationPending);
        Assert.Null(state.Units[1].EngagementTargetId);
    }

    [Fact]
    public void AwarenessSizeBonusIsAuthoredSeparatelyFromThePhysicalFootprint()
    {
        var source = Unit(1, 1, new(50, 50), 11);
        var target = Unit(2, 2, new(55, 50), 22);
        var state = World([source, target]).BeginMutation();
        Assert.Empty(UnitPolicyQueries.VisibleForeignCombatants(state, source, Rules(2)));
        Assert.Contains(new BattleSource(2, false), UnitPolicyQueries.VisibleForeignCombatants(state, source,
            Rules(2, [new(1, 3)])));
    }

    [Fact]
    public void NeutralLocationsDoNotCountAsAnEnemyInThePursuitNeighborhood()
    {
        var source = Unit(1, 1, new(50, 50), 11);
        var state = World([source]).BeginMutation();
        state.Replace(state.Settlements[2] with { OwnerId = null, CorpsId = null });
        var rules = Rules(garrisons: true);
        state.Replace(source with { Position = rules.Places[2].Position });
        Assert.Empty(UnitPolicyQueries.NearbyEnemies(state, state.Units[1], rules, 10));
    }

    [Fact]
    public void ForbiddenPursuitConsidersOtherEnemiesOnlyOnAFreshResponse()
    {
        var source = Unit(1, 1, new(50, 50), 11) with
        {
            Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 2 }, Policies = new()
        };
        var target = Unit(2, 2, new(55, 50), 22) with
        {
            Order = new(MilitaryOrderKind.Return, null, 2), AutonomousReturn = true, PursuitResponsePending = false
        };
        var other = Unit(3, 2, new(50, 55), 33);
        var state = World([source, target, other], 0).BeginMutation();
        MilitaryPolicies.ResolvePursuit(state, Rules(), []);
        Assert.Equal(MilitaryOrderKind.AttackUnit, state.Units[1].Order!.Kind);
        state.Replace(target with { PursuitResponsePending = true });
        MilitaryPolicies.ResolvePursuit(state, Rules(), []);
        Assert.Equal(MilitaryOrderKind.Return, state.Units[1].Order!.Kind);
    }

    private static UnitState Unit(int id, int owner, WorldPosition position, int commander) =>
        new(id, owner, position, 5000)
        {
            FormationId = 1, CommanderId = commander, ServicePlaceId = owner, Morale = 80,
            Policies = new(AttackApproachingEnemies: true), Order = new(MilitaryOrderKind.Move, new(95, 95), null),
            MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(commander, BattleRank.Front)
        };

    private static MilitaryRulebook Rules(int radius = 30, ImmutableArray<SoldierVisionBonus> size = default,
        bool garrisons = false) =>
        new(new MilitaryDefinition(20, 20, Enumerable.Repeat(0, 400).ToImmutableArray(), [new(0, 1, null)],
            [new(1, 5, 0, 100000, 1, new(1, 1), TravelMode.Land)
            {
                Vision = new(radius, 360), Combat = new(10, 10, 10, 10, 50), Attack = new(1, true),
                CaptureRanks = [BattleRank.Front]
            }], [new(1, new(5, 5), new(1, 1)) { Combat = garrisons ? new(10, 10, 10, 10, 50) : null },
                new(2, new(95, 5), new(1, 1)) { Combat = garrisons ? new(10, 10, 10, 10, 50) : null }])
        { PolicyRadius = 10, VisionSizeBands = size.IsDefault ? [] : size });

    private static WorldState World(IReadOnlyList<UnitState> units, int personality = 1) =>
        WorldState.Create(new(190, 1, 1), 42, [new(1, 100), new(2, 100)],
            [new(1, 1, 5000, 100, 100, 80, 500, 0, 100, 0, 100), new(2, 2, 5000, 100, 100, 80, 500, 0, 100, 0, 100)],
            units.Select(u => new OfficerState(u.CommanderId!.Value, u.OwnerId, null, new(80, 80, 80, 80), false)
                { UnitId = u.Id, Personality = u.OwnerId == 1 ? personality : 3 }), units,
            controls: [new("player", 1, ControllerKind.Player)],
            corps: [new(1, 1, null, new(10000, 10000)) { IsRoot = true }, new(2, 2, null, new(10000, 10000)) { IsRoot = true }]);
}
