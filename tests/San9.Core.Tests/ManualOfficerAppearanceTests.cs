using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class ManualOfficerAppearanceTests
{
    private static SimulationEngine Engine()
    {
        var force = new ForceState(1, 100) { LeaderId = 1 };
        var ruler = new OfficerState(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler };
        var dead = new OfficerState(2, null, null, new(50, 50, 50, 50), false) { Status = OfficerStatus.Dead, BirthYear = 130 };
        var world = WorldState.Create(new(190, 1, 1), 1, [force],
            [new SettlementState(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)], [ruler, dead],
            controls: [new("player", 1, ControllerKind.Player)],
            corps: [new CorpsState(1, 1, 1, new(1000, 1000)) { IsRoot = true }]);
        return new SimulationEngine(world);
    }

    [Theory]
    [InlineData(null, OfficerStatus.Free)]
    [InlineData(1, OfficerStatus.Serving)]
    public void ImmediateAppearanceResetsAgeAndPlacesOfficer(int? owner, OfficerStatus status)
    {
        var engine = Engine();
        var events = engine.ConfigureOfficerAppearance(2, new(190, 1, owner, 30));
        var officer = engine.Snapshot.Officers.Single(p => p.Id == 2);
        Assert.Equal(status, officer.Status);
        Assert.Equal(owner, officer.OwnerId);
        Assert.Equal(1, officer.SettlementId);
        Assert.Equal(160, officer.BirthYear);
        Assert.Null(officer.ManualAppearance);
        Assert.Contains(events, e => e is OfficerAppearedEvent appeared && appeared.OfficerId == 2);
    }

    [Fact]
    public void ScheduledAppearanceSurvivesRestoreAndRunsWhenAnnualAppearancesDisabled()
    {
        var engine = Engine();
        engine.ConfigureOfficerAppearance(2, new(191, 1, 1, 30));
        var saved = engine.CaptureStableState();
        var restored = WorldState.Restore(saved with { Clock = new(191, 1, 1) }).BeginMutation();
        Assert.Equal(new ManualOfficerAppearance(191, 1, 1, 30), restored.Officers[2].ManualAppearance);
        OfficerAppearance.AdvanceYear(restored, new PersonnelRulebook([]), []);
        Assert.Equal(OfficerStatus.Serving, restored.Officers[2].Status);
        Assert.Null(restored.Officers[2].ManualAppearance);
    }

    [Fact]
    public void ScheduledHistoricalAssignmentWaitsIfForceDisappears()
    {
        var engine = Engine();
        var plan = new ManualOfficerAppearance(191, 1, 1, 30) { KeepPendingIfForceUnavailable = true };
        engine.ConfigureOfficerAppearance(2, plan);
        var saved = engine.CaptureStableState();
        var world = WorldState.Restore(saved with { Clock = new(191, 1, 1) }).BeginMutation();
        world.Replace(world.Forces[1] with { IsEliminated = true });
        OfficerAppearance.AdvanceYear(world, new PersonnelRulebook([]), []);
        Assert.Equal(OfficerStatus.NotAppeared, world.Officers[2].Status);
        Assert.Equal(plan, world.Officers[2].ManualAppearance);
    }

    [Fact]
    public void InvalidatedHistoricalLocationFallsBackToCityRatherThanFacility()
    {
        var engine = Engine();
        engine.ConfigureOfficerAppearance(2, new(191, 1, 1, 30) { KeepPendingIfForceUnavailable = true });
        var saved = engine.CaptureStableState();
        var place = saved.Settlements.Single();
        var world = WorldState.Restore(saved with { Clock = new(191, 1, 1), Officers = saved.Officers.Select(p => p.Id == 1 ? p with { SettlementId = 3 } : p).ToArray(), Settlements = [
            place with { OwnerId = null }, place with { Id = 2, Kind = SettlementKind.Facility },
            place with { Id = 3, Kind = SettlementKind.City }] }).BeginMutation();
        OfficerAppearance.AdvanceYear(world, new PersonnelRulebook([]), []);
        Assert.Equal(3, world.Officers[2].SettlementId);
        Assert.Equal(1, world.Officers[2].OwnerId);

        var noCity = WorldState.Restore(saved with { Clock = new(191, 1, 1), Officers = saved.Officers.Select(p => p.Id == 1 ? p with { SettlementId = 2 } : p).ToArray(), Settlements = [
            place with { OwnerId = null }, place with { Id = 2, Kind = SettlementKind.Facility }] }).BeginMutation();
        OfficerAppearance.AdvanceYear(noCity, new PersonnelRulebook([]), []);
        Assert.NotNull(noCity.Officers[2].ManualAppearance);
        Assert.Equal(OfficerStatus.NotAppeared, noCity.Officers[2].Status);
    }

    [Fact]
    public void InvalidRequestsDoNotChangeWorld()
    {
        var engine = Engine();
        var before = engine.CaptureStableState();
        Assert.Throws<ArgumentException>(() => engine.ConfigureOfficerAppearance(1, new(190, 1, null, 30)));
        Assert.Throws<ArgumentException>(() => engine.ConfigureOfficerAppearance(2, new(189, 1, null, 30)));
        Assert.Throws<ArgumentException>(() => engine.ConfigureOfficerAppearance(2, new(190, 1, 99, 30)));
        Assert.Equal(before, engine.CaptureStableState());
    }
}
