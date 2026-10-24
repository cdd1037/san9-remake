using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Core.Tests;

public sealed class TacticFormationTests
{
    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(50, 49, true)]
    [InlineData(50, 50, false)]
    [InlineData(100, 99, true)]
    public void FieldDefensiveOpportunityUsesGlobalChance(int chance, int roll, bool allowed)
    {
        var (world, rules, _) = TacticSnipingTests.Create(defensiveChance: chance);
        var mutation = world.BeginMutation();
        mutation.SetSimulation(mutation.Simulation with { RandomStreams =
            [new("military-tactic-counter", TestSeeds.StateForRoll(roll))] });
        var target = new MilitaryOrder(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 2 };
        Assert.Equal(allowed ? TacticOpportunity.Ready : TacticOpportunity.Unavailable, TacticCombat.PrepareOpportunity(mutation, 1, target, rules, [], true));
        Assert.Equal(TacticOpportunity.Ready, TacticCombat.PrepareOpportunity(mutation, 1, target, rules, [], false));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void GlobalDefensiveChanceRejectsInvalidPercent(int chance)
    {
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(Definition(false, true) with { DefensiveTacticChance = chance }));
    }

    [Theory]
    [InlineData(UnitCondition.Normal, true)]
    [InlineData(UnitCondition.HighSpirits, true)]
    [InlineData(UnitCondition.Engaged, false)]
    [InlineData(UnitCondition.Trap, false)]
    [InlineData(UnitCondition.Disguised, false)]
    [InlineData(UnitCondition.NoFormation, false)]
    [InlineData(UnitCondition.Confused, false)]
    [InlineData(UnitCondition.Routed, false)]
    public void DefensiveOpportunityRequiresNormalOrHighSpiritsBeforeRolling(UnitCondition condition, bool allowed)
    {
        var (world, rules, _) = TacticSnipingTests.Create(defensiveChance: 50);
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Units[1] with { Condition = condition });
        var counterState = TestSeeds.StateForRoll(0);
        mutation.SetSimulation(mutation.Simulation with { RandomStreams = [new("military-tactic-counter", counterState)] });
        var target = new MilitaryOrder(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 2 };

        Assert.Equal(allowed ? TacticOpportunity.Ready : TacticOpportunity.Unavailable, TacticCombat.PrepareOpportunity(mutation, 1, target, rules, [], true));
        Assert.Equal(allowed, mutation.Simulation.RandomStreams.Single().State != counterState);
    }

    private static MilitaryDefinition Definition(bool water, bool compatible)
    {
        var combat = new CombatProfile(10, 10, 10, 10, 50);
        return new(100, 3, Enumerable.Repeat(water ? 1 : 0, 300).ToImmutableArray(),
            [new(0, 1, null), new(1, 1, 1) { IsWater = true }],
            [new(100, 5, 0, 100000, 2, new(1, 1), TravelMode.Land)
                { Combat = combat, Attack = new(10, false), CaptureRanks = [BattleRank.Front, BattleRank.Middle], Tactics = new(100, 2, [], []) },
             new(200, 5, 0, 100000, 2, new(1, 1), TravelMode.Water)
                { Combat = combat, Attack = new(10, false), CaptureRanks = [BattleRank.Middle, BattleRank.Middle], Tactics = new(100, 2, [], []) }], [])
        {
            Tactics = [new(9000, 8, TacticEffect.Inspire, 10, 9, 0, true, false,
                compatible ? [100, 200] : [100])]
        };
    }

    [Theory]
    [InlineData("construction", false)]
    [InlineData("construction", true)]
    [InlineData("transport", false)]
    [InlineData("transport", true)]
    [InlineData("unformed", false)]
    [InlineData("no-formation", true)]
    public void ForbiddenSourcesCannotStartCombatOrSupportEvenWithCompatibleEquipment(string kind, bool water)
    {
        var (world, _, _) = TacticSnipingTests.Create();
        var definition = Definition(water, true);
        if (kind == "construction") definition = definition with { ConstructionFormations = new(100, 200) };
        if (kind == "transport") definition = definition with { TransportFormations = new(100, 200) };
        if (kind == "unformed") definition = definition with { UnformedFormationId = 100 };
        var rules = new SimulationRules { Military = new(definition) };
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Units[1] with { NavalFormationId = 200, Morale = 79,
            Condition = kind == "no-formation" ? UnitCondition.NoFormation : UnitCondition.Normal });
        var originalOfficer = mutation.Officers[11];
        var target = new MilitaryOrder(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 2 };
        List<DomainEvent> events = [];
        Assert.Equal(TacticOpportunity.Unavailable, TacticCombat.PrepareOpportunity(mutation, 1, target, rules, events, false));
        TacticCombat.ResolveOpportunity(mutation, 1, target, rules, events);
        TacticSupport.AdvanceDay(mutation, rules, events);
        Assert.Empty(events);
        Assert.Empty(mutation.Simulation.RandomStreams);
        Assert.Equal(originalOfficer, mutation.Officers[11]);
        Assert.Equal(79, mutation.Units[1].Morale);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void SupportCompatibilityUsesTheCurrentLandOrNavalFormation(bool water, bool compatible, bool activates)
    {
        var (world, _, _) = TacticSnipingTests.Create();
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Units[1] with { NavalFormationId = 200, Morale = 79 });
        mutation.SetSimulation(mutation.Simulation with { RandomStreams =
            [new("military-tactic-support-member", TestSeeds.StateForRoll(0))] });
        List<DomainEvent> events = [];
        TacticSupport.AdvanceDay(mutation, new() { Military = new(Definition(water, compatible)) }, events);
        Assert.Equal(activates, events.OfType<TacticActivatedEvent>().Any());
        Assert.Equal(activates, mutation.Units[1].Morale > 79);
        Assert.Equal(9000, mutation.Officers[11].EquippedTacticId);
    }
}
