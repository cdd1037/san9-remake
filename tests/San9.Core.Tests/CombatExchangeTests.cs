using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Core.Tests;

public sealed class CombatExchangeTests
{
    private static (WorldState World, SimulationRules Rules, BattleSource Source, BattleSource Target) Create(string path = "field", TacticEffect effect = TacticEffect.Martial,
        bool companion = false, bool reprisal = false)
    {
        bool facility = path is "assault" or "facility";
        bool automatic = path == "naval";
        var combat = new CombatProfile(10, 10, reprisal ? 10 : 0, 10, 50);
        var tactic = new TacticRule(9000, effect == TacticEffect.Martial ? 0 : 7, effect == TacticEffect.Trap ? TacticEffect.TrapSelection : effect, effect == TacticEffect.Taunt ? 0 : 10, 9, 0, true, true, [100]);
        var military = new MilitaryRulebook(new(10, 1, [.. Enumerable.Repeat(0, 10)], [new(0, 1, 1)],
            [new(100, 5, 0, 100000, 2, new(1, 1), automatic ? TravelMode.Water : TravelMode.Land)
                { Combat = combat, Attack = new(1, reprisal), CaptureRanks = [BattleRank.Front, BattleRank.Middle],
                    Tactics = new(100, 2, [0], automatic ? [9000] : []) { AutomaticOpportunityChance = automatic ? 100 : null } }],
            [new(1, new(2, 2), new(1, 1)) { Combat = combat, FacilityTypeId = 8000 },
                new(2, new(facility ? 17 : 47, 2), new(1, 1)) { Combat = combat, FacilityTypeId = 8000, Attack = new(1, false) }])
        {
            DefensiveTacticChance = 50, Tactics = [tactic, new(9001, 0, TacticEffect.Martial, 10, 9, 0, true, true, [100]),
                ..(effect == TacticEffect.Trap ? new[] { new TacticRule(9002, 7, TacticEffect.Trap, 10, 9, 0, false, true, [100]) } : [])],
            Elevations = [.. Enumerable.Repeat(0, 10)], TrapTerrains = [new(0, effect == TacticEffect.Trap ? 9002 : null, false)],
            Facilities = [new(8000, 0, 100, new(1, 1), 0, false, true, true, 100000, null, null, null, [])
                { Combat = combat, Attack = new(1, false), Tactics = new(100, 9, false, [9000]),
                    Reprisal = reprisal ? new(6, 1000, 10, 0, 10, 5) : null }]
        });
        OfficerState Officer(int id, int owner) => new(id, owner, owner == 2 && facility ? 2 : null, new(80, 70, 70, 70), false)
        {
            UnitId = owner == 2 && facility ? null : owner, IsCommander = owner == 2 && facility,
            Status = OfficerStatus.Ruler, EquippedTacticId = 9000, LearnedTactics = [9000]
        };
        UnitState Unit(int id) => new(id, id, new(id == 1 ? facility ? 16 : 12 : 13, 2), 10000)
        {
            FormationId = 100, Morale = 100, ServicePlaceId = id, CommanderId = id * 10 + 1,
            MemberRanks = id == 1 && companion ? ImmutableDictionary<int, BattleRank>.Empty
                .Add(11, BattleRank.Front).Add(12, BattleRank.Middle) :
                ImmutableDictionary<int, BattleRank>.Empty.Add(id * 10 + 1, BattleRank.Front)
        };
        var world = WorldState.Create(new(190, 1, 1), 42,
            [new(1, 100) { LeaderId = 11 }, new(2, 100) { LeaderId = 21 }],
            [new(1, 1, 10000, 100, 100, 100, 0, 0, 100, 0, 100), new(2, 2, 10000, 100, 100, 100, 0, 0, 100, 0, 100)],
            [Officer(11, 1), Officer(21, 2), ..(companion ? new[] { Officer(12, 1) with { EquippedTacticId = 9001, LearnedTactics = [9001], Status = OfficerStatus.Serving } } : [])], units: facility ? [Unit(1)] : [Unit(1), Unit(2)],
            controls: [new("player", 1, ControllerKind.Player)],
            corps: [new(1, 1, 11, new(10000, 100000)) { IsRoot = true }, new(2, 2, 21, new(10000, 100000)) { IsRoot = true }]);
        var m = world.BeginMutation();
        m.SetSimulation(m.Simulation with { RandomStreams = [new("duel-trigger", TestSeeds.StateForRoll(99)),
            new("military-tactic-member", TestSeeds.StateForRoll(0)), new("military-tactic-intelligence", TestSeeds.StateForRoll(0)),
            new("military-tactic-double", TestSeeds.StateForRoll(99)), new("military-tactic-counter", TestSeeds.StateForRoll(0))] });
        world.Commit(m);
        return (world, new() { Military = military }, path == "facility" ? new(2, true) : new(1, false),
            path == "facility" ? new(1, false) : new(2, facility));
    }

