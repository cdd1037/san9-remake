using System.Collections.Immutable;
using San9.Application.AI;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed partial class OriginalAiActionsTests
{
    [Theory]
    [InlineData(10999u, 0)]
    [InlineData(11000u, 1)]
    [InlineData(12999u, 1)]
    [InlineData(13000u, 1)]
    public void RearCityReliefRetainsEightThousandPlusThePayload(uint soldiers, int expected)
    {
        var content = Fixture();
        content = content with
        {
            Settlements = content.Settlements.Select(s => s.Id == 1 ? s with { Soldiers = soldiers } : s).ToArray(),
            Military = content.Military! with { RegionIds = Enumerable.Repeat<int?>(1, 60).ToImmutableArray() }
        };
        var h = new Harness(content, w => w with
        {
            Units = w.Units.Where(u => u.Id == 10).ToArray(),
            Officers = w.Officers.Select(p => p.Id == 9 ? p with { UnitId = null, SettlementId = 2 } : p).ToArray()
        });
        var geography = new OriginalAiGeography(content, new(content.Military), h.Engine.Snapshot);
        Assert.False(geography.Frontier(1, 1));
        Assert.False(geography.Priority(1, 1));

        Assert.Equal(expected, h.Run(OriginalAiWork.Reinforce).Submitted);

        Assert.Equal(expected == 0 ? soldiers : soldiers - 3000, h.Engine.Snapshot.Settlements.Single(s => s.Id == 1).Soldiers);
    }

    [Theory]
    [InlineData(12000u, 12)]
    [InlineData(13000u, 11)]
    public void ReliefPriorityGateFiltersTheOriginBeforeItCanConsumeTheFirstTarget(uint soldiers, int actor)
    {
        var basis = Fixture();
        var content = basis with
        {
            Settlements = basis.Settlements.Select(s => s.Id == 1 ? s with { Soldiers = soldiers } : s)
                .Append(basis.Settlements[0] with { Id = 4, X = 5, Soldiers = 100000 }).ToArray(),
            Officers = basis.Officers.Where(p => p.State.Id != 9).Select(p => p.State.Id == 12 ? p with
                { State = p.State with { SettlementId = 4 } } : p).Append(basis.Officers.Single(p => p.State.Id == 8) with
                { NameKey = "second-target", State = basis.Officers.Single(p => p.State.Id == 8).State with { Id = 13, UnitId = 11 } }).ToArray(),
            Units = [basis.Units.Single(u => u.Id == 10), basis.Units.Single(u => u.Id == 10) with
                { Id = 11, Position = new(32, 7), Soldiers = 6000, ServicePlaceId = 4, CommanderId = 13,
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(13, BattleRank.Middle) }],
            CityConnections = [new(1, 4), new(4, 1), new(4, 2), new(2, 4), new(2, 3), new(3, 2)],
            RegionCityIds = new() { [1] = 1, [2] = 2, [3] = 3, [4] = 4 },
            RegionNameKeys = new() { [1] = "one", [2] = "two", [3] = "three", [4] = "donor" },
            Military = basis.Military! with
            {
                Places = basis.Military.Places.Add(new(4, new(27, 7), new(1, 1))),
                RegionIds = Enumerable.Range(0, 60).Select(i => (int?)(i % 20 < 4 ? 1 : i % 20 < 7 ? 4 : i % 20 < 13 ? 2 : 3)).ToImmutableArray()
            }
        };
        var h = new Harness(content, w => w with { Officers = w.Officers.Select(p => p.Id == 1 ? p with { Acted = true } : p).ToArray() });
        var geography = new OriginalAiGeography(content, new(content.Military!), h.Engine.Snapshot);
        Assert.False(geography.Frontier(1, 1));
        Assert.True(geography.Priority(1, 1));

        var report = h.Run(OriginalAiWork.Reinforce);

        Assert.Equal(1, report.Submitted);
        var command = Assert.IsType<UnitStrategyCommand>(Assert.Single(h.Commands));
        Assert.Equal(10, command.TargetUnitId);
        Assert.Equal(actor, command.OfficerId);
    }

    [Theory]
    [InlineData(MilitaryOrderKind.Return, false, false, 0)]
    [InlineData(MilitaryOrderKind.Return, true, false, 1)]
    [InlineData(MilitaryOrderKind.Return, false, true, 0)]
    [InlineData(MilitaryOrderKind.AttackSettlement, false, false, 1)]
    public void ReturningReliefTargetRequiresActualEngagementEvenWithAStaleTemporaryTarget(
        MilitaryOrderKind kind, bool engaged, bool temporaryTarget, int expected)
    {
        var basis = Fixture();
        var combat = new CombatProfile(10, 10, 10, 10, 10);
        var content = basis with
        {
            Military = basis.Military! with
            {
                Formations = [basis.Military.Formations[0] with { Combat = combat, Attack = new(1, true) }],
                Places = [.. basis.Military.Places.Select(p => p with { Combat = combat })]
            }
        };
        var h = new Harness(content, world => world with
        {
            Units = world.Units.Select(u => u.Id == 10 ? u with
            {
                Position = new(47, 7), Order = new(kind, null, kind == MilitaryOrderKind.AttackSettlement ? 2 : 1),
                EngagementTargetId = temporaryTarget ? 20 : null
            } : u with
            {
                Position = engaged ? new(48, 7) : new(97, 7),
                Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 10 }
            }).ToArray()
        });
        var snapshot = h.Engine.Snapshot;
        var query = MilitaryEngagementQuery.Capture(snapshot, new(content.Military!));
        Assert.Equal(engaged, query.Units.Contains(10));
        Assert.DoesNotContain(1, query.Settlements);
        var manual = new UnitStrategyCommand(Guid.NewGuid(), 1, snapshot.Simulation.NextSequence, 11, UnitStrategyKind.Reinforce, 10);
        Assert.Equal(CommandRejection.None, h.Engine.GetPreparationEligibility(new("player"), manual));
        var clone = GameSession.EngineFactory(content)(WorldState.Restore(snapshot));
        Assert.True(clone.Execute(new("player"), manual).Submission.Accepted);

        var report = h.Run(OriginalAiWork.Reinforce);

        Assert.Equal(expected, report.Submitted);
        if (expected == 0)
        {
            Assert.Equal(0, report.Attempts);
            Assert.Empty(h.Commands);
        }
    }
}
