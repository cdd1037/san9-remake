using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed partial class UnitPolicyTests
{
    [Fact]
    public void ConfusionExpiringTodayCannotRespondToTodaysFreshRetreat()
    {
        var snapshot = World(0, new(), Attack(2)).CreateSnapshot();
        snapshot = snapshot with
        {
            Simulation = snapshot.Simulation with { Boundary = SimulationBoundary.Stable, ActiveControl = snapshot.Simulation.Controls.Length },
            Units = snapshot.Units.Select(u => u.Id == 1
                ? u with { Condition = UnitCondition.Confused, ConditionDaysRemaining = 1 }
                : u with { Order = Attack(1), Morale = 20, Policies = new(AutonomousRetreat: true) }).ToArray()
        };
        var engine = new SimulationEngine(WorldState.Restore(snapshot), EconomyRulebook.Empty,
            PersonnelRulebook.Empty, TreasureRulebook.Empty, military: Rules());
        var result = engine.AdvanceDay();
        Assert.Contains(result.Events.OfType<UnitOrderChangedEvent>(), e => e.UnitId == 2 && e.Reason == "autonomous-retreat");
        Assert.DoesNotContain(result.Events.OfType<UnitPolicyRespondedEvent>(), e => e.Unit.Id == 1);
        var source = result.Snapshot.Units.Single(u => u.Id == 1);
        Assert.Equal(UnitCondition.Normal, source.Condition);
        Assert.Equal(Attack(2), source.Order);
        Assert.Equal(new WorldPosition(20, 10), source.Position);
    }

    [Fact]
    public void HighSpiritsExpiryCannotCauseRetreatBeforeTodaysMovement()
    {
        var snapshot = World(policies: new(AutonomousRetreat: true)).CreateSnapshot();
        snapshot = snapshot with
        {
            Simulation = snapshot.Simulation with { Boundary = SimulationBoundary.Stable, ActiveControl = snapshot.Simulation.Controls.Length },
            Units = snapshot.Units.Select(u => u.Id == 1
                ? u with { Condition = UnitCondition.HighSpirits, ConditionDaysRemaining = 1,
                    HighSpiritsMoralePenaltyPending = true, Morale = 50, OrderClass = UnitOrderClass.Military }
                : u with { Position = new(80, 10) }).ToArray()
        };
        var engine = new SimulationEngine(WorldState.Restore(snapshot), EconomyRulebook.Empty,
            PersonnelRulebook.Empty, TreasureRulebook.Empty, military: Rules());
        var first = engine.AdvanceDay();
        Assert.DoesNotContain(first.Events.OfType<UnitOrderChangedEvent>(), e => e.UnitId == 1 && e.Reason == "autonomous-retreat");
        Assert.InRange(first.Snapshot.Units.Single(u => u.Id == 1).Morale, 1, 28);
        Assert.Contains(first.Events, e => e is HighSpiritsExpiredEvent { UnitId: 1 });
        var second = engine.AdvanceDay();
        Assert.Contains(second.Events.OfType<UnitOrderChangedEvent>(), e => e.UnitId == 1 && e.Reason == "autonomous-retreat");
    }

    [Theory]
    [InlineData(25)]
    [InlineData(35)]
    public void RecklessPursuitDoesNotReverseItsDecisionWithTheRetreatingTargetsDistance(int targetX)
    {
        var snapshot = World(0, new(), Attack(2)).CreateSnapshot();
        snapshot = snapshot with
        {
            Simulation = snapshot.Simulation with { Boundary = SimulationBoundary.Stable, ActiveControl = snapshot.Simulation.Controls.Length },
            Units = snapshot.Units.Select(u => u.Id == 2 ? u with
                { Position = new(targetX, 10), Order = Attack(1), Morale = 20, Policies = new(AutonomousRetreat: true) } : u).ToArray()
        };
        var engine = new SimulationEngine(WorldState.Restore(snapshot), EconomyRulebook.Empty,
            PersonnelRulebook.Empty, TreasureRulebook.Empty, military: Rules());
        var result = engine.AdvanceDay();
        Assert.Contains(result.Events.OfType<UnitOrderChangedEvent>(), e => e.UnitId == 2 && e.Reason == "autonomous-retreat");
        Assert.Equal(Attack(2), result.Snapshot.Units.Single(u => u.Id == 1).Order);
        Assert.Contains(result.Events.OfType<UnitPolicyRespondedEvent>(), e => e.Unit.Id == 1 &&
            e.Response == UnitPolicyResponse.PursueDespiteOrders);
    }

    [Fact]
    public void DailyMovementCannotReopenAnAlreadyConsumedPursuitResponse()
    {
        var snapshot = World(0, new(), Attack(2)).CreateSnapshot();
        var third = snapshot.Units.Single(u => u.Id == 2) with
        {
            Id = 3, Position = new(38, 18), CommanderId = 33,
            MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(33, BattleRank.Front),
            Order = new(MilitaryOrderKind.Move, new(20, 5), null)
        };
        snapshot = snapshot with
        {
            Simulation = snapshot.Simulation with
                { Boundary = SimulationBoundary.Stable, ActiveControl = snapshot.Simulation.Controls.Length, NextUnitId = 4 },
            Units = snapshot.Units.Select(u => u.Id == 2 ? u with
                { Position = new(35, 10), Order = Attack(1), Morale = 20, Policies = new(AutonomousRetreat: true) } : u).Append(third).ToArray(),
            Officers = snapshot.Officers.Append(snapshot.Officers.Single(o => o.Id == 22) with { Id = 33, UnitId = 3 }).ToArray()
        };
        var engine = new SimulationEngine(WorldState.Restore(snapshot), EconomyRulebook.Empty,
            PersonnelRulebook.Empty, TreasureRulebook.Empty, military: Rules());
        var timeline = new List<string>();
        var first = engine.AdvanceDay();
        Assert.Contains(first.Events.OfType<UnitOrderChangedEvent>(), e => e.UnitId == 2 && e.Reason == "autonomous-retreat");
        Assert.Equal(MilitaryOrderKind.AttackUnit, first.Snapshot.Units.Single(u => u.Id == 1).Order!.Kind);
        timeline.Add($"{first.Snapshot.Clock}: target began autonomous return; source continues attack");
        for (int day = 0; day < 9; day++)
        {
            var result = engine.AdvanceDay();
            timeline.Add($"{result.Snapshot.Clock}: " + string.Join("; ", result.Snapshot.Units.Select(u => $"u{u.Id}@{u.Position} {u.Order?.Kind}")));
            Assert.DoesNotContain(result.Events.OfType<UnitOrderChangedEvent>(), e => e.UnitId == 2 && e.Reason == "autonomous-retreat");
            Assert.True(result.Snapshot.Units.Single(u => u.Id == 1).Order!.Kind == MilitaryOrderKind.AttackUnit,
                string.Join("\n", timeline) + "\n" + string.Join("; ", result.Events.OfType<UnitOrderChangedEvent>().Select(e => $"u{e.UnitId} {e.Reason}")));
        }
    }
}
