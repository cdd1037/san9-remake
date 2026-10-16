using System.Collections.Immutable;
using San9.Application.AI;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Application.Tests;

public sealed class OriginalAiMilitaryTests
{
    private static ScenarioInitialState Fixture(int people = 12)
    {
        var basis = G01ScenarioTests.Fixture();
        return basis with
        {
            Settlements = [basis.Settlements[0] with { Soldiers = 90000, Morale = 100, DraftPopulation = 100000 },
                basis.Settlements[1] with { X = 13, Y = 1, Soldiers = 1000, Morale = 100 }],
            Officers = Enumerable.Range(1, people).Select(id => new OfficerInitialState($"person.{id}",
                new OfficerState(id, 1, 1, new(90, 80, 70, 95), false) { Status = id == 1 ? OfficerStatus.Ruler : OfficerStatus.Serving,
                    BirthYear = 160 }.WithLoyalty(1, 100))).Append(new("enemy", new(100, 2, 2, new(30, 30, 30, 30), false)
                    { Status = OfficerStatus.Ruler, BirthYear = 160 })).ToArray(),
            Forces = basis.Forces.Select(f => f.Id == 2 ? f with { LeaderId = 100 } : f).ToArray(),
            Information = basis.Information with { Corps = basis.Information.Corps.Select(c => c with
                { LeaderId = c.Id == 1 ? 1 : 100, Treasury = new(100000, 100000) }).ToArray(), Relations = [new(1, 2, 0)] },
            CityConnections = [new(1, 2), new(2, 1)], RegionCityIds = new() { [1] = 1, [2] = 2 },
            RegionNameKeys = new() { [1] = "region.1", [2] = "region.2" },
            Military = new(20, 3, Enumerable.Repeat(0, 60).ToImmutableArray(), [new(0, 1, null)],
                [new(400, 10, 100, 30000, 5, new(1, 1), TravelMode.Land)
                    { Combat = new(30, 300, 300, 30, 10), Attack = new(1, true),
                        CaptureRanks = Enumerable.Repeat(BattleRank.Middle, 5).ToImmutableArray() },
                 new(500, 10, 0, 30000, 5, new(1, 1), TravelMode.Land)],
                [new(1, new(7, 7), new(1, 1)) { Combat = new(1, 1, 1, 1, 10) },
                 new(2, new(67, 7), new(1, 1)) { Combat = new(1, 1, 1, 1, 10) }])
                    { TransportFormations = new(500, null),
                        RegionIds = Enumerable.Range(0, 60).Select(i => (int?)(i % 20 < 10 ? 1 : 2)).ToImmutableArray(),
                        TerritoryExits = Enumerable.Repeat(TerritoryExits.All, 60).ToImmutableArray() },
            OriginalAi = OriginalAiDefinition.Default with { Advanced = true,
                Military = new() { Formations = new Dictionary<OriginalAiFormationRole, int>
                    { [OriginalAiFormationRole.March] = 400, [OriginalAiFormationRole.Defense] = 400 }.ToImmutableDictionary() },
                WorkRules = OriginalAiDefinition.Default.WorkRules.ToImmutableDictionary(p => p.Key, p => p.Value with { OpeningWaitDays = 0, PeriodDays = 0 }) }
        };
    }

    private sealed class Harness
    {
        internal SimulationEngine Engine { get; }
        internal OriginalAiMilitary Military { get; }
        internal OriginalAiState State { get; }
        internal List<GameCommand> Commands { get; } = [];
        internal Harness(ScenarioInitialState content, Func<WorldSnapshot, WorldSnapshot>? change = null, Func<int, int>? draw = null, bool reject = false,
            Func<GameCommand, bool>? rejectCommand = null)
        {
            var world = new ScenarioSessionDefinition(content).CreateWorld(190).CreateSnapshot();
            world = world with { Simulation = world.Simulation with { Boundary = SimulationBoundary.Stable } };
            Engine = GameSession.EngineFactory(content)(WorldState.Restore(change?.Invoke(world) ?? world));
            var context = new CommandContext("player");
            Military = new(Engine, content, new(content.Military!), 1, context, new OriginalAiRandom(draw ?? (_ => 0)), command =>
            {
                Commands.Add(command);
                if (reject || rejectCommand?.Invoke(command) == true) return false;
                var result = Engine.Execute(context, command);
                Assert.True(result.Submission.Accepted, $"{command}: {result.Submission.Rejection}");
                return true;
            });
            State = OriginalAiState.Create(content) with { Corps = [new(1, OriginalAiMode.TargetForce, 2, null)] };
        }
        internal OriginalAiWorkReport Run(OriginalAiWork work, int limit = 20) => Military.Run(work, limit, State, State.Corps[0]);
    }

