using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class OfficerGrowthTests
{
    private static WorldMutation World(int ability = 99, ulong seed = 52)
    {
        var world = WorldState.Create(new(190, 1, 1), 42,
            [new(1, 1000, 1000, 0) { LeaderId = 1 }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(1, 1, 1, new(ability, ability, ability, ability), false) { Status = OfficerStatus.Ruler }]);
        var mutation = world.BeginMutation();
        mutation.SetSimulation(mutation.Simulation with { RandomStreams = [new("growth", seed)] });
        return mutation;
    }

    [Theory]
    [InlineData(52UL, 99, 100)]
    [InlineData(56UL, 99, 99)]
    [InlineData(52UL, 100, 100)]
    [InlineData(56UL, 100, 100)]
    [InlineData(52UL, 120, 120)]
    public void OrdinaryAbilityGrowthUsesStrictThreePercentAndSkipsRandomAtCap(ulong seed, int before, int expected)
    {
        foreach (var ability in Enum.GetValues<OfficerAbility>())
        {
            var world = World(before, seed);
            List<DomainEvent> events = [];
            OfficerAbilityGrowth.TryIncrease(world, 1, ability, "growth", events);
            var random = new StableRandom(seed);
            Assert.Equal(seed == 52 ? 2UL : 3UL, random.NextUInt64() % 100);
            Assert.Equal(before >= 100 ? seed : random.State, Assert.Single(world.Simulation.RandomStreams).State);
            if (expected == before) Assert.Empty(events);
            else Assert.Equal(new OfficerAbilityGrownEvent(world.Clock, 1, ability.ToString().ToLowerInvariant(), before, expected), Assert.Single(events));
            var actual = world.Officers[1].BaseAbilities;
            Assert.Equal(ability == OfficerAbility.War ? expected : before, actual.War);
            Assert.Equal(ability == OfficerAbility.Intelligence ? expected : before, actual.Intelligence);
            Assert.Equal(ability == OfficerAbility.Politics ? expected : before, actual.Politics);
            Assert.Equal(ability == OfficerAbility.Leadership ? expected : before, actual.Leadership);
        }
    }

    [Fact]
    public void DirectRewardsSaturateWithoutRandomAndDoNotReduceAuthorDefinedAbilities()
    {
        var world = World(95);
        List<DomainEvent> events = [];
        OfficerAbilityGrowth.Increase(world, 1, OfficerAbility.War, int.MaxValue, events);
        OfficerAbilityGrowth.Increase(world, 1, OfficerAbility.War, 1, events);
        Assert.Equal(100, world.Officers[1].BaseAbilities.War);
        Assert.Single(events);
        Assert.Equal(52UL, Assert.Single(world.Simulation.RandomStreams).State);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    public void InvalidProficiencyCategoriesHaveNoEffect(int category)
    {
        var world = World();
        List<DomainEvent> events = [];
        OfficerTacticGrowth.Increase(world, 1, category, 3, events);
        Assert.All(world.Officers[1].TacticProficiencies, value => Assert.Equal(0, value));
        Assert.Empty(events);
    }

    [Theory]
    [InlineData(false, 3, 3)]
    [InlineData(true, 3, 9)]
    [InlineData(true, int.MaxValue, 1000)]
    public void ProficiencyAppliesMasteryAndSaturatesWithoutOverflow(bool mastered, int amount, int expected)
    {
        var world = World();
        world.Replace(world.Officers[1] with { MasteredCategories = mastered ? [0] : [] });
        List<DomainEvent> events = [];
        OfficerTacticGrowth.Increase(world, 1, 0, amount, events);
        Assert.Equal(expected, world.Officers[1].TacticProficiencies[0]);
        Assert.Single(events);
        Assert.Equal(52UL, Assert.Single(world.Simulation.RandomStreams).State);
    }

    [Fact]
    public void DirectTeachingBypassesOrdinaryGateAndIsIdempotent()
    {
        var world = World();
        var tactic = new TacticRule(10001, 0, TacticEffect.Martial, 1, 1, 500, true, false, []);
        List<DomainEvent> events = [];
        OfficerTacticGrowth.TryLearn(world, 1, tactic, "growth", events);
        Assert.Empty(events);
        OfficerTacticGrowth.Grant(world, 1, tactic, events);
        OfficerTacticGrowth.Grant(world, 1, tactic, events);
        OfficerTacticGrowth.TryLearn(world, 1, tactic, "growth", events);
        Assert.Equal(10001, Assert.Single(world.Officers[1].LearnedTactics));
        Assert.IsType<TacticLearnedEvent>(Assert.Single(events));
        Assert.Equal(52UL, Assert.Single(world.Simulation.RandomStreams).State);
    }
}
