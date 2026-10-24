using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Core.Tests;

public sealed class CombatExchangeTests
{
    private static (WorldMutation World, SimulationRules Rules, BattleSource Source, BattleSource Target) Fixture(
        string path, bool counter, int bonus, bool martial = true)
    {
        var profile = new CombatProfile(10, 10, 0, 10, 50);
        var military = new MilitaryRulebook(new(20, 1, [.. Enumerable.Repeat(0, 20)], [new(0, 1, null)],
            [new(100, 5, 0, 100000, 1, new(1, 1), TravelMode.Land)
                { Combat = profile, Attack = new(10, false), CaptureRanks = [BattleRank.Middle], Tactics = new(100, 2, [0], []) }],
            [new(1, new(2, 2), new(1, 1)) { Combat = profile, Attack = new(10, false), FacilityTypeId = 800 },
             new(2, new(92, 2), new(1, 1)) { Combat = profile, Attack = new(10, false), FacilityTypeId = 800 }])
        {
            DefensiveTacticChance = 50,
            Facilities = [new(800, 0, 100, new(1, 1), 0, false, true, true, 100000, null, null, null, [])
                { Combat = profile, Attack = new(10, false), Tactics = new(100, 3, false, [9000]) }],
            Tactics = [new(9000, 0, TacticEffect.Martial, 13, 9, 0, true, true, [100])]
        });
        bool facility = path == "facility", assault = path == "assault";
        OfficerState Person(int id, int owner, bool garrison) => new(id, owner, garrison ? owner : null, new(80, 70, 70, 80), false)
        {
            Status = OfficerStatus.Ruler, IsCommander = garrison, UnitId = garrison ? null : owner,
            LearnedTactics = [9000], EquippedTacticId = 9000,
            TacticCooldown = !martial || owner == 1 && counter ? 9 : 0
        };
        UnitState Unit(int id, WorldPosition position) => new(id, id, position, 100000)
        {
            FormationId = 100, Morale = 100, ServicePlaceId = id, CommanderId = id * 10 + 1,
            MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(id * 10 + 1, BattleRank.Middle)
        };
        var state = WorldState.Create(new(190, 1, 1), 42,
            [new(1, 100) { LeaderId = 11 }, new(2, 100) { LeaderId = 21 }],
            [new(1, 1, 100000, 100, 100, 100, 0, 0, 100, 0, 100),
             new(2, 2, 100000, 100, 100, 100, 0, 0, 100, 0, 100)],
            [Person(11, 1, facility), Person(21, 2, assault)],
            units: [..(facility ? Array.Empty<UnitState>() : [Unit(1, new(assault ? 87 : 42, 2))]),
                ..(assault ? Array.Empty<UnitState>() : [Unit(2, new(facility ? 7 : 43, 2))])],
            controls: [new("player", 1, ControllerKind.Player)], computerDamageBonusPercent: bonus,
            corps: [new CorpsState(1, 1, 11, new(10000, 100000)) { IsRoot = true }, new CorpsState(2, 2, 21, new(10000, 100000)) { IsRoot = true }]);
        var world = state.BeginMutation();
        world.SetSimulation(world.Simulation with { RandomStreams =
            [new("military-tactic-double", TestSeeds.StateForRoll(99)), new("military-tactic-counter", TestSeeds.StateForRoll(0))] });
        BattleSource source = new(1, facility), target = new(2, assault);
        MilitaryEngagements.Open(world, source, target, military);
        return (world, new() { Military = military }, source, target);
    }

    public static TheoryData<string, bool, int> MartialCases()
    {
        var cases = new TheoryData<string, bool, int>();
        foreach (string path in new[] { "field", "assault", "facility" })
        foreach (bool counter in new[] { false, true })
        foreach (int bonus in new[] { 0, 100, 200 }) cases.Add(path, counter, bonus);
        return cases;
    }

