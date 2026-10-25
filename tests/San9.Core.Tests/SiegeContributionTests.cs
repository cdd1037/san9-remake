using System.Collections.Immutable;
using System.Text.Json;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Core.Tests;

public sealed class SiegeContributionTests
{
    [Fact]
    public void ActualLossesAccumulateSparselyWithoutPerHitRoundingOrOriginalScoreLimit()
    {
        var world = State().BeginMutation();
        var untouched = world.Settlements[20];
        Assert.DoesNotContain("siegeContributions", CanonicalJson.Text(untouched));
        Hit(world, 100, 1, 2);
        Hit(world, 100, 1);
        Hit(world, 201, uint.MaxValue, int.MaxValue);
        var ledger = world.Settlements[10].SiegeContributions!;
        Assert.Equal(42, ledger[1].Score);
        Assert.Equal(500 - 2 + 20L * (1000 - 2), ledger[2].Score);
        Assert.Equal(ledger[1].Score, ledger[1].Units[100]);
        Assert.Equal(new[] { 1, 2 }, ledger.Keys.Order());
        Assert.Null(world.Settlements[20].SiegeContributions);
        WorldInvariants.Validate(world);

        var large = State().BeginMutation();
        large.Replace(large.Settlements[10] with { Soldiers = 100000 });
        Hit(large, 100, 70000);
        Assert.Equal(70000, large.Settlements[10].SiegeContributions![1].Score);
    }

