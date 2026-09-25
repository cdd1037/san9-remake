using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class UnitPolicyTests
{
    private static MilitaryOrder Attack(int id) => new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = id };
    private static readonly MilitaryOrder Mission = new(MilitaryOrderKind.Move, new(90, 10), null);
    private static MilitaryRulebook Rules() => new(new(20, 5, Enumerable.Repeat(0, 100).ToImmutableArray(),
        [new(0, 1, null)], [new(1, 1, 0, 100000, 1, new(1, 1), TravelMode.Land)
        { Combat = new(10, 10, 10, 10, 50), Attack = new(1, true), CaptureRanks = [BattleRank.Front] }],
        [new(1, new(2, 2), new(1, 1)) { Combat = new(10, 10, 10, 10, 50) },
         new(2, new(95, 2), new(1, 1)) { Combat = new(10, 10, 10, 10, 50) }]) { PolicyRadius = 10 });

    private static WorldState World(int personality = 1, UnitPolicies? policies = null, MilitaryOrder? order = null) =>
        WorldState.Create(new(190, 1, 1), 42, [new(1, 100), new(2, 100)],
            [new(1, 1, 5000, 100, 100, 80, 500, 0, 100, 0, 100), new(2, 2, 5000, 100, 100, 80, 500, 0, 100, 0, 100)],
            [new(11, 1, null, new(80, 80, 80, 80), false) { UnitId = 1, Personality = personality },
             new(22, 2, null, new(80, 80, 80, 80), false) { UnitId = 2, Personality = 3 }],
            [new(1, 1, new(20, 10), 5000) { FormationId = 1, CommanderId = 11, ServicePlaceId = 1, Morale = 80,
                Policies = policies ?? new(), Order = order ?? Mission, MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(11, BattleRank.Front) },
             new(2, 2, new(25, 10), 5000) { FormationId = 1, CommanderId = 22, ServicePlaceId = 2, Morale = 80,
                MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(22, BattleRank.Front) }],
            controls: [new("player", 1, ControllerKind.Player)], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, null, new(10000, 10000)) { IsRoot = true }]);

    [Theory]
    [InlineData(true, 0, 0, true)]
    [InlineData(true, 1, 0, true)]
    [InlineData(true, 2, 75, false)]
    [InlineData(true, 2, 76, true)]
    [InlineData(true, 3, 100, false)]
    [InlineData(true, 3, 101, true)]
    [InlineData(false, 0, 99, false)]
    [InlineData(false, 0, 100, true)]
    [InlineData(false, 1, 124, false)]
    [InlineData(false, 1, 125, true)]
    [InlineData(false, 2, 1000, false)]
    [InlineData(false, 3, 1000, false)]
    public void AttackUsesPersonalityAndStrictPermittedThreshold(bool permitted, int personality, long ratio, bool expected) =>
        Assert.Equal(expected, UnitPolicyDecisions.Attack(permitted, personality, ratio));

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void RetreatPreservesBothStrictThresholds(int personality)
    {
        int morale = 4 * personality + 25;
        uint soldiers = (uint)(300 * personality + 2000);
        Assert.False(UnitPolicyDecisions.Retreat(true, personality, morale, soldiers, soldiers + 1));
        Assert.True(UnitPolicyDecisions.Retreat(true, personality, morale - 1, soldiers, 0));
        Assert.True(UnitPolicyDecisions.Retreat(true, personality, morale, soldiers - 1, soldiers));
        Assert.False(UnitPolicyDecisions.Retreat(true, personality, morale, soldiers - 1, soldiers - 1));
        if (personality < 2) Assert.False(UnitPolicyDecisions.Retreat(false, personality, 0, 1, 10000));
        else
        {
            morale = 4 * (personality - 2) + 25;
            soldiers = (uint)(500 * (personality + 1));
            Assert.False(UnitPolicyDecisions.Retreat(false, personality, morale, soldiers, soldiers + 1));
            Assert.True(UnitPolicyDecisions.Retreat(false, personality, morale - 1, soldiers, 0));
            Assert.True(UnitPolicyDecisions.Retreat(false, personality, morale, soldiers - 1, soldiers));
        }
    }

    [Fact]
    public void ApproachTemporarilyInterceptsAndTargetRemovalRestoresMission()
    {
        var world = World(policies: new(AttackApproachingEnemies: true));
        var mutation = world.BeginMutation(); var events = new List<DomainEvent>();
        MilitaryPolicies.AdvanceDay(mutation, Rules(), events);
        Assert.Equal(2, mutation.Units[1].EngagementTargetId);
        Assert.Equal(Mission, mutation.Units[1].Order);
        Assert.Equal(Attack(2), mutation.Units[1].EffectiveOrder);
        Assert.Empty(mutation.Simulation.RandomStreams);
        MilitaryPolicies.RemoveTarget(mutation, 2, false, events);
        Assert.Null(mutation.Units[1].EngagementTargetId);
        Assert.Equal(Mission, mutation.Units[1].EffectiveOrder);
        Assert.False(mutation.Units[1].AwaitingPostAction);
    }

    [Fact]
    public void TemporaryTargetDrivesActualMovementAndCombatWithoutReplacingMission()
    {
        var mutation = World(policies: new(AttackApproachingEnemies: true)).BeginMutation();
        mutation.Replace(mutation.Units[2] with { Position = new(22, 10) });
        var events = new List<DomainEvent>(); var rules = Rules();
        MilitaryPolicies.AdvanceDay(mutation, rules, events);
        MilitaryMovement.AdvanceDay(mutation, rules, new([]), PersonnelRulebook.Empty, events);
        Assert.Equal(new WorldPosition(21, 10), mutation.Units[1].Position);
        MilitaryCombat.AdvanceDay(mutation, new() { Military = rules }, events);
        Assert.Contains(events.OfType<BattleDamageEvent>(), e => e.AttackerId == 1 && e.TargetUnitId == 2 && e.SoldierLoss > 0);
        Assert.Equal(Mission, mutation.Units[1].Order);
        Assert.Contains("unit-engagement-changed", CanonicalJson.Text(events));
    }

    [Theory]
    [InlineData(UnitCondition.Confused, false)]
    [InlineData(UnitCondition.Routed, false)]
    [InlineData(UnitCondition.HighSpirits, true)]
    public void OnlyResponsiveConditionsIntercept(UnitCondition condition, bool intercepts)
    {
        var mutation = World(policies: new(AttackApproachingEnemies: true)).BeginMutation();
        mutation.Replace(mutation.Units[1] with { Condition = condition });
        MilitaryPolicies.AdvanceDay(mutation, Rules(), []);
        Assert.Equal(intercepts, mutation.Units[1].EngagementTargetId is not null);
    }

    [Theory]
    [InlineData(MilitaryOrderKind.Transport)]
    [InlineData(MilitaryOrderKind.Construct)]
    public void WorkAndTransportDoNotAbandonMissionForApproachingEnemies(MilitaryOrderKind kind)
    {
        var mutation = World(policies: new(AttackApproachingEnemies: true)).BeginMutation();
        var order = kind == MilitaryOrderKind.Transport ? new MilitaryOrder(kind, null, 1) :
            new(kind, null, null) { Construction = new(1, 1, new(30, 10), 11) };
        mutation.Replace(mutation.Units[1] with { Order = order });
        MilitaryPolicies.AdvanceDay(mutation, Rules(), []);
        Assert.Equal(order, mutation.Units[1].EffectiveOrder);
        Assert.Null(mutation.Units[1].EngagementTargetId);
    }

    [Theory]
    [InlineData(true, true, 1, true, false)]
    [InlineData(true, false, 0, true, false)]
    [InlineData(true, false, 1, false, false)]
    [InlineData(false, false, 0, false, false)]
    [InlineData(false, true, 1, false, true)]
    [InlineData(false, true, 0, true, false)]
    public void PursuitDistinguishesPrimaryAndTemporaryTargets(bool permitted, bool main, int personality, bool chasing, bool returning)
    {
        var mutation = World(personality, new(AttackApproachingEnemies: true, Pursue: permitted), main ? Attack(2) : Mission).BeginMutation();
        if (!main) mutation.Replace(mutation.Units[1] with { EngagementTargetId = 2 });
        mutation.Replace(mutation.Units[2] with { Order = new(MilitaryOrderKind.Return, null, 2) });
        MilitaryPolicies.AdvanceDay(mutation, Rules(), []);
        Assert.Equal(chasing, mutation.Units[1].EffectiveOrder?.UnitId == 2);
        Assert.Equal(returning, mutation.Units[1].Order?.Kind == MilitaryOrderKind.Return);
        if (!main && !chasing) Assert.Equal(Mission, mutation.Units[1].EffectiveOrder);
    }

    [Fact]
    public void RetreatRunsBeforePursuitEvenWhenRetreatingTargetHasHigherId()
    {
        var mutation = World(order: Attack(2)).BeginMutation();
        mutation.Replace(mutation.Units[2] with { Order = Attack(1), Morale = 20, Policies = new(AutonomousRetreat: true) });
        MilitaryPolicies.AdvanceDay(mutation, Rules(), []);
        Assert.Equal(MilitaryOrderKind.Return, mutation.Units[2].Order!.Kind);
        Assert.Equal(MilitaryOrderKind.Return, mutation.Units[1].Order!.Kind);
    }

    [Fact]
    public void MissingHomeDoesNotConsumeRetreatOrRandomAndNextDayCanRetry()
    {
        var mutation = World(policies: new(AutonomousRetreat: true)).BeginMutation();
        mutation.Replace(mutation.Units[1] with { Morale = 20, ServicePlaceId = null });
        MilitaryPolicies.AdvanceDay(mutation, Rules(), []);
        Assert.Equal(Mission, mutation.Units[1].Order);
        mutation.Replace(mutation.Units[1] with { ServicePlaceId = 1 });
        MilitaryPolicies.AdvanceDay(mutation, Rules(), []);
        Assert.Equal(MilitaryOrderKind.Return, mutation.Units[1].Order!.Kind);
        Assert.Empty(mutation.Simulation.RandomStreams);
    }

    [Theory]
    [InlineData(true, MilitaryOrderKind.AttackUnit)]
    [InlineData(false, MilitaryOrderKind.Return)]
    public void PostActionIsConsumedOnceAndSelectsARealNewTarget(bool attack, MilitaryOrderKind expected)
    {
        var mutation = World(policies: new(AttackAfterAction: attack)).BeginMutation();
        mutation.Replace(mutation.Units[1] with { Order = null, AwaitingPostAction = true });
        var events = new List<DomainEvent>();
        MilitaryPolicies.ResolveOrders(mutation, Rules(), events);
        Assert.Equal(expected, mutation.Units[1].Order!.Kind);
        Assert.False(mutation.Units[1].AwaitingPostAction);
        Assert.Single(events.OfType<UnitOrderChangedEvent>());
        MilitaryPolicies.ResolveOrders(mutation, Rules(), events);
        Assert.Single(events.OfType<UnitOrderChangedEvent>());
    }

    [Fact]
    public void PublicPolicyChangeValidatesAuthorityAndDoesNotReplaceMission()
    {
        var engine = new SimulationEngine(World(), EconomyRulebook.Empty, PersonnelRulebook.Empty, TreasureRulebook.Empty, military: Rules());
        var command = new SetUnitPoliciesCommand(Guid.NewGuid(), 1, 0, 1, new(true, true, true, true));
        var before = engine.Snapshot.StateHash;
        Assert.False(engine.Execute(new("player"), command with { UnitId = 2 }).Submission.Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
        Assert.True(engine.Execute(new("player"), command).Submission.Accepted);
        Assert.Equal(command.Policies, engine.Snapshot.Units.Single(u => u.Id == 1).Policies);
        Assert.Equal(Mission, engine.Snapshot.Units.Single(u => u.Id == 1).Order);
        Assert.False(engine.Execute(new("player"), command).Submission.Accepted);
    }
}
