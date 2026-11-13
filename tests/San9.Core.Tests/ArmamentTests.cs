using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class ArmamentTests
{
    [Fact]
    public void OverflowReservePreservesReturnedSetsAndReleasesOnlyAvailableRoom()
    {
        var inventory = ImmutableDictionary<string, uint>.Empty.Add("spear", uint.MaxValue - 10);
        var (full, reserve) = ArmamentInventory.ReturnWithReserve(inventory, ImmutableDictionary<string, ulong>.Empty,
            new("spear", 100, 100, 20));
        Assert.Equal(uint.MaxValue, full["spear"]);
        Assert.Equal(70ul, reserve["spear"]);
        var (part, remaining) = ArmamentInventory.ReleaseReserve(full.SetItem("spear", uint.MaxValue - 30), reserve);
        Assert.Equal(uint.MaxValue, part["spear"]);
        Assert.Equal(40ul, remaining["spear"]);
        var (complete, empty) = ArmamentInventory.ReleaseReserve(part.SetItem("spear", uint.MaxValue - 50), remaining);
        Assert.Equal(uint.MaxValue - 10, complete["spear"]);
        Assert.Empty(empty);
    }

    [Fact]
    public void CasualtiesAreCumulativeRatherThanRoundedAfterEachExchange()
    {
        var issued = new ArmamentAllocation("iron-spear", 333, 1000, 0);
        var split = issued.ApplyCasualties(200).ApplyCasualties(300);
        var single = issued.ApplyCasualties(500);

        Assert.Equal(single.RemainingSets, split.RemainingSets);
        Assert.Equal(166u, split.RemainingSets);
        Assert.Equal(0u, split.ApplyCasualties(500).RemainingSets);
        Assert.Throws<ArgumentOutOfRangeException>(() => split.ApplyCasualties(501));
    }

    [Fact]
    public void PartialCoverageAndReinforcementScaleTheBonus()
    {
        var issued = new ArmamentAllocation("iron-spear", 500, 1000, 0);
        Assert.Equal(1000, issued.EffectiveBonusBasisPoints(20, 1000));
        Assert.Equal(500, issued.EffectiveBonusBasisPoints(20, 2000));
        Assert.Equal(0, issued.EffectiveBonusBasisPoints(20, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => issued.EffectiveBonusBasisPoints(-1, 1000));
        Assert.Equal(80, new ArmamentAllocation("iron-spear", 1000, 10000, 0).EffectiveBonusBasisPoints(8, 10000));
    }

    [Fact]
    public void DefinitionsCanLimitWeaponsToSpecificTroopTypes()
    {
        var spear = new ArmamentDefinition("iron-spear", "铁矛", ArmamentSlot.Weapon,
            ImmutableHashSet.Create(0, 4), 10, 0, 100, null);
        Assert.True(spear.IsValid);
        Assert.True(spear.Supports(0));
        Assert.False(spear.Supports(2));
        Assert.False((spear with { PurchaseCostPerThousand = -1 }).IsValid);
    }

    [Fact]
    public void PartialIssueAndReturnPreserveExactSetsRatherThanRoundingToBatches()
    {
        var spear = new ArmamentDefinition("iron-spear", "铁矛", ArmamentSlot.Weapon,
            ImmutableHashSet.Create(0), 10, 0, 100, null);
        var inventory = ArmamentInventory.AddBatches(ImmutableDictionary<string, uint>.Empty, spear.Id, 1);
        var (remaining, allocation) = ArmamentInventory.Issue(inventory, spear, 0, 1500);
        Assert.Empty(remaining);
        Assert.Equal(1000u, allocation.IssuedSets);
        var returned = ArmamentInventory.Return(remaining, allocation.ApplyCasualties(300));
        Assert.Equal(800u, returned[spear.Id]);
        var (afterIssue, next) = ArmamentInventory.Issue(returned, spear, 0, 700);
        Assert.Equal(100u, afterIssue[spear.Id]);
        Assert.Equal(700u, next.IssuedSets);
        Assert.Throws<ArgumentException>(() => ArmamentInventory.Issue(returned, spear, 1, 700));
        Assert.Throws<OverflowException>(() => ArmamentInventory.AddBatches(inventory, spear.Id, uint.MaxValue));
    }

    [Fact]
    public void ReturningAndReissuingCannotRecoverFractionalCasualtyLoss()
    {
        var spear = new ArmamentDefinition("spear", "铁矛", ArmamentSlot.Weapon, [0], 1, 0, 1, null);
        var stock = ArmamentInventory.AddBatches(ImmutableDictionary<string, uint>.Empty, spear.Id, 1);
        var (afterIssue, issued) = ArmamentInventory.Issue(stock, spear, 0, 2000);
        var returned = ArmamentInventory.Return(afterIssue, issued.ApplyCasualties(1));
        Assert.Equal(999u, returned[spear.Id]);
        var (_, reissued) = ArmamentInventory.Issue(returned, spear, 0, 1999);
        Assert.Equal(999u, reissued.IssuedSets);
    }

    [Fact]
    public void RulebookRejectsDuplicateAndUnresolvedArmamentDefinitions()
    {
        var armor = new ArmamentDefinition("leather", "皮甲", ArmamentSlot.Armor, [], 0, 8, 100, null);
        var military = new MilitaryDefinition(1, 1, [0], [new(0, 1, null)], [], []);
        Assert.Empty(new MilitaryRulebook(military).Armaments);
        Assert.Single(new MilitaryRulebook(military with { Armaments = [armor] }).Armaments);
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(military with { Armaments = [armor, armor] }));
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(military with
            { Armaments = [armor with { ResearchProjectId = "missing" }] }));
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(military with
            { Armaments = [armor with { TroopTypeIds = [42] }] }));
    }

    [Fact]
    public void CombatLossUpdatesBothSlotsAndDoesNotEquipReinforcements()
    {
        var detachment = new DetachmentState(0, 1, 1000)
        {
            Weapon = new("spear", 500, 1000, 0),
            Armor = new("armor", 1000, 1000, 0)
        };
        var initial = ImmutableDictionary<int, DetachmentState>.Empty.Add(1, detachment);
        var once = DetachmentAllocation.ResizeForCombatLoss(initial, 800);
        Assert.Equal(400u, once[1].Weapon!.RemainingSets);
        Assert.Equal(800u, once[1].Armor!.RemainingSets);
        var reinforced = once.SetItem(1, once[1] with { Soldiers = 1000 });
        var afterLoss = DetachmentAllocation.ResizeForCombatLoss(reinforced, 900);
        Assert.Equal(360u, afterLoss[1].Weapon!.RemainingSets);
        Assert.Equal(720u, afterLoss[1].Armor!.RemainingSets);
        Assert.Throws<ArgumentOutOfRangeException>(() => DetachmentAllocation.ResizeForCombatLoss(initial, 1001));
    }

    [Fact]
    public void CombatBonusWeightsCoverageAndTheParticipatingDetachment()
    {
        var definition = new MilitaryDefinition(1, 1, [0], [new(0, 1, null)], [], [])
        {
            TroopTypes = [new(0, "枪兵", 1, ImmutableDictionary<int, int>.Empty)],
            Armaments = [new("spear", "铁矛", ArmamentSlot.Weapon, [0], 20, 0, 100, null),
                new("armor", "皮甲", ArmamentSlot.Armor, [], 0, 10, 100, null)]
        };
        var military = new MilitaryRulebook(definition);
        var unit = new UnitState(1, 1, new(0, 0), 2000)
        {
            Detachments = ImmutableDictionary<int, DetachmentState>.Empty
                .Add(1, new(0, 1, 1000) { Weapon = new("spear", 500, 1000, 0), Armor = new("armor", 1000, 1000, 0) })
                .Add(2, new(0, 1, 1000))
        };
        Assert.Equal(1000, MixedTroopCombat.ArmamentBonus(unit, military, ArmamentSlot.Weapon, 1));
        Assert.Equal(500, MixedTroopCombat.ArmamentBonus(unit, military, ArmamentSlot.Weapon, null));
        Assert.Equal(500, MixedTroopCombat.ArmamentBonus(unit, military, ArmamentSlot.Armor, null));
        Assert.Equal(0, MixedTroopCombat.ArmamentBonus(unit, military, ArmamentSlot.Weapon, 2));
    }
}
