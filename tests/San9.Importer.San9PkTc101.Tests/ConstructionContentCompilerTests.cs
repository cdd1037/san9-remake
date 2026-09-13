using San9.Core.Domain;
using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class ConstructionContentCompilerTests
{
    private static San9ScenarioFixedBuildingRecord[] Buildings() => Enumerable.Range(0, 17)
        .Select(i => new San9ScenarioFixedBuildingRecord(i, (ushort)i, 0, 0, (ushort)(100 + i), 600, 0,
            (ushort)(i is 10 or 12 or 14 ? 1 : 3), (ushort)(i is 11 or 13 or 15 ? 1 : 3), 0, (ushort)i, 65535,
            i == 8 ? 1UL << 40 : 0)).ToArray();

    private static San9ScenarioFormationRecord[] Formations() => Enumerable.Range(0, 28)
        .Select(i => new San9ScenarioFormationRecord(i, (ushort)i, 0, 0, 0,
            17, 23, 31, 41, 12, 1, 1, 100, 0, new byte[] { 1, 3, 4, 5, 7 }, 0)).ToArray();

    [Fact]
    public void CompleteFacilityCatalogKeepsSourceValuesAndExplicitConstructionCapabilities()
    {
        var buildings = Buildings();
        var rules = ConstructionContentCompiler.Facilities(buildings, Formations());
        Assert.Equal(Enumerable.Range(0, 17), rules.Select(f => f.Id));
        foreach (var rule in rules)
        {
            Assert.Equal(new CombatProfile(17, 23, 0, 41, 50), rule.Combat);
            Assert.Equal(buildings[rule.Id].ConstructionCost, rule.ConstructionCost);
            Assert.Equal(buildings[rule.Id].MaxDurability, rule.MaxDurability);
            Assert.Equal(rule.Id, rule.WoundedRecovery);
            Assert.Equal(rule.Id >= 6, rule.Buildable);
            Assert.Equal(rule.Id < 10, rule.CanHostPersons);
            Assert.Equal(rule.Id < 10, rule.ConsumesFood);
            Assert.Equal(rule.Id == 0 ? 500000u : 250000u, rule.SoldierCapacity);
            Assert.Equal(rule.Id == 16 ? 20 : (int?)null, rule.RequiredTacticId);
            Assert.Equal(rule.Id >= 6 ? 19 : (int?)null, rule.CostReductionTacticId);
            Assert.Equal(rule.Id >= 6 ? rule.Id == 16 ? 20 : 19 : (int?)null, rule.ConstructionGrowthTacticId);
            Assert.Equal(rule.Id < 10 ? new OrdinaryAttack(10, false) { ElevationBonusPerLevel = 5, MaximumElevationBonus = 10 } : null, rule.Attack);
            Assert.Equal(rule.Id == 1, rule.IsHarbor);
            Assert.Equal(rule.Id >= 10, rule.IsWork);
            Assert.Equal(rule.Id == 16 ? new FacilityReprisal(6, 1000, 10, 10, 10, 5) : null, rule.Reprisal);
            Assert.Equal(rule.Id switch { 8 => new FacilityAttackModifier(false, 5), 9 => new(true, 0), _ => null }, rule.InnateAttack);
        }
        Assert.Equal(new[] { 40 }, rules[8].TacticIds);
        Assert.Equal(new[] { 9, 10, 11, 23, 24, 25, 26, 27, 28, 29, 30 }, rules[0].Tactics!.TacticPriority);
        Assert.True(rules[0].Tactics!.RequiresEnabledTactic);
        foreach (int type in new[] { 2, 3, 4, 5, 7, 8, 9 })
        {
            var profile = rules[type].Tactics!;
            Assert.Equal(type < 6 ? 11 : type == 8 ? 10 : 9, Assert.Single(profile.TacticPriority));
            Assert.False(profile.RequiresEnabledTactic);
            Assert.Equal(10, profile.OpportunityChance);
            Assert.Equal(3, profile.MartialCooldown);
        }
        Assert.All(new[] { 1, 6, 10, 11, 12, 13, 14, 15, 16 }, id => Assert.Null(rules[id].Tactics));
        Assert.Equal(new[] { 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 },
            rules.Where(r => r.DestroyedAtZeroDurability).Select(r => r.Id));
        Assert.Throws<InvalidDataException>(() => ConstructionContentCompiler.Facilities(buildings[..16], Formations()));
    }

    [Fact]
    public void PlacementUsesCompleteFootprintRegionAndShapeSpecificFlagsWithoutFilteringByCurrentBuildings()
    {
        var squares = Enumerable.Range(0, 40000).Select(i => new San9ScenarioSquareRecord(i, (ushort)i, 0, 65535, 0, 0, 0, 3, 0, 0, 0, 0)).ToArray();
        void Square(int x, int y, ushort flags, ushort region = 600)
        {
            int i = y * 200 + x;
            squares[i] = squares[i] with { ZoneCode = region, SquareFlags = flags };
        }
        for (int y = 9; y <= 11; y++) for (int x = 9; x <= 11; x++) Square(x, y, 1);
        for (int x = 19; x <= 21; x++) Square(x, 10, 1 << 12);
        for (int y = 9; y <= 11; y++) Square(30, y, 1 << 13);
        var sites = ConstructionContentCompiler.Sites(squares, Buildings(), new HashSet<int> { 600 });
        Assert.Equal(new[] { 6, 7, 8, 9, 16 }, sites.Single(s => s.Position == MapCoordinates.Center(new(10, 10))).FacilityIds);
        Assert.Equal(new[] { 11, 13, 15 }, sites.Single(s => s.Position == MapCoordinates.Center(new(20, 10))).FacilityIds);
        Assert.Equal(new[] { 10, 12, 14 }, sites.Single(s => s.Position == MapCoordinates.Center(new(30, 10))).FacilityIds);
        Square(9, 9, 1, 601);
        Assert.DoesNotContain(ConstructionContentCompiler.Sites(squares, Buildings(), new HashSet<int> { 600, 601 }),
            s => s.Position == MapCoordinates.Center(new(10, 10)));
        Assert.Empty(ConstructionContentCompiler.Sites(squares, Buildings(), new HashSet<int>()));
    }
}
