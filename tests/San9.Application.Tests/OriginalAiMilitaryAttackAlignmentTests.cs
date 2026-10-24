using System.Collections.Immutable;
using San9.Application.AI;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed partial class OriginalAiMilitaryTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void ExplicitAttackBuildingPrecedesFallbackCities(int target)
    {
        var basis = Fixture();
        var content = basis with
        {
            Settlements = [.. basis.Settlements, basis.Settlements[1] with { Id = 3, Kind = SettlementKind.Facility, X = 13, Y = 2 }],
            RegionCityIds = new(basis.RegionCityIds) { [3] = 2 },
            RegionNameKeys = new(basis.RegionNameKeys) { [3] = "target" },
            Military = basis.Military! with { Places = basis.Military.Places.Add(new(3, new(67, 12), new(1, 1))
                { Combat = basis.Military.Places[1].Combat }) }
        };
        var h = new Harness(content);
        AssertAttackAlignmentCommandAvailable(content, h.Engine, 20000, target);
        Assert.Equal(1, h.Military.Run(OriginalAiWork.Attack, 1, h.State,
            new(1, OriginalAiMode.TargetSettlement, null, target)).Submitted);
        Assert.Equal(target, Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).Order.SettlementId);
    }

    [Theory]
    [InlineData(10000u, 1)]
    [InlineData(1000u, 0)]
    public void AttackStrengthKeepsWeakCityNegativeContribution(uint weakTroops, int expected)
    {
        var basis = AttackAlignmentReserveContent(false, 40000);
        var content = basis with
        {
            Settlements = basis.Settlements.Select(s => s.Id == 3 ? s with { Soldiers = weakTroops, Morale = 100 } : s).ToArray(),
            Officers = basis.Officers.Select(p => p.State.Id == 12 ? p with { State = p.State with { SettlementId = 3 } } : p).ToArray()
        };
        content.Validate();
        var h = new Harness(content);
        AssertAttackAlignmentCommandAvailable(content, h.Engine, 20000);
        Assert.Equal(expected, h.Run(OriginalAiWork.Attack, 1).Submitted);
    }

    [Theory]
    [InlineData(false, 26000u, 0u)]
    [InlineData(true, 26000u, 0u)]
    [InlineData(false, 35000u, 0u)]
    [InlineData(true, 35000u, 0u)]
    [InlineData(false, 35001u, 20000u)]
    [InlineData(true, 35001u, 20000u)]
    [InlineData(false, 40000u, 20000u)]
    [InlineData(true, 40000u, 20000u)]
    public void CityAndPassStrengthExcludeTheGarrisonReservedByAllocation(bool pass, uint troops, uint deployed)
    {
        var content = AttackAlignmentReserveContent(pass, troops);
        content.Validate();
        var h = new Harness(content);
        AssertAttackAlignmentCommandAvailable(content, h.Engine, Math.Min(troops - 10000, 20000));
        Assert.Equal(deployed == 0 ? 0 : 1, h.Run(OriginalAiWork.Attack, 1).Submitted);
        if (deployed == 0) Assert.Empty(h.Commands);
        else Assert.Equal(deployed, Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).Soldiers);
        Assert.Equal(troops - deployed, h.Engine.Snapshot.Settlements.Single(s => s.Id == 1).Soldiers);
    }

    [Theory]
    [InlineData(false, 499u, 0)]
    [InlineData(false, 500u, 1)]
    [InlineData(true, 499u, 1)]
    [InlineData(true, 500u, 1)]
    public void OrdinaryAttackOwnsTheFiveHundredMoneyGate(bool request, uint money, int expected)
    {
        var basis = AttackAlignmentRequest(Fixture());
        var content = basis with { Information = basis.Information with { Corps = basis.Information.Corps.Select(c => c.Id == 1
            ? c with { Treasury = c.Treasury with { Money = money } } : c).ToArray() } };
        var h = new Harness(content, w => w with { Simulation = w.Simulation with { AttackRequests = [new(1, 3, 2, 2, 19)] } });
        AssertAttackAlignmentCommandAvailable(content, h.Engine, 20000);
        Assert.Equal(expected, h.Run(request ? OriginalAiWork.FulfillRequest : OriginalAiWork.Attack, 1).Submitted);
    }

    [Theory]
    [InlineData(OriginalAiWork.Attack, 2, 1)]
    [InlineData(OriginalAiWork.Attack, 3, 2)]
    [InlineData(OriginalAiWork.Attack, 4, 2)]
    [InlineData(OriginalAiWork.FulfillRequest, 2, 1)]
    [InlineData(OriginalAiWork.FulfillRequest, 3, 2)]
    [InlineData(OriginalAiWork.FulfillRequest, 4, 2)]
    public void AttackCompanionBudgetCountsPeopleAfterTakingCommander(OriginalAiWork work, int available, int expected)
    {
        var content = Fixture(available);
        if (work == OriginalAiWork.FulfillRequest) content = AttackAlignmentRequest(content);
        var h = new Harness(content, w => work == OriginalAiWork.FulfillRequest ? w with
            { Simulation = w.Simulation with { AttackRequests = [new(1, 3, 2, 2, 19)] } } : w);
        Assert.Equal(1, h.Run(work, 1).Submitted);
        Assert.Equal(expected, Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).OfficerIds.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoAttackTargetDoesNotStartIndependentConstruction(bool unbuilt)
    {
        var basis = unbuilt ? AttackAlignmentConstruction() : Fixture();
        var content = basis with { Information = basis.Information with { Relations = [new(1, 2, 100)] } };
        var h = new Harness(content);
        if (unbuilt) AssertAttackAlignmentConstructionAvailable(content, h.Engine, 3, 700, new(27, 7));
        Assert.Equal(0, h.Run(OriginalAiWork.Attack).Submitted);
        Assert.Empty(h.Commands);
    }

    [Theory]
    [InlineData(44000u, 1, 0, false)]
    [InlineData(60000u, 1, 24, true)]
    [InlineData(60000u, 1, 25, false)]
    [InlineData(60000u, 2, 49, true)]
    [InlineData(60000u, 2, 50, false)]
    public void ConstructionConversionConsumesBothRouteEndpointsBeforeItsProbability(
        uint defenders, int intermediateCount, int draw, bool construct)
    {
        var basis = AttackAlignmentConstruction(intermediateCount);
        var content = basis with { Settlements = basis.Settlements.Select(s => s.Id == 2 ? s with { Soldiers = defenders } : s).ToArray() };
        var h = new Harness(content, draw: _ => draw);
        int region = intermediateCount == 1 ? 3 : 4;
        var position = intermediateCount == 1 ? new WorldPosition(27, 7) : new(42, 7);
        AssertAttackAlignmentConstructionAvailable(content, h.Engine, region, 700, position);
        Assert.Equal(1, h.Run(OriginalAiWork.Attack, 1).Submitted);
        if (construct) Assert.Equal(region, Assert.IsType<ConstructFacilityCommand>(Assert.Single(h.Commands)).RegionId);
        else Assert.Equal(2, Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).Order.SettlementId);
    }

    [Fact]
    public void ConstructionConversionUsesTheRoutePrefixEndingAtTheInterceptingBuilding()
    {
        var basis = AttackAlignmentConstruction();
        var content = basis with
        {
            Settlements = [.. basis.Settlements.Select(s => s.Id == 2 ? s with { Soldiers = 1000 } : s),
                basis.Settlements[1] with { Id = 3, X = 9, Soldiers = 60000, Kind = SettlementKind.Facility }],
            UnbuiltPlaces = [new(4, new(27, 7))],
            RegionCityIds = new() { [1] = 1, [2] = 2, [3] = 2, [4] = 1 },
            RegionNameKeys = new() { [1] = "one", [2] = "two", [3] = "intercepting", [4] = "unbuilt" },
            Military = basis.Military! with
            {
                Places = basis.Military.Places.Add(new(3, new(47, 7), new(1, 1))
                    { Combat = basis.Military.Places[1].Combat, FacilityTypeId = 700 }),
                ConstructionSites = [new(4, new(27, 7), [700])],
                RegionIds = Enumerable.Range(0, 60).Select(i => (int?)(i % 20 < 4 ? 1 : i % 20 < 8 ? 4 : i % 20 < 11 ? 3 : 2)).ToImmutableArray()
            }
        };
        var h = new Harness(content);
        AssertAttackAlignmentConstructionAvailable(content, h.Engine, 4, 700, new(27, 7));
        Assert.Equal(1, h.Run(OriginalAiWork.Attack, 1).Submitted);
        Assert.Equal(4, Assert.IsType<ConstructFacilityCommand>(Assert.Single(h.Commands)).RegionId);
    }

    [Theory]
    [InlineData(1000u, 1)]
    [InlineData(100000u, 0)]
    public void FailedSelectedTargetConsumesItsCityWithoutTryingAnotherTarget(uint defenders, int expected)
    {
        var basis = Fixture();
        var content = basis with
        {
            Settlements = [.. basis.Settlements.Select(s => s.Id == 2 ? s with { Soldiers = defenders } : s),
                basis.Settlements[1] with { Id = 3, X = 13, Y = 13, Soldiers = 1000 }],
            CityConnections = [new(1, 2), new(2, 1), new(1, 3), new(3, 1)],
            RegionCityIds = new(basis.RegionCityIds) { [3] = 3 },
            RegionNameKeys = new(basis.RegionNameKeys) { [3] = "weaker" },
            Military = basis.Military! with { Height = 20, TerrainIds = Enumerable.Repeat(0, 400).ToImmutableArray(),
                TerritoryExits = Enumerable.Repeat(TerritoryExits.All, 400).ToImmutableArray(),
                Places = basis.Military.Places.Add(new(3, new(67, 67), new(1, 1)) { Combat = basis.Military.Places[1].Combat }),
                RegionIds = Enumerable.Range(0, 400).Select(i => (int?)(i / 20 >= 10 ? 3 : i % 20 < 10 ? 1 : 2)).ToImmutableArray() }
        };
        var h = new Harness(content);
        AssertAttackAlignmentCommandAvailable(content, h.Engine, 20000, 3);
        var other = new Harness(content);
        Assert.Equal(1, other.Military.Run(OriginalAiWork.Attack, 1, other.State, new(1, OriginalAiMode.TargetSettlement, null, 3)).Submitted);
        Assert.Equal(3, Assert.IsType<DeployUnitCommand>(Assert.Single(other.Commands)).Order.SettlementId);
        Assert.Equal(expected, h.Military.Run(OriginalAiWork.Attack, 1, h.State, new(1, OriginalAiMode.TargetSettlement, null, 2)).Submitted);
    }

    [Theory]
    [InlineData(ConstructionDirection.None, false, false, 700)]
    [InlineData(ConstructionDirection.None, true, false, 700)]
    [InlineData(ConstructionDirection.Horizontal, true, false, 716)]
    [InlineData(ConstructionDirection.Vertical, true, false, 716)]
    [InlineData(ConstructionDirection.Horizontal | ConstructionDirection.Vertical, true, true, 711)]
    [InlineData(ConstructionDirection.Vertical, true, true, 712)]
    public void ConstructionShapePreferenceReadsItsOwnSitePermission(
        ConstructionDirection directions, bool prefer, bool elongated, int expected)
    {
        var basis = AttackAlignmentConstruction();
        var content = basis with
        {
            Officers = basis.Officers.Select(p => p.State.OwnerId == 1 ? p with { State = p.State with { LearnedTactics = [20] } } : p).ToArray(),
            Military = basis.Military! with
            {
                Tactics = [new(20, 6, TacticEffect.StoneFormation, 0, 0, 0, false, false, [])],
                Facilities = basis.Military.Facilities.Add(new(716, 2000, 100, new(3, 3), 0, true, false, false, 30000, 20, null, null, []))
                    .Add(new(711, 3000, 100, new(3, 1), 0, true, false, false, 30000, null, null, null, []))
                    .Add(new(712, 4000, 100, new(1, 3), 0, true, false, false, 30000, null, null, null, [])),
                ConstructionSites = [new(3, new(27, 7), elongated ? [700, 711, 712] : [700, 716]) { PreferenceDirections = directions }]
            }
        };
        var h = new Harness(content);
        AssertAttackAlignmentConstructionAvailable(content, h.Engine, 3, expected, new(27, 7));
        Assert.Equal(1, h.Run(OriginalAiWork.Attack, 1, prefer ? OriginalAiGroup.ReducedActivity : OriginalAiGroup.Unassigned).Submitted);
        Assert.Equal(expected, Assert.IsType<ConstructFacilityCommand>(Assert.Single(h.Commands)).FacilityTypeId);
    }

    [Fact]
    public void EmptyHorizontalPreferenceFallsBackToOrdinaryRatherThanTryingVertical()
    {
        var basis = AttackAlignmentConstruction();
        var content = basis with { Military = basis.Military! with
        {
            Facilities = basis.Military.Facilities.Add(new(712, 4000, 100, new(1, 3), 0, true, false, false, 30000, null, null, null, [])),
            ConstructionSites = [new(3, new(27, 7), [700, 712])
                { PreferenceDirections = ConstructionDirection.Horizontal | ConstructionDirection.Vertical }]
        } };
        var h = new Harness(content);
        Assert.Equal(1, h.Run(OriginalAiWork.Attack, 1, OriginalAiGroup.ReducedActivity).Submitted);
        Assert.Equal(700, Assert.IsType<ConstructFacilityCommand>(Assert.Single(h.Commands)).FacilityTypeId);
    }

    private static ScenarioInitialState AttackAlignmentReserveContent(bool pass, uint sourceTroops)
    {
        var basis = Fixture();
        return basis with
        {
            Settlements = [.. basis.Settlements.Select(s => s.Id == 1 ? s with
                { Kind = pass ? SettlementKind.Facility : SettlementKind.City, Soldiers = sourceTroops } : s with { Soldiers = 20000 }),
                basis.Settlements[0] with { Id = 3, X = 1, Y = 2, Soldiers = 0, Morale = 0 }],
            CityConnections = pass ? [new(3, 2), new(2, 3)] : [new(1, 2), new(2, 1), new(3, 2), new(2, 3)],
            RegionCityIds = new() { [1] = pass ? 3 : 1, [2] = 2, [3] = 3 },
            RegionNameKeys = new() { [1] = "one", [2] = "two", [3] = "three" },
            OriginalAi = basis.OriginalAi with { Military = basis.OriginalAi.Military with { FortifiedFacilityIds = [700] } },
            Military = basis.Military! with
            {
                Facilities = [new(700, 0, 100, new(1, 1), 0, false, true, true, 500000, null, null, null, []),
                    new(702, 0, 100, new(1, 1), 0, false, true, true, 500000, null, null, null, [])],
                Places = basis.Military.Places.Select(p => p with { FacilityTypeId = p.Id == 1 && pass ? 700 : 702 })
                    .Append(new(3, new(7, 12), new(1, 1)) { Combat = basis.Military.Places[0].Combat, FacilityTypeId = 702 }).ToImmutableArray(),
                RegionIds = Enumerable.Range(0, 60).Select(i => (int?)(i / 20 == 2 ? 3 : i % 20 < 10 ? 1 : 2)).ToImmutableArray()
            }
        };
    }

    private static ScenarioInitialState AttackAlignmentConstruction(int intermediates = 1)
    {
        var basis = WithConstruction(Fixture());
        return basis with
        {
            Settlements = basis.Settlements.Select(s => s.Id == 2 ? s with { Soldiers = 60000 } : s).ToArray(),
            OriginalAi = basis.OriginalAi with { Military = basis.OriginalAi.Military with { OrdinaryFacilityIds = [700] } },
            UnbuiltPlaces = intermediates == 1 ? basis.UnbuiltPlaces : [.. basis.UnbuiltPlaces, new(4, new(42, 7))],
            RegionCityIds = intermediates == 1 ? basis.RegionCityIds : new(basis.RegionCityIds) { [4] = 1 },
            RegionNameKeys = intermediates == 1 ? basis.RegionNameKeys : new(basis.RegionNameKeys) { [4] = "second-unbuilt" },
            Military = basis.Military! with
            {
                ConstructionSites = intermediates == 1 ? basis.Military.ConstructionSites : [new(4, new(42, 7), [700])],
                RegionIds = Enumerable.Range(0, 60).Select(i => (int?)(i % 20 < 4 ? 1 : i % 20 < 10
                    ? intermediates == 1 || i % 20 < 7 ? 3 : 4 : 2)).ToImmutableArray()
            }
        };
    }

    private static ScenarioInitialState AttackAlignmentRequest(ScenarioInitialState basis) => basis with
    {
        Forces = [.. basis.Forces, new(3, "requester", 3, 50) { LeaderId = 101 }],
        Settlements = [.. basis.Settlements, basis.Settlements[1] with { Id = 3, OwnerId = 3, CorpsId = 3, X = 17 }],
        Officers = [.. basis.Officers, new("requester", new(101, 3, 3, new(30, 30, 30, 30), false) { Status = OfficerStatus.Ruler })],
        RegionCityIds = new(basis.RegionCityIds) { [3] = 3 },
        RegionNameKeys = new(basis.RegionNameKeys) { [3] = "requester" },
        Information = basis.Information with { Corps = [.. basis.Information.Corps, new(3, 3, 101, new(1000, 1000)) { IsRoot = true }] },
        Military = basis.Military! with { Places = basis.Military.Places.Add(new(3, new(87, 7), new(1, 1))) }
    };

    private static void AssertAttackAlignmentCommandAvailable(ScenarioInitialState content, SimulationEngine engine, uint soldiers, int target = 2)
    {
        var command = new DeployUnitCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence, 1, [1], soldiers, 400,
            new(MilitaryOrderKind.AttackSettlement, null, target)) { ConfirmAttack = true };
        Assert.Equal(CommandRejection.None, engine.GetPreparationEligibility(new("player"), command));
        Assert.True(GameSession.EngineFactory(content)(WorldState.Restore(engine.Snapshot)).Execute(new("player"), command).Submission.Accepted);
    }

    private static void AssertAttackAlignmentConstructionAvailable(ScenarioInitialState content, SimulationEngine engine,
        int region, int facility, WorldPosition position)
    {
        var command = new ConstructFacilityCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence, 1, [1], 20000, region, facility, position);
        Assert.Equal(CommandRejection.None, engine.GetPreparationEligibility(new("player"), command));
        Assert.True(GameSession.EngineFactory(content)(WorldState.Restore(engine.Snapshot)).Execute(new("player"), command).Submission.Accepted);
    }
}
