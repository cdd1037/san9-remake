using San9.Core.Domain;
using San9.Core.Simulation;
using System.Collections.Immutable;

namespace San9.Core.Tests;

public sealed class OfficerBattleMeritTests
{
    private static WorldMutation World(bool enabled = true) => WorldState.Create(new(190, 1, 1), 1,
        [new ForceState(1, 0) { LeaderId = 1 }, new ForceState(2, 0) { LeaderId = 2 }],
        [new SettlementState(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100),
         new SettlementState(2, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
        [new OfficerState(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler, IsCommander = true },
         new OfficerState(2, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler, IsCommander = true }],
        corps: [new CorpsState(1, 1, 1, new(0, 0)) { IsRoot = true }, new CorpsState(2, 2, 2, new(0, 0)) { IsRoot = true }],
        newGame: new() { PersonalPeerageEnabled = enabled }).BeginMutation();

    [Fact]
    public void CreditIsRecordedForTheForceAndDisabledWithTheModule()
    {
        var world = World(); List<DomainEvent> events = [];
        BattleLearning.DefeatedUnit(world, new(1, true), events);
        var officer = world.Officers[1];
        Assert.Equal(1, officer.BattleMeritFor(1));
        Assert.Equal(0, officer.BattleMeritFor(2));
        Assert.Contains(events, e => e is OfficerBattleMeritChangedEvent { OfficerId: 1, ForceId: 1, Current: 1 });
        world.Replace(world.Officers[2] with { IsCommander = false });
        world.Replace(officer with { OwnerId = 2, SettlementId = 2, Status = OfficerStatus.Serving });
        OfficerBattleMerit.Credit(world, new(2, true), 3, "settlement-captured", events);
        Assert.Equal(1, world.Officers[1].BattleMeritFor(1));
        Assert.Equal(3, world.Officers[1].BattleMeritFor(2));
        var disabled = World(false);
        OfficerBattleMerit.Credit(disabled, new(1, true), 1, "unit-defeated", []);
        Assert.Null(disabled.Officers[1].BattleMerit);
    }

    [Fact]
    public void MixedUnitCreditsOnlyMembersWithSoldiers()
    {
        var world = DuelContestTests.World([DuelContestTests.Person(11, 1), DuelContestTests.Person(12, 1),
            DuelContestTests.Person(21, 2)]).BeginMutation();
        world.Replace(world.Units[1] with { Detachments = ImmutableDictionary<int, DetachmentState>.Empty
            .Add(11, new(1, 1, 10000)).Add(12, new(1, 1, 0)) });
        OfficerBattleMerit.Credit(world, new(1, false), 1, "unit-defeated", []);
        Assert.Equal(1, world.Officers[11].BattleMeritFor(1));
        Assert.Null(world.Officers[12].BattleMerit);
        Assert.Null(world.Officers[21].BattleMerit);
    }
}
