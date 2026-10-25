using San9.Tests;
using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class CombatRuleCompletionTests
{
    private static MilitaryOrder Attack(int id) => new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = id };
    private static (WorldState World, SimulationRules Rules) Create(bool influence = false)
    {
        var military = new MilitaryRulebook(new(100, 3, [.. Enumerable.Repeat(0, 300)], [new(0, 1, 1)],
            [new(1, 5, 0, 100000, 1, new(1, 1), TravelMode.Land)
                { Combat = new(10, 10, 0, 10, 50), Attack = new(10, false), CaptureRanks = [BattleRank.Middle] }],
            [new(1, new(2, 7), new(1, 1)), new(2, new(497, 7), new(1, 1)), new(3, new(402, 7), new(1, 1))])
            { CityInfluence = influence ? new(8, 10) : null, Geography = new(5, []) });
        var world = WorldState.Create(new(190, 1, 1), 42,
            [new(1, 100) { LeaderId = 11 }, new(2, 100) { LeaderId = 21 }],
            [new(1, 1, 10000, 100, 100, 100, 0, 0, 100, 0, 100),
                new(2, 2, 10000, 100, 100, 100, 0, 0, 100, 0, 100),
                new(3, 1, 10000, 100, 100, 100, 0, 0, 100, 0, 100) { Kind = SettlementKind.Facility }],
            [new(11, 1, null, new(70, 70, 70, 70), false) { UnitId = 1, TacticCooldown = 9 },
                new(21, 2, null, new(70, 70, 70, 70), false) { UnitId = 2 },
                new(22, 2, null, new(70, 70, 70, 70), false) { UnitId = 3 }],
            units: [new(1, 1, new(102, 7), 10000) { FormationId = 1, Morale = 100, ServicePlaceId = 1, Order = Attack(2),
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(11, BattleRank.Middle) },
                new(2, 2, new(106, 7), 10000) { FormationId = 1, Morale = 100, ServicePlaceId = 2, Order = Attack(1),
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(21, BattleRank.Middle) },
                new(3, 2, new(107, 7), 10000) { FormationId = 1, Morale = 100, ServicePlaceId = 2, Order = Attack(1),
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(22, BattleRank.Middle) }], corps: [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 1000000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(10000, 1000000)) { IsRoot = true }]);
        return (world, new() { Military = military });
    }

    [Fact]
    public void IntensityCountsDirectedAttackersAndAddsOpponentOnce()
    {
        var (world, rules) = Create(); var m = world.BeginMutation(); var military = rules.Military!;
        Assert.Equal(2, MilitaryEngagements.Intensity(m, new(1, false), new(2, false), military));
        Assert.Equal(1, MilitaryEngagements.Intensity(m, new(2, false), new(1, false), military));
        uint crowded = MilitaryCombat.NormalDamage(m, new(1, false), new(2, false), military);
        m.Replace(m.Units[3] with { Order = null });
        Assert.Equal(1, MilitaryEngagements.Intensity(m, new(1, false), new(2, false), military));
        Assert.True(MilitaryCombat.NormalDamage(m, new(1, false), new(2, false), military) > crowded);
        m.Replace(m.Units[2] with { Order = null });
        Assert.Equal(1, MilitaryEngagements.Intensity(m, new(1, false), new(2, false), military));
        m.Replace(m.Units[3] with { Order = Attack(1), Position = new(150, 7) });
        Assert.Equal(0, m.Engagements(military).IncomingCount(new(1, false)));
        m.Replace(m.Units[3] with { Position = new(107, 7), Condition = UnitCondition.Confused });
        Assert.Equal(0, m.Engagements(military).IncomingCount(new(1, false)));
        m.Replace(m.Units[3] with { Condition = UnitCondition.Normal, OwnerId = 1 });
        Assert.Equal(0, m.Engagements(military).IncomingCount(new(1, false)));
    }

    [Theory]
    [InlineData(1, 7)]
    [InlineData(11, 8)]
    [InlineData(21, 7)]
    public void PeriodicLossUsesCalendarDayAndEngagement(int first, int expected)
    {
        var (world, rules) = Create(); var m = world.BeginMutation(); List<DomainEvent> events = [];
        for (int day = first; day < first + 10; day++)
        {
            m.SetClock(new(190, 1, day));
            MilitaryMorale.Apply(m, [new(new(ResourceOwnerKind.Corps, 1), false), new(new(ResourceOwnerKind.Corps, 2), false)], rules, events);
        }
        Assert.Equal(100 - expected, m.Units[1].Morale);
        Assert.Equal(expected, events.OfType<PeriodicMoraleLostEvent>().Count(e => e.UnitId == 1));
    }

    [Fact]
    public void GeographyOwnershipAndSupplyHaveSeparateEffects()
    {
        var (world, rules) = Create(influence: true); var m = world.BeginMutation(); List<DomainEvent> events = [];
        m.Replace(m.Units[1] with { Position = new(12, 7), Order = null });
        MilitaryMorale.Apply(m, [new(new(ResourceOwnerKind.Corps, 1), false), new(new(ResourceOwnerKind.Corps, 2), false)], rules, events);
        Assert.Equal(100, m.Units[1].Morale);
        MilitaryDailyCycle.Apply(m, [new(new(ResourceOwnerKind.Corps, 1), true)], rules, events);
        Assert.Equal(97, m.Units[1].Morale);
        m.Replace(m.Settlements[1] with { OwnerId = 2 });
        MilitaryMorale.Apply(m, [new(new(ResourceOwnerKind.Corps, 1), false), new(new(ResourceOwnerKind.Corps, 2), false)], rules, events);
        Assert.Equal(96, m.Units[1].Morale);
        m.Replace(m.Forces[1] with { IsBarbarian = true });
        MilitaryMorale.Apply(m, [new(new(ResourceOwnerKind.Corps, 1), false), new(new(ResourceOwnerKind.Corps, 2), false)], rules, events);
        Assert.Equal(96, m.Units[1].Morale);
    }

    [Fact]
    public void RetreatPrefersOwnCorpsBeforeDistanceAndFallsBackAcrossForce()
    {
        var (world, rules) = Create(); var m = world.BeginMutation();
        var rootTreasury77 = ResourceAccounts.Root(m, 1).Treasury; m.RemoveCorps(ResourceAccounts.Root(m, 1).Id); m.AddCorps(new(77, 1, 11, rootTreasury77) { IsRoot = true });
        m.Replace(m.Units[1] with { CorpsId = 77 });
        m.Replace(m.Settlements[3] with { CorpsId = 77 });
        Assert.Equal(3, BattleRetreat.Destination(m, m.Officers[11], new(12, 7), null, rules.Military!)!.Id);
        m.Replace(m.Settlements[3] with { OwnerId = 2, CorpsId = null });
        Assert.Equal(1, BattleRetreat.Destination(m, m.Officers[11], new(12, 7), null, rules.Military!)!.Id);
    }

    [Fact]
    public void PeriodicMoraleAtZeroTerminatesOnceAndIdleUnitOnlyLosesFivePerXun()
    {
        var (world, rules) = Create(); var m = world.BeginMutation(); List<DomainEvent> events = [];
        m.Replace(m.Units[1] with { Morale = 1 });
        m.Replace(m.Units[3] with { Order = null, Position = new(200, 7) });
        for (int day = 1; day <= 10; day++)
        {
            m.SetClock(new(190, 1, day));
            MilitaryMorale.Apply(m, [new(new(ResourceOwnerKind.Corps, 1), false), new(new(ResourceOwnerKind.Corps, 2), false)], rules, events);
        }
        Assert.False(m.Units.ContainsKey(1));
        Assert.Single(events.OfType<UnitTerminatedEvent>());
        Assert.Equal(95, m.Units[3].Morale);
    }

    [Fact]
    public void LastCityGateIgnoresRemainingHostingFacilityAndMount()
    {
        var (world, rules) = Create(); var m = world.BeginMutation(); List<DomainEvent> events = [];
        m.Replace(m.Settlements[1] with { OwnerId = 2 });
        Assert.Null(BattleRetreat.Destination(m, m.Officers[11], m.Units[1].Position, null, rules.Military!, true));
        Assert.Equal(3, BattleRetreat.Destination(m, m.Officers[11], m.Units[1].Position, null, rules.Military!)!.Id);
        Assert.True(BattlePersonnel.Resolve(m, 11, new(2, false), 3, 1, BattleRank.Middle, true, false,
            rules.Personnel, events, false, m.Units[1].Position, rules.Military!));
        Assert.Equal(OfficerStatus.Captive, m.Officers[11].Status);
        Assert.Empty(m.Simulation.RandomStreams);
    }

    [Fact]
    public void CaptureParticipantsMustTargetDefeatedObjectAndIntersectExpandedFootprint()
    {
        var (world, rules) = Create(); var m = world.BeginMutation(); var area = MilitaryCombat.Area(m.Units[1], rules.Military!);
        Assert.Equal(2, BattleRetreat.CaptureParticipants(m, new(1, false), 2, area, rules.Military!));
        m.Replace(m.Units[3] with { Position = new(108, 7) });
        Assert.Equal(1, BattleRetreat.CaptureParticipants(m, new(1, false), 2, area, rules.Military!));
        m.Replace(m.Units[2] with { Order = null });
        Assert.Equal(0, BattleRetreat.CaptureParticipants(m, new(1, false), 2, area, rules.Military!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SettlementDefeatSeparatesLastCityFromRemainingFacility(bool remainingCity)
    {
        var (world, rules) = Create(); var m = world.BeginMutation(); List<DomainEvent> events = [];
        m.Replace(m.Officers[11] with { UnitId = null, SettlementId = 1, BaseAbilities = new(150, 70, 70, 70) });
        UnitTerminationService.Complete(m, 1, null, 1, rules.Governance, events);
        m.Replace(m.Settlements[1] with { Soldiers = 0 });
        m.Replace(m.Settlements[3] with { Kind = remainingCity ? SettlementKind.City : SettlementKind.Facility });
        m.Replace(m.Units[2] with { Position = rules.Military!.Places[1].Position,
            Order = new(MilitaryOrderKind.AttackSettlement, null, 1) });
        BattleAftermath.ResolveSettlementDefeat(m, 1, new(2, false), rules, events);
        Assert.Equal(2, m.Settlements[1].OwnerId);
        Assert.Equal(remainingCity ? OfficerStatus.Serving : OfficerStatus.Captive, m.Officers[11].Status);
        if (remainingCity)
        {
            Assert.Equal(3, Assert.Single(m.Simulation.Tasks).OriginSettlementId);
            Assert.Equal(14, m.Simulation.Tasks[0].Journey!.ReturnLeg!.CountdownDays);
        }
        else Assert.Empty(m.Simulation.Tasks);
    }

    [Fact]
    public void EscapeReturnSurvivesRestoreAndArrivalIsDeferred()
    {
        var (world, rules) = Create(); var m = world.BeginMutation(); List<DomainEvent> events = [];
        BattlePersonnel.Escape(m, 11, 1, rules.Personnel, events, new(402, 7), rules.Military!);
        Assert.Equal(0, m.Officers[11].TacticCooldown);
        var task = Assert.Single(m.Simulation.Tasks);
        Assert.Equal(14, task.Journey!.ReturnLeg!.CountdownDays);
        UnitTerminationService.Complete(m, 1, null, 1, rules.Governance, events);
        world.Commit(m);
        SimulationEngine Engine(WorldState state) => new(state, rules.Economy, rules.Personnel, rules.Treasures, military: rules.Military);
        var engine = Engine(world);
        var restored = Engine(WorldState.Restore(engine.CaptureStableState()));
        EventAssert.Equal(engine.AdvanceXun().Events, restored.AdvanceXun().Events);
        Assert.Single(engine.Snapshot.Simulation.Tasks);
        var result = engine.AdvanceXun();
        EventAssert.Equal(result.Events, restored.AdvanceXun().Events);
        Assert.Equal(new GameClock(190, 1, 14), Assert.Single(result.Events.OfType<PersonnelReturnedEvent>()).Clock);
        Assert.Empty(engine.Snapshot.Simulation.Tasks);
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
    }

    [Fact]
    public void LostReturnDestinationReroutesAndNoHomeDetaches()
    {
        var (world, rules) = Create(); var m = world.BeginMutation(); List<DomainEvent> events = [];
        BattlePersonnel.Escape(m, 11, 1, rules.Personnel, events, new(402, 7), rules.Military!);
        var task = Assert.Single(m.Simulation.Tasks);
        m.SetSimulation(m.Simulation with { Tasks = [] });
        m.Replace(m.Settlements[1] with { OwnerId = 2 });
        BattlePersonnel.ResumeReturn(m, task, rules, events);
        task = Assert.Single(m.Simulation.Tasks);
        Assert.Equal(3, task.OriginSettlementId);
        m.SetSimulation(m.Simulation with { Tasks = [] });
        m.Replace(m.Settlements[3] with { OwnerId = 2 });
        BattlePersonnel.ResumeReturn(m, task, rules, events);
        Assert.Empty(m.Simulation.Tasks);
        Assert.Equal(OfficerStatus.Free, m.Officers[11].Status);
        Assert.Null(m.Officers[11].OwnerId);
    }

    [Fact]
    public void ReturnRerouteDoesNotCountCreationDateAdvanceAsTravel()
    {
        var (world, rules) = Create();
        var m = world.BeginMutation();
        m.CurrentDateCommandsConsumed = true;
        BattlePersonnel.Escape(m, 11, 1, rules.Personnel, [], new(402, 7), rules.Military!);
        var task = Assert.Single(m.Simulation.Tasks);
        // Date advancement increments general task age; this return missed that day's tick.
        m.SetClock(m.Clock.NextDay());
        m.CurrentDateCommandsConsumed = false;
        task = task with { ProgressDays = 1 };
        m.SetSimulation(m.Simulation with { Tasks = [] });
        m.Replace(m.Settlements[1] with { OwnerId = 2 });
        BattlePersonnel.ResumeReturn(m, task, rules, []);
        var rerouted = Assert.Single(m.Simulation.Tasks);
        Assert.Equal(3, rerouted.OriginSettlementId);
        Assert.Equal(0, rerouted.Journey!.ReturnLeg!.CountdownDays);
        Assert.Equal(m.Clock, rerouted.Due);
    }
}