    [Theory]
    [MemberData(nameof(MartialCases))]
    public void MartialResultCommitsTheFrozenPairOnceAndEndsTheExchange(string path, bool counter, int bonus)
    {
        var (world, rules, source, target) = Fixture(path, counter, bonus);
        var prepared = CombatExchange.Calculate(world, source, target, rules.Military!);
        uint ordinary = prepared.OrdinaryDamage(counter ? target : source);
        uint reply = prepared.OrdinaryDamage(counter ? source : target);
        Assert.True(ordinary > 0 && reply > 0);
        var random = world.Simulation.RandomStreams.Single(s => s.Name == "military-tactic-counter");
        List<DomainEvent> events = [];

        Assert.True(MilitaryCombat.ResolveExchange(world, source, target, rules, events, world.Clock, new([], [], [])));

        var activation = Assert.Single(events.OfType<TacticActivatedEvent>());
        Assert.Equal(counter ? 2 : 1, activation.SourceId);
        var damage = events.OfType<BattleDamageEvent>().ToArray();
        Assert.Equal(2, damage.Length);
        Assert.IsType<TacticActivatedEvent>(events.First(e => e is TacticActivatedEvent or BattleDamageEvent));
        var martial = Assert.Single(damage, e => e.TacticId is not null);
        var normal = Assert.Single(damage, e => e.TacticId is null);
        uint combined = TacticMath.MartialDamage([new(13, 0, 80, false)], activation.Doubled, ordinary, activation.Effect, false, 100000).Applied;
        Assert.Equal(counter ? combined * (uint)(100 + bonus) / 100 : combined, martial.SoldierLoss);
        Assert.Equal(counter ? reply : reply * (uint)(100 + bonus) / 100, normal.SoldierLoss);
        Assert.Equal(100000u - damage.Single(e => e.TargetUnitId == 1 || e.SettlementId == 1).SoldierLoss, source.Soldiers(world));
        Assert.Equal(100000u - damage.Single(e => e.TargetUnitId == 2 || e.SettlementId == 2).SoldierLoss, target.Soldiers(world));
        Assert.All(damage, e => Assert.Equal(e.SoldierLoss, e.AddedWounded + e.Dead));
        if (!counter)
        {
            Assert.Equal(0, world.Officers[21].TacticCooldown);
            Assert.Equal(random, world.Simulation.RandomStreams.Single(s => s.Name == random.Name));
        }
        WorldInvariants.Validate(world);
    }

    [Theory]
    [InlineData("field")]
    [InlineData("assault")]
    [InlineData("facility")]
    public void NoMartialResultCommitsOrdinaryDamageOnceAtTheEnd(string path)
    {
        var (world, rules, source, target) = Fixture(path, false, 0, martial: false);
        var pair = CombatExchange.Calculate(world, source, target, rules.Military!);
        List<DomainEvent> events = [];

        Assert.True(MilitaryCombat.ResolveExchange(world, source, target, rules, events, world.Clock, new([], [], [])));

        Assert.Empty(events.OfType<TacticActivatedEvent>());
        Assert.Equal(2, events.OfType<BattleDamageEvent>().Count());
        Assert.Equal(100000u - pair.State.DamageToTarget, target.Soldiers(world));
        Assert.Equal(100000u - pair.State.DamageToSource, source.Soldiers(world));
    }

    [Theory]
    [InlineData("field", false)]
    [InlineData("assault", false)]
    [InlineData("facility", false)]
    [InlineData("field", true)]
    [InlineData("assault", true)]
    [InlineData("facility", true)]
    public void LethalOrdinaryDamageResolvesWithoutOpeningTacticOpportunities(string path, bool sourceDies)
    {
        var (world, rules, source, target) = Fixture(path, false, 0);
        var victim = sourceDies ? source : target;
        if (victim.IsSettlement) world.Replace(world.Settlements[victim.Id] with { Soldiers = 1 });
        else world.Replace(world.Units[victim.Id] with { Soldiers = 1 });
        List<DomainEvent> events = [];

        Assert.True(MilitaryCombat.ResolveExchange(world, source, target, rules, events, world.Clock, new([], [], [])));

        Assert.Empty(events.OfType<TacticActivatedEvent>());
        Assert.Equal(2, events.OfType<BattleDamageEvent>().Count());
        Assert.DoesNotContain(world.Simulation.RandomStreams, s => s.Name == "military-tactic-member");
        ResolutionPipeline.CheckTerminal(world, events);
        WorldInvariants.Validate(world);
    }
}
