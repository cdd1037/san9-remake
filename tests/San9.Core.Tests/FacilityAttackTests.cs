using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class FacilityAttackTests
{
    private static MilitaryRulebook Rules(bool enabled, FacilityAttackModifier? innate = null)
    {
        var combat = new CombatProfile(10, 10, 0, 10, 50);
        return new(new(6, 1, [0, 0, 0, 0, 0, 0], [new(0, 1, null)],
            [new(7000, 1, 0, 100000, 1, new(1, 1), TravelMode.Land) { Combat = combat, CaptureRanks = [BattleRank.Front] }],
            [new(9000, new(7, 2), new(1, 1)) { FacilityTypeId = 8000, Combat = combat, Attack = new(1, false), EnabledTacticIds = enabled ? [9500, 9501] : [] },
                new(9001, new(27, 2), new(1, 1)) { FacilityTypeId = 8000, Combat = combat }])
        {
            Facilities = [new(8000, 0, 100, new(1, 1), 0, true, true, true, 100000, null, null, null, [])
                { Combat = combat, Attack = new(1, false), InnateAttack = innate }],
            Tactics = [new(9500, null, TacticEffect.FacilityAttack, 0, 0, 0, false, false, []) { FacilityAttack = new(true, 0) },
                new(9501, null, TacticEffect.FacilityAttack, 0, 0, 0, false, false, []) { FacilityAttack = new(false, 2) }]
        });
    }

    private static SettlementState Home() => new(9000, 1, 100000, 100, 100, 100, 0, 0, 100, 0, 100);

    [Fact]
    public void InnateAndEnabledCapabilitiesCombineWithoutDuplicatingRange()
    {
        var enabled = Rules(true, new(false, 2)).FacilityAttack(Home() with { EnabledTacticIds = [9500, 9501] });
        Assert.True(enabled.UsesRockfallPower);
        Assert.Equal(3, enabled.Attack!.Range);
        var innate = Rules(false, new(false, 2)).FacilityAttack(Home());
        Assert.False(innate.UsesRockfallPower);
        Assert.Equal(3, innate.Attack!.Range);
        Assert.Equal(1, Rules(false).FacilityAttack(Home()).Attack!.Range);
        Assert.Null(Rules(true).FacilityAttack(Home() with { IsDestroyed = true }).Attack);
        Assert.Null(Rules(true).FacilityAttack(Home() with { Facility = new(8000, new(7, 2), 1) }).Attack);
    }

    [Theory]
    [InlineData(true, 3, 10)]
    [InlineData(true, 4, 0)]
    [InlineData(false, 3, 0)]
    [InlineData(true, 3, 10, 100, false, true)]
    [InlineData(true, 3, 10, 100, true, false)]
    [InlineData(true, 3, 10, 100, true, true)]
    [InlineData(true, 3, 10, 100, false, false)]
    public void EnabledFacilityAbilitiesModifyOneDailyOrdinaryAttack(bool enabled, int distance, int attacks, int bonus = 0, bool sourcePlayer = true, bool targetPlayer = false)
    {
        var rules = Rules(enabled);
        var state = WorldState.Create(new(190, 1, 1), 42,
            [new(1, 100) { LeaderId = 11 }, new(2, 100) { LeaderId = 22 }],
            [Home() with { EnabledTacticIds = enabled ? [9500, 9501] : [] }, new(9001, 2, 10000, 100, 100, 100, 0, 0, 100, 0, 100)],
            [new(11, 1, 9000, new(80, 80, 80, 80), false) { Status = OfficerStatus.Ruler },
                new(21, 2, null, new(80, 80, 80, 80), false) { UnitId = 7001 },
                new(22, 2, 9001, new(80, 80, 80, 80), false) { Status = OfficerStatus.Ruler }],
            units: [new(7001, 2, new(7 + distance, 2), 100000) { FormationId = 7000, Morale = 100, ServicePlaceId = 9001,
                MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(21, BattleRank.Front) }],
            controls: [new("first", 1, sourcePlayer ? ControllerKind.Player : ControllerKind.Automatic),
                new("second", 2, targetPlayer ? ControllerKind.Player : ControllerKind.Automatic)], computerDamageBonusPercent: bonus, corps: [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 1000000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 22, new(10000, 1000000)) { IsRoot = true }]);
        var engine = new SimulationEngine(state, EconomyRulebook.Empty, PersonnelRulebook.Empty, TreasureRulebook.Empty, military: rules);
        engine.EndControl(new("first"));
        engine.EndControl(new("second"));
        var result = engine.AdvanceXun();
        var damage = result.Events.OfType<BattleDamageEvent>().Where(e => e.AttackerIsSettlement && e.AttackerId == 9000).ToArray();
        Assert.Equal(attacks, damage.Length);
        Assert.All(damage.GroupBy(e => e.Clock), group => Assert.Single(group));
        Assert.Empty(result.Events.OfType<TacticActivatedEvent>());
        if (attacks > 0)
        {
            var strength = new CombatStrength(100000, 100, 80, 1, 10);
            uint expected = CombatMath.TroopDamage(strength, strength with { Morale = 99 }, 10, 10, 0, usesRockfallPower: true);
            if (!sourcePlayer && targetPlayer) expected = expected * (uint)(100 + bonus) / 100;
            Assert.Equal(expected, damage[0].SoldierLoss);
            Assert.Equal(damage[0].SoldierLoss, damage[0].AddedWounded + damage[0].Dead);
            Assert.True(engine.Snapshot.Units.Single().Wounded > 0);
        }
    }
}
