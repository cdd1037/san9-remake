using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed partial class OriginalAiMilitaryTests
{
    [Theory]
    [InlineData(OriginalAiWork.Attack, true)]
    [InlineData(OriginalAiWork.Deploy, true)]
    [InlineData(OriginalAiWork.FulfillRequest, true)]
    [InlineData(OriginalAiWork.Attack, false)]
    [InlineData(OriginalAiWork.Deploy, false)]
    [InlineData(OriginalAiWork.FulfillRequest, false)]
    public void LowWarBarbarianDeploymentStartsAtTheFrontWhileOrdinaryDeploymentKeepsItsOffset(
        OriginalAiWork work, bool barbarian)
    {
        var content = Fixture(2);
        content = content with
        {
            Forces = content.Forces.Select(f => f.Id == 1
                ? f with { IsBarbarian = barbarian, AdditionalFormationIds = barbarian ? [400] : [] } : f).ToArray(),
            Officers = content.Officers.Select(p => p.State.OwnerId == 1 ? p with
            {
                State = p.State with
                {
                    BaseAbilities = p.State.BaseAbilities with { War = p.State.Id == 1 ? 18 : 46 }
                }
            } : p).ToArray(),
            Military = content.Military! with
            {
                Facilities = work == OriginalAiWork.FulfillRequest
                    ? [new(701, 0, 500, new(1, 1), 0, false, true, true, 500000, null, null, null, [])]
                    : content.Military.Facilities,
                Places = work == OriginalAiWork.FulfillRequest
                    ? content.Military.Places.Select(p => p with { FacilityTypeId = 701 }).ToImmutableArray()
                    : content.Military.Places,
                Formations = content.Military.Formations.Select(f => f.Id == 400 ? f with
                {
                    IsCommon = !barbarian,
                    CaptureRanks = [BattleRank.Front, BattleRank.Front, BattleRank.Middle, BattleRank.Rear, BattleRank.Rear]
                } : f).ToImmutableArray()
            }
        };
        content = WithFormationRequest(content, work);
        var h = new Harness(content, w =>
        {
            w = WithFormationRequest(w, work);
            w = w with
            {
                Forces = w.Forces.Select(f => f.Id == 1 ? f with { IsBarbarian = barbarian } : f).ToImmutableArray()
            };
            return work == OriginalAiWork.Deploy ? EnemyArmy(w) : w;
        });
        Assert.Equal(1, h.Run(work, 1).Submitted);
        var command = Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands));
        Assert.Equal(1, Assert.Single(command.OfficerIds));
        Assert.Equal(400, command.FormationId);
        var expected = barbarian ? BattleRank.Front : BattleRank.Rear;
        Assert.Equal(expected, command.Ranks[1]);
        Assert.Equal(expected, h.Engine.Snapshot.Units.Single(u => u.OwnerId == 1).MemberRanks[1]);
    }

    [Theory]
    [InlineData(OriginalAiWork.Attack, UnitCondition.Normal, true)]
    [InlineData(OriginalAiWork.Attack, UnitCondition.NoFormation, true)]
    [InlineData(OriginalAiWork.Attack, UnitCondition.Confused, true)]
    [InlineData(OriginalAiWork.Attack, UnitCondition.HighSpirits, true)]
    [InlineData(OriginalAiWork.Attack, UnitCondition.Routed, false)]
    [InlineData(OriginalAiWork.Attack, UnitCondition.Disguised, false)]
    [InlineData(OriginalAiWork.FulfillRequest, UnitCondition.Normal, true)]
    [InlineData(OriginalAiWork.FulfillRequest, UnitCondition.NoFormation, true)]
    [InlineData(OriginalAiWork.FulfillRequest, UnitCondition.Confused, true)]
    [InlineData(OriginalAiWork.FulfillRequest, UnitCondition.HighSpirits, true)]
    [InlineData(OriginalAiWork.FulfillRequest, UnitCondition.Routed, false)]
    [InlineData(OriginalAiWork.FulfillRequest, UnitCondition.Disguised, false)]
    public void SiegeFormationSelectionUsesTheExistingUnitsNominalFormationAndAttackEligibility(
        OriginalAiWork work, UnitCondition condition, bool eligible)
    {
        var content = WithNavalFormations(Fixture(), false, true);
        var land = content.Military!.Formations[0];
        content = content with
        {
            Military = content.Military with { Formations = content.Military.Formations
                .Add(land with { Id = 600, Combat = new(1, 1, 1, 1, 10) }) },
            OriginalAi = content.OriginalAi with { Military = content.OriginalAi.Military with
                { Formations = content.OriginalAi.Military.Formations.Add(OriginalAiFormationRole.Ram, 600) } }
        };
        content = WithFormationRequest(content, work);
        Harness Host(bool includeExistingUnit, List<int> draws) => new(content, w =>
        {
            w = WithFormationRequest(w, work);
            return w with
            {
                Settlements = w.Settlements.Select(s => s.Id == 2 ? s with { Durability = 100 } : s).ToArray(),
                Units = includeExistingUnit
                    ? [new(900, 2, new(62, 7), 1000) { FormationId = 600, NavalFormationId = 800,
                        ServicePlaceId = 2, Condition = condition, HasEnteredDailyCycle = true,
                        Order = new(MilitaryOrderKind.AttackSettlement, null, 2) }]
                    : [],
                Simulation = w.Simulation with { NextUnitId = 901 }
            };
        }, maximum => { draws.Add(maximum); return 0; });
        var baselineDraws = new List<int>();
        var baseline = Host(false, baselineDraws);
        Assert.Equal(1, baseline.Run(work, 1).Submitted);
        Assert.Equal(400, Assert.IsType<DeployUnitCommand>(Assert.Single(baseline.Commands)).FormationId);
        var draws = new List<int>();
        var h = Host(true, draws);
        Assert.Equal(1, h.Run(work, 1).Submitted);
        var command = Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands));
        int expected = eligible ? 600 : 400;
        Assert.Equal(expected, command.FormationId);
        Assert.Equal(expected, h.Engine.Snapshot.Units.Single(u => u.OwnerId == 1).FormationId);
        Assert.Equal(baselineDraws, draws.Take(baselineDraws.Count));
        Assert.Equal(eligible ? new[] { 100 } : Array.Empty<int>(), draws.Skip(baselineDraws.Count));
    }
}