    [Theory]
    [InlineData("field", false)]
    [InlineData("field", true)]
    [InlineData("assault", false)]
    [InlineData("assault", true)]
    [InlineData("facility", false)]
    [InlineData("facility", true)]
    [InlineData("naval", false)]
    [InlineData("naval", true)]
    public void MartialActivationCommitsIndependentOrdinaryAndTacticLossesOnce(string path, bool defensive)
    {
        var (world, rules, source, target) = Create(path);
        var baseline = Create(path).World.BeginMutation();
        foreach (var officer in baseline.Officers.Values.ToArray()) baseline.Replace(officer with { TacticCooldown = 100 });
        List<DomainEvent> normal = [];
        Assert.True(MilitaryCombat.BeginExchange(baseline, source, target, rules, normal, world.Clock, () => new([], [], [])));
        Assert.Equal(2, normal.OfType<BattleDamageEvent>().Count());
        Assert.All(normal.OfType<BattleDamageEvent>(), e => Assert.True(e.SoldierLoss > 0));

        var m = world.BeginMutation();
        if (defensive) m.Replace(m.Officers[source.Owner(m) * 10 + 1] with { TacticCooldown = 100 });
        ulong counterRandom = m.Simulation.RandomStreams.Single(r => r.Name == "military-tactic-counter").State;
        List<DomainEvent> events = [];
        Assert.True(MilitaryCombat.BeginExchange(m, source, target, rules, events, world.Clock, () => new([], [], [])));
        var activation = Assert.Single(events.OfType<TacticActivatedEvent>());
        var actor = defensive ? target : source;
        Assert.Equal(actor.Id, activation.SourceId);
        Assert.Equal(actor.IsSettlement, activation.SourceIsSettlement);
        var hits = events.OfType<BattleDamageEvent>().ToArray();
        Assert.Equal(3, hits.Length);
        Assert.All(hits.Where(h => h.TacticId is null), hit =>
        {
            var ordinary = Assert.Single(normal.OfType<BattleDamageEvent>(), e => e.AttackerId == hit.AttackerId && e.AttackerIsSettlement == hit.AttackerIsSettlement);
            Assert.Equal(ordinary.SoldierLoss, hit.SoldierLoss);
            Assert.True(hit.IsTacticExchange);
        });
        Assert.All(hits, hit => Assert.Equal(hit.SoldierLoss, hit.AddedWounded + hit.Dead));
        Assert.Equal(1050u, Assert.Single(hits, h => h.TacticId is not null).SoldierLoss);
        Assert.Equal(defensive, counterRandom != m.Simulation.RandomStreams.Single(r => r.Name == "military-tactic-counter").State);
        Assert.Equal(10000u - (uint)hits.Where(h => h.TargetUnitId == 1).Sum(h => (long)h.SoldierLoss), m.Units[1].Soldiers);
    }

