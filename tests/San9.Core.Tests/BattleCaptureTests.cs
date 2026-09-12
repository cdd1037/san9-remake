using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class BattleCaptureTests
{
    private static WorldState World() => WorldState.Create(new(190, 1, 1), 42,
        [new(1, 10000, 10000, 100) { AdviserId = 11 }, new(2, 10000, 10000, 100)],
        [new(1, 1, 100, 100, 100, 70, 500, 0, 100, 0, 100), new(2, 2, 100, 100, 100, 70, 500, 0, 100, 0, 100)],
        [new(11, 1, null, new(70, 70, 70, 70), false) { Status = OfficerStatus.Strategist, UnitId = 0 },
         new(22, 2, null, new(70, 70, 70, 70), false) { UnitId = 1 }],
        units: [new(0, 1, new(2, 7), 100) { FormationId = 7 },
            new(1, 2, new(36, 7), 100) { FormationId = 7, Order = new(MilitaryOrderKind.Return, null, 2) }]);

    private static MilitaryRulebook Rules() => new(new(8, 3,
        Enumerable.Repeat(0, 24).ToImmutableArray(), [new(0, 1, null)],
        [new(7, 10, 0, 10000, 1, new(1, 1), TravelMode.Land)],
        [new(1, new(2, 7), new(1, 1)), new(2, new(37, 7), new(1, 1))]));

    [Fact]
    public void FieldCaptureClearsOldUnitAndOfficeThenReturnsWithoutChangingAllegiance()
    {
        var world = World();
        _ = new SimulationEngine(world, EconomyRulebook.Empty, PersonnelRulebook.Empty, TreasureRulebook.Empty, military: Rules());
        var mutation = world.BeginMutation();
        var events = new List<DomainEvent>();
        Assert.Throws<InvalidOperationException>(() => UnitTerminationService.Complete(mutation, 0, 1, 1, new([]), events));
        Assert.True(BattlePersonnel.Resolve(mutation, 11, 1, null, 1, BattleRank.Middle, false, false,
            PersonnelRulebook.Empty, new([]), events));
        UnitTerminationService.Complete(mutation, 0, 1, 1, new([]), events);
        world.Commit(mutation);
        var captured = world.CreateSnapshot();
        var person = captured.Officers.Single(p => p.Id == 11);
        Assert.Equal(OfficerStatus.Captive, person.Status);
        Assert.Equal(1, person.OwnerId);
        Assert.Equal(1, person.UnitId);
        Assert.Null(captured.Forces.Single(f => f.Id == 1).AdviserId);
        Assert.Single(events.OfType<OfficerCapturedEvent>());
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<WorldSnapshot>(CanonicalJson.Text(captured), CanonicalJson.Options)!;
        var restored = WorldState.Restore(snapshot);
        // One serving slot plus a prisoner is valid; prisoners are not formation members.
        MilitaryMovement.Validate(snapshot, Rules());
        List<DomainEvent> Advance(WorldState state)
        {
            var candidate = state.BeginMutation();
            var result = new List<DomainEvent>();
            MilitaryMovement.AdvanceDay(candidate, Rules(), new([]), PersonnelRulebook.Empty, result);
            state.Commit(candidate);
            return result;
        }
        Assert.Equal(CanonicalJson.Text(Advance(world)), CanonicalJson.Text(Advance(restored)));
        Assert.Equal(world.CreateSnapshot().StateHash, restored.CreateSnapshot().StateHash);
        Assert.Empty(world.CreateSnapshot().Units);
        person = world.CreateSnapshot().Officers.Single(p => p.Id == 11);
        Assert.Equal(OfficerStatus.Captive, person.Status);
        Assert.Equal(1, person.OwnerId);
        Assert.Equal(2, person.SettlementId);
        Assert.Null(person.UnitId);
    }

    [Fact]
    public void InvalidDetentionHasNoEffectsAndSettlementCaptureUsesSameTransition()
    {
        var world = World();
        var mutation = world.BeginMutation();
        var events = new List<DomainEvent>();
        var person = mutation.Officers[11];
        Assert.Throws<ArgumentException>(() => PersonnelTransitions.Capture(mutation, 11, null, 0, PersonnelRulebook.Empty, events));
        Assert.Throws<ArgumentException>(() => PersonnelTransitions.Capture(mutation, 11, 2, 1, PersonnelRulebook.Empty, events));
        Assert.Equal(person, mutation.Officers[11]);
        Assert.Empty(events);
        PersonnelTransitions.Capture(mutation, 11, 2, null, PersonnelRulebook.Empty, events);
        UnitTerminationService.Complete(mutation, 0, 1, 1, new([]), events);
        world.Commit(mutation);
        Assert.Equal(2, world.CreateSnapshot().Officers.Single(p => p.Id == 11).SettlementId);
        Assert.Null(world.CreateSnapshot().Officers.Single(p => p.Id == 11).UnitId);
    }

    [Fact]
    public void EscapeKeepsAllegianceAndOfficeWhileClearingFieldLocation()
    {
        var world = World();
        var mutation = world.BeginMutation();
        var events = new List<DomainEvent>();
        Assert.Throws<ArgumentException>(() => BattlePersonnel.Resolve(mutation, 11, 1, 2, 1, BattleRank.Front,
            false, false, PersonnelRulebook.Empty, new([]), events));
        Assert.Empty(events);
        Assert.Empty(mutation.Simulation.RandomStreams);
        Assert.False(BattlePersonnel.Resolve(mutation, 11, 1, 1, 1, BattleRank.Middle, true, false,
            PersonnelRulebook.Empty, new([]), events));
        UnitTerminationService.Complete(mutation, 0, 1, 1, new([]), events);
        world.Commit(mutation);
        var person = world.CreateSnapshot().Officers.Single(p => p.Id == 11);
        Assert.Equal(OfficerStatus.Strategist, person.Status);
        Assert.Equal(1, person.OwnerId);
        Assert.Equal(1, person.SettlementId);
        Assert.Null(person.UnitId);
        Assert.Equal(11, world.CreateSnapshot().Forces.Single(f => f.Id == 1).AdviserId);
        Assert.Single(events.OfType<OfficerEscapedBattleEvent>());
    }

    [Fact]
    public void ProbabilisticOutcomeAndTerminationRepeatAfterStableRestore()
    {
        var world = World();
        var engine = new SimulationEngine(world, EconomyRulebook.Empty, PersonnelRulebook.Empty, TreasureRulebook.Empty, military: Rules());
        var restored = WorldState.Restore(engine.CaptureStableState());
        List<DomainEvent> Resolve(WorldState state)
        {
            var mutation = state.BeginMutation();
            var events = new List<DomainEvent>();
            BattlePersonnel.Resolve(mutation, 11, 1, 1, 1, BattleRank.Middle, false, false,
                PersonnelRulebook.Empty, new([]), events);
            UnitTerminationService.Complete(mutation, 0, 1, 1, new([]), events);
            state.Commit(mutation);
            return events;
        }
        Assert.Equal(CanonicalJson.Text(Resolve(world)), CanonicalJson.Text(Resolve(restored)));
        Assert.Equal(world.CreateSnapshot().StateHash, restored.CreateSnapshot().StateHash);
        Assert.Contains(world.CreateSnapshot().Simulation.RandomStreams, s => s.Name == "battle-personnel-capture");
    }
}
