using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class NaturalDeathLocationTests
{
    private static (WorldState World, SimulationEngine Engine, Func<WorldState, SimulationEngine> Factory) Create(string location)
    {
        OfficerState Person(int id, int owner, int home) => new(id, owner, home, new(60, 60, 60, 60), false);
        var people = new[] { Person(1, 1, 1) with { Status = OfficerStatus.Ruler }, Person(2, 2, 2) with { Status = OfficerStatus.Ruler },
            Person(11, 1, 1) with { BirthYear = 90 }, Person(12, 1, 1), Person(21, 2, 2) };
        List<UnitState> units = [];
        if (location is "field-commander" or "field-last")
        {
            people[2] = people[2] with { SettlementId = null, UnitId = 10 };
            if (location == "field-commander") people[3] = people[3] with { SettlementId = null, UnitId = 10 };
            int[] members = location == "field-commander" ? [11, 12] : [11];
            units.Add(new(10, 1, new(6, 2), 1000) { FormationId = 1, Morale = 100, CommanderId = 11,
                MemberRanks = members.ToImmutableDictionary(id => id, _ => BattleRank.Front), ServicePlaceId = 1 });
        }
        if (location is "captive-place" or "captive-field")
        {
            people[2] = people[2] with { Status = OfficerStatus.Captive, SettlementId = location == "captive-place" ? 2 : null,
                UnitId = location == "captive-field" ? 10 : null };
            if (location == "captive-field")
            {
                people[4] = people[4] with { SettlementId = null, UnitId = 10 };
                units.Add(new(10, 2, new(7, 2), 1000) { FormationId = 1, Morale = 100, CommanderId = 21,
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(21, BattleRank.Front), ServicePlaceId = 2 });
            }
        }
        var world = WorldState.Create(new(190, 1, 1), 42,
            [new(1, 10000, 100000, 100) { LeaderId = 1 }, new(2, 10000, 100000, 100) { LeaderId = 2 }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 0, 0, 0), new(2, 2, 0, 100, 100, 0, 0, 0, 0, 0, 0),
                new(3, 1, 0, 100, 100, 0, 0, 0, 0, 0, 0)], people, units,
            treasures: [new(7, 11, TreasureDiscovery.Discovered, null)], controls: [new("player", 1, ControllerKind.Player)]);
        var places = new PersonnelRulebook([new(1, new(2, 2)), new(2, new(17, 2)), new(3, new(1202, 2))],
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }, [new(1, 2), new(1, 3)]);
        var military = new MilitaryRulebook(new(241, 1, Enumerable.Repeat(0, 241).ToImmutableArray(), [new(0, 1, null)],
            [new(1, 5, 0, 10000, 2, new(1, 1), TravelMode.Land)],
            [new(1, new(2, 2), new(1, 1)), new(2, new(17, 2), new(1, 1)), new(3, new(1202, 2), new(1, 1))]));
        SimulationEngine Factory(WorldState state) => new(state, EconomyRulebook.Empty, places, new([new(7, default, 0)]), military: military);
        var engine = Factory(world);
        var mutation = world.BeginMutation();
        mutation.SetClock(new(190, 12, 21));
        world.Commit(mutation);
        if (location == "travel")
            Assert.True(engine.Execute(new("player"), new MoveOfficersCommand(Guid.NewGuid(), 1, 0, [11], 3)).Submission.Accepted);
        return (world, engine, Factory);
    }

    [Theory]
    [InlineData("home")]
    [InlineData("field-commander")]
    [InlineData("field-last")]
    [InlineData("travel")]
    [InlineData("captive-place")]
    [InlineData("captive-field")]
    public void QuarterDeathClearsLocationAndOccupationAndSettlesTreasureExactlyOnce(string location)
    {
        var (_, engine, factory) = Create(location);
        var saved = engine.CaptureStableState();
        var restored = factory(WorldState.Restore(saved));
        var mission = location == "travel" ? Assert.Single(saved.Simulation.Tasks) : null;
        foreach (var running in new[] { engine, restored }) running.EndControl(new("player"));
        var result = engine.AdvanceXun();
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
        var dead = engine.Snapshot.Officers.Single(p => p.Id == 11);
        Assert.Equal(OfficerStatus.Dead, dead.Status);
        Assert.Null(dead.OwnerId);
        Assert.Null(dead.SettlementId);
        Assert.Null(dead.UnitId);
        Assert.Null(dead.RegionId);
        Assert.DoesNotContain(engine.Snapshot.Simulation.Tasks, t => t.OfficerId == 11 || t.Journey?.TargetOfficerId == 11);
        Assert.DoesNotContain(engine.Snapshot.Units, u => u.CommanderId == 11 || u.MemberRanks.ContainsKey(11));
        Assert.Equal(location.StartsWith("captive", StringComparison.Ordinal) ? 2 : 1, Assert.Single(engine.Snapshot.Treasures).HolderId);
        Assert.Single(result.Events.OfType<OfficerDiedEvent>(), e => e.OfficerId == 11 && e.Cause == OfficerDeathCause.Natural);
        Assert.Single(result.Events.OfType<TreasureTransferredEvent>(), e => e.TreasureId == 7);
        if (mission is not null)
        {
            Assert.Single(result.Events.OfType<TaskCancelledEvent>(), e => e.TaskId == mission.Id);
            Assert.DoesNotContain(result.Events.OfType<PersonnelReturnedEvent>(), e => e.OfficerId == 11);
        }
        if (location == "field-last") Assert.DoesNotContain(engine.Snapshot.Units, u => u.Id == 10);
        if (location == "field-commander") Assert.Equal(12, Assert.Single(engine.Snapshot.Units).CommanderId);
        if (location == "captive-field") Assert.Equal(21, Assert.Single(engine.Snapshot.Units).CommanderId);
        var loaded = factory(WorldState.Restore(engine.CaptureStableState()));
        Assert.Empty(loaded.InitialEvents);
        loaded.EndControl(new("player"));
        Assert.DoesNotContain(loaded.AdvanceXun().Events, e => e is OfficerDiedEvent or TreasureTransferredEvent);
    }
}
