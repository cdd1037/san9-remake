using San9.Application;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class StrategyRosterTests
{
    [Fact]
    public void OfficerPageFiltersCorpsPinsRulerAndKeepsIndependentScrollAndIdentity()
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "roster", 190, 1, 1, 5,
            [new(5, "force", 5, 100) { LeaderId = 15 }],
            Enumerable.Range(0, 40).Select(id => new SettlementInitialState(id, $"city.{id}", 5, id, 1, 1, 1, 1, 50, [])
                { CorpsId = id == 2 ? 6 : 5, IsFacilityListEntry = true, MaxCommerce = 100 }).ToArray(), [], [])
        {
            Officers = Enumerable.Range(0, 40).Reverse().Select(id => new OfficerInitialState($"officer.{id}",
                new(id, 5, id, new(50, 60, 70, 80), id == 3))).ToArray(),
            Information = new([], [], [], [new(5, 5, 15, new(100, 100)) { IsRoot = true }, new(6, 5, 2, new(100, 100))], [])
        };
        var session = GameSession.Start(definition, 1);
        Assert.True(session.Develop(new(5, 3, [3], San9.Core.Domain.DomesticActivity.Commerce)).Accepted);
        var view = session.View;
        var roster = new StrategyRoster(); roster.Refresh(view, 5);
        roster.Current.ScrollTo(4);
        roster.SelectPage(RosterPage.Officers);
        Assert.Equal(new RosterKey(RosterPage.Officers, 15), roster.Current.Pinned!.Key);
        Assert.Equal(Enumerable.Range(0, 40).Where(id => id is not (2 or 15)), roster.Current.Ordinary.Select(e => e.Key.Id));
        Assert.Null(roster.Find(new(RosterPage.Facilities, 15)));
        Assert.False(roster.Find(new(RosterPage.Officers, 3))!.IsAvailable);
        Assert.Equal(new RosterSettlementLocation(3), roster.Find(new(RosterPage.Officers, 3))!.Location);
        roster.Current.ScrollTo(9);
        roster.Refresh(view, 5);
        roster.SelectPage(RosterPage.Facilities); Assert.Equal(4, roster.Current.Offset);
        roster.SelectPage(RosterPage.Officers); Assert.Equal(9, roster.Current.Offset);
        roster.Refresh(view, 6);
        Assert.Equal(0, roster.Officers.Offset); Assert.Equal(0, roster.Facilities.Offset);
        Assert.Equal(2, Assert.Single(roster.Current.Ordinary).Key.Id);
        Assert.Equal(15, roster.Current.Pinned.Key.Id);
    }

    [Fact]
    public void DeployedOfficersLocateTheirUnitAndReturnToSettlementAfterRecall()
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "field-roster", 190, 1, 1, 5,
            [new(5, "force", 5, 100) { LeaderId = 0 }],
            [new(0, "city", 5, 1, 1, 1000, 1, 1, 50, []) { CorpsId = 5, IsFacilityListEntry = true }], [], [])
        {
            Officers = Enumerable.Range(0, 3).Select(id => new OfficerInitialState($"officer.{id}",
                new(id, 5, 0, new(50, 50, 50, 50), false))).ToArray(),
            Information = new([], [], [], [new(5, 5, 0, new(100, 100)) { IsRoot = true }], [])
        };
        var resident = GameSession.Start(definition, 1).View;
        var deployed = definition with
        {
            Officers = definition.Officers.Select(o => o.State.Id < 2 ? o with
                { State = o.State with { SettlementId = null, UnitId = 7 } } : o).ToArray(),
            Units = [new(7, 5, new(10, 10), 1000) { CorpsId = 5, ServicePlaceId = 0 }]
        };
        var roster = new StrategyRoster();
        roster.Refresh(GameSession.Start(deployed, 1).View, 5);
        Assert.Equal(new RosterSettlementLocation(0), roster.Facilities.Pinned!.Location);
        Assert.Equal(new RosterUnitLocation(7), roster.Officers.Pinned!.Location);
        Assert.Equal(new RosterUnitLocation(7), roster.Officers.Ordinary.Single(o => o.Key.Id == 1).Location);
        Assert.False(roster.Officers.Ordinary.Single(o => o.Key.Id == 1).IsAvailable);
        Assert.Equal(new RosterUnitLocation(7), roster.Units.Pinned!.Location);
        Assert.Empty(roster.Units.Ordinary);
        Assert.Equal(new[] { 1, 2 }, roster.Officers.Ordinary.Select(o => o.Key.Id));
        Assert.Empty(roster.Facilities.Ordinary);
        Assert.Equal(new RosterSettlementLocation(0), roster.Find(new(RosterPage.Facilities, 0))!.Location);
        roster.Refresh(resident, 5);
        Assert.Equal(0, roster.Officers.Pinned!.Key.Id);
        Assert.Equal(new RosterSettlementLocation(0), roster.Officers.Pinned.Location);
        Assert.Null(roster.Units.Pinned);
        Assert.Empty(roster.Units.Ordinary);
        Assert.Equal(new[] { 1, 2 }, roster.Officers.Ordinary.Select(o => o.Key.Id));
    }

    [Fact]
    public void AllPagesFollowRuntimeOwnershipLocationsAndRootCorpsInsteadOfInitialIds()
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "runtime-roster", 190, 1, 1, 5,
            [new(5, "force", 5, 100) { LeaderId = 0 }],
            [new(0, "city", 5, 1, 1, 1000, 1, 1, 50, []) { CorpsId = 5, IsFacilityListEntry = false }], [], [])
        {
            Officers = [new("ruler", new(0, 5, 0, new(50, 50, 50, 50), false))],
            Information = new([], [], [], [new(5, 5, 0, new(100, 100)) { IsRoot = true }], [])
        };
        var initial = GameSession.Start(definition, 1).View.World;
        var deployed = initial with
        {
            Corps = [new(42, 5, 0, new(100, 100)) { IsRoot = true }],
            Settlements = initial.Settlements.Select(s => s with { CorpsId = 42 }).ToArray(),
            Officers = initial.Officers.Select(o => o with { SettlementId = null, UnitId = 7 }).ToArray(),
            Units = Enumerable.Range(7, 20).Select(id => new UnitState(id, 5, new(10, 10), 1000)
                { CorpsId = 42, CommanderId = id == 7 ? 0 : null }).Append(
                    new(99, 6, new(10, 10), 1000) { CorpsId = 42 }).ToArray()
        };
        var roster = new StrategyRoster();
        roster.Refresh(new(definition, deployed));
        Assert.Equal(42, roster.CorpsId);
        Assert.Equal(0, Assert.Single(roster.Facilities.Ordinary).Key.Id);
        Assert.Equal(new RosterUnitLocation(7), roster.Officers.Pinned!.Location);
        Assert.Equal("ruler", roster.Units.Pinned!.NameKey);
        Assert.Equal(19, roster.Units.Ordinary.Count);
        roster.SelectPage(RosterPage.Units);
        roster.Current.ScrollTo(6);
        roster.SelectPage(RosterPage.Officers);
        roster.SelectPage(RosterPage.Units);
        Assert.Equal(6, roster.Current.Offset);
        Assert.Null(roster.Find(new(RosterPage.Officers, 7)));

        var returned = deployed with
        {
            Officers = initial.Officers,
            Units = [],
            Settlements = deployed.Settlements.Select(s => s with { CanHostPersons = false }).ToArray()
        };
        roster.Refresh(new(definition, returned));
        Assert.Null(roster.Facilities.Pinned);
        Assert.Empty(roster.Facilities.Ordinary);
        Assert.Equal(new RosterSettlementLocation(0), roster.Officers.Pinned!.Location);
        Assert.Null(roster.Units.Pinned);
        Assert.Empty(roster.Units.Ordinary);
        Assert.Equal(0, roster.Units.Offset);
        Assert.Null(roster.Find(new(RosterPage.Units, 7)));
    }

    [Fact]
    public void RulerFacilityTracksServicePlaceRatherThanPositionOrLastResidence()
    {
        var resident = View(20);
        var world = resident.World;
        var deployed = world with
        {
            Officers = world.Officers.Select(o => o.Id == 0 ? o with { SettlementId = null, UnitId = 70 } : o).ToArray(),
            Units = [new(70, 5, new(3, 1), 100) { CorpsId = 5, ServicePlaceId = 7 }]
        };
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "service-place", 190, 1, 1, 5,
            [new(5, "force", 5, 100) { LeaderId = 0 }],
            Enumerable.Range(0, 20).Select(id => new SettlementInitialState(id, $"city.{id}", 5, id, 1, 1, 1, 1, 50, [])).ToArray(), [], [])
        {
            Officers = [new("ruler", world.Officers.Single(o => o.Id == 0)), new("governor", world.Officers.Single(o => o.Id == 1))]
        };
        var roster = new StrategyRoster();
        roster.Refresh(new(definition, deployed));
        Assert.Equal(new RosterSettlementLocation(7), roster.Facilities.Pinned!.Location);
        Assert.DoesNotContain(roster.Facilities.Ordinary, e => e.Key.Id == 7);
        Assert.Contains(roster.Facilities.Ordinary, e => e.Key.Id == 3);
        roster.Facilities.ScrollTo(2);
        deployed = deployed with { Units = [deployed.Units[0] with { ServicePlaceId = 9 }] };
        roster.Refresh(new(definition, deployed));
        Assert.Equal(new RosterSettlementLocation(9), roster.Facilities.Pinned!.Location);
        Assert.Equal(2, roster.Facilities.Offset);
        Assert.Contains(roster.Facilities.Ordinary, e => e.Key.Id == 7);
        Assert.DoesNotContain(roster.Facilities.Ordinary, e => e.Key.Id == 9);
        deployed = deployed with { Units = [deployed.Units[0] with { ServicePlaceId = null }] };
        roster.Refresh(new(definition, deployed));
        Assert.Null(roster.Facilities.Pinned);
        Assert.Equal(20, roster.Facilities.Ordinary.Count);
    }

    private static StrategySnapshot View(int count, int? ruler = 7, int? subcorpsEntry = null, int? hiddenEntry = null)
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "roster", 190, 1, 1, 5,
            [new(5, "force", 5, 100) { LeaderId = ruler is null ? null : 0 }],
            Enumerable.Range(0, count).Select(id => new SettlementInitialState(id, $"city.{id}", 5, id, 1, 1, 1, 1, 50, [])
                { CorpsId = id == subcorpsEntry ? 6 : 5, IsFacilityListEntry = id != hiddenEntry, CanHostPersons = id != hiddenEntry }).ToArray(), [], [])
            {
                Officers = count == 0 ? [] : [new("ruler", new(0, 5, ruler ?? 0, new(0,0,50,0), false)), new("governor", new(1, 5, subcorpsEntry ?? 0, new(0,0,50,0), false))],
                Information = new([], [], [], subcorpsEntry is not null
                    ? [new(5, 5, 0, new(100, 100)) { IsRoot = true }, new(6, 5, 1, new(100,100))]
                    : [new(5, 5, ruler is null ? null : 0, new(100,100)) { IsRoot = true }], [])
            };
        return GameSession.Start(definition, 1).View;
    }

    [Fact]
    public void RulerIsSeparateAndOrdinaryRowsUseDirectCorpsAndStableIds()
    {
        var view = View(20, subcorpsEntry: 2, hiddenEntry: 3);
        var roster = new StrategyRoster(); roster.Refresh(view, 5);
        Assert.Equal(7, roster.Current.Pinned?.Key.Id);
        Assert.DoesNotContain(roster.Current.Ordinary, s => s.Key.Id == 7);
        Assert.Equal(Enumerable.Range(0, 20).Where(id => id is not (7 or 2 or 3)), roster.Current.Ordinary.Select(s => s.Key.Id));
        roster.Refresh(view, 6);
        Assert.Equal(2, Assert.Single(roster.Current.Ordinary).Key.Id); Assert.Equal(7, roster.Current.Pinned?.Key.Id);
    }

    [Fact]
    public void WheelUsesOnlyDirectionAndScrollingNeverMovesRuler()
    {
        var roster = new StrategyRoster(); roster.Refresh(View(40), 5);
        roster.Current.Wheel(120); Assert.Equal(1, roster.Current.Offset);
        roster.Current.Wheel(-999); Assert.Equal(0, roster.Current.Offset);
        roster.Current.ScrollTo(RosterPageState.PageSize); Assert.Equal(13, roster.Current.Offset);
        roster.Current.ScrollTo(int.MaxValue); Assert.Equal(26, roster.Current.Offset);
        Assert.Equal(13, roster.Current.Visible.Count()); Assert.Equal(7, roster.Current.Pinned?.Key.Id);
        roster.Current.Wheel(1); Assert.Equal(26, roster.Current.Offset);
        roster.Current.ScrollTo(-1); Assert.Equal(0, roster.Current.Offset);
    }

    [Fact]
    public void RefreshClampsViewportAndCorpsChangeResetsIt()
    {
        var roster = new StrategyRoster(); roster.Refresh(View(40), 5); roster.Current.ScrollTo(20);
        roster.Refresh(View(40, 8), 5);
        Assert.Equal(20, roster.Current.Offset); Assert.Equal(8, roster.Current.Pinned?.Key.Id);
        Assert.Contains(roster.Current.Ordinary, s => s.Key.Id == 7);
        roster.Refresh(View(16), 5); Assert.Equal(2, roster.Current.Offset);
        roster.Refresh(View(40), 6); Assert.Equal(0, roster.Current.Offset);
        roster.Refresh(View(40), 5); Assert.Equal(0, roster.Current.Offset);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(13, 0)]
    [InlineData(14, 1)]
    public void EmptyAndOverflowThresholdCountOnlyOrdinaryRows(int count, int maximum)
    {
        var roster = new StrategyRoster(); roster.Refresh(View(count, null), 5);
        Assert.Null(roster.Current.Pinned); Assert.Equal(maximum, roster.Current.MaximumOffset);
        Assert.Equal(Math.Min(count, 13), roster.Current.Visible.Count());
    }
}