    [Theory]
    [InlineData(false, 10, false, 10000u, 0, 1550u, 1050u, 1050u)]
    [InlineData(true, 15, true, 10000u, 0, 4595u, 4095u, 4095u)]
    [InlineData(true, 1, true, 10000u, 100, 1546u, 546u, 546u)]
    [InlineData(false, 10, false, 700u, 0, 700u, 200u, 1050u)]
    [InlineData(false, 10, false, 400u, 0, 400u, 0u, 1050u)]
    public void TacticNumbersExcludeOrdinaryDamageAfterModifiersAndCapacity(bool doubled, int effect, bool abnormal,
        uint soldiers, int bonus, uint total, uint pure, uint calculatedPure)
    {
        var (world, rules, source, target) = Create();
        var m = world.BeginMutation();
        m.Replace(m.Units[2] with { Soldiers = soldiers, Condition = abnormal ? UnitCondition.Confused : UnitCondition.Normal });
        m.SetSimulation(m.Simulation with { ComputerDamageBonusPercent = bonus, Controls = [new("player", 2, ControllerKind.Player)] });
        List<DomainEvent> events = [];
        MilitaryCombat.CommitOrdinary(m, source, target, new(500, 300), rules.Military!, events);
        TacticCombat.ResolveMartial(m, source, 11, rules.Military!.Tactics[9000], MilitaryCombat.TargetOrder(target),
            [new(10, 0, 80, false)], doubled, effect, rules, events);
        var hit = Assert.Single(events.OfType<BattleDamageEvent>(), e => e.TacticId is not null);
        Assert.Equal(total, (uint)events.OfType<BattleDamageEvent>().Where(e => e.TargetUnitId == 2).Sum(e => (long)e.SoldierLoss));
        Assert.Equal(pure, hit.SoldierLoss);
        Assert.Equal(calculatedPure, hit.CalculatedSoldierLoss);
        Assert.Equal(300u, Assert.Single(events.OfType<BattleDamageEvent>(), e => e.TargetUnitId == 1).SoldierLoss);
        Assert.Equal(pure, hit.Dead + hit.AddedWounded);
    }

    [Theory]
    [InlineData("field", TacticEffect.Trap)]
    [InlineData("assault", TacticEffect.Trap)]
    [InlineData("facility", TacticEffect.Trap)]
    [InlineData("field", TacticEffect.Defection)]
    [InlineData("assault", TacticEffect.Defection)]
    [InlineData("facility", TacticEffect.Defection)]
    [InlineData("field", TacticEffect.Taunt)]
    public void IntelligenceActivationKeepsBothOrdinaryLossesAndClosesTheOtherSide(string path, TacticEffect effect)
    {
        var (world, rules, source, target) = Create(path, effect);
        var m = world.BeginMutation();
        uint outgoing = MilitaryCombat.NormalDamage(m, source, target, rules.Military!);
        uint incoming = MilitaryCombat.NormalDamage(m, target, source, rules.Military!);
        var counter = m.Simulation.RandomStreams.Single(r => r.Name == "military-tactic-counter");
        List<DomainEvent> events = [];
        Assert.True(MilitaryCombat.BeginExchange(m, source, target, rules, events, world.Clock, () => new([], [], [])));
        var activation = Assert.Single(events.OfType<TacticActivatedEvent>());
        Assert.Equal(source.Id, activation.SourceId);
        Assert.True(activation.Effect > 0);
        Assert.Equal(9, m.Officers[source.Owner(m) * 10 + 1].TacticCooldown);
        var ordinary = events.OfType<BattleDamageEvent>().Where(e => e.TacticId is null).ToArray();
        Assert.Equal(2, ordinary.Length);
        Assert.Equal(outgoing, ordinary[0].SoldierLoss);
        Assert.Equal(incoming, ordinary[1].SoldierLoss);
        Assert.Equal(counter, m.Simulation.RandomStreams.Single(r => r.Name == counter.Name));
        Assert.DoesNotContain(m.Simulation.RandomStreams, r => r.Name == "military-tactic-chain-member");
    }

    [Theory]
    [InlineData("field", true)]
    [InlineData("assault", true)]
    [InlineData("facility", false)]
    public void FailedIntelligenceConsumesCooldownWithoutAnotherCandidateOrDefensiveOpportunity(string path, bool companion)
    {
        var (world, rules, source, target) = Create(path, TacticEffect.Confusion, companion);
        var m = world.BeginMutation();
        m.SetSimulation(m.Simulation with { RandomStreams = m.Simulation.RandomStreams.Select(r =>
            r.Name == "military-tactic-intelligence" ? r with { State = TestSeeds.StateForRoll(99) } : r).ToImmutableArray() });
        var counter = m.Simulation.RandomStreams.Single(r => r.Name == "military-tactic-counter");
        List<DomainEvent> events = [];
        Assert.True(MilitaryCombat.BeginExchange(m, source, target, rules, events, world.Clock, () => new([], [], [])));
        var activation = Assert.Single(events.OfType<TacticActivatedEvent>());
        Assert.Equal(0, activation.Effect);
        Assert.Equal(9000, activation.TacticId);
        Assert.Equal(source.Id, activation.SourceId);
        Assert.Equal(9, m.Officers[source.Owner(m) * 10 + 1].TacticCooldown);
        if (companion) Assert.Equal(0, m.Officers[12].TacticCooldown);
        Assert.Equal(0, m.Officers[target.Owner(m) * 10 + 1].TacticCooldown);
        Assert.Equal(counter, m.Simulation.RandomStreams.Single(r => r.Name == counter.Name));
        Assert.Equal(0, Assert.Single(events.OfType<IntelligenceEffectResolvedEvent>()).Effect);
        Assert.Equal(2, events.OfType<BattleDamageEvent>().Count());
    }

