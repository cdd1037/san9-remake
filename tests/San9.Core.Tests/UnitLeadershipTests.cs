using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class UnitLeadershipTests
{
    private static OfficerState Person(int id, int leadership, int? office = null, OfficerStatus status = OfficerStatus.Serving) =>
        new(id, 1, 1, new(50, 50, 50, leadership), false) { OfficeId = office, Status = status };

    private static int[] Ordered(GovernanceRulebook rules, params OfficerState[] people) =>
        rules.OrderCommanders(people, people.ToDictionary(p => p.Id, p => p.BaseAbilities)).Select(p => p.Id).ToArray();

    [Fact]
    public void IdentityPrecedesOfficeAndLeadership()
    {
        var rules = new GovernanceRulebook([new(900, 30000, 10)]);
        Assert.Equal([7, 8, 9], Ordered(rules, Person(9, 100, 900),
            Person(8, 20, status: OfficerStatus.Governor), Person(7, 1, status: OfficerStatus.Ruler)));
    }

    [Fact]
    public void CapacityThenOfficePriorityPrecedeLeadershipWithoutIdentityConstants()
    {
        var rules = new GovernanceRulebook([new(8000, 20000, 1), new(9000, 15000, 50), new(10000, 15000, 80)]);
        Assert.Equal([3, 2, 1], Ordered(rules, Person(1, 100, 9000), Person(2, 20, 10000), Person(3, 1, 8000)));
    }

    [Fact]
    public void MissingOfficeUsesTenAndLowCapacityDoesNotCompareOfficePriority()
    {
        var rules = new GovernanceRulebook([new(9000, 10000, 999), new(8000, 5000, 999)]);
        Assert.Equal([30, 20, 10], Ordered(rules, Person(10, 100, 8000), Person(20, 60, 9000), Person(30, 70)));
    }

    [Fact]
    public void EqualKeysUseIdentityRegardlessOfInputOrder()
    {
        Assert.Equal([10, 50, 90], Ordered(new([]), Person(90, 70), Person(10, 70), Person(50, 70)));
    }

    private static MilitaryRulebook Military() => new(new(4, 2, Enumerable.Repeat(0, 8).ToImmutableArray(),
        [new(0, 1, null)], [new(1, 1, 0, 10000, 3, new(1, 1), TravelMode.Land)
            { CaptureRanks = [BattleRank.Front, BattleRank.Middle, BattleRank.Rear] }],
        [new(1, new(2, 2), new(1, 1))]));

    private static SimulationEngine Deploy(out WorldState world, out GovernanceRulebook governance, params OfficerState[] people)
    {
        governance = new([new(9000, 15000, 10), new(8000, 15000, 10)
            { AbilityBonuses = new(0, 0, 0, 20) }]);
        world = WorldState.Create(new(190, 1, 1), 42, [new(1, 10000, 10000, 100)],
            [new(1, 1, 5000, 100, 100, 80, 100, 0, 100, 0, 100)], people,
            controls: [new("player", 1, ControllerKind.Player)]);
        var engine = new SimulationEngine(world, new([new(9000, 0), new(8000, 0)], []), PersonnelRulebook.Empty, TreasureRulebook.Empty, governance, Military());
        Assert.True(engine.Execute(new("player"), new DeployUnitCommand(Guid.NewGuid(), 1, 0, 1,
            people.Select(p => p.Id).ToImmutableArray(), 2000, 1, new(MilitaryOrderKind.Move, new(17, 7), null))).Submission.Accepted);
        return engine;
    }

    [Fact]
    public void DeploymentUsesCurrentBonusesAndDoesNotReassignFormationSlots()
    {
        var engine = Deploy(out _, out _, Person(90, 70, 9000), Person(10, 60, 8000));
        var unit = Assert.Single(engine.Snapshot.Units);
        Assert.Equal(10, unit.CommanderId);
        Assert.Equal(BattleRank.Front, unit.MemberRanks[90]);
        Assert.Equal(BattleRank.Middle, unit.MemberRanks[10]);
    }

    [Fact]
    public void ReformationCannotMakeLowerPriorityFirstMemberCommander()
    {
        var engine = Deploy(out _, out _, Person(90, 100), Person(10, 30, 9000));
        var unit = Assert.Single(engine.Snapshot.Units);
        Assert.True(engine.Execute(new("player"), new ReformUnitCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence,
            unit.Id, 1, [90, 10], unit.Order)).Submission.Accepted);
        Assert.Equal(10, Assert.Single(engine.Snapshot.Units).CommanderId);
        Assert.Equal(BattleRank.Front, Assert.Single(engine.Snapshot.Units).MemberRanks[90]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LostCommanderUsesOfficePriorityAndIdentityTieBreakAfterRestore(bool restore)
    {
        var engine = Deploy(out var world, out var governance, Person(70, 95, 9000), Person(90, 60, 9000), Person(10, 60, 9000));
        if (restore) world = WorldState.Restore(engine.CaptureStableState());
        var mutation = world.BeginMutation(new(TreasureRulebook.Empty, governance));
        var unit = Assert.Single(mutation.Units.Values);
        var person = mutation.Officers[70];
        mutation.Replace(person with { UnitId = null, SettlementId = 1 });
        mutation.MaintainLeadership([]);
        Assert.Equal(10, mutation.Units[unit.Id].CommanderId);
        Assert.DoesNotContain(70, mutation.Units[unit.Id].MemberRanks.Keys);
        world.Commit(mutation);
    }

    [Fact]
    public void SuccessionReadsCurrentOfficeInsteadOfPersistingAStaleRanking()
    {
        Deploy(out var world, out var governance, Person(70, 95, 9000), Person(90, 90), Person(10, 20, 9000));
        var mutation = world.BeginMutation(new(TreasureRulebook.Empty, governance));
        var unit = Assert.Single(mutation.Units.Values);
        mutation.Replace(mutation.Officers[70] with { UnitId = null, SettlementId = 1 });
        mutation.Replace(mutation.Officers[90] with { OfficeId = 8000 });
        mutation.MaintainLeadership([]);
        Assert.Equal(90, mutation.Units[unit.Id].CommanderId);
        world.Commit(mutation);
    }
    [Fact]
    public void OfficePromotionReselectsAUnitCommanderWithoutChangingFormationRanks()
    {
        var engine = Deploy(out _, out _, Person(90, 90), Person(10, 20));
        var before = Assert.Single(engine.Snapshot.Units);
        Assert.Equal(90, before.CommanderId);
        var result = engine.Execute(new("player"), new AssignOfficesCommand(Guid.NewGuid(), 1,
            engine.Snapshot.Simulation.NextSequence, [new(10, 9000)]));
        Assert.True(result.Submission.Accepted);
        var after = Assert.Single(engine.Snapshot.Units);
        Assert.Equal(10, after.CommanderId);
        Assert.Equal(before.MemberRanks, after.MemberRanks);
    }
}
