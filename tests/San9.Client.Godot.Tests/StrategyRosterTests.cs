using San9.Application;
using San9.Application.Presentation;
using San9.Content.Scenarios;

namespace San9.Client.Godot.Tests;

public sealed class StrategyRosterTests
{
    [Fact]
    public void OfficerPageFiltersCorpsPinsRulerAndKeepsIndependentScrollAndIdentity()
    {
        var definition = new ScenarioInitialState(11, "roster", 190, 1, 1, 5,
            [new(5, "force", 5, 100, 100, 100) { LeaderId = 15 }],
            Enumerable.Range(0, 40).Select(id => new SettlementInitialState(id, $"city.{id}", 5, id, 1, 1, 1, 1, 50, [])
                { CorpsId = id == 2 ? 6 : 5, IsFacilityListEntry = true }).ToArray(), [], [])
        {
            Officers = Enumerable.Range(0, 40).Reverse().Select(id => new OfficerInitialState($"officer.{id}",
                new(id, 5, id, new(50, 60, 70, 80), id == 3))).ToArray()
        };
        var view = GameSession.Start(definition, 1).View;
        var roster = new StrategyRoster(); roster.Refresh(view, 5);
        roster.Current.ScrollTo(4);
        roster.SelectPage(RosterPage.Officers);
        Assert.Equal(new RosterKey(RosterPage.Officers, 15), roster.Current.Pinned!.Key);
        Assert.Equal(Enumerable.Range(0, 40).Where(id => id is not (2 or 15)), roster.Current.Ordinary.Select(e => e.Key.Id));
        Assert.Null(roster.Find(new(RosterPage.Facilities, 15)));
        Assert.False(roster.Find(new(RosterPage.Officers, 3))!.IsAvailable);
        Assert.Equal(3, roster.Find(new(RosterPage.Officers, 3))!.SettlementId);
        roster.Current.ScrollTo(9);
        roster.Refresh(view, 5);
        roster.SelectPage(RosterPage.Facilities); Assert.Equal(4, roster.Current.Offset);
        roster.SelectPage(RosterPage.Officers); Assert.Equal(9, roster.Current.Offset);
        roster.Refresh(view, 6);
        Assert.Equal(0, roster.Officers.Offset); Assert.Equal(0, roster.Facilities.Offset);
        Assert.Equal(2, Assert.Single(roster.Current.Ordinary).Key.Id);
        Assert.Equal(15, roster.Current.Pinned.Key.Id);
    }

    private static StrategySnapshot View(int count, int? ruler = 7, int? subcorpsEntry = null, int? hiddenEntry = null)
    {
        var definition = new ScenarioInitialState(11, "roster", 190, 1, 1, 5,
            [new(5, "force", 5, 100, 100, 100) { LeaderId = ruler is null ? null : 0 }],
            Enumerable.Range(0, count).Select(id => new SettlementInitialState(id, $"city.{id}", 5, id, 1, 1, 1, 1, 50, [])
                { CorpsId = id == subcorpsEntry ? 6 : 5, IsFacilityListEntry = id != hiddenEntry }).ToArray(), [], [])
            { Officers = ruler is int location ? [new("ruler", new(0, 5, location, new(0,0,50,0), false))] : [] };
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
