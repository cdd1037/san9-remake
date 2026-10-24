using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed partial class IntelligenceTacticTests
{
    public static TheoryData<TacticEffect, bool, int, int, bool, bool> AreaDiplomacyCases()
    {
        var cases = new TheoryData<TacticEffect, bool, int, int, bool, bool>();
        foreach (var effect in new[] { TacticEffect.Illusion, TacticEffect.Sorcery })
        foreach (bool settlement in new[] { false, true })
        {
            cases.Add(effect, settlement, 39, 0, false, true);
            cases.Add(effect, settlement, 40, 0, false, false);
            cases.Add(effect, settlement, 80, 0, false, false);
            cases.Add(effect, settlement, 0, 1, false, false);
            cases.Add(effect, settlement, 0, -1, false, false);
            cases.Add(effect, settlement, 0, 0, true, false);
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(AreaDiplomacyCases))]
    public void AreaEffectsUseFullAutomaticEnemyEligibility(TacticEffect effect, bool settlement, int relation,
        int requestDirection, bool allied, bool affected)
    {
        MilitaryRulebook military = null!;
        var engine = Engine(effect, area: true, areaSettlement: settlement, captureRules: value => military = value);
        var world = WorldState.Restore(engine.CaptureStableState()).BeginMutation();
        world.SetRelation(new(1, 3, relation));
        if (allied) world.SetSimulation(world.Simulation with { Alliance = new(2, 1, [1, 3], 20) });
        if (requestDirection != 0)
            AttackRequests.Establish(world, requestDirection > 0 ? 1 : 3, requestDirection > 0 ? 3 : 1, 2, []);
        var previousUnit = settlement ? null : world.Units[2];
        var previousSettlement = world.Settlements[3];
        List<DomainEvent> events = [];

        IntelligenceTactics.Resolve(world, new(0, false), world.Officers[11], military.Tactics[9000],
            new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 }, 10, new() { Military = military }, events);

        Assert.Contains(events.OfType<IntelligenceEffectResolvedEvent>(), e => e.TargetId == 1 && !e.TargetIsSettlement);
        Assert.Equal(affected, events.OfType<IntelligenceEffectResolvedEvent>()
            .Any(e => e.TargetId == (settlement ? 3 : 2) && e.TargetIsSettlement == settlement));
        if (affected)
            Assert.True(settlement ? world.Settlements[3].Soldiers < previousSettlement.Soldiers :
                world.Units[2].Soldiers < previousUnit!.Soldiers);
        else if (settlement) Assert.Equal(previousSettlement, world.Settlements[3]);
        else Assert.Equal(previousUnit, world.Units[2]);
        WorldInvariants.Validate(world);
    }
}