    [Theory]
    [InlineData(TacticEffect.Confusion)]
    [InlineData(TacticEffect.Trap)]
    [InlineData(TacticEffect.Defection)]
    public void IntelligenceFollowupAddsOnlyMartialDamageWithoutAnotherOrdinaryPair(TacticEffect effect)
    {
        var (world, rules, source, target) = Create(effect: effect, companion: true);
        var m = world.BeginMutation();
        uint outgoing = MilitaryCombat.NormalDamage(m, source, target, rules.Military!);
        uint incoming = MilitaryCombat.NormalDamage(m, target, source, rules.Military!);
        m.SetSimulation(m.Simulation with { RandomStreams = m.Simulation.RandomStreams.Add(new("military-tactic-chain-allocation", TestSeeds.StateForRoll(0)))
            .Add(new("military-tactic-secondary", TestSeeds.StateForRoll(0))) });
        List<DomainEvent> events = [];
        Assert.True(MilitaryCombat.BeginExchange(m, source, target, rules, events, world.Clock, () => new([], [], [])));
        var activations = events.OfType<TacticActivatedEvent>().ToArray();
        Assert.Equal(2, activations.Length);
        Assert.Equal(9001, activations[1].TacticId);
        Assert.True(activations[1].IsChain);
        Assert.All(activations, a => Assert.Equal(source.Id, a.SourceId));
        var ordinary = events.OfType<BattleDamageEvent>().Where(e => e.TacticId is null).ToArray();
        Assert.Equal(2, ordinary.Length);
        Assert.Equal(outgoing, ordinary[0].SoldierLoss);
        Assert.Equal(incoming, ordinary[1].SoldierLoss);
        Assert.Equal(1365u, Assert.Single(events.OfType<BattleDamageEvent>(), e => e.TacticId == 9001).SoldierLoss);
        Assert.Equal(TestSeeds.StateForRoll(0), m.Simulation.RandomStreams.Single(r => r.Name == "military-tactic-counter").State);
    }

    [Fact]
    public void FacilitySupportPublishesActivationBeforeItsOrdinaryPair()
    {
        var (world, rules, source, target) = Create("facility", TacticEffect.Inspire);
        var m = world.BeginMutation();
        m.Replace(m.Settlements[source.Id] with { Morale = 79 });
        List<DomainEvent> events = [];
        Assert.True(MilitaryCombat.BeginExchange(m, source, target, rules, events, world.Clock, () => new([], [], [])));
        var activated = Assert.Single(events.OfType<TacticActivatedEvent>());
        var ordinary = events.OfType<BattleDamageEvent>().ToArray();
        Assert.Equal(2, ordinary.Length);
        Assert.All(ordinary, hit => { Assert.True(hit.IsTacticExchange); Assert.True(events.IndexOf(activated) < events.IndexOf(hit)); });
        Assert.True(m.Settlements[source.Id].Morale > 79);
    }

    [Theory]
    [InlineData(TacticEffect.Martial)]
    [InlineData(TacticEffect.Confusion)]
    public void LethalOrdinaryLossesRetainTheChosenActivationUntilBothSidesAreSettled(TacticEffect effect)
    {
        var (world, rules, source, target) = Create(effect: effect);
        var m = world.BeginMutation();
        m.Replace(m.Units[source.Id] with { Soldiers = 1 });
        m.Replace(m.Units[target.Id] with { Soldiers = 1 });
        List<DomainEvent> events = [];
        Assert.True(MilitaryCombat.BeginExchange(m, source, target, rules, events, world.Clock, () => new([], [], [])));
        var activation = Assert.Single(events.OfType<TacticActivatedEvent>());
        Assert.All(events.OfType<UnitTerminatedEvent>(), e => Assert.True(events.IndexOf(activation) < events.IndexOf(e)));
        Assert.Equal(2, events.OfType<UnitTerminatedEvent>().Count());
        Assert.Empty(m.Units);
        Assert.Equal(2, events.OfType<BattleDamageEvent>().Where(e => e.TacticId is null).Sum(e => (long)e.SoldierLoss));
    }