    [Theory]
    [InlineData(300, 1)]
    [InlineData(200, 2)]
    [InlineData(250, 2)]
    public void ForceTotalsIncludeTheLethalHitAndTiesPreferTheTrigger(int firstForceDamage, int expected)
    {
        var world = State().BeginMutation();
        Hit(world, 100, (uint)(firstForceDamage / 2));
        Hit(world, 101, (uint)(firstForceDamage - firstForceDamage / 2));
        Hit(world, 201, (uint)(500 - firstForceDamage));
        List<DomainEvent> events = [];
        BattleAftermath.ResolveSettlementDefeat(world, 10, new(201, false), Rules(), events);
        Assert.Equal(expected, world.Settlements[10].OwnerId);
        Assert.Null(world.Settlements[10].SiegeContributions);
        var fact = Assert.Single(events.OfType<SettlementCapturedEvent>());
        Assert.Equal(expected, fact.Source.ForceId);
        Assert.Equal(2, fact.Attacker.ForceId);
        Assert.Equal(201, fact.Attacker.Id);
        WorldInvariants.Validate(world);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void ForceCreditSurvivesAUnitLossButNeedsAnEligibleOccupyingUnit(bool withdraw, int expected)
    {
        var world = State().BeginMutation();
        Hit(world, 100, 300);
        world.RemoveUnit(100);
        if (withdraw) world.Replace(world.Units[101] with { Position = new(2, 25) });
        Hit(world, 201, 200);
        BattleAftermath.ResolveSettlementDefeat(world, 10, new(201, false), Rules(), []);
        Assert.Equal(expected, world.Settlements[10].OwnerId);
        WorldInvariants.Validate(world);
    }

    [Fact]
    public void CreditResumesAfterWithdrawalAndTheHighestEligibleUnitReceivesForItsCorps()
    {
        var world = State().BeginMutation();
        Hit(world, 101, 300);
        world.Replace(world.Units[101] with { Position = new(2, 25), Order = null });
        Assert.Equal(300, world.Settlements[10].SiegeContributions![1].Score);
        world.Replace(world.Units[101] with { Position = new(32, 10), Order = Attack });
        Hit(world, 201, 200);
        List<DomainEvent> events = [];
        BattleAftermath.ResolveSettlementDefeat(world, 10, new(201, false), Rules(), events);
        Assert.Equal(11, world.Settlements[10].CorpsId);
        Assert.Equal(101, Assert.Single(events.OfType<SettlementCapturedEvent>()).Winner.Id);
        Assert.Contains(events.OfType<UnitAbsorbedEvent>(), e => e.UnitId == 101);
        Assert.DoesNotContain(events.OfType<UnitAbsorbedEvent>(), e => e.UnitId == 100 || e.UnitId == 201);
        Assert.Equal(Attack, world.Units[201].Order);
        WorldInvariants.Validate(world);
    }

    [Theory]
    [InlineData(0, 101)]
    [InlineData(1, 100)]
    public void DepartureReadinessGovernsBothReceiptAndAbsorption(int preparationDays, int receiver)
    {
        var world = State().BeginMutation();
        Hit(world, 101, 300);
        world.Replace(world.Units[101] with { CorpsId = 1, DepartureDaysRemaining = preparationDays });
        Hit(world, 201, 200);
        List<DomainEvent> events = [];
        BattleAftermath.ResolveSettlementDefeat(world, 10, new(201, false), Rules(), events);
        Assert.Equal(receiver, Assert.Single(events.OfType<SettlementCapturedEvent>()).Winner.Id);
        Assert.Equal(preparationDays == 0 ? new[] { 100, 101 } : new[] { 100 },
            events.OfType<UnitAbsorbedEvent>().Select(e => e.UnitId));
        Assert.Equal(preparationDays > 0, world.Units.ContainsKey(101));
        WorldInvariants.Validate(world);
    }

    [Fact]
    public void ReceivingForceGetsResourcesAndPersonnelWhileTheTriggerGetsItsBattleReward()
    {
        var world = State().BeginMutation();
        world.Replace(world.Officers[31] with { SettlementId = 10 });
        world.Replace(world.Units[201] with { Morale = 60 });
        // No defender city remains: the shared personnel chain cannot escape capture.
        world.Replace(world.Settlements[11] with { Kind = SettlementKind.Facility });
        Hit(world, 100, 300);
        Hit(world, 201, 200);
        List<DomainEvent> events = [];
        BattleAftermath.ResolveSettlementDefeat(world, 10, new(201, false), Rules(), events);
        Assert.Equal(1, world.Settlements[10].OwnerId);
        Assert.Equal(70, world.Units[201].Morale);
        Assert.All(events.OfType<ResourceChangedEvent>().Where(e => e.Reason == "settlement-captured" && e.Actual > 0),
            e => Assert.Equal(new ResourceAccount(ResourceOwnerKind.Corps, 1), e.Account));
        Assert.Contains(events.OfType<ResourceChangedEvent>(), e => e.Reason == "settlement-captured" && e.Actual > 0);
        Assert.Equal(OfficerStatus.Captive, world.Officers[31].Status);
        Assert.Equal(10, world.Officers[31].SettlementId);
        WorldInvariants.Validate(world);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnershipChangeAndDestructionClearEveryForcesCredit(bool destroyed)
    {
        var world = State().BeginMutation();
        Hit(world, 100, 10);
        Hit(world, 201, 20);
        var place = world.Settlements[10];
        world.Replace(place with { CorpsId = null });
        Assert.NotNull(world.Settlements[10].SiegeContributions);
        world.Replace(place with { OwnerId = destroyed ? null : 1, CorpsId = null, IsDestroyed = destroyed,
            Durability = destroyed ? 0 : place.Durability, Soldiers = destroyed ? 0 : place.Soldiers, Wounded = 0 });
        Assert.Null(world.Settlements[10].SiegeContributions);
    }

    [Fact]
    public void FacilityCreditCanWinForItsForcesGroundTroops()
    {
        var world = State().BeginMutation();
        world.Replace(world.Units[100] with { Order = null });
        MilitaryCombat.DamageSettlement(world, world.Settlements[10], 300,
            Rules().Military!.Place(world.Settlements[10]).Combat!, 20, [], attackerIsSettlement: true);
        Assert.Empty(world.Settlements[10].SiegeContributions![1].Units);
        Hit(world, 201, 200);
        List<DomainEvent> events = [];
        BattleAftermath.ResolveSettlementDefeat(world, 10, new(201, false), Rules(), events);
        var capture = Assert.Single(events.OfType<SettlementCapturedEvent>());
        Assert.Equal(1, capture.Source.ForceId);
        Assert.Equal(new BattleSource(101, false), capture.Winner);
        WorldInvariants.Validate(world);
    }

    [Fact]
    public void MultipleDefeatedTargetsChooseSeparateRecipientsBeforeAnySourceIsAbsorbed()
    {
        var world = State().BeginMutation();
        Hit(world, 100, 300);
        Hit(world, 201, 200);
        world.Replace(world.Settlements[11] with { Soldiers = 0 });
        world.Replace(world.Units[201] with { Position = new(92, 25),
            Order = new(MilitaryOrderKind.AttackSettlement, null, 11) });
        List<DomainEvent> events = [];
        BattleAftermath.ResolveSettlementDefeats(world, [10, 11], new(201, false), Rules(), events);
        Assert.Equal(new[] { 1, 2 }, events.OfType<SettlementCapturedEvent>().Select(e => e.Source.ForceId!.Value));
        Assert.All(events.OfType<SettlementCapturedEvent>(), e => Assert.Equal(201, e.Attacker.Id));
        Assert.Equal(new[] { 100, 201 }, events.OfType<UnitAbsorbedEvent>().Select(e => e.UnitId));
        Assert.Null(world.Settlements[10].SiegeContributions);
        Assert.Null(world.Settlements[11].SiegeContributions);
        WorldInvariants.Validate(world);
    }

    [Fact]
    public void NewOwnerStartsANewSiegeAndRestorePreservesTheSameOutcome()
    {
        var state = State();
        var world = state.BeginMutation();
        Hit(world, 100, 300);
        world.SetSimulation(world.Simulation with { Boundary = SimulationBoundary.Stable });
        state.Commit(world);
        string json = CanonicalJson.Text(state.CreateSnapshot());
        var restored = WorldState.Restore(JsonSerializer.Deserialize<WorldSnapshot>(json, CanonicalJson.Options)!);
        foreach (var branch in new[] { state, restored })
        {
            var mutation = branch.BeginMutation();
            Hit(mutation, 201, 200);
            BattleAftermath.ResolveSettlementDefeat(mutation, 10, new(201, false), Rules(), []);
            Assert.Null(mutation.Settlements[10].SiegeContributions);
            Hit(mutation, 201, 1);
            Assert.Equal(1, Assert.Single(mutation.Settlements[10].SiegeContributions!).Value.Score);
            WorldInvariants.Validate(mutation);
            branch.Commit(mutation);
        }
        StateAssert.Equal(state.CreateSnapshot(), restored.CreateSnapshot());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RestoreRejectsNonpositiveCredit(long score)
    {
        var snapshot = State().CreateSnapshot();
        snapshot = snapshot with { Simulation = snapshot.Simulation with { Boundary = SimulationBoundary.Stable } };
        var invalid = snapshot with { Settlements = snapshot.Settlements.Select(s => s.Id != 10 ? s : s with
            { SiegeContributions = ImmutableDictionary<int, SiegeContribution>.Empty.Add(1, new(score, ImmutableDictionary<int, long>.Empty)) }).ToArray() };
        Assert.Throws<ArgumentException>(() => WorldState.Restore(invalid));
    }

    private static readonly MilitaryOrder Attack = new(MilitaryOrderKind.AttackSettlement, null, 10);

    private static void Hit(WorldMutation world, int unit, uint soldiers, int walls = 0) =>
        MilitaryCombat.DamageSettlement(world, world.Settlements[10], soldiers, Rules().Military!.Place(world.Settlements[10]).Combat!,
            unit, [], walls);

    private static SimulationRules Rules() => new() { Military = new(new(20, 8, [.. Enumerable.Repeat(0, 160)],
        [new(0, 1, 1)], [new(1, 5, 0, 100000, 1, new(1, 1), TravelMode.Land)
            { Combat = new(10, 10, 10, 10, 50), Attack = new(1, true), CaptureRanks = [BattleRank.Middle] }],
        [new(10, new(32, 7), new(1, 1)) { Combat = new(10, 10, 0, 10, 50) },
         new(11, new(92, 25), new(1, 1)) { Combat = new(10, 10, 0, 10, 50) },
         new(20, new(2, 7), new(1, 1)) { Combat = new(10, 10, 0, 10, 50) },
         new(30, new(77, 25), new(1, 1)) { Combat = new(10, 10, 0, 10, 50) }])) };

    private static WorldState State() => WorldState.Create(new(190, 1, 1), 42,
        [new(1, 500) { LeaderId = 1 }, new(2, 500) { LeaderId = 2 }, new(3, 500) { LeaderId = 3 }],
        [Place(10, 3) with { Soldiers = 500 }, Place(11, 3), Place(20, 1), Place(30, 2)],
        [Person(1, 1, 20) with { Status = OfficerStatus.Ruler }, Person(2, 2, 30) with { Status = OfficerStatus.Ruler },
         Person(3, 3, 11) with { Status = OfficerStatus.Ruler }, Person(12, 1, 20) with { SettlementId = null, UnitId = 101 }, Person(31, 3, 11)],
        [Unit(100, 1, 1), Unit(101, 1, 11) with { CommanderId = 12,
            MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(12, BattleRank.Middle) }, Unit(201, 2, 2)],
        [new(1, 1, 1, new(1000, 1000)) { IsRoot = true }, new(11, 1, 12, new(1000, 1000)),
         new(2, 2, 2, new(1000, 1000)) { IsRoot = true }, new(3, 3, 3, new(10000, 10000)) { IsRoot = true }]);

    private static SettlementState Place(int id, int owner) => new(id, owner, 5000, 1000, 1000, 80, 500, 0, 100, 0, 100)
        { CorpsId = owner };
    private static OfficerState Person(int id, int owner, int place) => new(id, owner, place, new(80, 80, 80, 80), false);
    private static UnitState Unit(int id, int owner, int corps) => new(id, owner, new(28, 7), 1000)
        { FormationId = 1, Morale = 80, CorpsId = corps, ServicePlaceId = owner == 1 ? 20 : 30, Order = Attack };
}
