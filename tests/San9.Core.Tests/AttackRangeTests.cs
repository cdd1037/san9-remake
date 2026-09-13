using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class AttackRangeTests
{
    private static readonly CommandContext Player = new("player");
    private static SimulationEngine Engine(bool restricted = false, bool waterTarget = false, bool harbor = false, Action<MilitaryRulebook>? captureRules = null)
    {
        var combat = new CombatProfile(0, 0, 0, 1000, 50);
        var rules = new MilitaryRulebook(new(7, 1, [0, 0, 0, 0, 0, 1, 1],
            [new(0, 1, 1), new(1, 1, 1) { IsWater = waterTarget }],
            [new(7000, 100, 0, 100000, 1, new(1, 1), TravelMode.Land)
                { Combat = combat, CaptureRanks = [BattleRank.Front],
                    Attack = new(1, false) { ElevationBonusPerLevel = restricted ? 0 : 5, MaximumElevationBonus = restricted ? 0 : 10,
                        RequiresWaterOrHarbor = restricted }, Tactics = new(100, 0, 2, [0], []) },
                new(7001, 1, 0, 100000, 1, new(1, 1), TravelMode.Land)
                { Combat = combat, Attack = new(1, false), CaptureRanks = [BattleRank.Front] }],
            [new(1, new(2, 2), new(1, 1)) { FacilityTypeId = 8000, Combat = combat },
                new(2, new(32, 2), new(1, 1)) { FacilityTypeId = 8000, Combat = combat }])
        {
            Elevations = [0, 0, 0, 2, 0, 0, 0],
            Facilities = [new(8000, 0, 100, new(1, 1), 0, false, true, true, 100000, null, null, null, []) { IsHarbor = harbor }],
            Tactics = [new(9000, 0, TacticEffect.Martial, 1, 9, 0, true, true, [7000])]
        });
        var state = WorldState.Create(new(190, 1, 1), 42,
            [new(1, 10000, 1000000, 100) { LeaderId = 11 }, new(2, 10000, 1000000, 100) { LeaderId = 22 }],
            [new(1, 1, 10000, 100, 100, 100, 0, 0, 100, 0, 100), new(2, 2, 10000, 100, 100, 100, 0, 0, 100, 0, 100)],
            [new(11, 1, null, new(80, 80, 80, 80), false) { UnitId = 0, LearnedTactics = [9000], EquippedTacticId = 9000 },
                new(21, 2, null, new(80, 80, 80, 80), false) { UnitId = 1 }, new(22, 2, 2, new(80, 80, 80, 80), false)],
            units: [new(0, 1, new(7, 2), 100000) { FormationId = 7000, Morale = 100, ServicePlaceId = 1,
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(11, BattleRank.Front) },
                new(1, 2, new(27, 2), 100000) { FormationId = 7001, Morale = 100, ServicePlaceId = 2,
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(21, BattleRank.Front) }],
            controls: [new("player", 1, ControllerKind.Player)]);
        captureRules?.Invoke(rules);
        return new(state, EconomyRulebook.Empty, PersonnelRulebook.Empty, TreasureRulebook.Empty, military: rules);
    }

    [Fact]
    public void ApproachStopsAtHighGroundAndUsesSameRangeForCombatAndTactics()
    {
        MilitaryRulebook rules = null!;
        var engine = Engine(captureRules: value => rules = value);
        Assert.True(engine.Execute(Player, new OrderUnitCommand(Guid.NewGuid(), 1, 0, 0,
            new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 })).Submission.Accepted);
        engine.EndControl(Player);
        var events = engine.AdvanceXun().Events;
        Assert.Equal(new WorldPosition(16, 2), events.OfType<UnitMovedEvent>().First(e => e.UnitId == 0).To);
        Assert.Contains(events.OfType<TacticActivatedEvent>(), e => e.SourceId == 0 && e.Clock.Day == 2);
        Assert.All(events.OfType<BattleDamageEvent>().Where(e => e.AttackerId == 1), e => Assert.Equal(0u, e.SoldierLoss));
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), EconomyRulebook.Empty,
            PersonnelRulebook.Empty, TreasureRulebook.Empty, military: rules);
        engine.EndControl(Player); restored.EndControl(Player);
        Assert.Equal(CanonicalJson.Text(engine.AdvanceXun().Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
    }

    [Theory]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, true, false)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, false, false)]
    public void RestrictedAttackUsesTargetWaterTerrainOrActualHarbor(bool settlement, bool water, bool harbor, bool accepted)
    {
        var engine = Engine(restricted: true, waterTarget: water, harbor: harbor);
        var before = engine.Snapshot.StateHash;
        var order = settlement ? new MilitaryOrder(MilitaryOrderKind.AttackSettlement, null, 2) :
            new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 };
        Assert.Equal(accepted, engine.Execute(Player, new OrderUnitCommand(Guid.NewGuid(), 1, 0, 0, order)).Submission.Accepted);
        if (!accepted) Assert.Equal(before, engine.Snapshot.StateHash);
    }

    [Theory]
    [InlineData(-2, 10)]
    [InlineData(0, 10)]
    [InlineData(1, 15)]
    [InlineData(2, 20)]
    [InlineData(100, 20)]
    public void ElevationBonusIsDirectionalAndCapped(int difference, int expected)
    {
        var attack = new OrdinaryAttack(10, false) { ElevationBonusPerLevel = 5, MaximumElevationBonus = 10 };
        var rules = new MilitaryRulebook(new(2, 1, [0, 0], [new(0, 1, 1)], [], []) { Elevations = [difference, 0] });
        Assert.Equal(expected, rules.AttackRange(attack, new(2, 2), new(7, 2), false, false));
    }
}