    [Theory]
    [InlineData(false, 600)]
    [InlineData(true, 400)]
    public void DormantFacilitiesDoNotTriggerSiegeEquipment(bool dormant, int formation)
    {
        var content = Fixture();
        content = content with
        {
            Settlements = content.Settlements.Append(content.Settlements[1] with
                { Id = 3, OwnerId = null, X = 7, Soldiers = 0, Morale = 0, Kind = SettlementKind.Facility }).ToArray(),
            RegionCityIds = new() { [1] = 1, [2] = 2, [3] = 1 },
            RegionNameKeys = new() { [1] = "region.1", [2] = "region.2", [3] = "region.3" },
            Military = content.Military! with
            {
                Formations = content.Military.Formations.Add(content.Military.Formations[0] with
                    { Id = 600, Combat = new(1, 1, 1, 1, 10) }),
                Facilities = [new(700, 1000, 100, new(1, 1), 0, true, true, true, 30000, null, null, null, []),
                    new(701, 1000, 100, new(1, 1), 0, true, true, true, 30000, null, null, null, [])],
                Places = content.Military.Places.Select(p => p with { FacilityTypeId = 701 }).ToImmutableArray()
                    .Add(new(3, new(37, 7), new(1, 1)) { FacilityTypeId = 700 }),
                RegionIds = Enumerable.Range(0, 60).Select(i => (int?)(i % 20 < 5 ? 1 : i % 20 < 10 ? 3 : 2)).ToImmutableArray()
            },
            OriginalAi = content.OriginalAi with { Military = content.OriginalAi.Military with
                { RouteObstacleFacilityIds = [700], Formations = content.OriginalAi.Military.Formations.Add(OriginalAiFormationRole.Ram, 600) } }
        };
        var h = new Harness(content, w => w with { Settlements = w.Settlements.Select(s => s.Id == 3 ? s with { IsDormant = dormant } : s).ToArray() });
        Assert.Equal(1, h.Run(OriginalAiWork.Attack).Submitted);
        Assert.Equal(formation, Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).FormationId);
    }

    [Fact]
    public void AttackUsesAuthoredFormationReferencesAndActuallyRemovesTroopsAndMembers()
    {
        var h = new Harness(Fixture());
        var report = h.Run(OriginalAiWork.Attack);
        var command = Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands));
        Assert.Equal(1, report.Submitted);
        Assert.Equal(400, command.FormationId);
        Assert.Equal(20000u, command.Soldiers);
        Assert.Equal(70000u, h.Engine.Snapshot.Settlements.Single(s => s.Id == 1).Soldiers);
        Assert.Equal(new UnitPolicies(false, false, true, true), command.Policies);
        Assert.All(command.OfficerIds, id => Assert.NotNull(h.Engine.Snapshot.Officers.Single(p => p.Id == id).UnitId));
    }

    [Fact]
    public void AttackDoesNotUseTheTransportFrontierReserve()
    {
        var content = Fixture();
        content = content with { Settlements = content.Settlements.Select(s => s.Id == 1 ? s with { Soldiers = 20000 } : s).ToArray() };
        var h = new Harness(content);
        Assert.Equal(1, h.Run(OriginalAiWork.Attack).Submitted);
        Assert.Equal(10000u, Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).Soldiers);
        Assert.Equal(10000u, h.Engine.Snapshot.Settlements.Single(s => s.Id == 1).Soldiers);
    }

    [Fact]
    public void RepeatedSourceAllocationsReadActualPeopleAndFundsAfterEachSubmission()
    {
        var content = Fixture(20);
        content = content with { Settlements = content.Settlements.Select(s => s.Id == 2 ? s with { Soldiers = 15000, Durability = 200, MaxDurability = 200 } : s).ToArray() };
        var h = new Harness(content);
        h.Run(OriginalAiWork.Attack);
        var commands = h.Commands.Cast<DeployUnitCommand>().ToArray();
        Assert.Equal(3, commands.Length); // 20000 + 10000 is equal; the third allocation strictly exceeds demand.
        Assert.All(commands, c => Assert.Equal(1, c.SettlementId));
        Assert.Equal(commands.SelectMany(c => c.OfficerIds).Count(), commands.SelectMany(c => c.OfficerIds).Distinct().Count());
        Assert.Equal(50000u, h.Engine.Snapshot.Settlements.Single(s => s.Id == 1).Soldiers);
    }

    [Theory]
    [InlineData(true, 9999, false)]
    [InlineData(true, 10000, true)]
    [InlineData(true, 10001, true)]
    [InlineData(false, 4999, false)]
    [InlineData(false, 5000, true)]
    [InlineData(false, 5001, true)]
    public void FinalCapacityCannotBypassTheSourceMinimum(bool city, uint capacity, bool expected)
    {
        var content = Fixture();
        content = content with
        {
            Settlements = content.Settlements.Select(s => s.Id == 1 && !city ? s with { Kind = SettlementKind.Facility } : s).ToArray(),
            RegionCityIds = city ? content.RegionCityIds : new() { [1] = 2, [2] = 2 },
            CityConnections = city ? content.CityConnections : [],
            Military = content.Military! with { Formations = content.Military.Formations.Select(f =>
                f.Id == 400 ? f with { SoldierCapacity = capacity } : f).ToImmutableArray() }
        };
        var h = new Harness(content);
        Assert.Equal(expected ? 1 : 0, h.Run(OriginalAiWork.Attack).Submitted);
    }

    [Fact]
    public void UnreachableRouteDoesNotBecomeAZeroDayAttack()
    {
        var content = Fixture();
        content = content with { Military = content.Military! with
        {
            TerrainIds = Enumerable.Range(0, 60).Select(i => i % 20 == 8 ? 1 : 0).ToImmutableArray(),
            Terrains = [new(0, 1, null), new(1, null, null)]
        } };
        var h = new Harness(content);
        Assert.Equal(0, h.Run(OriginalAiWork.Attack).Submitted);
        Assert.Empty(h.Commands);
    }

    [Fact]
    public void RefusedAttackConsumesFiniteAllocationsWithoutReservingActualResources()
    {
        var h = new Harness(Fixture(), reject: true);
        var before = h.Engine.Snapshot;
        var report = h.Run(OriginalAiWork.Attack);
        Assert.Equal(1, report.Rejected);
        StateAssert.Equal(before, h.Engine.Snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(true, 1000)]
    [InlineData(false, 1000)]
    public void FulfillmentContinuesWithSecondDistinctSourceAndPreservesBothMinimums(bool refuseFirst, uint targetSoldiers = 15000)
    {
        var content = Fixture(20);
        content = content with
        {
            Forces = content.Forces.Append(new(3, "requester", 3, 50) { LeaderId = 101 }).ToArray(),
            Settlements = content.Settlements.Select(s => s.Id == 2 ? s with { Soldiers = targetSoldiers } : s)
                .Append(content.Settlements[0] with { Id = 3, X = 3 }).Append(content.Settlements[1] with { Id = 4, X = 17, OwnerId = 3 }).ToArray(),
            Officers = content.Officers.Select(p => p.State.Id is >= 11 and <= 20 ? p with { State = p.State with { SettlementId = 3 } } : p)
                .Append(new("requester", new(101, 3, 4, new(30, 30, 30, 30), false) { Status = OfficerStatus.Ruler })).ToArray(),
            Information = content.Information with { Corps = content.Information.Corps.Append(new(3, 3, 101, new(1000, 1000)) { IsRoot = true }).ToArray() },
            RegionCityIds = new() { [1] = 1, [2] = 2, [3] = 1, [4] = 4 },
            RegionNameKeys = new() { [1] = "region.1", [2] = "region.2", [3] = "region.3", [4] = "region.4" },
            Military = content.Military! with { Places = content.Military.Places.Add(new(3, new(17, 7), new(1, 1)))
                .Add(new(4, new(87, 7), new(1, 1))) }
        };
        var h = new Harness(content, w => w with { Simulation = w.Simulation with { AttackRequests = [new(1, 3, 2, 2, 19)] } },
            rejectCommand: c => refuseFirst && c is DeployUnitCommand { SettlementId: 1 });
        Assert.Equal(refuseFirst || targetSoldiers == 1000 ? 1 : 2, h.Run(OriginalAiWork.FulfillRequest).Submitted);
        Assert.Equal(!refuseFirst && targetSoldiers == 1000 ? new[] { 1 } : new[] { 1, 3 },
            h.Commands.Cast<DeployUnitCommand>().Select(c => c.SettlementId));
    }

    [Fact]
    public void ActualAutomaticCampaignCapturesAndSurvivesRestoreAndRecordingReplay()
    {
        var content = Fixture();
        // Isolate the military chain from personnel reassignment and unrelated random events.
        content = content with
        {
            Settlements = content.Settlements.Select(s => s.Id == 1 ? s with { Soldiers = 9000 } : s).ToArray(),
            OriginalAi = content.OriginalAi with { SecondaryBatches = content.OriginalAi.SecondaryBatches
                .ToImmutableDictionary(p => p.Key, _ => ImmutableArray.Create(OriginalAiWork.Attack, OriginalAiWork.Recruit,
                    OriginalAiWork.Train, OriginalAiWork.Commerce, OriginalAiWork.Cultivation)) }
        };
        var session = GameSession.Start(content, 190, playerForceController: ControllerKind.Automatic);
        session.BeginRecording();
        bool captured = false;
        var history = new List<DomainEvent>();
        for (int turn = 0; turn < 12 && !captured; turn++)
        {
            var restored = SaveService.Decode(SaveService.Encode(session), content);
            var events = session.AdvanceXun().Events;
            history.AddRange(events);
            EventAssert.Equal(events, restored.AdvanceXun().Events);
            StateAssert.Equal(session.Capture(), restored.Capture());
            captured = session.Snapshot.Settlements.Single(s => s.Id == 2).OwnerId == 1;
        }
        Assert.True(captured);
        int firstDeployment = history.FindIndex(e => e is UnitDeployedEvent { SettlementId: 1 });
        Assert.True(firstDeployment > 0);
        Assert.Contains(history.Take(firstDeployment), e => e is DevelopmentCompletedEvent { Command.Activity: DomesticActivity.Recruit });
        Assert.Contains(history.Take(firstDeployment), e => e is DevelopmentCompletedEvent { Command.Activity: DomesticActivity.Commerce or DomesticActivity.Cultivation });
        var recording = session.EndRecording();
        var replay = RecordingService.Replay(RecordingService.Encode(recording), content, GameSession.EngineFactory(content), verifyEvents: true);
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    private static WorldSnapshot EnemyArmy(WorldSnapshot world, int wait = 0, int target = 1) => world with
    {
        Officers = world.Officers.Select(p => p.Id == 100 ? p with { SettlementId = null, UnitId = 900 } : p).ToImmutableArray(),
        Units = [new(900, 2, new(57, 7), 20000) { FormationId = 400, CommanderId = 100, Morale = 100,
            CorpsId = 2, ServicePlaceId = 2, Order = new(MilitaryOrderKind.AttackSettlement, null, target),
            DepartureDaysRemaining = wait, MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(100, BattleRank.Middle) }],
        Simulation = world.Simulation with { NextUnitId = 901 }
    };

    [Fact]
    public void DeploymentRejectsUnrelatedRegionsBeforeSearchingRoutes()
    {
        var content = Fixture() with { CityConnections = [] };
        var h = new Harness(content, w => EnemyArmy(w) with { Simulation = w.Simulation with
            { OtherForceOrdersVisible = false, NextUnitId = 901 } });
        var report = h.Run(OriginalAiWork.Deploy);
        Assert.Equal(0, report.Submitted);
        Assert.Equal(0, report.PathQueries);
        Assert.Empty(h.Commands);
    }

    [Fact]
    public void DeploymentUsesTargetCombatWaitOnceWithoutChangingForeignOrders()
    {
        var a = new Harness(Fixture(), w => EnemyArmy(w, 8));
        var b = new Harness(Fixture(), w => EnemyArmy(w, 9));
        var target = a.Engine.Snapshot.Units.Single();
        Assert.Equal(1, a.Run(OriginalAiWork.Deploy, 1).Submitted);
        Assert.Equal(1, b.Run(OriginalAiWork.Deploy, 1).Submitted);
        var first = Assert.IsType<DeployUnitCommand>(Assert.Single(a.Commands));
        var second = Assert.IsType<DeployUnitCommand>(Assert.Single(b.Commands));
        Assert.Equal(first.DepartureDelayDays + 1, second.DepartureDelayDays);
        Assert.Equal(target, a.Engine.Snapshot.Units.Single(u => u.Id == 900));
        Assert.Equal(20000u, first.Soldiers);
    }

    [Fact]
    public void DeploymentRejectsFormationCapacityBelowItsOwnMinimum()
    {
        var content = Fixture();
        content = content with { Military = content.Military! with
        {
            Formations = content.Military.Formations.Select(f => f with { SoldierCapacity = 7999 }).ToImmutableArray()
        } };
        var h = new Harness(content, w =>
        {
            var enemy = EnemyArmy(w);
            return enemy with { Units = enemy.Units.Select(u => u with { Soldiers = 7000 }).ToImmutableArray() };
        });
        Assert.Equal(0, h.Run(OriginalAiWork.Deploy, 1).Submitted);
        Assert.Empty(h.Commands);
    }

    [Fact]
    public void HiddenEnemyOrdersDoNotEnterOrderDependentDeploymentGroup()
    {
        var h = new Harness(Fixture(), w => EnemyArmy(w) with { Simulation = w.Simulation with
            { OtherForceOrdersVisible = false, NextUnitId = 901 } });
        // Group 1 can use publicly visible approach, but no group-0 order is read or rewritten.
        var before = h.Engine.Snapshot.Units.Single();
        h.Run(OriginalAiWork.Deploy, 1);
        Assert.Equal(before, h.Engine.Snapshot.Units.Single(u => u.Id == before.Id));
    }

    [Fact]
    public void SinglePersonPressureReliefUsesItsOwnPoliciesAndNeverDrawsRandom()
    {
        var content = Fixture(1);
        content = content with
        {
            Settlements = content.Settlements.Append(content.Settlements[0] with { Id = 3, X = 5, Soldiers = 1000 }).ToArray(),
            RegionCityIds = new() { [1] = 1, [2] = 2, [3] = 1 },
            RegionNameKeys = new() { [1] = "region.1", [2] = "region.2", [3] = "region.3" },
            Military = content.Military! with { Places = content.Military.Places.Add(new(3, new(27, 7), new(1, 1)) { Combat = new(1, 1, 1, 1, 10) }),
                RegionIds = Enumerable.Range(0, 60).Select(i => (int?)(i % 20 < 2 ? 1 : i % 20 < 10 ? 3 : 2)).ToImmutableArray() }
        };
        var h = new Harness(content, w => EnemyArmy(w, target: 3), _ => throw new InvalidOperationException("Unexpected planning draw"));
        Assert.Equal(1, h.Run(OriginalAiWork.Deploy, 1).Submitted);
        var command = Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands));
        Assert.Single(command.OfficerIds);
        Assert.Equal(10000u, command.Soldiers);
        Assert.Equal(new UnitPolicies(false, false, false, true), command.Policies);
        Assert.Equal(0, command.DepartureDelayDays);
        Assert.Equal(3, command.Order.SettlementId);
    }

    [Theory]
    [InlineData(9999, 1)]
    [InlineData(10000, 0)]
    public void BarbarianRetreatFallsBackToMainBuildingWhileEngaged(uint garrison, int recalled)
    {
        var content = Fixture();
        content = content with { Settlements = content.Settlements.Select(s => s.Id == 2 ? s with { Soldiers = garrison } : s).ToArray() };
        var h = new Harness(content, w =>
        {
            var enemy = EnemyArmy(w);
            return enemy with
            {
                Forces = enemy.Forces.Select(f => f.Id == 1 ? f with { IsBarbarian = true } : f).ToImmutableArray(),
                Officers = enemy.Officers.Select(p => p.Id == 2 ? p with { SettlementId = null, UnitId = 901 } : p).ToImmutableArray(),
                Units = enemy.Units.ToImmutableArray().Add(new(901, 1, new(17, 7), 5000) { FormationId = 400, CommanderId = 2, Morale = 100,
                    CorpsId = 1, ServicePlaceId = 1, EngagementTargetId = 900,
                    Order = new(MilitaryOrderKind.AttackSettlement, null, 2),
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(2, BattleRank.Middle) }),
                Simulation = enemy.Simulation with { NextUnitId = 902 }
            };
        });
        Assert.Equal(recalled, h.Run(OriginalAiWork.Retreat).Submitted);
        if (recalled > 0) Assert.Equal(901, Assert.IsType<OrderUnitCommand>(Assert.Single(h.Commands)).UnitId);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(4000, 0)]
    public void RetreatCountsAlreadyReturningActiveAndHalfWoundedWithoutCountingUnitsAsSoldiers(uint wounded, int recalled)
    {
        var content = Fixture();
        content = content with { Settlements = content.Settlements.Select(s => s.Id == 1 ? s with { Soldiers = 5000 } : s).ToArray() };
        var h = new Harness(content, w =>
        {
            var enemy = EnemyArmy(w);
            enemy = enemy with { Units = enemy.Units.Select(u => u with { Position = new(37, 7) }).ToImmutableArray() };
            return enemy with
            {
                Officers = enemy.Officers.Select(p => p.Id is 2 or 3 ? p with { SettlementId = null, UnitId = p.Id + 900 } : p).ToImmutableArray(),
                Units = enemy.Units.ToImmutableArray().Add(new(902, 1, new(17, 7), 4000) { FormationId = 400, CommanderId = 2, Morale = 100,
                    CorpsId = 1, ServicePlaceId = 1, Wounded = wounded, Order = new(MilitaryOrderKind.Return, null, 1),
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(2, BattleRank.Middle) })
                    .Add(new(903, 1, new(22, 7), 5000) { FormationId = 400, CommanderId = 3, Morale = 100, CorpsId = 1,
                        Order = new(MilitaryOrderKind.Move, new(47, 7), null),
                        MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(3, BattleRank.Middle) }),
                Simulation = enemy.Simulation with { NextUnitId = 904 }
            };
        });
        Assert.Equal(recalled, h.Run(OriginalAiWork.Retreat).Submitted);
        if (recalled > 0) Assert.Equal(903, Assert.IsType<OrderUnitCommand>(Assert.Single(h.Commands)).UnitId);
    }

    [Fact]
    public void TransportUsesPositiveRearBudgetAndTheLeastAbleOfficer()
    {
        var content = Fixture();
        content = content with
        {
            Settlements = content.Settlements.Append(content.Settlements[0] with { Id = 3, X = 3, Soldiers = 30000 }).ToArray(),
            Officers = content.Officers.Select(p => p.State.Id is 2 or 3 ? p with { State = p.State with { SettlementId = 3,
                BaseAbilities = p.State.Id == 3 ? new(10, 10, 10, 10) : p.State.BaseAbilities } } : p).ToArray(),
            CityConnections = [new(1, 2), new(2, 1), new(3, 1)],
            RegionCityIds = new() { [1] = 1, [2] = 2, [3] = 3 },
            RegionNameKeys = new() { [1] = "region.1", [2] = "region.2", [3] = "region.3" },
            Military = content.Military! with { Places = content.Military.Places.Add(new(3, new(17, 7), new(1, 1))),
                RegionIds = Enumerable.Range(0, 60).Select(i => (int?)(i % 20 == 3 ? 3 : i % 20 < 10 ? 1 : 2)).ToImmutableArray(),
                TerritoryExits = Enumerable.Repeat(TerritoryExits.All, 60).ToImmutableArray() }
        };
        var h = new Harness(content, draw: _ => throw new InvalidOperationException("Transport must not draw"));
        Assert.Equal(1, h.Run(OriginalAiWork.Transport).Submitted);
        var command = Assert.IsType<TransportUnitCommand>(Assert.Single(h.Commands));
        Assert.Equal(3, command.SettlementId);
        Assert.Equal(1, command.DestinationId);
        Assert.Equal(new[] { 3 }, command.OfficerIds);
        Assert.Equal(10000u, command.Soldiers);
        Assert.Equal(20000u, h.Engine.Snapshot.Settlements.Single(s => s.Id == 3).Soldiers);
    }

    [Fact]
    public void AttackBuildsInAnUnbuiltFriendlyCityRegionThroughThePublicConstructionCommand()
    {
        var content = Fixture();
        content = content with
        {
            Information = content.Information with { Relations = [new(1, 2, 100)] },
            UnbuiltPlaces = [new(3, new(27, 7))],
            RegionCityIds = new() { [1] = 1, [2] = 2, [3] = 1 },
            RegionNameKeys = new() { [1] = "region.1", [2] = "region.2", [3] = "region.3" },
            Military = content.Military! with
            {
                Formations = content.Military.Formations.Add(new(600, 10, 0, 30000, 5, new(1, 1), TravelMode.Land)),
                ConstructionFormations = new(600, null),
                Facilities = [new(700, 1000, 100, new(1, 1), 0, true, true, true, 30000, null, null, null, [])],
                Places = content.Military.Places.Select(p => p with { FacilityTypeId = 700 }).ToImmutableArray(),
                ConstructionSites = [new(3, new(27, 7), [700])]
            },
            OriginalAi = content.OriginalAi with { Military = content.OriginalAi.Military with { OrdinaryFacilityIds = [700] } }
        };
        var h = new Harness(content);
        var report = h.Run(OriginalAiWork.Attack);
        Assert.Equal(1, report.Submitted);
        var command = Assert.IsType<ConstructFacilityCommand>(Assert.Single(h.Commands));
        Assert.Equal(3, command.RegionId);
        Assert.Equal(700, command.FacilityTypeId);
        Assert.Equal(99000u, h.Engine.Snapshot.Corps.Single(c => c.Id == 1).Treasury.Money);
        Assert.Equal(MilitaryOrderKind.Construct, Assert.Single(h.Engine.Snapshot.Units).Order!.Kind);
    }

    [Fact]
    public void RareAttackTacticsDrawOnlyFromTheirOwnSubsetAfterAffinityDraws()
    {
        var content = Fixture();
        int[] ids = [7000, 7001, 7002];
        content = content with
        {
            Officers = content.Officers.Select(p => p.State.OwnerId == 1 ? p with { State = p.State with { LearnedTactics = ids.ToImmutableArray() } } : p).ToArray(),
            Military = content.Military! with { Tactics = new[] { TacticEffect.Sorcery, TacticEffect.Illusion, TacticEffect.Heal }
                .Select((effect, i) => new TacticRule(ids[i], 7, effect, 1, 1, 0, true, false, [400])).ToImmutableArray() }
        };
        var draws = new List<int>();
        var h = new Harness(content, draw: maximum => { draws.Add(maximum); return 0; });
        Assert.Equal(1, h.Run(OriginalAiWork.Attack).Submitted);
        var command = Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands));
        Assert.All(command.Tactics.Values, tactic => Assert.Equal(7000, tactic));
        Assert.Equal(new[] { 100, 100, 100, 100, 3, 3, 3, 3, 3 }, draws);
    }

    [Fact]
    public void PublicRouteTraceDistinguishesAlreadyArrivedFromUnreachable()
    {
        var content = Fixture();
        var h = new Harness(content);
        var unit = new UnitState(0, 1, new(27, 7), 1000) { FormationId = 400, Order = new(MilitaryOrderKind.Move, new(27, 7), null) };
        var arrived = MilitaryRouteQuery.Plan(h.Engine.Snapshot, new(content.Military!), unit, null);
        Assert.Equal(NavigationStatus.Arrived, arrived.Status);
        Assert.Equal(0, arrived.Days);
        content = content with { Military = content.Military! with
            { TerrainIds = Enumerable.Range(0, 60).Select(i => i % 20 == 8 ? 1 : 0).ToImmutableArray(), Terrains = [new(0, 1, null), new(1, null, null)] } };
        var blocked = new Harness(content);
        var unreachable = MilitaryRouteQuery.Plan(blocked.Engine.Snapshot, new(content.Military!), unit with
            { Order = new(MilitaryOrderKind.Move, new(67, 7), null) }, null);
        Assert.Equal(NavigationStatus.Unreachable, unreachable.Status);
        Assert.Null(unreachable.Days);
    }

    [Fact]
    public void EmptyingANonCityTransportSourceUsesSinglePersonCombatFallback()
    {
        var content = Fixture();
        content = content with
        {
            Settlements = content.Settlements.Append(content.Settlements[0] with { Id = 3, X = 3, Soldiers = 5000, Kind = SettlementKind.Facility })
                .Append(content.Settlements[0] with { Id = 4, X = 17, Soldiers = 0, Morale = 0 }).ToArray(),
            Officers = content.Officers.Select(p => p.State.Id == 2 ? p with { State = p.State with { SettlementId = 3 } } : p).ToArray(),
            CityConnections = [new(1, 2), new(2, 1), new(4, 1)],
            RegionCityIds = new() { [1] = 1, [2] = 2, [3] = 4, [4] = 4 },
            RegionNameKeys = new() { [1] = "region.1", [2] = "region.2", [3] = "region.3", [4] = "region.4" },
            Military = content.Military! with { Places = content.Military.Places.Add(new(3, new(17, 7), new(1, 1))).Add(new(4, new(87, 7), new(1, 1))),
                RegionIds = Enumerable.Range(0, 60).Select(i => (int?)(i % 20 == 3 ? 3 : i % 20 < 10 ? 1 : i % 20 > 16 ? 4 : 2)).ToImmutableArray(),
                TerritoryExits = Enumerable.Repeat(TerritoryExits.All, 60).ToImmutableArray() }
        };
        var h = new Harness(content, draw: _ => throw new InvalidOperationException("Unexpected fallback draw"));
        Assert.Equal(1, h.Run(OriginalAiWork.Transport).Submitted);
        var command = Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands));
        Assert.Equal(3, command.SettlementId);
        Assert.Equal(5000u, command.Soldiers);
        Assert.Equal(new UnitPolicies(false, false, false, true), command.Policies);
        Assert.Equal(0u, h.Engine.Snapshot.Settlements.Single(s => s.Id == 3).Soldiers);
    }

    [Fact]
    public void UnoccupiedTargetUsesTheOrdinaryDeploymentCommand()
    {
        var content = Fixture();
        content = content with
        {
            Settlements = content.Settlements.Append(content.Settlements[0] with { Id = 3, X = 5, OwnerId = null, Soldiers = 0, Morale = 0,
                Kind = SettlementKind.Facility }).ToArray(),
            Information = content.Information with { Relations = [new(1, 2, 100)] },
            RegionCityIds = new() { [1] = 1, [2] = 2, [3] = 1 },
            RegionNameKeys = new() { [1] = "region.1", [2] = "region.2", [3] = "region.3" },
            Military = content.Military! with { Places = content.Military.Places.Add(new(3, new(27, 7), new(1, 1))
                { Combat = new(1, 1, 1, 1, 10) }) }
        };
        var h = new Harness(content);
        var report = h.Run(OriginalAiWork.Attack);
        Assert.True(report.Submitted == 1, report.ToString());
        Assert.Equal(3, Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).Order.SettlementId);
    }

    [Theory]
    [InlineData(false, 600, false)]
    [InlineData(true, 599, false)]
    [InlineData(true, 600, true)]
    public void BarbarianFormationBranchRequiresItsOwnAvailableAndAffordableFormation(bool available, uint money, bool expected)
    {
        var content = Fixture();
        content = content with
        {
            Forces = content.Forces.Select(f => f.Id == 1 ? f with { IsBarbarian = true,
                AdditionalFormationIds = available ? [400] : [] } : f).ToArray(),
            Information = content.Information with { Corps = content.Information.Corps.Select(c => c.Id == 1 ? c with { Treasury = new(money, 100000) } : c).ToArray() },
            Military = content.Military! with { Formations = content.Military.Formations.Select(f => f.Id == 400
                ? f with { IsCommon = !available, DeploymentCost = 600 } : f).ToImmutableArray() }
        };
        var h = new Harness(content, w => w with { Forces = w.Forces.Select(f => f.Id == 1 ? f with { IsBarbarian = true } : f).ToImmutableArray() });
        Assert.Equal(expected ? 1 : 0, h.Run(OriginalAiWork.Attack).Submitted);
        if (expected) Assert.Equal(new UnitPolicies(true, true, true, false), Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).Policies);
    }
}
