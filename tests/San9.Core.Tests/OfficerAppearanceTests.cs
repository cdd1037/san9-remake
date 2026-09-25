using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class OfficerAppearanceTests
{
    private static readonly PersonnelRulebook Places = new([new(10, new(0, 0)), new(20, new(100, 0)),
        new(30, new(90, 0)), new(40, new(150, 0)), new(99, new(80, 0))]);

    private static OfficerState Child(int appearance = 191, int? parent = 1, int? bloodline = 8) =>
        new(9000, null, null, new(60, 70, 80, 90), false)
        { Status = OfficerStatus.NotAppeared, RegionId = 99, AppearanceYear = appearance, BirthYear = 177,
            ParentId = parent, BloodlineId = bloodline };

    private static WorldState World(OfficerState child, bool enabled = true, IEnumerable<OfficerState>? others = null,
        GameClock? clock = null) => WorldState.Create(clock ?? new(190, 12, 21), 190,
        [new(1, 0) { LeaderId = 1 }, new(2, 0) { LeaderId = 2 }],
        [new(10, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new(20, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100),
         new(30, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new(40, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
        new OfficerState[]
        {
            new(1, 1, 10, new(70, 70, 70, 70), false) { Status = OfficerStatus.Ruler, BloodlineId = 8, BirthYear = 150 },
            new(2, 2, 20, new(70, 70, 70, 70), false) { Status = OfficerStatus.Ruler, BloodlineId = 8, BirthYear = 160 }, child
        }.Concat(others ?? []), annualAppearancesEnabled: enabled, corps: [new San9.Core.Domain.CorpsState(1, 1, 1, new(1000, 1000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 2, new(1000, 1000)) { IsRoot = true }]);

    [Theory]
    [InlineData(189, OfficerStatus.Undiscovered)]
    [InlineData(190, OfficerStatus.Undiscovered)]
    [InlineData(191, OfficerStatus.NotAppeared)]
    public void InitializationUsesIndependentAppearanceYearAndPreservesAuthoredHiddenLocation(int year, OfficerStatus expected)
    {
        var engine = new SimulationEngine(World(Child(year) with { BirthYear = 100 }, clock: new(190, 7, 1)));
        var person = engine.Snapshot.Officers.Single(p => p.Id == 9000);
        Assert.Equal(expected, person.Status);
        Assert.Null(person.OwnerId);
        Assert.Equal(99, person.RegionId);
        Assert.Equal(100, person.BirthYear);
        Assert.Equal(year <= 190 ? 1 : 0, engine.InitialEvents.OfType<OfficerAppearedEvent>().Count());
        Assert.Empty(new SimulationEngine(WorldState.Restore(engine.CaptureStableState())).InitialEvents);
    }

    [Fact]
    public void JanuaryJoinsParentAndRestoredContinuationProducesTheSameEventExactlyOnce()
    {
        var engine = new SimulationEngine(World(Child()), EconomyRulebook.Empty, Places);
        Assert.Equal(OfficerStatus.NotAppeared, engine.Snapshot.Officers.Single(p => p.Id == 9000).Status);
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), EconomyRulebook.Empty, Places);
        var expected = engine.AdvanceXun().Events;
        var actual = restored.AdvanceXun().Events;
        Assert.Equal(CanonicalJson.Text(expected), CanonicalJson.Text(actual));
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
        var person = engine.Snapshot.Officers.Single(p => p.Id == 9000);
        Assert.Equal(OfficerStatus.Serving, person.Status);
        Assert.Equal(1, person.OwnerId);
        Assert.Equal(10, person.SettlementId);
        Assert.Null(person.RegionId);
        Assert.Equal(191, person.StatusStartYear);
        Assert.Equal(new OfficerAppearedEvent(new(191, 1, 1), 9000, OfficerStatus.Serving, 1, 1, 10, null),
            Assert.Single(expected.OfType<OfficerAppearedEvent>()));
        Assert.DoesNotContain(engine.AdvanceXun().Events, e => e is OfficerAppearedEvent);
    }

    [Fact]
    public void AppearanceDoesNotRunAtOrdinaryMonthBoundariesOrWhenDisabled()
    {
        var world = World(Child(191));
        var mutation = world.BeginMutation();
        mutation.SetClock(new(191, 2, 21));
        mutation.SetSimulation(mutation.Simulation with { Boundary = SimulationBoundary.Stable });
        world.Commit(mutation);
        var engine = new SimulationEngine(world, EconomyRulebook.Empty, Places);
        Assert.DoesNotContain(engine.AdvanceXun().Events, e => e is OfficerAppearedEvent);
        Assert.Equal(OfficerStatus.NotAppeared, engine.Snapshot.Officers.Single(p => p.Id == 9000).Status);
        var disabled = new SimulationEngine(World(Child(), enabled: false), EconomyRulebook.Empty, Places);
        Assert.DoesNotContain(disabled.AdvanceXun().Events, e => e is OfficerAppearedEvent);
        Assert.Equal(OfficerStatus.NotAppeared, disabled.Snapshot.Officers.Single(p => p.Id == 9000).Status);
    }

    [Theory]
    [InlineData(OfficerStatus.Free, null, 99)]
    [InlineData(OfficerStatus.Captive, 1, 30)]
    [InlineData(OfficerStatus.NotAppeared, null, 99)]
    public void LivingParentStatusControlsAffiliationWithoutFallingBackToOtherKin(OfficerStatus status, int? owner, int place)
    {
        var parent = new OfficerState(50, status == OfficerStatus.Captive ? 1 : null,
            status == OfficerStatus.Captive ? 20 : null, new(50, 50, 50, 50), false)
        { Status = status, RegionId = status == OfficerStatus.Captive ? null : 99, AppearanceYear = 210 };
        var mutation = World(Child(parent: 50), others: [parent]).BeginMutation();
        mutation.SetClock(new(191, 1, 1));
        OfficerAppearance.AdvanceYear(mutation, Places, []);
        var person = mutation.Officers[9000];
        Assert.Equal(owner, person.OwnerId);
        Assert.Equal(owner is null ? OfficerStatus.Undiscovered : OfficerStatus.Serving, person.Status);
        Assert.Equal(place, person.SettlementId ?? person.RegionId);
    }

    [Fact]
    public void MissingOrDeadParentUsesOldestRulerRatherThanOlderOrdinaryRelative()
    {
        var dead = new OfficerState(3, null, 10, new(50, 50, 50, 50), false) { Status = OfficerStatus.Dead };
        var ordinary = new OfficerState(4, 2, 20, new(50, 50, 50, 50), false) { BloodlineId = 8, BirthYear = 100 };
        foreach (int? parent in new int?[] { null, 3, 100000 })
        {
            var mutation = World(Child(parent: parent), others: [dead, ordinary]).BeginMutation();
            mutation.SetClock(new(191, 1, 1));
            List<DomainEvent> events = [];
            OfficerAppearance.AdvanceYear(mutation, Places, events);
            Assert.Equal(1, mutation.Officers[9000].OwnerId);
            Assert.Equal(1, Assert.Single(events.OfType<OfficerAppearedEvent>()).RelatedOfficerId);
            Assert.Empty(mutation.Simulation.RandomStreams);
        }
    }

    [Fact]
    public void OrdinaryBloodRelativeProvidesLocationButDoesNotRecruitTheNewPerson()
    {
        var kin = new OfficerState(3, 2, 20, new(50, 50, 50, 50), false) { BloodlineId = 80000, BirthYear = 100 };
        var mutation = World(Child(parent: null, bloodline: 80000), others: [kin]).BeginMutation();
        mutation.SetClock(new(191, 1, 1));
        OfficerAppearance.AdvanceYear(mutation, Places, []);
        Assert.Equal(OfficerStatus.Undiscovered, mutation.Officers[9000].Status);
        Assert.Null(mutation.Officers[9000].OwnerId);
        Assert.Equal(20, mutation.Officers[9000].SettlementId);
        Assert.Null(mutation.Officers[9000].RegionId);
    }

    [Fact]
    public void NoFamilyRetainsTheAuthoredLocationAndNullBloodlinesNeverMatch()
    {
        var mutation = World(Child(parent: null, bloodline: null)).BeginMutation();
        mutation.SetClock(new(191, 1, 1));
        OfficerAppearance.AdvanceYear(mutation, Places, []);
        Assert.Equal(OfficerStatus.Undiscovered, mutation.Officers[9000].Status);
        Assert.Null(mutation.Officers[9000].OwnerId);
        Assert.Equal(99, mutation.Officers[9000].RegionId);
    }

    [Theory]
    [InlineData(false, 10)]
    [InlineData(true, 30)]
    public void FieldParentUsesServiceHomeOrOriginalForceHomeInsteadOfJoiningAFieldRoster(bool captive, int destination)
    {
        var original = World(Child(parent: 50)).CreateSnapshot();
        var parent = new OfficerState(50, 1, null, new(50, 50, 50, 50), false)
        { UnitId = 5000, Status = captive ? OfficerStatus.Captive : OfficerStatus.Serving };
        var world = WorldState.Create(original.Clock, 190, original.Forces, original.Settlements,
            original.Officers.Append(parent), [new(5000, captive ? 2 : 1, new(100, 0), 1000)
            { ServicePlaceId = captive ? 20 : 10 }], corps: original.Corps);
        var mutation = world.BeginMutation();
        mutation.SetClock(new(191, 1, 1));
        OfficerAppearance.AdvanceYear(mutation, Places, []);
        Assert.Equal(1, mutation.Officers[9000].OwnerId);
        Assert.Equal(destination, mutation.Officers[9000].SettlementId);
        Assert.Null(mutation.Officers[9000].UnitId);
        world.Commit(mutation);
    }

    [Fact]
    public void EqualAgeRulerRelativesUseStableIdentityOrder()
    {
        var mutation = World(Child(parent: null)).BeginMutation();
        mutation.Replace(mutation.Officers[1] with { BirthYear = 160 });
        mutation.SetClock(new(191, 1, 1));
        List<DomainEvent> events = [];
        OfficerAppearance.AdvanceYear(mutation, Places, events);
        Assert.Equal(1, Assert.Single(events.OfType<OfficerAppearedEvent>()).RelatedOfficerId);
    }
}
