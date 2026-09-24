using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class EndingSnapshotTests
{
    private static readonly EndingDefinition Definition = new([101], [102], [101], [102], [101], [102], [901]);
    private static WorldSnapshot World()
    {
        var date = new GameClock(220, 1, 1);
        return new(date, new(date, 1))
        {
            Forces = [new(8, 100, 200, 950) { LeaderId = 400 }, new(9, 900, 900, 0) { IsBarbarian = true }],
            Corps = [new(10, 8, 400, null), new(11, 8, 401, new(300, 400)), new(12, 9, 900, new(900, 900))],
            Settlements = [
                new(101, 8, 1000, 100, 100, 0, 600, 400, 400, 0, 0) { Wounded = 100 },
                new(102, 9, 9999, 200, 200, 0, 700, 500, 500, 0, 0),
                new(103, 8, 2000, 100, 100, 0, 0, 0, 0, 0, 0) { Kind = SettlementKind.Facility, Wounded = 200 }],
            Units = [new(77, 8, new(0, 0), 3000) { Wounded = 300, CargoMoney = 900, CargoFood = 900 }, new(78, 9, new(0, 0), 9999)],
            Officers = Enumerable.Range(400, 6).Select(id => new OfficerState(id, 8, 101, new(90, 80, 80, 80), false)
                {
                    Status = id == 400 ? OfficerStatus.Ruler : id == 405 ? OfficerStatus.Captive : OfficerStatus.Serving,
                    TacticProficiencies = [0, 0, 0, 0, 400, 0, 200, 0, 0], LearnedTactics = [5, 6],
                }).ToArray(),
            Treasures = [new(901, 405, TreasureDiscovery.Discovered, null), new(902, 401, TreasureDiscovery.Discovered, null)],
        };
    }

    private static TreasureRulebook Treasures => new([
        new(901, default, 0), new(902, new(5, 0, 0, 0), 0)]);
    private static OfficerAbilityRules Abilities => new(Treasures, new([]));

    [Fact]
    public void DormantPeopleCitiesAndForcesDoNotContributeToEndingRequirements()
    {
        var before = World();
        var world = before with
        {
            Forces = before.Forces.Select(f => f.Id == 9 ? f with { IsDormant = true } : f).ToArray(),
            Settlements = before.Settlements.Select(s => s.Id == 102 ? s with { IsDormant = true } : s).ToArray(),
            Officers = before.Officers.Select(p => p.Id is 401 or 405 ? p with { IsDormant = true } : p).ToArray()
        };
        var result = EndingSnapshotFactory.Create(world, 8, Definition, Abilities, Treasures);
        Assert.False(result.HasLivingBarbarianForce);
        Assert.Equal(4, result.Officers.Length);
        Assert.Equal(0, result.CulturalTreasureCount);
        Assert.Equal(0, result.DomesticPublicOrder);
        Assert.False(result.OwnsExpansionGateways);
    }

    [Fact]
    public void ProjectionUsesCurrentOwnershipAndCountsEachResourceAccountOnce()
    {
        var world = World();
        string before = world.StateHash;
        var snapshot = EndingSnapshotFactory.Create(world, 8, Definition, Abilities, Treasures);
        Assert.Equal(5, snapshot.Officers.Length);
        Assert.DoesNotContain(snapshot.Officers, o => o.Id == 405);
        Assert.Equal(6600, snapshot.SoldiersAndWounded);
        Assert.Equal(400, snapshot.Money);
        Assert.Equal(600, snapshot.Food);
        Assert.Equal(1, snapshot.CulturalTreasureCount);
        Assert.Equal(95, snapshot.Officers.Single(o => o.Id == 401).Abilities.War);
        Assert.Equal(400, snapshot.Officers[0].NavalProficiency);
        Assert.Equal(200, snapshot.Officers[0].KnowledgeProficiency);
        Assert.Equal(2, snapshot.Officers[0].LearnedTacticCount);
        Assert.Equal(600, snapshot.ExpansionPublicOrder);
        Assert.Equal(400, snapshot.ExpansionCommerce);
        Assert.Equal(200, snapshot.DomesticDurability);
        Assert.Equal(700, snapshot.DomesticPublicOrder);
        Assert.False(snapshot.OwnsExpansionGateways);
        Assert.True(snapshot.OwnsDomesticGateways);
        Assert.True(snapshot.HasLivingBarbarianForce);
        Assert.Equal(before, world.StateHash);
    }

    [Fact]
    public void CapturesEliminationAndTreasureTransfersChangeDerivedSnapshot()
    {
        var world = World();
        var old = EndingSnapshotFactory.Create(world, 8, Definition, Abilities, Treasures);
        world = world with
        {
            Forces = world.Forces.Select(f => f.Id == 9 ? f with { IsEliminated = true } : f).ToArray(),
            Settlements = world.Settlements.Select(s => s.Id == 102 ? s with { OwnerId = 8 } : s).ToArray(),
            Treasures = world.Treasures.Select(t => t.Id == 901 ? t with { HolderId = null } : t).ToArray(),
        };
        var current = EndingSnapshotFactory.Create(world, 8, Definition, Abilities, Treasures);
        Assert.True(current.OwnsExpansionGateways);
        Assert.False(current.HasLivingBarbarianForce);
        Assert.Equal(16599, current.SoldiersAndWounded);
        Assert.Equal(0, current.CulturalTreasureCount);
        Assert.False(old.OwnsExpansionGateways);
        Assert.Equal(1, old.CulturalTreasureCount);
    }

    [Fact]
    public void ContinentalEndingCountsEachMinistersCurrentTacticsAtTheSixtyThreshold()
    {
        var original = World();
        var world = original with
        {
            Forces = original.Forces.Select(f => f.Id == 8 ? f with { ProvidedTactics = [5, 9000] }
                : f with { IsEliminated = true }).ToArray(),
            Officers = original.Officers.Select(o => o with { LearnedTactics = Enumerable.Range(0, 18).ToImmutableArray() }).ToArray(),
            Treasures = [new(901, 401, TreasureDiscovery.Discovered, null),
                new(902, 402, TreasureDiscovery.Discovered, null), new(903, 403, TreasureDiscovery.Discovered, null),
                new(904, 401, TreasureDiscovery.Discovered, null)]
        };
        var treasures = new TreasureRulebook(Enumerable.Range(901, 4).Select(id =>
            new TreasureRule(id, default, 0) { TacticId = 9001 }));
        var abilities = new OfficerAbilityRules(treasures, new([]));
        EndingSnapshot Project(WorldSnapshot state) => EndingSnapshotFactory.Create(state, 8, Definition, abilities, treasures)
            with { SoldiersAndWounded = 300000, ExpansionPublicOrder = 2000, OwnsExpansionGateways = true };
        var sixty = Project(world);
        var court = new EndingOfficeholders(401, 402, 403, 404);
        Assert.All(sixty.Officers.Where(o => o.Id is 401 or 402 or 403), o => Assert.Equal(20, o.LearnedTacticCount));
        Assert.Equal(EndingOutcome.GreatEmpire, EndingResolution.Resolve(sixty, court, EndingPolicy.ContinentalExpansion).Outcome);
        var removed = world with { Treasures = world.Treasures.Select(t => t.Id == 903 ? t with { HolderId = 400 } : t).ToArray() };
        var fiftyNine = Project(removed);
        Assert.Equal(19, fiftyNine.Officers.Single(o => o.Id == 403).LearnedTacticCount);
        Assert.Equal(EndingOutcome.CommercialPower, EndingResolution.Resolve(fiftyNine, court, EndingPolicy.ContinentalExpansion).Outcome);
        Assert.Equal(20, sixty.Officers.Single(o => o.Id == 403).LearnedTacticCount);
        Assert.All(world.Officers, o => Assert.Equal(18, o.LearnedTactics.Length));
    }

    [Fact]
    public void InvalidContentGroupsAndIneligibleWinnerAreRejected()
    {
        var world = World();
        foreach (var definition in new[] { Definition with { ExpansionGatewayCityIds = [] },
            Definition with { ExpansionGatewayCityIds = [101, 101] }, Definition with { ExpansionGatewayCityIds = [103] },
            Definition with { CulturalTreasureIds = [999] } })
            Assert.Throws<ArgumentException>(() => EndingSnapshotFactory.Create(world, 8, definition, Abilities, Treasures));
        Assert.Throws<ArgumentException>(() => EndingSnapshotFactory.Create(world, 9, Definition, Abilities, Treasures));
        Assert.Throws<ArgumentException>(() => EndingSnapshotFactory.Create(world, 99, Definition, Abilities, Treasures));
    }
}
