using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class SnipingContinuationTests
{
    [Fact]
    public void CounterFinishesOnceAndUsesCurrentCommanderProtection()
    {
        var (original, rules, _) = TacticSnipingTests.Create(counterChance: 100);
        var initial = original.CreateSnapshot();
        var people = initial.Officers.Select(p => p.Id switch {
            11 => p with { BaseAbilities = new(70, 70, 70, 70), LearnedTactics = [], EquippedTacticId = null },
            21 => p with { Status = OfficerStatus.Serving, Injury = 0, LearnedTactics = [9000], EquippedTacticId = 9000,
                TacticProficiencies = [0, 0, 0, 1000, 0, 0, 0, 0, 0] },
            22 => p with { Status = OfficerStatus.Ruler }, _ => p }).Concat([
                initial.Officers.Single(p => p.Id == 11) with { Id = 12, Status = OfficerStatus.Serving },
                new OfficerState(13, 1, 1, new(50, 50, 50, 50), false),
                new OfficerState(23, 2, 3, new(50, 50, 50, 50), false)]);
        var world = WorldState.Create(initial.Clock, 42,
            initial.Forces.Select(f => f.Id == 2 ? f with { LeaderId = 22 } : f), initial.Settlements, people,
            units: initial.Units.Select(u => u.Id == 1 ? u with { CommanderId = 12,
                MemberRanks = u.MemberRanks.Add(12, BattleRank.Rear) } : u),
            controls: [new("first", 1, ControllerKind.Player), new("second", 2, ControllerKind.Player)], battleDeathsEnabled: true, corps: initial.Corps);
        var m = world.BeginMutation();
        const ulong seed = 30UL;
        var random = new StableRandom(seed);
        Assert.True(random.NextUInt64() % 100 < 16 && random.NextUInt64() % 100 < 16);
        m.SetSimulation(m.Simulation with { Boundary = SimulationBoundary.Stable, ActiveControl = 2,
            RandomStreams = [new("military-sniping-death", seed)] });
        m.SetClock(new(190, 1, 2));
        List<DomainEvent> events = [];
        MilitaryCombat.AdvanceDay(m, rules, events, initial.Clock);
        if (m.Simulation.PendingSniping is null) Assert.Fail(CanonicalJson.Text(events));
        Assert.Equal(22, m.Simulation.PendingSniping!.RulerId);
        Assert.True(m.Simulation.PendingSniping.Exchange!.CounterPending);
        world.Commit(m);
        m = world.BeginMutation();
        MilitaryCombat.ResumeSniping(m, rules, events, 23);
        Assert.Null(m.Simulation.PendingSniping);
        Assert.Equal(OfficerStatus.Dead, m.Officers[22].Status);
        Assert.Equal(OfficerStatus.Ruler, m.Officers[11].Status);
        Assert.Equal(11, m.Units[1].CommanderId);
        Assert.True(m.Officers[11].Injury > 0);
        Assert.Equal(11, m.Forces[1].LeaderId);
        Assert.Equal(23, m.Forces[2].LeaderId);
        Assert.Single(events.OfType<SnipingSuccessionRequestedEvent>());
        Assert.Single(events.OfType<OfficerDiedEvent>());
        Assert.Single(events.OfType<TacticActivatedEvent>(), e => e.SourceId == 1);
        Assert.Single(events.OfType<TacticActivatedEvent>(), e => e.SourceId == 2);
        Assert.Equal(4, events.OfType<BattleDamageEvent>().Count(e => !e.AttackerIsSettlement && e.TargetUnitId is not null));
    }
}
