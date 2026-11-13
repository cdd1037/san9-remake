using System.Collections.Immutable;
using San9.Application.AI;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed partial class OriginalAiMilitaryTests
{
    [Fact]
    public void AiEquipsOnlyCompatibleAvailableArmamentsWithoutDuplicatingStock()
    {
        var definition = Fixture().Military!;
        var rules = new MilitaryRulebook(definition with
        {
            TroopTypes = [new(10, "弓", 1, ImmutableDictionary<int, int>.Empty), new(20, "枪", 1, ImmutableDictionary<int, int>.Empty)],
            Armaments = [new("spear", "铁矛", ArmamentSlot.Weapon, [20], 10, 0, 100, null),
                new("armor", "皮甲", ArmamentSlot.Armor, [], 0, 8, 100, null)]
        });
        var force = new ForceState(1, 0) { ArmamentInventory = ImmutableDictionary<string, uint>.Empty.Add("spear", 400).Add("armor", 500) };
        var detachments = ImmutableDictionary<int, DetachmentState>.Empty.Add(2, new(20, 1, 700)).Add(1, new(10, 1, 400));
        var equipped = MixedDeploymentSelection.EquipArmaments(detachments, rules, force);
        Assert.Null(equipped[1].Weapon);
        Assert.Equal(400u, equipped[1].Armor!.IssuedSets);
        Assert.Equal(400u, equipped[2].Weapon!.IssuedSets);
        Assert.Equal(100u, equipped[2].Armor!.IssuedSets);
        Assert.Equal(500u, force.ArmamentInventory!["armor"]);
    }

    [Fact]
    public void AiDeploymentDoesNotUseUnresearchedArmamentsEvenWhenTheyAreStronger()
    {
        var definition = Fixture().Military!;
        var rules = new MilitaryRulebook(definition with
        {
            TroopTypes = [new(20, "枪", 1, ImmutableDictionary<int, int>.Empty)],
            ResearchProjects = [new("advanced", 100, 30, [])],
            Armaments = [new("spear", "铁矛", ArmamentSlot.Weapon, [20], 10, 0, 100, null),
                new("halberd", "铁戟", ArmamentSlot.Weapon, [20], 20, 0, 100, "advanced")]
        });
        var force = new ForceState(1, 0) { ArmamentInventory = ImmutableDictionary<string, uint>.Empty.Add("spear", 500).Add("halberd", 500) };
        var detachments = ImmutableDictionary<int, DetachmentState>.Empty.Add(1, new(20, 1, 500));
        Assert.Equal("spear", MixedDeploymentSelection.EquipArmaments(detachments, rules, force)[1].Weapon!.DefinitionId);
        Assert.Equal("halberd", MixedDeploymentSelection.EquipArmaments(detachments, rules,
            force with { CompletedResearch = force.CompletedResearch.Add("advanced") })[1].Weapon!.DefinitionId);
    }

    [Fact]
    public void MixedDeploymentMovesMeleeToFrontAndKeepsSoldiersAndTactics()
    {
        var definition = Fixture().Military!;
        var formation = definition.Formations.Single(f => f.Id == 400) with
        {
            OfficerCapacity = 2,
            Positions = [new(0, BattleRank.Front), new(1, BattleRank.Middle)]
        };
        var rules = new MilitaryRulebook(definition with
        {
            Formations = definition.Formations.Select(f => f.Id == formation.Id ? formation : f).ToImmutableArray(),
            TroopTypes = [new(10, "弓", 1, ImmutableDictionary<int, int>.Empty) { IsRangedAttack = true },
                new(20, "枪", 1, ImmutableDictionary<int, int>.Empty)]
        });
        var positions = new Dictionary<int, FormationPosition> { [1] = formation.Positions[0], [2] = formation.Positions[1] }.ToImmutableDictionary();
        var soldiers = new Dictionary<int, DetachmentState> { [1] = new(10, 1, 900), [2] = new(20, 1, 100) }.ToImmutableDictionary();
        var result = MixedDeploymentSelection.EnsureFrontLine(formation, positions, soldiers, rules, new ForceState(1, 0));
        Assert.Equal(BattleRank.Front, result.Positions[2].Rank);
        Assert.Equal(BattleRank.Middle, result.Positions[1].Rank);
        Assert.Equal(soldiers, result.Detachments);
        var allRanged = soldiers.SetItem(2, soldiers[2] with { TroopTypeId = 10 });
        var fallback = MixedDeploymentSelection.EnsureFrontLine(formation, positions, allRanged, rules, new ForceState(1, 0));
        Assert.Equal(20, fallback.Detachments[1].TroopTypeId);
        Assert.Equal(900u, fallback.Detachments[1].Soldiers);
    }

    [Fact]
    public void MixedDeploymentSelectsAffordableUnlockedSiegeEquipment()
    {
        var content = Fixture();
        var definition = content.Military!;
        var basis = definition.Formations.Single(f => f.Id == 400);
        var rules = new MilitaryRulebook(definition with
        {
            Formations = definition.Formations.Add(basis with { Id = 401, Combat = new(10, 10, 20, 10, 50) })
                .Add(basis with { Id = 402, Combat = new(10, 10, 40, 10, 50) }),
            ResearchProjects = [new("ram", 1, 1, []), new("stone", 1, 1, [])],
            SiegeEquipment = [new(10, "冲车", "ram", 401, 100), new(20, "发石", "stone", 402, 500)]
        });
        var force = new ForceState(1, 0) { CompletedResearch = ["ram", "stone"] };
        var treasures = new ScenarioSessionDefinition(content).Rules.Treasures;
        Assert.Equal(10, MixedDeploymentSelection.SiegeEquipment(rules, force, [], [], treasures, 499));
        Assert.Equal(20, MixedDeploymentSelection.SiegeEquipment(rules, force, [], [], treasures, 500));
        Assert.Null(MixedDeploymentSelection.SiegeEquipment(rules, force with { CompletedResearch = [] }, [], [], treasures, 500));
    }
}
