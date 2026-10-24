using System.Collections.Immutable;
using San9.Application.AI;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed partial class OriginalAiMilitaryTests
{
    private static ScenarioInitialState WithFormationRequest(ScenarioInitialState content, OriginalAiWork work) =>
        work != OriginalAiWork.FulfillRequest ? content : content with
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

    private static WorldSnapshot WithFormationRequest(WorldSnapshot world, OriginalAiWork work) =>
        work != OriginalAiWork.FulfillRequest ? world : world with
            { Simulation = world.Simulation with { AttackRequests = [new(1, 3, 2, 2, 19)] } };

    [Theory]
    [InlineData(OriginalAiWork.Attack, true, true, true, 400)]
    [InlineData(OriginalAiWork.Attack, true, false, true, 600)]
    [InlineData(OriginalAiWork.Attack, false, true, true, 400)]
    [InlineData(OriginalAiWork.Attack, true, true, false, 400)]
    [InlineData(OriginalAiWork.FulfillRequest, true, true, true, 400)]
    [InlineData(OriginalAiWork.FulfillRequest, true, false, true, 600)]
    [InlineData(OriginalAiWork.FulfillRequest, false, true, true, 400)]
    [InlineData(OriginalAiWork.FulfillRequest, true, true, false, 400)]
    public void SlowHarborAttackChoosesMobilityBeforeSiegeProbability(OriginalAiWork work, bool existing, bool slow,
        bool marchAffordable, int formation)
    {
        var content = WithNavalFormations(Fixture(), slow, true);
        var land = content.Military!.Formations[0];
        content = content with
        {
            Military = content.Military with { Formations = content.Military.Formations
                .Add(land with { Id = 600, Combat = new(1, 1, 1, 1, 10) })
                .Add(land with { Id = 650, DeploymentCost = 100001 }) },
            OriginalAi = content.OriginalAi with { Military = content.OriginalAi.Military with
                { Formations = content.OriginalAi.Military.Formations.Add(OriginalAiFormationRole.Ram, 600)
                    .SetItem(OriginalAiFormationRole.March, marchAffordable ? 400 : 650) } }
        };
        content = WithFormationRequest(content, work);
        Harness Host(bool includeSiege, List<int> draws) => new(content, w =>
        {
            w = WithFormationRequest(w, work);
            return w with
            {
                Settlements = w.Settlements.Select(s => s.Id == 2 ? s with { Durability = 100 } : s).ToArray(),
                Units = includeSiege ? [new(900, 2, new(62, 7), 1000) { FormationId = 600, NavalFormationId = 800,
                    ServicePlaceId = 2, Order = new(MilitaryOrderKind.AttackSettlement, null, 2) }] : [],
                Simulation = w.Simulation with { NextUnitId = 901 }
            };
        }, maximum => { draws.Add(maximum); return 0; });
        var draws = new List<int>();
        var h = Host(existing, draws);
        Assert.Equal(1, h.Run(work, 1).Submitted);
        var command = Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands));
        Assert.Equal(formation, command.FormationId);
        Assert.Equal(slow ? 801 : 800, command.NavalFormationId);
        if (existing)
        {
            var baselineDraws = new List<int>();
            var baseline = Host(false, baselineDraws);
            Assert.Equal(1, baseline.Run(work, 1).Submitted);
            Assert.Equal(slow ? 0 : 1, draws.Count(n => n == 100) - baselineDraws.Count(n => n == 100));
            if (slow) Assert.Equal(baselineDraws, draws);
        }
    }

    [Theory]
    [InlineData(OriginalAiWork.Attack)]
    [InlineData(OriginalAiWork.FulfillRequest)]
    public void RouteObstacleKeepsSiegePriorityEvenOnSlowHarborAttack(OriginalAiWork work)
    {
        var content = WithNavalFormations(Fixture(), true, true);
        var land = content.Military!.Formations[0];
        content = content with
        {
            Settlements = [.. content.Settlements, content.Settlements[1] with
                { Id = 4, OwnerId = 1, X = 7, Soldiers = 0, Morale = 0 }],
            RegionCityIds = new(content.RegionCityIds) { [4] = 1 },
            RegionNameKeys = new(content.RegionNameKeys) { [4] = "obstacle" },
            Military = content.Military with
            {
                Formations = content.Military.Formations.Add(land with { Id = 600, Combat = new(1, 1, 1, 1, 10) }),
                Facilities = content.Military.Facilities.Add(content.Military.Facilities[1] with { Id = 702 }),
                Places = content.Military.Places.Add(new(4, new(37, 7), new(1, 1)) { FacilityTypeId = 702 }),
                RegionIds = Enumerable.Range(0, 60).Select(i => (int?)(i % 20 < 5 ? 1 : i % 20 < 10 ? 4 : 2)).ToImmutableArray()
            },
            OriginalAi = content.OriginalAi with { Military = content.OriginalAi.Military with
                { RouteObstacleFacilityIds = [702], Formations = content.OriginalAi.Military.Formations.Add(OriginalAiFormationRole.Ram, 600) } }
        };
        var h = new Harness(WithFormationRequest(content, work), w => WithFormationRequest(w, work));
        Assert.Equal(1, h.Run(work, 1).Submitted);
        Assert.Equal(600, Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).FormationId);
    }

    [Theory]
    [InlineData(OriginalAiWork.Attack, 2, false, 600)]
    [InlineData(OriginalAiWork.Attack, 3, false, 400)]
    [InlineData(OriginalAiWork.Attack, 4, false, 400)]
    [InlineData(OriginalAiWork.Attack, 4, true, 700)]
    [InlineData(OriginalAiWork.FulfillRequest, 2, false, 600)]
    [InlineData(OriginalAiWork.FulfillRequest, 3, false, 400)]
    [InlineData(OriginalAiWork.FulfillRequest, 4, false, 400)]
    [InlineData(OriginalAiWork.FulfillRequest, 4, true, 700)]
    public void MixedRangedAttackKeepsItsFormationBranchBeforeGeneralFallback(OriginalAiWork work, int category,
        bool mixedFormationAvailable, int formation)
    {
        var content = WithNavalFormations(Fixture(), false);
        var land = content.Military!.Formations[0];
        content = content with
        {
            Officers = content.Officers.Select(p => p with { State = p.State with
                { LearnedTactics = [category == 3 || category == 4 && p.State.Id % 2 == 0 ? 7001 : 7000] } }).ToArray(),
            Military = content.Military with
            {
                Formations = content.Military.Formations
                    .Add(land with { Id = 600, Combat = new(1, 1, 1, 1, 10) })
                    .Add(land with { Id = 700, IsCommon = mixedFormationAvailable, Combat = new(1, 1, 1, 1, 10) }),
                Tactics = [new(7000, 2, TacticEffect.Martial, 20, 1, 0, true, false, [400, 600, 700]),
                    new(7001, 3, TacticEffect.Martial, 20, 1, 0, true, false, [400, 600, 700])]
            },
            OriginalAi = content.OriginalAi with { Military = content.OriginalAi.Military with
                { Formations = content.OriginalAi.Military.Formations.Add(OriginalAiFormationRole.Mobile, 600)
                    .Add(OriginalAiFormationRole.MixedRanged, 700) } }
        };
        var h = new Harness(WithFormationRequest(content, work), w => WithFormationRequest(w, work));
        Assert.Equal(1, h.Run(work, 1).Submitted);
        var command = Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands));
        if (category == 4)
        {
            Assert.Contains(7000, command.Tactics.Values);
            Assert.Contains(7001, command.Tactics.Values);
        }
        Assert.Equal(formation, command.FormationId);
    }
}
