using San9.Tests;
using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class AttackRangeTests
{
    private static readonly CommandContext Player = new("player");
    private static SimulationEngine Engine(bool restricted = false, bool waterTarget = false, bool harbor = false,
        Action<MilitaryRulebook>? captureRules = null, int movementBudget = 100)
    {
        var combat = new CombatProfile(0, 0, 0, 1000, 50);
        var rules = new MilitaryRulebook(new(7, 1, [0, 0, 0, 0, 0, 1, 1],
            [new(0, 1, 1), new(1, 1, 1) { IsWater = waterTarget }],
            [new(7000, movementBudget, 0, 100000, 1, new(1, 1), TravelMode.Land)
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
            [new(1, 100) { LeaderId = 11 }, new(2, 100) { LeaderId = 22 }],
            [new(1, 1, 10000, 100, 100, 100, 0, 0, 100, 0, 100), new(2, 2, 10000, 100, 100, 100, 0, 0, 100, 0, 100)],
            [new(11, 1, null, new(80, 80, 80, 80), false) { UnitId = 0, LearnedTactics = [9000], EquippedTacticId = 9000 },
                new(21, 2, null, new(80, 80, 80, 80), false) { UnitId = 1 }, new(22, 2, 2, new(80, 80, 80, 80), false)],
            units: [new(0, 1, new(7, 2), 100000) { FormationId = 7000, Morale = 100, ServicePlaceId = 1,
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(11, BattleRank.Front) },
                new(1, 2, new(27, 2), 100000) { FormationId = 7001, Morale = 100, ServicePlaceId = 2,
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(21, BattleRank.Front) }],
            controls: [new("player", 1, ControllerKind.Player)], corps: [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 1000000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 22, new(10000, 1000000)) { IsRoot = true }]);
        captureRules?.Invoke(rules);
        return new(state, EconomyRulebook.Empty, PersonnelRulebook.Empty, TreasureRulebook.Empty, military: rules);
    }

    [Fact]
    public void HighGroundAllowsCombatAndTacticsWhileOrdinaryApproachContinues()
    {
        MilitaryRulebook rules = null!;
        var engine = Engine(captureRules: value => rules = value, movementBudget: 9);
        Assert.True(engine.Execute(Player, new OrderUnitCommand(Guid.NewGuid(), 1, 0, 0,
            new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 })).Submission.Accepted);
        engine.EndControl(Player);
        var events = engine.AdvanceXun().Events;
        Assert.Equal(new WorldPosition(16, 2), events.OfType<UnitMovedEvent>().First(e => e.UnitId == 0).To);
        var destination = events.OfType<UnitMovedEvent>().Last(e => e.UnitId == 0).To;
        Assert.True(new MapOccupancy(destination, rules.Formations[7000].Footprint).IntersectsExpanded(MilitaryCombat.Area(engine.Snapshot.Units.Single(u => u.Id == 1), rules), 1));
        Assert.Contains(events.OfType<TacticActivatedEvent>(), e => e.SourceId == 0 && e.Clock.Day == 2);
        Assert.Contains(events.OfType<BattleDamageEvent>(), e => e.AttackerId == 0 && e.Clock.Day == 2);
        Assert.DoesNotContain(events.OfType<BattleDamageEvent>(), e => e.AttackerId == 1 && e.Clock.Day == 2 && e.SoldierLoss > 0);
        Assert.Contains(events.OfType<BattleDamageEvent>(), e => e.AttackerId == 1 && e.Clock.Day >= 4 && e.SoldierLoss > 0);
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), EconomyRulebook.Empty,
            PersonnelRulebook.Empty, TreasureRulebook.Empty, military: rules);
        engine.EndControl(Player); restored.EndControl(Player);
        EventAssert.Equal(engine.AdvanceXun().Events, restored.AdvanceXun().Events);
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
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
        var before = engine.Snapshot;
        var order = settlement ? new MilitaryOrder(MilitaryOrderKind.AttackSettlement, null, 2) :
            new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 };
        Assert.Equal(accepted, engine.Execute(Player, new OrderUnitCommand(Guid.NewGuid(), 1, 0, 0, order)).Submission.Accepted);
        if (!accepted) StateAssert.Equal(before, engine.Snapshot);
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