    [Fact]
    public void LethalSiegeReprisalStillCommitsThePrecomputedOrdinaryAttack()
    {
        var (world, rules, source, target) = Create("assault", reprisal: true);
        var m = world.BeginMutation();
        m.Replace(m.Units[source.Id] with { Soldiers = 50 });
        uint expected = MilitaryCombat.NormalDamage(m, source, target, rules.Military!);
        int walls = MilitaryCombat.WallDamage(m, m.Units[source.Id], m.Settlements[target.Id], rules.Military!);
        Assert.True(expected > 0 && walls > 0);
        List<DomainEvent> events = [];
        Assert.True(MilitaryCombat.BeginExchange(m, source, target, rules, events, world.Clock,
            () => throw new InvalidOperationException("Uninterrupted exchanges do not capture the remaining day.")));
        Assert.Equal(10000u - expected, m.Settlements[target.Id].Soldiers);
        Assert.Equal(100 - walls, m.Settlements[target.Id].Durability);
        Assert.Single(events.OfType<UnitTerminatedEvent>());
        Assert.DoesNotContain(events, e => e is TacticActivatedEvent);
        Assert.False(m.Units.ContainsKey(source.Id));
    }

    [Fact]
    public void SiegeTroopLossContinuesThePreviouslyPublishedDurabilityStrike()
    {
        var (world, rules, source, target) = Create("assault", reprisal: true);
        var m = world.BeginMutation();
        m.Replace(m.Officers[11] with { TacticCooldown = 100 });
        m.Replace(m.Officers[21] with { TacticCooldown = 100 });
        List<DomainEvent> events = [];
        Assert.True(MilitaryCombat.BeginExchange(m, source, target, rules, events, world.Clock, () => new([], [], [])));
        var hits = events.OfType<BattleDamageEvent>().Where(e => e.SettlementId == target.Id).ToArray();
        Assert.Equal(2, hits.Length);
        Assert.True(hits[0].DurabilityLoss > 0);
        Assert.False(hits[0].ContinuesSiege);
        Assert.True(hits[1].SoldierLoss > 0);
        Assert.True(hits[1].ContinuesSiege);
        Assert.DoesNotContain(events.OfType<BattleDamageEvent>(), e => e.TargetUnitId is not null && e.ContinuesSiege);
    }

    [Theory]
    [InlineData("field")]
    [InlineData("assault")]
    [InlineData("facility")]
    public void AnUninterruptedExchangeDoesNotCopyTheRestOfTheDay(string path)
    {
        var (world, rules, source, target) = Create(path);
        var m = world.BeginMutation();
        List<DomainEvent> events = [];
        Assert.True(MilitaryCombat.BeginExchange(m, source, target, rules, events, world.Clock,
            () => throw new InvalidOperationException("No day continuation is needed without a decision.")));
        Assert.Single(events.OfType<TacticActivatedEvent>());
    }

    [Fact]
    public void EachSideCanInitiateItsOwnMartialExchangeOnTheSameDay()
    {
        var (world, rules, _, _) = Create();
        var m = world.BeginMutation();
        m.Replace(m.Units[1] with { Order = MilitaryCombat.TargetOrder(new(2, false)) });
        m.Replace(m.Units[2] with { Order = MilitaryCombat.TargetOrder(new(1, false)) });
        List<DomainEvent> events = [];
        MilitaryCombat.AdvanceDay(m, rules, events);
        Assert.Equal(new[] { 1, 2 }, events.OfType<TacticActivatedEvent>().Select(e => e.SourceId));
        Assert.Equal(6, events.OfType<BattleDamageEvent>().Count());
        Assert.Equal(TestSeeds.StateForRoll(0), m.Simulation.RandomStreams.Single(r => r.Name == "military-tactic-counter").State);
    }
}
