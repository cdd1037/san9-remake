using System.Collections.Immutable;
using San9.Application.AI;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Application.Tests;

public sealed partial class OriginalAiMilitaryTests
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
                        Positions = San9.Tests.FormationTestData.Layout(Enumerable.Repeat(BattleRank.Middle, 5).ToImmutableArray()) },
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
        internal MilitaryRulebook Rules { get; }
        internal OriginalAiTerrainRoutes TerrainRoutes { get; } = new();
        internal List<GameCommand> Commands { get; } = [];
        internal Harness(ScenarioInitialState content, Func<WorldSnapshot, WorldSnapshot>? change = null, Func<int, int>? draw = null, bool reject = false,
            Func<GameCommand, bool>? rejectCommand = null, bool automaticContext = false)
        {
            var definition = new ScenarioSessionDefinition(content);
            var world = definition.CreateWorld(190).CreateSnapshot();
            world = world with
            {
                Settlements = world.Settlements.Select(s => s with { Soldiers = content.Settlements.Single(p => p.Id == s.Id).Soldiers }).ToArray(),
                Corps = content.Information.Corps,
                Simulation = world.Simulation with { Boundary = SimulationBoundary.Stable }
            };
            Engine = GameSession.EngineFactory(content)(WorldState.Restore(change?.Invoke(world) ?? world));
            if (automaticContext) Engine.ConfigureCompanion(new() { PlayerCorpsAutomation = PlayerCorpsAutomationMode.Full });
            var context = new CommandContext(automaticContext ? CorpsControl.ControllerId(1) : "player");
            var rules = Rules = definition.Rules.Military!;
            Military = new(Engine, content, rules, definition.Rules,
                new(content, rules), TerrainRoutes, 1, context, new OriginalAiRandom(draw ?? (_ => 0)), (command, prepared) =>
            {
                Commands.Add(command);
                if (reject || rejectCommand?.Invoke(command) == true) return false;
                var result = prepared is null ? Engine.Execute(context, command) : Engine.Execute(context, (DeployUnitCommand)command, prepared);
                Assert.True(result.Submission.Accepted, $"{command}: {result.Submission.Rejection}");
                return true;
            });
            State = OriginalAiState.Create(content) with { Corps = [new(1, OriginalAiMode.TargetForce, 2, null)] };
        }
        internal OriginalAiWorkReport Run(OriginalAiWork work, int limit = 20, OriginalAiGroup group = OriginalAiGroup.Unassigned)
        {
            var state = State with { Forces = State.Forces.Select(f => f.ForceId == 1 ? f with { Group = group } : f).ToImmutableArray() };
            return Military.Run(work, limit, state, state.Corps[0]);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void OrdinaryAttackFiltersBlockedOriginsBeforePlanning(int state)
    {
        var h = new Harness(Fixture(), w =>
        {
            if (state == 0) return w;
            var enemy = EnemyArmy(w);
            return enemy with
            {
                Settlements = enemy.Settlements.Select(s => s.Id == 1 ? s with { ConfusionDaysRemaining = state == 1 ? 5 : 0 } : s).ToArray(),
                Units = [.. enemy.Units.Select(u => u with { Position = state == 2 ? new(8, 7) : u.Position })]
            };
        });
        var before = h.Engine.Snapshot;
        Assert.Equal(state == 2, MilitaryEngagementQuery.Capture(before, new(Fixture().Military!)).Settlements.Contains(1));
        var report = h.Run(OriginalAiWork.Attack, 1);
        Assert.Equal(state == 0 ? 1 : 0, report.Submitted);
        if (state != 0)
        {
            Assert.Equal(0, report.Attempts);
            Assert.Empty(h.Commands);
            StateAssert.Equal(before, h.Engine.Snapshot);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefensiveDeploymentAllowsEngagementButExcludesConfusedSource(bool confused)
    {
        var content = Fixture(4);
        var h = new Harness(content, w =>
        {
            var enemy = EnemyArmy(w);
            return enemy with
            {
                Settlements = enemy.Settlements.Select(s => s.Id == 1 ? s with { ConfusionDaysRemaining = confused ? 5 : 0 } : s).ToArray(),
                Units = [.. enemy.Units.Select(u => u with { Position = new(8, 7) })]
            };
        });
        Assert.Contains(1, MilitaryEngagementQuery.Capture(h.Engine.Snapshot, new(content.Military!)).Settlements);
        var before = h.Engine.Snapshot;
        var report = h.Run(OriginalAiWork.Deploy, 1);
        Assert.Equal(confused ? 0 : 1, report.Submitted);
        if (confused)
        {
            Assert.Equal(0, report.Attempts);
            Assert.Empty(h.Commands);
            StateAssert.Equal(before, h.Engine.Snapshot);
        }
    }

    [Theory]
    [InlineData(2, true, 1)]
    [InlineData(3, true, 0)]
    [InlineData(3, false, 1)]
    public void RetreatPressureBlockageUsesTheApproachingForceAndActualBuildingFootprint(int buildingOwner, bool crossing, int expected)
    {
        var content = Fixture();
        content = content with
        {
            Forces = content.Forces.Append(new(3, "third", 3, 50) { LeaderId = 101 }).ToArray(),
            Settlements = content.Settlements.Select(s => s.Id == 1 ? s with { Soldiers = 8000 } : s)
                .Append(content.Settlements[0] with { Id = 3, X = 17, Soldiers = 20000 })
                .Append(content.Settlements[0] with { Id = 4, X = 5, Y = crossing ? 1 : 2, OwnerId = buildingOwner,
                    Soldiers = 0, Morale = 0 })
                .Append(content.Settlements[0] with { Id = 5, X = 19, OwnerId = 3, Soldiers = 0, Morale = 0 }).ToArray(),
            Officers = content.Officers.Append(new("third", new(101, 3, 5, new(30, 30, 30, 30), false)
                { Status = OfficerStatus.Ruler, BirthYear = 160 })).ToArray(),
            Information = content.Information with
            {
                Corps = content.Information.Corps.Append(new(3, 3, 101, new(1000, 1000)) { IsRoot = true }).ToArray(),
                Relations = [new(1, 2, 80), new(1, 3, 80), new(2, 3, 0)]
            },
            RegionCityIds = new() { [1] = 1, [2] = 2, [3] = 3, [4] = 4, [5] = 5 },
            RegionNameKeys = new() { [1] = "region.1", [2] = "region.2", [3] = "region.3", [4] = "region.4", [5] = "region.5" },
            Military = content.Military! with
            {
                Formations = content.Military.Formations.Select(f => f with { MovementBudget = 20 }).ToImmutableArray(),
                Places = content.Military.Places.Add(new(3, new(87, 7), new(1, 1)))
                    .Add(new(4, new(27, crossing ? 7 : 12), new(1, 1))).Add(new(5, new(97, 7), new(1, 1))),
                RegionIds = Enumerable.Range(0, 60).Select(i => (int?)(i % 20 == 19 ? 5 : i % 20 is >= 4 and <= 6 ? 4 : i % 20 >= 16 ? 3 :
                    i % 20 < 10 ? 1 : 2)).ToImmutableArray()
            }
        };
        var h = new Harness(content, w =>
        {
            var enemy = EnemyArmy(w);
            return enemy with
            {
                Officers = enemy.Officers.Select(p => p.Id == 2 ? p with { SettlementId = null, UnitId = 901 } : p).ToImmutableArray(),
                Units = enemy.Units.Select(u => u with { Position = new(47, 7) }).ToImmutableArray().Add(new(901, 1, new(17, 7), 5000)
                {
                    FormationId = 400, CommanderId = 2, CorpsId = 1, ServicePlaceId = 3, Morale = 100,
                    Order = new(MilitaryOrderKind.Move, new(17, 7), null), MemberPositions = San9.Tests.FormationTestData.Members(ImmutableDictionary<int, BattleRank>.Empty.Add(2, BattleRank.Middle))
                }),
                Simulation = enemy.Simulation with { NextUnitId = 902 }
            };
        });
        Assert.Equal(expected, h.Run(OriginalAiWork.Retreat, 1).Submitted);
        if (expected > 0)
        {
            var command = Assert.IsType<OrderUnitCommand>(Assert.Single(h.Commands));
            Assert.Equal(901, command.UnitId);
            Assert.Equal(1, command.Order.SettlementId);
        }
        else Assert.Empty(h.Commands);
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

    [Theory]
    [InlineData(true, 700, 1)]
    [InlineData(false, 400, 0)]
    public void ExistingSiegeFormationIdentityDoesNotDependOnTheNewTeamsAvailableFormations(bool catapultAvailable, int formation, int draws)
    {
        var content = Fixture();
        var basis = content.Military!.Formations[0];
        content = content with
        {
            Military = content.Military with { Formations = content.Military.Formations
                .Add(basis with { Id = 600, IsCommon = false })
                .Add(basis with { Id = 700, IsCommon = catapultAvailable, Combat = new(1, 1, 1, 1, 10) }) },
            OriginalAi = content.OriginalAi with { Military = content.OriginalAi.Military with
                { Formations = content.OriginalAi.Military.Formations.Add(OriginalAiFormationRole.Ram, 600)
                    .Add(OriginalAiFormationRole.Catapult, 700) } }
        };
        int probabilityDraws = 0;
        var h = new Harness(content, w => w with { Simulation = w.Simulation with { NextUnitId = 901 },
            Units = w.Units.Append(new UnitState(900, 2, new(62, 7), 1000)
            { FormationId = 600, ServicePlaceId = 2, Order = new(MilitaryOrderKind.AttackSettlement, null, 2) }).ToImmutableArray() },
            maximum => { if (maximum == 100) probabilityDraws++; return 0; });
        Assert.Equal(1, h.Run(OriginalAiWork.Attack).Submitted);
        Assert.Equal(formation, Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).FormationId);
        // Member selection also draws; isolate the extra siege probability through an otherwise identical world.
        int baselineDraws = 0;
        var baseline = new Harness(content, draw: maximum => { if (maximum == 100) baselineDraws++; return 0; });
        Assert.Equal(1, baseline.Run(OriginalAiWork.Attack).Submitted);
        Assert.Equal(draws, probabilityDraws - baselineDraws);
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

    [Theory]
    [InlineData("target-city", 2)]
    [InlineData("neighbor-city", 2)]
    [InlineData("neighbor-facility", 2)]
    [InlineData("distant-facility", 2)]
    [InlineData("other-corps", 0)]
    [InlineData("single-officer", 1)]
    public void AttackSourcesUseActualRoutesAndKeepCorpsAndPersonnelLimits(string relation, int expected)
    {
        bool city = relation is "neighbor-city" or "other-corps" or "single-officer";
        bool distant = relation == "distant-facility";
        var content = Fixture();
        content = content with
        {
            Settlements = content.Settlements.Select(s => s.Id == 1 ? s with { Soldiers = 30000 } : s with { Soldiers = 20000 })
                .Append(content.Settlements[0] with { Id = 3, X = city || distant ? 17 : 11, Soldiers = 20000,
                    Kind = city ? SettlementKind.City : SettlementKind.Facility, CorpsId = relation == "other-corps" ? 3 : null })
                .Append(content.Settlements[0] with { Id = 4, X = 19, Soldiers = 0, Morale = 0 }).ToArray(),
            Officers = content.Officers.Select(p => (p.State.Id == 11 || p.State.Id == 12 && relation != "single-officer") ? p with { State = p.State with
                { SettlementId = 3, Status = relation == "other-corps" && p.State.Id == 11 ? OfficerStatus.Governor : OfficerStatus.Serving } } : p).ToArray(),
            Information = content.Information with { Corps = relation == "other-corps"
                ? content.Information.Corps.Append(new(3, 1, 11, new(1000, 1000))).ToArray() : content.Information.Corps },
            CityConnections = [new(1, 2), new(2, 1), new(2, city ? 3 : 4)],
            RegionCityIds = new() { [1] = 1, [2] = 2, [3] = city ? 3 : relation == "target-city" ? 2 : 4, [4] = 4, [5] = 4 },
            RegionNameKeys = new() { [1] = "region.1", [2] = "region.2", [3] = "region.3", [4] = "region.4", [5] = "region.5" },
            UnbuiltPlaces = distant ? [new(5, new(77, 7))] : [],
            Military = content.Military! with
            {
                Places = content.Military.Places.Add(new(3, new(city || distant ? 87 : 57, 7), new(1, 1)))
                    .Add(new(4, new(97, 7), new(1, 1))),
                RegionIds = Enumerable.Range(0, 60).Select(i => (int?)(i % 20 == 19 ? 4 :
                    city || distant ? i % 20 >= 16 ? 3 : distant && i % 20 >= 14 ? 5 : i % 20 < 10 ? 1 : 2
                        : i % 20 is 10 or 11 ? 3 : i % 20 < 10 ? 1 : 2)).ToImmutableArray()
            }
        };
        var h = new Harness(content);
        Assert.Equal(expected, h.Run(OriginalAiWork.Attack, 2).Submitted);
        if (expected > 0)
        {
            Assert.Equal(expected == 1 ? new[] { 1 } : new[] { 1, 3 }, h.Commands.Cast<DeployUnitCommand>().Select(c => c.SettlementId).ToArray());
            Assert.All(h.Commands.Cast<DeployUnitCommand>(), c => Assert.Equal(2, c.Order.SettlementId));
        }
        else Assert.Empty(h.Commands);
    }

    private static ScenarioInitialState AttackReserveFixture(uint enemySoldiers = 60000)
    {
        var content = Fixture();
        return content with
        {
            Settlements = content.Settlements.Select(s => s with { Soldiers = s.Id == 1 ? 60000u : 20000u })
                .Append(content.Settlements[1] with { Id = 3, X = 19, Soldiers = enemySoldiers, Kind = SettlementKind.Facility }).ToArray(),
            RegionCityIds = new(content.RegionCityIds) { [3] = 2 },
            RegionNameKeys = new(content.RegionNameKeys) { [3] = "region.3" },
            Military = content.Military! with { Places = content.Military.Places.Add(new(3, new(97, 7), new(1, 1))) }
        };
    }

    [Theory]
    [InlineData(OriginalAiGroup.Favored, true, false, 59999u, true)]
    [InlineData(OriginalAiGroup.Favored, true, false, 60000u, false)]
    [InlineData(OriginalAiGroup.Favored, false, true, 60000u, false)]
    [InlineData(OriginalAiGroup.Favored, false, false, 60000u, true)]
    [InlineData(OriginalAiGroup.Unassigned, true, false, 60000u, true)]
    [InlineData(OriginalAiGroup.ReducedActivity, true, false, 60000u, true)]
    public void FavoredAttackReservesHalfTheLargestRelatedEnemyGarrisonOnlyForFortifiedSources(
        OriginalAiGroup group, bool city, bool fortified, uint enemySoldiers, bool expected)
    {
        var content = AttackReserveFixture(enemySoldiers);
        if (!city)
            content = content with
            {
                Settlements = content.Settlements.Select(s => s.Id == 1 ? s with { Soldiers = 0, Morale = 0 } : s)
                    .Append(content.Settlements[0] with { Id = 4, Y = 2, Kind = SettlementKind.Facility }).ToArray(),
                Officers = content.Officers.Select(p => p.State.OwnerId == 1 ? p with { State = p.State with { SettlementId = 4 } } : p).ToArray(),
                RegionCityIds = new(content.RegionCityIds) { [4] = 1 },
                RegionNameKeys = new(content.RegionNameKeys) { [4] = "region.4" },
                Military = content.Military! with
                {
                    Facilities = [new(700, 1000, 100, new(1, 1), 0, true, true, true, 500000, null, null, null, [])],
                    Places = content.Military.Places.Add(new(4, new(7, 12), new(1, 1)))
                        .Select(p => p with { FacilityTypeId = 700 }).ToImmutableArray()
                },
                OriginalAi = content.OriginalAi with { Military = content.OriginalAi.Military with { FortifiedFacilityIds = fortified ? [700] : [] } }
            };
        var h = new Harness(content);
        Assert.Equal(expected ? 1 : 0, h.Run(OriginalAiWork.Attack, 1, group).Submitted);
        if (expected)
        {
            var command = Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands));
            Assert.Equal(2, command.Order.SettlementId);
            // The estimate accepts or rejects a plan; it does not change allocation's existing reserve.
            Assert.Equal(20000u, command.Soldiers);
        }
        else Assert.Empty(h.Commands);
    }

    [Theory]
    [InlineData("same-city", false)]
    [InlineData("neighbor-city", false)]
    [InlineData("distant-city", false)]
    [InlineData("friendly", true)]
    [InlineData("unoccupied", true)]
    [InlineData("non-hostile", true)]
    [InlineData("other-enemy", false)]
    [InlineData("barbarian", true)]
    [InlineData("dormant", true)]
    [InlineData("zero-durability", true)]
    [InlineData("allied", true)]
    [InlineData("attack-request", true)]
    [InlineData("obstacle", true)]
    public void FavoredAttackReserveUsesIncomingEligibleEnemyBuildingsRegardlessOfParentCity(string relation, bool expected)
    {
        var content = AttackReserveFixture();
        content = content with
        {
            RegionCityIds = new(content.RegionCityIds) { [3] = relation == "same-city" ? 1 : relation == "distant-city" ? 4 : 2 },
            Settlements = content.Settlements.Select(s => s.Id != 3 ? s : s with
            {
                OwnerId = relation == "friendly" ? 1 : relation == "unoccupied" ? null : 2,
                IsDormant = relation == "dormant"
            }).ToArray(),
            Military = content.Military! with
            {
                Facilities = [new(700, 1000, 100, new(1, 1), 0, true, true, true, 500000, null, null, null, []),
                    new(701, 1000, 100, new(1, 1), 0, true, true, true, 500000, null, null, null, [])],
                Places = content.Military.Places.Select(p => p with { FacilityTypeId = p.Id == 3 ? 701 : 700 }).ToImmutableArray()
            },
            OriginalAi = content.OriginalAi with { Military = content.OriginalAi.Military with
                { RouteObstacleFacilityIds = relation == "obstacle" ? [701] : [] } }
        };
        // Keep the target hostile while changing the third building's ownership eligibility.
        if (relation is "non-hostile" or "other-enemy" or "barbarian" or "allied" or "attack-request")
            content = content with
            {
                Forces = content.Forces.Append(content.Forces[1] with { Id = 3, LeaderId = 101, IsBarbarian = relation == "barbarian" }).ToArray(),
                Officers = content.Officers.Append(new("third-ruler", content.Officers[^1].State with { Id = 101, OwnerId = 3, SettlementId = 3 })).ToArray(),
                Settlements = content.Settlements.Select(s => s.Id == 3 ? s with { OwnerId = 3 } : s).ToArray(),
                Information = content.Information with { Relations = [new(1, 2, 0), new(1, 3, relation == "non-hostile" ? 80 : 0)],
                    Corps = content.Information.Corps.Append(new(3, 3, 101, new(1000, 1000)) { IsRoot = true }).ToArray() }
            };
        if (relation == "distant-city")
            content = content with
            {
                Settlements = content.Settlements.Append(content.Settlements[0] with { Id = 4, X = 17, Y = 2, Soldiers = 0, Morale = 0 }).ToArray(),
                RegionCityIds = new(content.RegionCityIds) { [4] = 4 },
                RegionNameKeys = new(content.RegionNameKeys) { [4] = "region.4" },
                Military = content.Military! with { Places = content.Military.Places.Add(new(4, new(87, 12), new(1, 1)) { FacilityTypeId = 700 }) }
            };
        var h = new Harness(content, world => world with
        {
            // Against the alliance target the threshold is 75%; keep this case sensitive to the reserve.
            Settlements = world.Settlements.Select(s => s.Id == 1 && relation == "allied" ? s with { Soldiers = 45000 } :
                s.Id != 3 ? s : relation switch
            {
                "zero-durability" => s with { Durability = 0 },
                _ => s
            }).ToImmutableArray(),
            Simulation = world.Simulation with
            {
                Alliance = relation == "allied" ? new(2, 1, [1, 3], 10) : null,
                AttackRequests = relation == "attack-request" ? [new(1, 3, 2, 2, 10)] : []
            }
        });
        Assert.Equal(expected ? 1 : 0, h.Run(OriginalAiWork.Attack, 1, OriginalAiGroup.Favored).Submitted);
    }

    [Fact]
    public void FavoredAttackReserveExcludesBuildingsUnderConstruction()
    {
        var content = AttackReserveFixture();
        content = content with { Military = content.Military! with
        {
            Facilities = [new(701, 1000, 100, new(1, 1), 0, true, true, true, 500000, null, null, null, [])],
            Formations = content.Military.Formations.Add(new(600, 10, 0, 30000, 5, new(1, 1), TravelMode.Land)),
            ConstructionFormations = new(600, null),
            ConstructionSites = [new(3, new(97, 7), [701])],
            Places = content.Military.Places.Select(p => p with { FacilityTypeId = 701 }).ToImmutableArray()
        } };
        var h = new Harness(content, world =>
        {
            var enemy = EnemyArmy(world);
            return enemy with
            {
                Settlements = enemy.Settlements.Select(s => s.Id == 3 ? s with
                    { CanHostPersons = false, Durability = 50, MaxDurability = 100, SoldierCapacity = 500000, ConsumesFood = true,
                        Facility = new(701, new(97, 7), 900) } : s).ToImmutableArray(),
                Units = enemy.Units.Select(u => u with
                {
                    HasEnteredDailyCycle = true,
                    FormationId = 600,
                    Order = new(MilitaryOrderKind.Construct, null, null) { Construction = new(3, 701, new(97, 7), 100) }
                }).ToImmutableArray()
            };
        });
        Assert.Equal(1, h.Run(OriginalAiWork.Attack, 1, OriginalAiGroup.Favored).Submitted);
    }

    [Fact]
    public void FavoredAttackReserveExcludesTheAttackTarget()
    {
        var content = Fixture();
        content = content with { Settlements = content.Settlements.Select(s => s with { Soldiers = s.Id == 1 ? 100001u : 60000u }).ToArray() };
        var h = new Harness(content);
        Assert.Equal(1, h.Run(OriginalAiWork.Attack, 1, OriginalAiGroup.Favored).Submitted);
        Assert.Equal(2, Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).Order.SettlementId);
    }

    [Fact]
    public void FavoredAttackReserveDoesNotAddEnemyGarrisonsToLargerIncomingPressure()
    {
        var content = AttackReserveFixture(20000);
        content = content with { Settlements = content.Settlements.Select(s => s.Id == 1 ? s with { Soldiers = 50001 } : s).ToArray() };
        var h = new Harness(content, w =>
        {
            var enemy = EnemyArmy(w);
            return enemy with { Units = enemy.Units.Select(u => u with { Soldiers = 30000, HasEnteredDailyCycle = true }).ToImmutableArray() };
        });
        Assert.Equal(1, h.Run(OriginalAiWork.Attack, 1, OriginalAiGroup.Favored).Submitted);
    }

    [Fact]
    public void FavoredAttackStrengthReserveDoesNotRestrictActualAllocation()
    {
        var content = AttackReserveFixture(30000);
        content = content with { Settlements = content.Settlements.Select(s => s.Id == 1 ? s with { Soldiers = 20000 } :
            s.Id == 2 ? s with { Soldiers = 1000 } : s).ToArray() };
        var h = new Harness(content);
        Assert.Equal(1, h.Run(OriginalAiWork.Attack, 1, OriginalAiGroup.Favored).Submitted);
        var command = Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands));
        Assert.Equal(2, command.Order.SettlementId);
        Assert.Equal(10000u, command.Soldiers);
        Assert.Equal(10000u, h.Engine.Snapshot.Settlements.Single(s => s.Id == 1).Soldiers);
    }

    [Fact]
    public void FavoredAttackReserveTakesTheMaximumGarrisonRatherThanTheirSum()
    {
        var content = AttackReserveFixture(40000);
        content = content with
        {
            Settlements = content.Settlements.Append(content.Settlements[^1] with { Id = 4, Y = 2 }).ToArray(),
            RegionCityIds = new(content.RegionCityIds) { [4] = 2 },
            RegionNameKeys = new(content.RegionNameKeys) { [4] = "region.4" },
            Military = content.Military! with { Places = content.Military.Places.Add(new(4, new(97, 12), new(1, 1))) }
        };
        var h = new Harness(content);
        Assert.Equal(1, h.Run(OriginalAiWork.Attack, 1, OriginalAiGroup.Favored).Submitted);
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

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void AttackDefenseCountsOnlyServiceTroopsBeforeTheirFirstDailyCycle(bool advanced, bool entered, bool commanded)
    {
        var content = Fixture();
        content = content with
        {
            OriginalAi = content.OriginalAi with { Advanced = advanced },
            Settlements = content.Settlements.Select(s => s with { Soldiers = s.Id == 1 ? 40000u : 20000u }).ToArray()
        };
        var h = new Harness(content, w =>
        {
            var enemy = EnemyArmy(w);
            return enemy with { Units = enemy.Units.Select(u => u with { HasEnteredDailyCycle = entered,
                CommandedThisXun = commanded, DepartureDaysRemaining = 9,
                Order = new(MilitaryOrderKind.Move, new(57, 7), null) }).ToImmutableArray() };
        });

        Assert.Equal(entered ? 1 : 0, h.Run(OriginalAiWork.Attack, 1).Submitted);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void DeploymentResponseUsesDailyCycleHistoryInsteadOfTheCommandMarker(bool advanced, bool entered, bool expected)
    {
        var content = Fixture(4);
        content = content with { OriginalAi = content.OriginalAi with { Advanced = advanced } };
        var h = new Harness(content, w =>
        {
            var enemy = EnemyArmy(w);
            return enemy with { Units = enemy.Units.Select(u => u with
                { HasEnteredDailyCycle = entered, CommandedThisXun = true }).ToImmutableArray() };
        });

        Assert.Equal(expected ? 1 : 0, h.Run(OriginalAiWork.Deploy, 1).Submitted);
    }

    [Theory]
    [InlineData(MilitaryOrderKind.AttackUnit, true, 1)]
    [InlineData(MilitaryOrderKind.AttackUnit, false, 0)]
    [InlineData(MilitaryOrderKind.Move, true, 1)]
    [InlineData(MilitaryOrderKind.Move, false, 0)]
    [InlineData(MilitaryOrderKind.AttackSettlement, true, 1)]
    [InlineData(MilitaryOrderKind.Return, true, 0)]
    [InlineData(MilitaryOrderKind.Transport, true, 0)]
    public void RouteProjectionPreservesTaskKindAndRequiresActualBuildingCrossing(MilitaryOrderKind kind, bool crossing, int expected)
    {
        var content = Fixture();
        content = content with
        {
            Settlements = content.Settlements.Select(s => s.Id == 1 ? s with { X = 7, Y = crossing ? 1 : 2, Soldiers = 20000 } : s)
                .Append(content.Settlements[0] with { Id = 3, X = 3, Soldiers = 0, Morale = 0 }).ToArray(),
            RegionCityIds = new(content.RegionCityIds) { [3] = 3 },
            RegionNameKeys = new(content.RegionNameKeys) { [3] = "region.3" },
            Military = content.Military! with { Places = content.Military.Places.Select(p => p.Id == 1
                ? p with { Position = new(37, crossing ? 7 : 12) } : p).ToImmutableArray().Add(new(3, new(17, 7), new(1, 1))) }
        };
        var h = new Harness(content, w =>
        {
            var enemy = EnemyArmy(w);
            return enemy with
            {
                Units = enemy.Units.Select(u => u with
                {
                    FormationId = kind == MilitaryOrderKind.Transport ? 500 : 400,
                    Order = kind == MilitaryOrderKind.AttackUnit ? new(kind, null, null) { UnitId = 901 }
                        : kind == MilitaryOrderKind.Move ? new(kind, new(17, 7), null) : new(kind, null, 3)
                }).ToImmutableArray().Add(new(901, 1, new(17, 7), 1000)
                    { FormationId = 400, ServicePlaceId = 1, Order = new(MilitaryOrderKind.Move, new(17, 7), null) }),
                Simulation = enemy.Simulation with { NextUnitId = 902 }
            };
        });
        var before = h.Engine.Snapshot;
        Assert.Equal(expected, h.Run(OriginalAiWork.Deploy, 1).Submitted);
        Assert.Equal(before.Units.Single(u => u.Id == 900), h.Engine.Snapshot.Units.Single(u => u.Id == 900));
        if (expected > 0) Assert.Equal(900, Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).Order.UnitId);
        else Assert.Empty(h.Commands);
    }

    [Theory]
    [InlineData(0, false, 1)]
    [InlineData(1, false, 1)]
    [InlineData(2, false, 0)]
    [InlineData(0, true, 0)]
    [InlineData(3, false, 0)]
    public void ProjectionFollowsBothWaypointLegsAndNeverReplacesAnExistingTemporaryTarget(int route, bool temporary, int expected)
    {
        var content = Fixture();
        content = content with
        {
            Settlements = content.Settlements.Select(s => s.Id == 1 ? s with { X = 7, Soldiers = 20000 } : s).ToArray(),
            Military = content.Military! with
            {
                Places = content.Military.Places.Select(p => p.Id == 1 ? p with { Position = new(37, 7) } : p).ToImmutableArray(),
                TerrainIds = route == 3 ? Enumerable.Range(0, 60).Select(i => i % 20 == 13 ? 1 : 0).ToImmutableArray() : content.Military.TerrainIds,
                Terrains = [new(0, 1, null), new(1, null, null)]
            }
        };
        var h = new Harness(content, w =>
        {
            var enemy = EnemyArmy(w);
            return enemy with
            {
                Units = enemy.Units.Select(u => u with
                {
                    Position = route == 1 ? new(57, 12) : u.Position,
                    Waypoint = route == 1 ? new(57, 7) : route == 2 ? new(57, 12) : new(17, 7),
                    Order = temporary ? new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 901 } :
                        new(MilitaryOrderKind.Move, route == 3 ? new(87, 7) : route == 1 ? new(17, 7) : new(17, 12), null),
                    EngagementTargetId = temporary ? 902 : null
                }).ToImmutableArray()
                    .Add(new(901, 1, new(17, 7), 1000) { FormationId = 400, Order = new(MilitaryOrderKind.Move, new(17, 7), null) })
                    .Add(new(902, 1, new(57, 12), 1000) { FormationId = 400, Order = new(MilitaryOrderKind.Move, new(57, 12), null) }),
                Simulation = enemy.Simulation with { NextUnitId = 903 }
            };
        });
        var before = h.Engine.Snapshot;
        Assert.Equal(expected, h.Run(OriginalAiWork.Deploy, 1).Submitted);
        Assert.Equal(before.Units.Single(u => u.Id == 900), h.Engine.Snapshot.Units.Single(u => u.Id == 900));
        if (expected > 0) Assert.Equal(900, Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).Order.UnitId);
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void FulfillmentDistinguishesCityReinforcementFromAnAlreadyAssignedBuilding(bool city, bool friendly, bool blocked)
    {
        var content = Fixture();
        content = content with
        {
            Forces = content.Forces.Append(new(3, "requester", 3, 50) { LeaderId = 101 }).ToArray(),
            Settlements = content.Settlements.Select(s => s.Id == 2 ? s with
                { Kind = city ? SettlementKind.City : SettlementKind.Facility, MerchantPresenceChance = 0 } : s)
                .Append(content.Settlements[1] with { Id = 3, OwnerId = 3, CorpsId = 3, X = 17 }).ToArray(),
            Officers = content.Officers.Append(new("requester", new(101, 3, 3, new(30, 30, 30, 30), false)
                { Status = OfficerStatus.Ruler })).ToArray(),
            RegionCityIds = new(content.RegionCityIds) { [2] = city ? 2 : 1, [3] = 3 },
            RegionNameKeys = new(content.RegionNameKeys) { [3] = "region.3" },
            CityConnections = city ? content.CityConnections : [],
            Military = content.Military! with { Places = content.Military.Places.Add(new(3, new(87, 7), new(1, 1))) },
            Information = content.Information with { Corps = content.Information.Corps.Append(new(3, 3, 101, new(1000, 1000)) { IsRoot = true }).ToArray() }
        };
        var h = new Harness(content, w => w with
        {
            Units = [new(900, friendly ? 1 : 3, new(27, 7), 10000) { FormationId = 400, ServicePlaceId = 1,
                HasEnteredDailyCycle = false, Order = new(MilitaryOrderKind.AttackSettlement, null, 2) }],
            Simulation = w.Simulation with { NextUnitId = 901, AttackRequests = [new(1, 3, 2, 2, 19)] }
        });

        Assert.Equal(blocked ? 0 : 1, h.Run(OriginalAiWork.FulfillRequest, 1).Submitted);
        if (blocked) Assert.Empty(h.Commands);
    }

    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 0)]
    [InlineData(true, true, 5)]
    public void FulfillmentFiltersHostileBuildingsBeforeAllocatingSources(bool blocked, bool alternative, int sourceId)
    {
        var content = Fixture();
        content = content with
        {
            Forces = content.Forces.Append(new(3, "requester", 3, 50) { LeaderId = 101 }).ToArray(),
            Settlements = [.. content.Settlements,
                content.Settlements[1] with { Id = 3, OwnerId = 3, CorpsId = 3, X = 17 },
                content.Settlements[1] with { Id = 4, X = 7, OwnerId = blocked ? 2 : 1, Soldiers = 0, Morale = 0 }],
            Officers = content.Officers.Append(new("requester", new(101, 3, 3, new(30, 30, 30, 30), false)
                { Status = OfficerStatus.Ruler })).ToArray(),
            RegionCityIds = new(content.RegionCityIds) { [3] = 3, [4] = 2 },
            RegionNameKeys = new(content.RegionNameKeys) { [3] = "requester", [4] = "blocker" },
            Information = content.Information with
                { Corps = content.Information.Corps.Append(new(3, 3, 101, new(1000, 1000)) { IsRoot = true }).ToArray() },
            Military = content.Military! with
            {
                Terrains = [new(0, 1, null), new(1, null, null)],
                TerrainIds = Enumerable.Range(0, 60).Select(i => i / 20 == 1 ? 0 : 1).ToImmutableArray(),
                RegionIds = Enumerable.Range(0, 60).Select(i => (int?)(i % 20 < 6 ? 1 : i % 20 < 9 ? 4 : i % 20 < 16 ? 2 : 3)).ToImmutableArray(),
                Places = content.Military.Places.Add(new(3, new(87, 7), new(1, 1))).Add(new(4, new(37, 7), new(1, 1)))
            }
        };
        if (alternative)
            content = content with
            {
                Settlements = [.. content.Settlements, content.Settlements[0] with { Id = 5, X = 11, Soldiers = 50000 }],
                Officers = content.Officers.Select(p => p.State.Id is >= 7 and <= 12
                    ? p with { State = p.State with { SettlementId = 5 } } : p).ToArray(),
                RegionCityIds = new(content.RegionCityIds) { [5] = 1 },
                RegionNameKeys = new(content.RegionNameKeys) { [5] = "alternative" },
                Military = content.Military with { Places = content.Military.Places.Add(new(5, new(57, 7), new(1, 1))) }
            };
        var h = new Harness(content, w => w with
            { Simulation = w.Simulation with { AttackRequests = [new(1, 3, 2, 2, 19)] } });
        var before = h.Engine.Snapshot;
        var report = h.Run(OriginalAiWork.FulfillRequest, 1);
        Assert.Equal(sourceId == 0 ? 0 : 1, report.Submitted);
        if (sourceId == 0)
        {
            Assert.Equal(0, report.Attempts);
            Assert.Empty(h.Commands);
            StateAssert.Equal(before, h.Engine.Snapshot);
        }
        else
        {
            var command = Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands));
            Assert.Equal(sourceId, command.SettlementId);
            Assert.Equal(2, command.Order.SettlementId);
        }
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
    [InlineData(false, 15000, false)]
    [InlineData(true, 15000, false)]
    [InlineData(true, 1000, false)]
    [InlineData(false, 1000, false)]
    public void FulfillmentContinuesWithSecondDistinctSourceAndPreservesBothMinimums(bool refuseFirst, uint targetSoldiers = 15000, bool visible = true)
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
        var h = new Harness(content, w => w with { Simulation = w.Simulation with { AttackRequests = [new(1, 3, 2, 2, 19)], OtherForceOrdersVisible = visible } },
            rejectCommand: c => refuseFirst && c is DeployUnitCommand { SettlementId: 1 });
        Assert.Equal(refuseFirst || targetSoldiers == 1000 ? 1 : 2, h.Run(OriginalAiWork.FulfillRequest).Submitted);
        Assert.Equal(!refuseFirst && targetSoldiers == 1000 ? new[] { 1 } : new[] { 1, 3 },
            h.Commands.Cast<DeployUnitCommand>().Select(c => c.SettlementId));
    }

    [Fact]
    public void ActualAutomaticCampaignCapturesAndSurvivesRestore()
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
    }

    private static WorldSnapshot EnemyArmy(WorldSnapshot world, int wait = 0, int target = 1) => world with
    {
        Officers = world.Officers.Select(p => p.Id == 100 ? p with { SettlementId = null, UnitId = 900 } : p).ToImmutableArray(),
        Units = [new(900, 2, new(57, 7), 20000) { FormationId = 400, CommanderId = 100, Morale = 100,
            CorpsId = 2, ServicePlaceId = 2, Order = new(MilitaryOrderKind.AttackSettlement, null, target),
            DepartureDaysRemaining = wait, MemberPositions = San9.Tests.FormationTestData.Members(ImmutableDictionary<int, BattleRank>.Empty.Add(100, BattleRank.Middle)) }],
        Simulation = world.Simulation with { NextUnitId = 901 }
    };

    private static ScenarioInitialState WithConstruction(ScenarioInitialState content) => content with
    {
        UnbuiltPlaces = [new(3, new(27, 7))],
        RegionCityIds = new(content.RegionCityIds) { [3] = 1 },
        RegionNameKeys = new(content.RegionNameKeys) { [3] = "region.3" },
        Military = content.Military! with
        {
            Formations = content.Military.Formations.Add(new(600, 10, 0, 30000, 5, new(1, 1), TravelMode.Land)
                { Combat = content.Military.Formations[0].Combat }),
            ConstructionFormations = new(600, null),
            Facilities = [new(700, 1000, 100, new(1, 1), 0, true, true, true, 30000, null, null, null, [])],
            Places = content.Military.Places.Select(p => p with { FacilityTypeId = 700 }).ToImmutableArray(),
            ConstructionSites = [new(3, new(27, 7), [700])],
            RegionIds = content.Military.RegionIds.Select((id, i) => i % 20 is >= 4 and <= 8 ? (int?)3 : id).ToImmutableArray()
        }
    };

    private static MilitaryOrder EnemyConstruction() => new(MilitaryOrderKind.Construct, null, null)
        { Construction = new(3, 700, new(27, 7), 100) };

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, true)]
    public void DeploymentObstacleGateBelongsToTheDefensiveGroup(bool defending, bool obstacle, bool expected)
    {
        var content = WithConstruction(Fixture());
        content = content with
        {
            Settlements = content.Settlements.Append(content.Settlements[1] with
                { Id = 4, OwnerId = null, X = 7, Kind = SettlementKind.Facility, Soldiers = 0, Morale = 0 }).ToArray(),
            RegionCityIds = new(content.RegionCityIds) { [4] = 2 },
            RegionNameKeys = new(content.RegionNameKeys) { [4] = "region.4" },
            Military = content.Military! with
            {
                Facilities = content.Military.Facilities.Add(content.Military.Facilities[0] with { Id = 701 }),
                Places = content.Military.Places.Select(p => p with { FacilityTypeId = 701 }).ToImmutableArray()
                    .Add(new(4, new(37, 7), new(1, 1)) { FacilityTypeId = obstacle ? 700 : 701 }),
                RegionIds = content.Military.RegionIds.Select((id, i) => i % 20 is >= 6 and <= 8 ? (int?)4 : id).ToImmutableArray()
            },
            OriginalAi = content.OriginalAi with { Military = content.OriginalAi.Military with { RouteObstacleFacilityIds = [700] } }
        };
        var h = new Harness(content, w =>
        {
            var enemy = EnemyArmy(w);
            return enemy with
            {
                Units = enemy.Units.Select(u => u with { ServicePlaceId = 4,
                    FormationId = defending ? 400 : 600,
                    Order = defending ? u.Order : EnemyConstruction() }).ToImmutableArray()
            };
        });
        Assert.Equal(expected ? 1 : 0, h.Run(OriginalAiWork.Deploy, 1).Submitted);
    }

    [Fact]
    public void DefensiveApproachCanCrossEnemyTerritoryWithoutCrossingTheEnemyBuilding()
    {
        var content = Fixture();
        content = content with
        {
            Settlements = content.Settlements.Append(content.Settlements[1] with
                { Id = 3, X = 7, Y = 2, Kind = SettlementKind.Facility, Soldiers = 0, Morale = 0 }).ToArray(),
            RegionCityIds = new(content.RegionCityIds) { [3] = 2 },
            RegionNameKeys = new(content.RegionNameKeys) { [3] = "region.3" },
            Military = content.Military! with
            {
                Places = content.Military.Places.Add(new(3, new(37, 12), new(1, 1))),
                RegionIds = content.Military.RegionIds.Select((id, i) => i % 20 is >= 4 and <= 8 ? (int?)3 : id).ToImmutableArray()
            }
        };
        var h = new Harness(content, w => EnemyArmy(w));
        Assert.Equal(1, h.Run(OriginalAiWork.Deploy, 1).Submitted);
        Assert.Equal(900, Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).Order.UnitId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    public void ReliefCanDefendAnotherCorpsWithoutBorrowingItsSourceTroops(int ruler)
    {
        var content = Fixture();
        content = content with
        {
            Settlements = content.Settlements.Select(s => s.Id == 1 ? s with { Soldiers = 15000 } : s)
                .Append(content.Settlements[0] with { Id = 3, X = 7, CorpsId = 3, Soldiers = 1000 }).ToArray(),
            Officers = content.Officers.Select(p => p.State.Id == 2 ? p with { State = p.State with
                { SettlementId = 3, Status = OfficerStatus.Governor } } : p.State.OwnerId == 1 ? p with
                { State = p.State with { Status = p.State.Id == ruler ? OfficerStatus.Ruler : OfficerStatus.Serving } } : p).ToArray(),
            Forces = content.Forces.Select(f => f.Id == 1 ? f with { LeaderId = ruler } : f).ToArray(),
            Information = content.Information with { Corps = content.Information.Corps.Select(c => c.Id == 1 ? c with { LeaderId = ruler } : c)
                .Append(new(3, 1, 2, new(1000, 1000))).ToArray() },
            CityConnections = content.CityConnections.Add(new(1, 3)).Add(new(3, 1)),
            RegionCityIds = new(content.RegionCityIds) { [3] = 3 },
            RegionNameKeys = new(content.RegionNameKeys) { [3] = "region.3" },
            Military = content.Military! with
            {
                Places = content.Military.Places.Add(new(3, new(37, 7), new(1, 1))),
                RegionIds = content.Military.RegionIds.Select((id, i) => i % 20 is >= 5 and <= 9 ? (int?)3 : id).ToImmutableArray()
            }
        };
        var h = new Harness(content, w => EnemyArmy(w, target: 3));
        var report = h.Run(OriginalAiWork.Deploy, 1);
        Assert.True(report.Submitted == 1, report.ToString());
        var command = Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands));
        Assert.Equal(1, command.SettlementId);
        Assert.Equal(3, command.Order.SettlementId);
        Assert.Equal(MilitaryOrderKind.Return, command.Order.Kind);
        Assert.Equal(5000u, command.Soldiers);
        Assert.Equal(new[] { ruler }, command.OfficerIds);
        Assert.DoesNotContain(2, command.OfficerIds);
        Assert.Equal(1000u, h.Engine.Snapshot.Settlements.Single(s => s.Id == 3).Soldiers);
    }

    [Fact]
    public void DeploymentUsesConnectedEndpointRegionsWithoutGeographicCityLinks()
    {
        var content = WithConstruction(Fixture()) with { CityConnections = [] };
        var h = new Harness(content, w =>
        {
            var enemy = EnemyArmy(w);
            return enemy with { Units = enemy.Units.Select(u => u with
                { FormationId = 600, Order = EnemyConstruction() }).ToImmutableArray() };
        });
        var report = h.Run(OriginalAiWork.Deploy);
        Assert.Equal(1, report.Submitted);
        Assert.Equal(MilitaryOrderKind.AttackUnit, Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).Order.Kind);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    public void DeploymentRequiresTwoAvailablePeopleButAllowsASingleCommander(int people, bool defending)
    {
        var content = Fixture(people);
        if (!defending) content = WithConstruction(content);
        content = content with { Settlements = content.Settlements.Select(s => s.Id == 1 ? s with { Soldiers = 20000 } : s).ToArray() };
        var h = new Harness(content, w =>
        {
            var enemy = EnemyArmy(w);
            return enemy with { Units = enemy.Units.Select(u => u with
            {
                Position = new(17, 7),
                FormationId = defending ? u.FormationId : 600,
                Order = defending ? u.Order : EnemyConstruction()
            }).ToImmutableArray() };
        });

        var report = h.Run(OriginalAiWork.Deploy, 1);

        Assert.True(report.Submitted == (people >= 2 ? 1 : 0), report.ToString());
        if (people < 2)
        {
            Assert.Empty(h.Commands);
            return;
        }
        var command = Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands));
        Assert.Equal(new[] { 1 }, command.OfficerIds);
        Assert.Equal(defending ? 8000u : 12000u, command.Soldiers);
        Assert.Equal(MilitaryOrderKind.AttackUnit, command.Order.Kind);
        Assert.Equal(900, command.Order.UnitId);
        Assert.Equal(new UnitPolicies(false, false, true, true), command.Policies);
        Assert.Equal(20000u - command.Soldiers, h.Engine.Snapshot.Settlements.Single(s => s.Id == 1).Soldiers);
        Assert.Equal(900, h.Engine.Snapshot.Units.Single(u => u.OwnerId == 1).Order!.UnitId);
    }

    [Theory]
    [InlineData(MilitaryOrderKind.Move, false)]
    [InlineData(MilitaryOrderKind.Return, false)]
    [InlineData(MilitaryOrderKind.Transport, false)]
    [InlineData(MilitaryOrderKind.AttackUnit, false)]
    [InlineData(MilitaryOrderKind.AttackSettlement, false)]
    [InlineData(MilitaryOrderKind.Construct, true)]
    public void UnthreatenedSourceOnlyInterceptsEnemyConstruction(MilitaryOrderKind kind, bool expected)
    {
        var content = WithConstruction(Fixture());
        var h = new Harness(content, w =>
        {
            var enemy = EnemyArmy(w);
            var order = kind switch
            {
                MilitaryOrderKind.Move => new(kind, new(67, 7), null),
                MilitaryOrderKind.AttackUnit => new(kind, null, null) { UnitId = 901 },
                MilitaryOrderKind.Construct => EnemyConstruction(),
                _ => new MilitaryOrder(kind, null, 2)
            };
            return enemy with
            {
                Units = enemy.Units.Select(u => u with { Order = order,
                    FormationId = kind == MilitaryOrderKind.Construct ? 600 : kind == MilitaryOrderKind.Transport ? 500 : 400 }).ToImmutableArray()
                    .Add(new(901, 1, new(37, 7), 1000) { FormationId = 400, Order = new(MilitaryOrderKind.Move, new(37, 7), null) }),
                Simulation = enemy.Simulation with { NextUnitId = 902 }
            };
        });

        var report = h.Run(OriginalAiWork.Deploy, 1);
        Assert.True(report.Submitted == (expected ? 1 : 0), report.ToString());
        if (expected)
        {
            var command = Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands));
            Assert.Equal(MilitaryOrderKind.AttackUnit, command.Order.Kind);
            Assert.Equal(900, command.Order.UnitId);
        }
        else Assert.Empty(h.Commands);
    }

    [Fact]
    public void ThreatenedSourceDoesNotReplaceItsAttackerWithANearerPassingEnemy()
    {
        var h = new Harness(Fixture(), w =>
        {
            var enemy = EnemyArmy(w);
            return enemy with
            {
                Units = enemy.Units.ToImmutableArray().Add(new(901, 2, new(17, 7), 20000)
                    { FormationId = 400, Order = new(MilitaryOrderKind.Move, new(67, 7), null) }),
                Simulation = enemy.Simulation with { NextUnitId = 902 }
            };
        });

        Assert.Equal(1, h.Run(OriginalAiWork.Deploy, 1).Submitted);
        Assert.Equal(900, Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).Order.UnitId);
    }

    [Theory]
    [InlineData(true, 8000u)]
    [InlineData(false, 12000u)]
    public void FriendlyReturnDoesNotConsumeTheEnemyApproachReserve(bool defending, uint soldiers)
    {
        var content = WithConstruction(Fixture(4));
        content = content with { Settlements = content.Settlements.Select(s => s.Id == 1 ? s with { Soldiers = 20000 } : s).ToArray() };
        var h = new Harness(content, w =>
        {
            var enemy = EnemyArmy(w);
            return enemy with
            {
                Units = enemy.Units.Select(u => u with { Position = new(17, 7),
                    FormationId = defending ? 400 : 600, Order = defending ? u.Order : EnemyConstruction() }).ToImmutableArray()
                    .Add(new(901, 1, new(22, 7), 1000) { FormationId = 400, ServicePlaceId = 2,
                        Order = new(MilitaryOrderKind.Return, null, 1) }),
                Simulation = enemy.Simulation with { NextUnitId = 902 }
            };
        });

        Assert.Equal(1, h.Run(OriginalAiWork.Deploy, 1).Submitted);
        Assert.Equal(soldiers, Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).Soldiers);
    }

    [Theory]
    [InlineData(UnitCondition.Normal, true)]
    [InlineData(UnitCondition.HighSpirits, true)]
    [InlineData(UnitCondition.Confused, false)]
    [InlineData(UnitCondition.Disguised, false)]
    [InlineData(UnitCondition.NoFormation, false)]
    [InlineData(UnitCondition.Routed, false)]
    [InlineData(UnitCondition.Normal, false, true, true)]
    [InlineData(UnitCondition.Normal, true, true, false)]
    public void StrategicRecallUsesItsOwnConditionGate(UnitCondition condition, bool expected,
        bool assisted = false, bool commanded = true)
    {
        var content = Fixture();
        content = content with { Settlements = content.Settlements.Select(s => s.Id == 1 ? s with { Soldiers = 5000 } : s).ToArray() };
        var h = new Harness(content, w =>
        {
            var enemy = EnemyArmy(w);
            return enemy with
            {

                Officers = enemy.Officers.Select(p => p.Id == 2 ? p with { SettlementId = null, UnitId = 901 } : p).ToImmutableArray(),
                Units = enemy.Units.ToImmutableArray().Add(new(901, 1, new(17, 7), 5000)
                {
                    FormationId = 400, CommanderId = 2, CorpsId = 1, ServicePlaceId = 1, Morale = 100, CommandedThisXun = commanded,
                    Condition = condition, ConditionDaysRemaining = 2, HighSpiritsMoralePenaltyPending = condition == UnitCondition.HighSpirits,
                    Order = new(MilitaryOrderKind.Move, new(47, 7), null),
                    MemberPositions = San9.Tests.FormationTestData.Members(ImmutableDictionary<int, BattleRank>.Empty.Add(2, BattleRank.Middle))
                }),
                Simulation = enemy.Simulation with { NextUnitId = 902, Controls = enemy.Simulation.Controls.Select(c => c.ForceId == 1 ? c with { Kind = assisted ? ControllerKind.Player : ControllerKind.Automatic } : c).ToImmutableArray() }
            };
        }, automaticContext: true);

        Assert.Equal(expected ? 1 : 0, h.Run(OriginalAiWork.Retreat, 1).Submitted);
        if (expected)
        {
            Assert.Equal(901, Assert.IsType<OrderUnitCommand>(Assert.Single(h.Commands)).UnitId);
            var recalled = Assert.Single(h.Engine.Snapshot.Units, u => u.Id == 901);
            Assert.Equal(MilitaryOrderKind.Return, recalled.Order!.Kind);
            Assert.Equal(1, recalled.Order.SettlementId);
            Assert.Equal(condition, recalled.Condition);
            Assert.Equal(2, recalled.ConditionDaysRemaining);
            Assert.Equal(condition == UnitCondition.HighSpirits, recalled.HighSpiritsMoralePenaltyPending);
        }
        else Assert.Empty(h.Commands);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AttackRouteConflictUsesConstructionDestinationInsteadOfTransportDestination(bool construction)
    {
        var content = WithConstruction(Fixture());
        content = content with
        {
            UnbuiltPlaces = [],
            Settlements = content.Settlements.Append(content.Settlements[1] with
                { Id = 3, OwnerId = null, CorpsId = null, X = 5, Soldiers = 0, Morale = 0,
                    MerchantPresenceChance = 0, Kind = SettlementKind.Facility }).ToArray(),
            Military = content.Military! with
            {
                Places = content.Military.Places.Select(p => p with { FacilityTypeId = 701 }).ToImmutableArray()
                    .Add(new(3, new(27, 7), new(1, 1)) { FacilityTypeId = 701, Combat = new(1, 1, 1, 1, 10) }),
                Facilities = content.Military.Facilities.Select(f => f with { IsWork = true }).ToImmutableArray()
                    .Add(content.Military.Facilities[0] with { Id = 701 }),
                ConstructionSites = [new(3, new(37, 7), [700])]
            }
        };
        var h = new Harness(content, w => w with
        {
            Units = [new(900, 1, new(17, 7), 1000)
            {
                FormationId = construction ? 600 : 500, CorpsId = 1, ServicePlaceId = 1,
                Order = construction ? new(MilitaryOrderKind.Construct, null, null) { Construction = new(3, 700, new(37, 7), 2) }
                    : new(MilitaryOrderKind.Transport, null, 3),
                MemberPositions = San9.Tests.FormationTestData.Members(ImmutableDictionary<int, BattleRank>.Empty.Add(2, BattleRank.Middle))
            }],
            Officers = w.Officers.Select(p => p.Id == 2 ? p with { SettlementId = null, UnitId = 900 } : p).ToImmutableArray(),
            Simulation = w.Simulation with { NextUnitId = 901 }
        });

        Assert.Equal(construction ? 0 : 1, h.Run(OriginalAiWork.Attack, 1).Submitted);
        if (!construction) Assert.Equal(3, Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).Order.SettlementId);
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

    [Theory]
    [InlineData(OriginalAiWork.Attack)]
    [InlineData(OriginalAiWork.FulfillRequest)]
    [InlineData(OriginalAiWork.Deploy)]
    [InlineData(OriginalAiWork.Retreat)]
    public void MilitaryDecisionsDoNotDependOnPlayerOrderDisplay(OriginalAiWork work)
    {
        var visibleDraws = new List<int>();
        var hiddenDraws = new List<int>();
        Harness Create(bool visible, List<int> draws) => new(Fixture(), w => EnemyArmy(w) with
        {
            Simulation = w.Simulation with { OtherForceOrdersVisible = visible, NextUnitId = 901 }
        }, maximum => { draws.Add(maximum); return 0; });
        var visible = Create(true, visibleDraws);
        var hidden = Create(false, hiddenDraws);
        var expected = visible.Run(work);
        var actual = hidden.Run(work);
        Assert.Equal(expected.End, actual.End);
        Assert.Equal(expected.Attempts, actual.Attempts);
        Assert.Equal(expected.Submitted, actual.Submitted);
        Assert.Equal(expected.PathQueries, actual.PathQueries);
        Assert.Equal(visibleDraws, hiddenDraws);
        string Commands(Harness h) => System.Text.Json.JsonSerializer.Serialize(h.Commands);
        Assert.Equal(Commands(visible), Commands(hidden));
        var world = hidden.Engine.Snapshot;
        StateAssert.Equal(visible.Engine.Snapshot, world with
            { Simulation = world.Simulation with { OtherForceOrdersVisible = true } });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SinglePersonPressureReliefUsesNormalTargetAndItsOwnPoliciesWithoutRandom(bool confused)
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
        var h = new Harness(content, w =>
        {
            var enemy = EnemyArmy(w, target: 3);
            return enemy with { Settlements = enemy.Settlements.Select(s => s.Id == 3
                ? s with { ConfusionDaysRemaining = confused ? 5 : 0 } : s).ToArray() };
        }, _ => throw new InvalidOperationException("Unexpected planning draw"));
        Assert.Equal(confused ? 0 : 1, h.Run(OriginalAiWork.Deploy, 1).Submitted);
        if (confused) { Assert.Empty(h.Commands); return; }
        var command = Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands));
        Assert.Single(command.OfficerIds);
        Assert.Equal(10000u, command.Soldiers);
        Assert.Equal(new UnitPolicies(false, false, false, true), command.Policies);
        Assert.Equal(0, command.DepartureDelayDays);
        Assert.Equal(3, command.Order.SettlementId);
    }

    [Theory]
    [InlineData(9999, ControllerKind.Automatic, "ai:2", 1)]
    [InlineData(10000, ControllerKind.Automatic, "ai:2", 0)]
    [InlineData(9999, ControllerKind.Player, "player", 0)]
    [InlineData(9999, ControllerKind.Player, "custom-player", 0)]
    [InlineData(9999, ControllerKind.Automatic, "player", 1)]
    public void BarbarianRetreatFallsBackToMainBuildingWhileEngaged(uint garrison, ControllerKind targetKind,
        string targetController, int recalled)
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
                    MemberPositions = San9.Tests.FormationTestData.Members(ImmutableDictionary<int, BattleRank>.Empty.Add(2, BattleRank.Middle)) }),
                Simulation = enemy.Simulation with
                {
                    NextUnitId = 902,
                    Controls = enemy.Simulation.Controls.Select(c => c.ForceId == 2
                        ? c with { Kind = targetKind, ControllerId = targetController } : c).ToImmutableArray()
                }
            };
        });
        Assert.Equal(recalled, h.Run(OriginalAiWork.Retreat).Submitted);
        if (recalled > 0) Assert.Equal(901, Assert.IsType<OrderUnitCommand>(Assert.Single(h.Commands)).UnitId);
    }

    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    public void ReliefDeductsServiceTroopsAndReturningTroopsBeforeDeploying(bool service, bool returning, int deployed)
    {
        var content = Fixture(1);
        content = content with
        {
            OriginalAi = content.OriginalAi with { Advanced = false },
            Settlements = content.Settlements.Append(content.Settlements[0] with { Id = 3, X = 5, Soldiers = 1000 }).ToArray(),
            RegionCityIds = new() { [1] = 1, [2] = 2, [3] = 1 },
            RegionNameKeys = new() { [1] = "region.1", [2] = "region.2", [3] = "region.3" },
            Military = content.Military! with
            {
                Places = content.Military.Places.Add(new(3, new(27, 7), new(1, 1)) { Combat = new(1, 1, 1, 1, 10) }),
                RegionIds = Enumerable.Range(0, 60).Select(i => (int?)(i % 20 < 2 ? 1 : i % 20 < 10 ? 3 : 2)).ToImmutableArray()
            }
        };
        var h = new Harness(content, w =>
        {
            var enemy = EnemyArmy(w, target: 3);
            return enemy with
            {
                Units = enemy.Units.ToImmutableArray().Add(new(901, 1, new(37, 7), 19000)
                {
                    FormationId = 400, CorpsId = 1, ServicePlaceId = service ? 3 : 1, CommandedThisXun = returning,
                    Order = returning ? new(MilitaryOrderKind.Return, null, 3) : new(MilitaryOrderKind.Move, new(47, 7), null)
                }),
                Simulation = enemy.Simulation with { NextUnitId = 902 }
            };
        }, _ => throw new InvalidOperationException("Relief must not draw planning random"));

        Assert.Equal(deployed, h.Run(OriginalAiWork.Deploy, 1).Submitted);
        if (deployed == 0) Assert.Empty(h.Commands);
        else Assert.Equal(3, Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).Order.SettlementId);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(4000, 0)]
    [InlineData(0, 1, false)]
    [InlineData(4000, 0, false)]
    public void RetreatCountsAlreadyReturningActiveAndHalfWoundedWithoutCountingUnitsAsSoldiers(uint wounded, int recalled, bool visible = true)
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
                    MemberPositions = San9.Tests.FormationTestData.Members(ImmutableDictionary<int, BattleRank>.Empty.Add(2, BattleRank.Middle)) })
                    .Add(new(903, 1, new(22, 7), 5000) { FormationId = 400, CommanderId = 3, Morale = 100, CorpsId = 1,
                        Order = new(MilitaryOrderKind.Move, new(47, 7), null),
                        MemberPositions = San9.Tests.FormationTestData.Members(ImmutableDictionary<int, BattleRank>.Empty.Add(3, BattleRank.Middle)) }),
                Simulation = enemy.Simulation with { NextUnitId = 904, OtherForceOrdersVisible = visible }
            };
        });
        Assert.Equal(recalled, h.Run(OriginalAiWork.Retreat).Submitted);
        if (recalled > 0) Assert.Equal(903, Assert.IsType<OrderUnitCommand>(Assert.Single(h.Commands)).UnitId);
    }

    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 1)]
    [InlineData(true, true, 0)]
    public void TransportAndSinglePersonFallbackRejectHostileTransit(bool hostile, bool fallback, int expected)
    {
        var content = Fixture();
        content = content with
        {
            Settlements = content.Settlements.Append(content.Settlements[0] with
                { Id = 3, X = 3, Soldiers = fallback ? 5000u : 30000u, Kind = fallback ? SettlementKind.Facility : SettlementKind.City })
                .Append(content.Settlements[0] with { Id = 4, X = 2, OwnerId = hostile ? 2 : 1,
                    Soldiers = 0, Morale = 0, Kind = SettlementKind.Facility })
                .Append(content.Settlements[0] with { Id = 5, X = 17, Soldiers = 0, Morale = 0 }).ToArray(),
            Officers = content.Officers.Select(p => p.State.Id is 2 or 3 ? p with { State = p.State with { SettlementId = 3 } } : p).ToArray(),
            CityConnections = [new(1, 2), new(2, 1), new(fallback ? 5 : 3, 1)],
            RegionCityIds = new() { [1] = 1, [2] = 2, [3] = fallback ? 5 : 3, [4] = 2, [5] = 5 },
            RegionNameKeys = new() { [1] = "region.1", [2] = "region.2", [3] = "region.3", [4] = "region.4", [5] = "region.5" },
            Military = content.Military! with
            {
                Places = content.Military.Places.Add(new(3, new(17, 7), new(1, 1))).Add(new(4, new(12, 7), new(1, 1)))
                    .Add(new(5, new(87, 7), new(1, 1))),
                RegionIds = Enumerable.Range(0, 60).Select(i => (int?)(i % 20 == 3 ? 3 : i % 20 == 2 ? 4 : i % 20 < 10 ? 1 : 2)).ToImmutableArray()
            }
        };
        var h = new Harness(content);
        Assert.Equal(expected, h.Run(OriginalAiWork.Transport, 1).Submitted);
        if (expected > 0) Assert.IsType(fallback ? typeof(DeployUnitCommand) : typeof(TransportUnitCommand), Assert.Single(h.Commands));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void TransportUsesReadyRearSourceAndTheLeastAbleOfficer(int state, bool crossCorps)
    {
        var content = Fixture();
        content = content with
        {
            Settlements = content.Settlements.Select(s => s.Id == 1 ? s with { CorpsId = crossCorps ? 3 : s.CorpsId } : s)
                .Append(content.Settlements[0] with { Id = 3, X = 3, Soldiers = 30000 }).ToArray(),
            Officers = content.Officers.Select(p => p.State.Id is 1 or 2 or 3 ? p with { State = p.State with { SettlementId = 3,
                BaseAbilities = p.State.Id == 3 ? new(10, 10, 10, 10) : p.State.BaseAbilities } } :
                p.State.Id == 4 && crossCorps ? p with { State = p.State with { Status = OfficerStatus.Governor } } : p).ToArray(),
            Information = content.Information with { Corps = crossCorps ? content.Information.Corps
                .Append(new(3, 1, 4, new(10000, 10000))).ToArray() : content.Information.Corps },
            CityConnections = [new(1, 2), new(2, 1), new(3, 1)],
            RegionCityIds = new() { [1] = 1, [2] = 2, [3] = 3 },
            RegionNameKeys = new() { [1] = "region.1", [2] = "region.2", [3] = "region.3" },
            Military = content.Military! with { Places = content.Military.Places.Add(new(3, new(17, 7), new(1, 1)) { Combat = new(10, 10, 10, 10, 50) }),
                RegionIds = Enumerable.Range(0, 60).Select(i => (int?)(i % 20 == 3 ? 3 : i % 20 < 10 ? 1 : 2)).ToImmutableArray(),
                TerritoryExits = Enumerable.Repeat(TerritoryExits.All, 60).ToImmutableArray() }
        };
        var h = new Harness(content, w =>
        {
            if (state == 0) return w;
            var enemy = EnemyArmy(w, target: 3);
            return enemy with
            {
                Settlements = enemy.Settlements.Select(s => s.Id == 3 ? s with { ConfusionDaysRemaining = state == 1 ? 5 : 0 } : s).ToArray(),
                Units = [.. enemy.Units.Select(u => u with { Position = state == 2 ? new(18, 7) : u.Position })]
            };
        }, draw: _ => throw new InvalidOperationException("Transport must not draw"));
        var before = h.Engine.Snapshot;
        var report = h.Run(OriginalAiWork.Transport);
        Assert.Equal(state == 0 ? 1 : 0, report.Submitted);
        if (state != 0)
        {
            Assert.Equal(0, report.Attempts);
            Assert.Empty(h.Commands);
            StateAssert.Equal(before, h.Engine.Snapshot);
            return;
        }
        var command = Assert.IsType<TransportUnitCommand>(Assert.Single(h.Commands));
        Assert.Equal(3, command.SettlementId);
        Assert.Equal(1, command.DestinationId);
        Assert.Equal(new[] { 3 }, command.OfficerIds);
        Assert.Equal(10000u, command.Soldiers);
        Assert.Equal(20000u, h.Engine.Snapshot.Settlements.Single(s => s.Id == 3).Soldiers);
    }

    [Theory]
    [InlineData(true, UnitCondition.Normal, 0)]
    [InlineData(false, UnitCondition.Normal, 1)]
    [InlineData(true, UnitCondition.Disguised, 1)]
    public void TransportBudgetExcludesOrdinaryConstructionButRetainsOtherConstruction(bool ordinary, UnitCondition condition, int deployed)
    {
        var content = WithConstruction(Fixture());
        content = content with
        {
            Settlements = content.Settlements.Append(content.Settlements[0] with { Id = 3, X = 3, Soldiers = 9000 }).ToArray(),
            Officers = content.Officers.Select(p => p.State.Id is 2 or 3 ? p with { State = p.State with { SettlementId = 3 } } : p).ToArray(),
            CityConnections = [new(1, 2), new(2, 1), new(3, 1)],
            RegionCityIds = new() { [1] = 1, [2] = 2, [3] = 3, [4] = 3 },
            RegionNameKeys = new() { [1] = "region.1", [2] = "region.2", [3] = "region.3", [4] = "region.4" },
            UnbuiltPlaces = [new(4, new(27, 12))],
            Military = content.Military! with
            {
                Places = content.Military.Places.Add(new(3, new(17, 7), new(1, 1)) { FacilityTypeId = 700 }),
                Facilities = content.Military.Facilities.Add(content.Military.Facilities[0] with { Id = 701 }),
                ConstructionSites = [new(4, new(27, 12), [700, 701])],
                RegionIds = Enumerable.Range(0, 60).Select(i => (int?)(i == 45 ? 4 : i % 20 == 3 ? 3 : i % 20 < 10 ? 1 : 2)).ToImmutableArray()
            },
            OriginalAi = content.OriginalAi with { Military = content.OriginalAi.Military with { OrdinaryFacilityIds = [700] } }
        };
        var h = new Harness(content, w => w with
        {
            Officers = w.Officers.Select(p => p.Id == 3 ? p with { SettlementId = null, UnitId = 900 } : p).ToImmutableArray(),
            Units = [new(900, 1, new(17, 12), 4000)
            {
                FormationId = 600, CorpsId = 1, ServicePlaceId = 3, Condition = condition, ConditionDaysRemaining = 2,
                Order = new(MilitaryOrderKind.Construct, null, null) { Construction = new(4, ordinary ? 700 : 701, new(27, 12), 3) },
                MemberPositions = San9.Tests.FormationTestData.Members(ImmutableDictionary<int, BattleRank>.Empty.Add(3, BattleRank.Middle))
            }],
            Simulation = w.Simulation with { NextUnitId = 901 }
        });

        Assert.Equal(deployed, h.Run(OriginalAiWork.Transport, 1).Submitted);
        if (deployed > 0)
        {
            var command = Assert.IsType<TransportUnitCommand>(Assert.Single(h.Commands));
            Assert.Equal(3, command.SettlementId);
            Assert.Equal(9000u, command.Soldiers);
        }
        else Assert.Empty(h.Commands);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AttackConvertsItsRouteToConstructionWithStableSiteTies(bool reverse)
    {
        var content = Fixture();
        var sites = new[] { new ConstructionSite(3, new(27, 6), [700]), new ConstructionSite(3, new(27, 8), [700]) };
        if (reverse) Array.Reverse(sites);
        content = content with
        {
            Settlements = content.Settlements.Select(s => s.Id == 2 ? s with { Soldiers = 60000 } : s).ToArray(),
            UnbuiltPlaces = [new(3, new(27, 7))],
            RegionCityIds = new() { [1] = 1, [2] = 2, [3] = 1 },
            RegionNameKeys = new() { [1] = "region.1", [2] = "region.2", [3] = "region.3" },
            Military = content.Military! with
            {
                Formations = content.Military.Formations.Add(new(600, 10, 0, 30000, 5, new(1, 1), TravelMode.Land)),
                ConstructionFormations = new(600, null),
                Facilities = [new(700, 1000, 100, new(1, 1), 0, true, true, true, 30000, null, null, null, [])],
                Places = content.Military.Places.Select(p => p with { FacilityTypeId = 700 }).ToImmutableArray(),
                ConstructionSites = sites.ToImmutableArray(),
                RegionIds = Enumerable.Range(0, 60).Select(i => (int?)(i % 20 < 4 ? 1 : i % 20 < 10 ? 3 : 2)).ToImmutableArray()
            },
            OriginalAi = content.OriginalAi with { Military = content.OriginalAi.Military with { OrdinaryFacilityIds = [700] } }
        };
        var h = new Harness(content);
        var report = h.Run(OriginalAiWork.Attack);
        Assert.Equal(1, report.Submitted);
        var command = Assert.IsType<ConstructFacilityCommand>(Assert.Single(h.Commands));
        Assert.Equal(3, command.RegionId);
        Assert.Equal(700, command.FacilityTypeId);
        Assert.Equal(sites[0].Position, command.Position);
        Assert.Equal(99000u, h.Engine.Snapshot.Corps.Single(c => c.Id == 1).Treasury.Money);
        Assert.Equal(MilitaryOrderKind.Construct, Assert.Single(h.Engine.Snapshot.Units).Order!.Kind);
    }

    [Theory]
    [InlineData(TacticEffect.CounterTrap, TacticEffect.TrapSelection)]
    [InlineData(TacticEffect.CounterConfusion, TacticEffect.Confusion)]
    public void DeploymentReadsEquippedEnemyTacticsRegardlessOfDisplay(TacticEffect counter, TacticEffect threat)
    {
        var content = Fixture();
        content = content with
        {
            Officers = content.Officers.Select(p => p with { State = p.State with
                { LearnedTactics = p.State.OwnerId == 1 ? [7000, 7001] : [7002] } }).ToArray(),
            Military = content.Military! with
            {
                Tactics = [new(7000, 6, counter, 1, 1, 0, true, false, [400]),
                    new(7001, 2, TacticEffect.Martial, 20, 1, 0, true, false, [400]),
                    new(7002, 6, threat, 1, 1, 0, true, false, [400])],
                Elevations = Enumerable.Repeat(0, 60).ToImmutableArray(),
                TrapTerrains = [new(0, null, false)]
            },
            OriginalAi = content.OriginalAi with { Military = content.OriginalAi.Military with { SlowTerrainIds = [0] } }
        };
        var draws = new List<int>[2] { [], [] };
        var hosts = new[] { true, false }.Select((visible, index) => new Harness(content, w =>
        {
            var enemy = EnemyArmy(w);
            return enemy with
            {
                Officers = enemy.Officers.Select(p => p.Id == 100 ? p with { EquippedTacticId = 7002 } : p).ToImmutableArray(),
                Simulation = enemy.Simulation with { OtherForceTacticsVisible = visible }
            };
        }, maximum => { draws[index].Add(maximum); return 0; })).ToArray();
        foreach (var h in hosts)
        {
            Assert.Equal(1, h.Run(OriginalAiWork.Deploy, 1).Submitted);
            var command = Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands));
            Assert.Contains(7000, command.Tactics.Values);
        }
        Assert.Equal(draws[0], draws[1]);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(hosts[0].Commands), System.Text.Json.JsonSerializer.Serialize(hosts[1].Commands));
        var world = hosts[1].Engine.Snapshot;
        StateAssert.Equal(hosts[0].Engine.Snapshot, world with
            { Simulation = world.Simulation with { OtherForceTacticsVisible = true } });
    }

    private static ScenarioInitialState WithNavalFormations(ScenarioInitialState content, bool slow, bool harbor = false) => content with
    {
        Settlements = content.Settlements.Select(s => s.Id == 2 && harbor ? s with { Kind = SettlementKind.Facility } : s).ToArray(),
        RegionCityIds = harbor ? new() { [1] = 1, [2] = 1 } : content.RegionCityIds,
        CityConnections = harbor ? [] : content.CityConnections,
        Military = content.Military! with
        {
            BasicNavalFormationId = 800,
            Formations = content.Military.Formations.Add(content.Military.Formations[0] with { Id = 800, TravelMode = TravelMode.Water })
                .Add(content.Military.Formations[0] with { Id = 801, TravelMode = TravelMode.Water }),
            Facilities = [new(700, 0, 500, new(1, 1), 0, false, true, true, 500000, null, null, null, []) { IsHarbor = true },
                new(701, 0, 500, new(1, 1), 0, false, true, true, 500000, null, null, null, [])],
            Places = content.Military.Places.Select(p => p with { FacilityTypeId = p.Id == 2 && harbor ? 700 : 701 }).ToImmutableArray()
        },
        OriginalAi = content.OriginalAi with { Military = content.OriginalAi.Military with { SlowTerrainIds = slow ? [0] : [], NavalPriority = [801] } }
    };

    [Theory]
    [InlineData(OriginalAiWork.Attack, false, false, 800)]
    [InlineData(OriginalAiWork.Attack, false, true, 800)]
    [InlineData(OriginalAiWork.Attack, true, false, 800)]
    [InlineData(OriginalAiWork.Attack, true, true, 801)]
    [InlineData(OriginalAiWork.FulfillRequest, false, false, 800)]
    [InlineData(OriginalAiWork.FulfillRequest, false, true, 800)]
    [InlineData(OriginalAiWork.FulfillRequest, true, false, 800)]
    [InlineData(OriginalAiWork.FulfillRequest, true, true, 801)]
    public void AttackSelectsAdvancedShipsOnlyForSlowHarborRoutes(OriginalAiWork work, bool slow, bool harbor, int naval)
    {
        var content = WithNavalFormations(Fixture(), slow, harbor);
        if (work == OriginalAiWork.FulfillRequest)
            content = content with
            {
                Forces = content.Forces.Append(new(3, "requester", 3, 50) { LeaderId = 101 }).ToArray(),
                Settlements = [.. content.Settlements, content.Settlements[0] with { Id = 3, OwnerId = 3, X = 17 }],
                Officers = [.. content.Officers, new("requester", new(101, 3, 3, new(30, 30, 30, 30), false) { Status = OfficerStatus.Ruler })],
                RegionCityIds = new(content.RegionCityIds) { [3] = 3 },
                RegionNameKeys = new(content.RegionNameKeys) { [3] = "requester" },
                Information = content.Information with
                    { Corps = content.Information.Corps.Append(new(3, 3, 101, new(1000, 1000)) { IsRoot = true }).ToArray() },
                Military = content.Military! with { Places = content.Military.Places.Add(new(3, new(87, 7), new(1, 1)) { FacilityTypeId = 701 }) }
            };
        var h = new Harness(content, w => work == OriginalAiWork.FulfillRequest ? w with
            { Simulation = w.Simulation with { AttackRequests = [new(1, 3, 2, 2, 19)] } } : w);
        Assert.Equal(1, h.Run(work, 1).Submitted);
        Assert.Equal(naval, Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).NavalFormationId);
    }

    [Theory]
    [InlineData(false, 800)]
    [InlineData(true, 801)]
    public void DefensiveDeploymentKeepsItsIndependentSlowRouteShipSelection(bool slow, int naval)
    {
        var content = WithNavalFormations(Fixture(), slow);
        var h = new Harness(content, w => EnemyArmy(w));
        Assert.Equal(1, h.Run(OriginalAiWork.Deploy, 1).Submitted);
        Assert.Equal(naval, Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).NavalFormationId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SelectedShipStillPassesFinalChecksWithChangedSpeedAndFootprint(int size)
    {
        var content = WithNavalFormations(Fixture(), slow: true, harbor: true);
        var baseline = new Harness(content with { OriginalAi = content.OriginalAi with
            { Military = content.OriginalAi.Military with { NavalPriority = [800] } } });
        var expected = baseline.Run(OriginalAiWork.Attack, 1);
        Assert.Equal(1, expected.Submitted);
        content = content with { Military = content.Military! with
            { Formations = content.Military.Formations.Select(f => f.Id == 801
                ? f with { Footprint = new(size, size), MovementBudget = 20 } : f).ToImmutableArray() } };
        var selected = new Harness(content);
        var actual = selected.Run(OriginalAiWork.Attack, 1);
        Assert.Equal(1, actual.Submitted);
        Assert.Equal(801, Assert.IsType<DeployUnitCommand>(Assert.Single(selected.Commands)).NavalFormationId);
        Assert.Equal(801, Assert.Single(selected.Engine.Snapshot.Units).NavalFormationId);
    }

    [Theory]
    [InlineData(false, 7001)]
    [InlineData(true, 7000)]
    public void SlowAttackUsesSpecialTacticPriorityOnlyAgainstHarbors(bool harbor, int tactic)
    {
        var content = Fixture();
        content = content with
        {
            Officers = content.Officers.Select(p => p.State.OwnerId == 1
                ? p with { State = p.State with { LearnedTactics = [7000, 7001] } } : p).ToArray(),
            OriginalAi = content.OriginalAi with { Military = content.OriginalAi.Military with { SlowTerrainIds = [0] } },
            Military = content.Military! with
            {
                Tactics = [new(7000, 6, TacticEffect.CounterTrap, 1, 1, 0, true, false, [400]),
                    new(7001, 2, TacticEffect.Martial, 20, 1, 0, true, false, [400])],
                Facilities = [new(700, 1000, 100, new(1, 1), 0, true, true, true, 30000, null, null, null, []) { IsHarbor = harbor }],
                Places = content.Military.Places.Select(p => p with { FacilityTypeId = 700 }).ToImmutableArray()
            }
        };
        var h = new Harness(content);
        Assert.Equal(1, h.Run(OriginalAiWork.Attack).Submitted);
        var command = Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands));
        Assert.Equal(tactic, command.Tactics[command.OfficerIds[0]]);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    public void DistantRecallUsesActualEngagementInsteadOfTemporaryTargetPresence(bool attacked, bool temporaryTarget, bool expected)
    {
        var content = Fixture();
        content = content with { Military = content.Military! with
            { Formations = content.Military.Formations.Select(f => f with { MovementBudget = 5 }).ToImmutableArray() } };
        var h = new Harness(content, w => w with
        {
            Units = [new(900, 1, new(87, 7), 5000)
                { FormationId = 400, ServicePlaceId = 1, Morale = 100,
                    Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 902 },
                    EngagementTargetId = temporaryTarget ? 902 : null },
                new(901, 2, new(86, 7), 5000)
                { FormationId = 400, ServicePlaceId = 2, Morale = 100, Order = attacked
                    ? new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 900 }
                    : new(MilitaryOrderKind.Move, new(86, 7), null) },
                new(902, 2, new(47, 7), 5000)
                { FormationId = 400, ServicePlaceId = 2, Morale = 100, Order = new(MilitaryOrderKind.Move, new(47, 7), null) }],
            Simulation = w.Simulation with { NextUnitId = 903 }
        });
        // The distant own target is outside its attack range; an incoming attacker can still engage it.
        Assert.Equal(expected ? 1 : 0, h.Run(OriginalAiWork.Retreat, 1).Submitted);
    }

    [Fact]
    public void RetreatDoesNotSearchRoutesForUnitsOutsideAllRecallGroups()
    {
        var h = new Harness(Fixture(), w => w with
        {
            Officers = w.Officers.Select(p => p.Id == 2 ? p with { SettlementId = null, UnitId = 901 } : p).ToArray(),
            Units = [new(901, 1, new(17, 7), 5000) { FormationId = 400, CommanderId = 2, Morale = 100,
                CorpsId = 1, ServicePlaceId = 1, Order = new(MilitaryOrderKind.Move, new(47, 7), null),
                MemberPositions = San9.Tests.FormationTestData.Members(ImmutableDictionary<int, BattleRank>.Empty.Add(2, BattleRank.Middle)) }],
            Simulation = w.Simulation with { NextUnitId = 902 }
        });
        var report = h.Run(OriginalAiWork.Retreat);
        Assert.Equal(0, report.PathQueries);
        Assert.Equal(0, report.Submitted);
        Assert.Empty(h.Commands);
    }

    [Fact]
    public void RareAttackTacticsDrawOnlyFromTheirOwnSubsetAfterAffinityDraws()
    {
        var content = Fixture();
        int[] ids = [7000, 7001, 7002];
        content = content with
        {
            Officers = content.Officers.Select(p => p.State.OwnerId == 1 ? p with { State = p.State with { LearnedTactics = ids.ToImmutableSortedSet() } } : p).ToArray(),
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
        Assert.Equal(unit.Position, Assert.Single(arrived.Steps));
        Assert.Null(arrived.Days);
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
        var content = PositionAuditTransportFixture();
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
                FormationGrants = available ? [new(400, FormationGrantCondition.Barbarian)] : [] } : f).ToArray(),
            Information = content.Information with { Corps = content.Information.Corps.Select(c => c.Id == 1 ? c with { Treasury = new(money, 100000) } : c).ToArray() },
            Military = content.Military! with { Formations = content.Military.Formations.Select(f => f.Id == 400
                ? f with { IsCommon = !available, DeploymentCost = 600 } : f).ToImmutableArray() }
        };
        var h = new Harness(content, w => w with { Forces = w.Forces.Select(f => f.Id == 1 ? f with { IsBarbarian = true } : f).ToImmutableArray() });
        Assert.Equal(expected ? 1 : 0, h.Run(OriginalAiWork.Attack).Submitted);
        if (expected) Assert.Equal(new UnitPolicies(true, true, true, false), Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).Policies);
    }
}
