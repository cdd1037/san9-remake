using System.Collections.Immutable;
using San9.Application.AI;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed partial class OriginalAiMilitaryTests
{
    private static ScenarioInitialState SupportReliefFixture(int people = 1, bool facility = false)
    {
        var basis = Fixture(people);
        return basis with
        {
            Settlements = [.. basis.Settlements.Select(s => s.Id == 1 && facility ? s with { Kind = SettlementKind.Facility } : s),
                basis.Settlements[0] with { Id = 3, X = 5, Soldiers = 1000 }],
            RegionCityIds = new() { [1] = facility ? 3 : 1, [2] = 2, [3] = facility ? 3 : 1 },
            CityConnections = facility ? [new(3, 2), new(2, 3)] : basis.CityConnections,
            RegionNameKeys = new() { [1] = "one", [2] = "two", [3] = "relief" },
            Military = basis.Military! with
            {
                Facilities = [new(700, 1000, 100, new(1, 1), 0, true, true, true, 100000, null, null, null, [])],
                Places = basis.Military.Places.Select(p => p with { FacilityTypeId = 700 }).ToImmutableArray()
                    .Add(new(3, new(27, 7), new(1, 1)) { FacilityTypeId = 700, Combat = new(1, 1, 1, 1, 10) }),
                RegionIds = Enumerable.Range(0, 60).Select(i => (int?)(i % 20 < 2 ? 1 : i % 20 < 10 ? 3 : 2)).ToImmutableArray()
            }
        };
    }

    private static void AssertSupportPublicCommand(ScenarioInitialState content, Harness h, GameCommand command)
    {
        var clone = GameSession.EngineFactory(content)(WorldState.Restore(h.Engine.Snapshot));
        Assert.Equal(CommandRejection.None, clone.GetPreparationEligibility(new("player"), command));
        Assert.True(clone.Execute(new("player"), command).Submission.Accepted);
    }

    [Theory]
    [InlineData(false, 10000u)]
    [InlineData(true, 20000u)]
    public void ReliefSourceTypeSelectsCityLimitOrCommanderCapacity(bool facility, uint soldiers)
    {
        var content = SupportReliefFixture(facility: facility);
        var h = new Harness(content, w => EnemyArmy(w, target: 3));
        AssertSupportPublicCommand(content, h, new DeployUnitCommand(Guid.NewGuid(), 1, h.Engine.Snapshot.Simulation.NextSequence,
            1, [1], soldiers, 400, new(MilitaryOrderKind.Return, null, 3)));
        Assert.Equal(1, h.Run(OriginalAiWork.Deploy, 1).Submitted);
        Assert.Equal(soldiers, Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).Soldiers);
        Assert.Equal(90000u - soldiers, h.Engine.Snapshot.Settlements.Single(s => s.Id == 1).Soldiers);
    }

    [Theory]
    [InlineData(true, 1, 1)]
    [InlineData(true, 4, 2)]
    [InlineData(false, 6, 2)]
    public void DeploymentCanReuseSuccessfulPairWithFreshOfficersAndGarrison(bool relief, int people, int expected)
    {
        var content = relief ? SupportReliefFixture(people) : Fixture(people);
        var h = new Harness(content, w => EnemyArmy(w, target: relief ? 3 : 1));
        Assert.Equal(expected, h.Run(OriginalAiWork.Deploy, 2).Submitted);
        var commands = h.Commands.Cast<DeployUnitCommand>().ToArray();
        Assert.Equal(expected, commands.Length);
        Assert.All(commands, command =>
        {
            Assert.Equal(1, command.SettlementId);
            Assert.Equal(relief ? 3 : null, command.Order.SettlementId);
            Assert.Equal(relief ? null : 900, command.Order.UnitId);
        });
        var officers = commands.SelectMany(c => c.OfficerIds).ToArray();
        Assert.Equal(officers.Length, officers.Distinct().Count());
        Assert.Equal(90000u - (uint)commands.Sum(c => (long)c.Soldiers), h.Engine.Snapshot.Settlements.Single(s => s.Id == 1).Soldiers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedDeploymentPairStopsAtAttemptBudgetWithoutReservingResources(bool relief)
    {
        var content = relief ? SupportReliefFixture(4) : Fixture(6);
        var h = new Harness(content, w => EnemyArmy(w, target: relief ? 3 : 1), reject: true);
        var before = h.Engine.Snapshot;
        var report = h.Run(OriginalAiWork.Deploy, 2);
        Assert.Equal(2, report.Attempts);
        Assert.Equal(2, report.Rejected);
        Assert.Equal(0, report.Submitted);
        Assert.Equal(2, h.Commands.Count);
        Assert.Same(before, h.Engine.Snapshot);
    }

    [Theory]
    [InlineData(1000u, 1)]
    [InlineData(100000u, 0)]
    public void ReliefSourceRetainsTroopsAgainstDistantIncomingAttacks(uint incoming, int expected)
    {
        var basis = Fixture(4);
        var content = basis with
        {
            Settlements = [.. basis.Settlements, basis.Settlements[0] with { Id = 3, X = 5, Y = 3, Soldiers = 1000 }],
            CityConnections = [new(1, 2), new(2, 1), new(1, 3), new(3, 1), new(2, 3), new(3, 2)],
            RegionCityIds = new() { [1] = 1, [2] = 2, [3] = 3 },
            RegionNameKeys = new() { [1] = "one", [2] = "two", [3] = "relief" },
            Military = basis.Military! with
            {
                Width = 100, Height = 5,
                TerrainIds = Enumerable.Repeat(0, 500).ToImmutableArray(),
                TerritoryExits = Enumerable.Repeat(TerritoryExits.All, 500).ToImmutableArray(),
                Formations = basis.Military.Formations.Add(basis.Military.Formations[0] with { Id = 778, MovementBudget = 1, SoldierCapacity = 200000 }),
                Places = basis.Military.Places.Add(new(3, new(27, 17), new(1, 1)) { Combat = new(1, 1, 1, 1, 10) }),
                RegionIds = Enumerable.Range(0, 500).Select(i => (int?)(i % 100 < 4 ? 1 : i % 100 < 10 && i / 100 >= 2 ? 3 : 2)).ToImmutableArray()
            }
        };
        var h = new Harness(content, world => world with
        {
            Units = [new(900, 2, new(37, 17), 20000) { FormationId = 400, ServicePlaceId = 2, Morale = 100,
                    Order = new(MilitaryOrderKind.AttackSettlement, null, 3) },
                new(901, 2, new(367, 7), incoming) { FormationId = 778, ServicePlaceId = 2, Morale = 100,
                    Order = new(MilitaryOrderKind.AttackSettlement, null, 1) }],
            Simulation = world.Simulation with { NextUnitId = 902 }
        });
        AssertSupportPublicCommand(content, h, new DeployUnitCommand(Guid.NewGuid(), 1, h.Engine.Snapshot.Simulation.NextSequence,
            1, [1], 10000, 400, new(MilitaryOrderKind.Return, null, 3)));
        Assert.Equal(expected, h.Run(OriginalAiWork.Deploy, 1).Submitted);
        Assert.Equal(expected == 0 ? 90000u : 80000u, h.Engine.Snapshot.Settlements.Single(s => s.Id == 1).Soldiers);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SinglePersonFallbackSelectsZeroProficiencyCategoryWithoutRandom(int proficiency)
    {
        var basis = SupportReliefFixture();
        var content = basis with
        {
            Officers = basis.Officers.Select(p => p.State.OwnerId == 1 ? p with { State = p.State with
                { LearnedTactics = [7000, 7001], TacticProficiencies = [proficiency, 0, 0, 0, 0, 0, 0, 0, 0] } } : p).ToArray(),
            Military = basis.Military! with { Tactics = [new(7000, 0, TacticEffect.Martial, 10, 1, 0, true, false, [400]),
                new(7001, 0, TacticEffect.Martial, 20, 1, 0, true, false, [400])] }
        };
        var h = new Harness(content, w => EnemyArmy(w, target: 3), _ => throw new InvalidOperationException("Fallback must not draw"));
        Assert.Equal(1, h.Run(OriginalAiWork.Deploy, 1).Submitted);
        var command = Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands));
        Assert.Equal(7001, command.Tactics[Assert.Single(command.OfficerIds)]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SinglePersonFallbackUsesBasicShipOnEveryRoute(bool slow)
    {
        var basis = SupportReliefFixture();
        var land = basis.Military!.Formations[0];
        var content = basis with
        {
            Military = basis.Military with
            {
                BasicNavalFormationId = 800,
                Formations = basis.Military.Formations.Add(land with { Id = 800, TravelMode = TravelMode.Water })
                    .Add(land with { Id = 801, TravelMode = TravelMode.Water })
            },
            OriginalAi = basis.OriginalAi with { Military = basis.OriginalAi.Military with
                { SlowTerrainIds = slow ? [0] : [], NavalPriority = [801] } }
        };
        var h = new Harness(content, w => EnemyArmy(w, target: 3));
        Assert.Equal(1, h.Run(OriginalAiWork.Deploy, 1).Submitted);
        Assert.Equal(800, Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).NavalFormationId);
    }

    [Theory]
    [InlineData(MilitaryOrderKind.Move, 1)]
    [InlineData(MilitaryOrderKind.AttackSettlement, 1)]
    [InlineData(MilitaryOrderKind.AttackUnit, 0)]
    public void BarbarianRecallHandlesEveryMarchTargetKind(MilitaryOrderKind kind, int expected)
    {
        var content = Fixture();
        var order = kind switch
        {
            MilitaryOrderKind.Move => new MilitaryOrder(kind, new(47, 7), null),
            MilitaryOrderKind.AttackUnit => new MilitaryOrder(kind, null, null) { UnitId = 902 },
            _ => new MilitaryOrder(kind, null, 2)
        };
        var h = new Harness(content, w => w with
        {
            Forces = w.Forces.Select(f => f.Id == 1 ? f with { IsBarbarian = true } : f).ToArray(),
            Officers = w.Officers.Select(p => p.Id == 2 ? p with { SettlementId = null, UnitId = 901 } : p).ToArray(),
            Units = [new(901, 1, new(17, 7), 5000) { FormationId = 400, CommanderId = 2, Morale = 100,
                CorpsId = 1, ServicePlaceId = 1, Order = order,
                MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(2, BattleRank.Middle) },
                new(902, 2, new(47, 7), 5000) { FormationId = 400, ServicePlaceId = 2, Morale = 100,
                    Order = new(MilitaryOrderKind.Move, new(47, 7), null) }],
            Simulation = w.Simulation with { NextUnitId = 903 }
        });
        AssertSupportPublicCommand(content, h, new OrderUnitCommand(Guid.NewGuid(), 1, h.Engine.Snapshot.Simulation.NextSequence,
            901, new(MilitaryOrderKind.Return, null, 1)));
        Assert.Equal(expected, h.Run(OriginalAiWork.Retreat, 5).Submitted);
    }

    [Theory]
    [InlineData(UnitCondition.Normal, 0)]
    [InlineData(UnitCondition.Engaged, 0)]
    [InlineData(UnitCondition.Confused, 1)]
    [InlineData(UnitCondition.Trap, 1)]
    [InlineData(UnitCondition.HighSpirits, 0)]
    public void RetreatBudgetOnlyCountsEligibleReturningConditions(UnitCondition condition, int expected)
    {
        var basis = Fixture();
        var content = basis with { Settlements = basis.Settlements.Select(s => s.Id == 1 ? s with { Soldiers = 5000 } : s).ToArray() };
        var h = new Harness(content, w =>
        {
            var enemy = EnemyArmy(w);
            return enemy with
            {
                Officers = enemy.Officers.Select(p => p.Id is 2 or 3 ? p with { SettlementId = null, UnitId = p.Id + 900 } : p).ToArray(),
                Units = [.. enemy.Units.Select(u => u with { Position = new(37, 7) }),
                    new(902, 1, new(17, 7), 5000) { FormationId = 400, CommanderId = 2, Morale = 100,
                        CorpsId = 1, ServicePlaceId = 1, Condition = condition,
                        ConditionDaysRemaining = condition is UnitCondition.Normal or UnitCondition.Engaged ? 0 : 5,
                        Order = new(MilitaryOrderKind.Return, null, 1),
                        MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(2, BattleRank.Middle) },
                    new(903, 1, new(22, 7), 5000) { FormationId = 400, CommanderId = 3, Morale = 100,
                        CorpsId = 1, Order = new(MilitaryOrderKind.Move, new(47, 7), null),
                        MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(3, BattleRank.Middle) }],
                Simulation = enemy.Simulation with { NextUnitId = 904 }
            };
        });
        AssertSupportPublicCommand(content, h, new OrderUnitCommand(Guid.NewGuid(), 1, h.Engine.Snapshot.Simulation.NextSequence,
            903, new(MilitaryOrderKind.Return, null, 1)));
        Assert.Equal(expected, h.Run(OriginalAiWork.Retreat, 5).Submitted);
        if (expected == 1) Assert.Equal(903, Assert.IsType<OrderUnitCommand>(Assert.Single(h.Commands)).UnitId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetreatResolvesMainBuildingWhenCurrentTargetIsAFieldUnit(bool temporary)
    {
        var basis = Fixture();
        var content = basis with { Settlements = basis.Settlements.Select(s => s.Id == 1 ? s with { Soldiers = 5000 } : s).ToArray() };
        var h = new Harness(content, w =>
        {
            var enemy = EnemyArmy(w);
            return enemy with
            {
                Officers = enemy.Officers.Select(p => p.Id == 2 ? p with { SettlementId = null, UnitId = 902 } : p).ToArray(),
                Units = [.. enemy.Units.Select(u => u with { Position = new(37, 7) }),
                    new(902, 1, new(17, 7), 5000) { FormationId = 400, CommanderId = 2, Morale = 100,
                        CorpsId = 1, ServicePlaceId = 1, EngagementTargetId = temporary ? 910 : null,
                        Order = new(MilitaryOrderKind.Return, null, 1),
                        MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(2, BattleRank.Middle) },
                    new(910, 2, new(18, 7), 5000) { FormationId = 400, ServicePlaceId = 2, Morale = 100,
                        Order = new(MilitaryOrderKind.Move, new(47, 7), null) }],
                Simulation = enemy.Simulation with { NextUnitId = 911 }
            };
        });
        Assert.Equal(0, h.Run(OriginalAiWork.Retreat, 5).Submitted);
        Assert.Empty(h.Commands);
        Assert.Equal(temporary ? 910 : null, h.Engine.Snapshot.Units.Single(u => u.Id == 902).EngagementTargetId);
    }
}
