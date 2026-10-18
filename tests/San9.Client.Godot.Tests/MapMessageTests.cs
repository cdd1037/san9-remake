using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class MapMessageTests
{
    private static ProducedMessage Message(string text = "result") => new("test",new GameClock(190,1,1),text);

    [Fact]
    public void ReportPreferenceFiltersNewImmediatePublicationsWithoutLosingHistoryOrReplayingOldRows()
    {
        var log = new MapMessageLog { ReportMode = Settings.ProgressReportMode.Summary };
        var detail = new MapMessageRecord(Guid.NewGuid().ToString("N"), Message("detail"), Importance: MapReportImportance.Detailed);
        var summary = new MapMessageRecord(Guid.NewGuid().ToString("N"), Message("summary"));
        log.Append(detail); log.Append(summary);
        Assert.Equal(summary, Assert.Single(log.Visible));
        Assert.Equal(2, log.Entries.Count);
        log.ReportMode = Settings.ProgressReportMode.Off;
        var hidden = summary with { Id = Guid.NewGuid().ToString("N") }; log.Append(hidden);
        Assert.Equal(summary, Assert.Single(log.Visible));
        log.ReportMode = Settings.ProgressReportMode.Detailed;
        Assert.Equal(summary, Assert.Single(log.Visible));
        Assert.False(log.Append(hidden));
        var next = detail with { Id = Guid.NewGuid().ToString("N") }; log.Append(next);
        Assert.Equal(new[] { summary, next }, log.Visible);
        Assert.Equal(4, log.Entries.Count);
    }

    [Fact]
    public void HistoryFiltersCapturedPublicationsAndResetsItsViewportWithoutChangingTheLog()
    {
        var log = new MapMessageLog { ReportMode = Settings.ProgressReportMode.Off };
        var original = new MapMessageRecord("first", Message("[preset=blue-outline]李儒[/preset] [lb]preset=green]"), new(8, 9));
        log.Append(original);
        for (int i = 0; i < 30; i++)
            log.Append(new($"detail-{i}", Message(), Importance: MapReportImportance.Detailed));
        var history = new ProgressRecordHistory();
        history.Refresh(log, log.ReportMode);
        Assert.Equal(Settings.ProgressReportMode.Detailed, history.Mode);
        Assert.Equal(31, history.Rows.Length);
        Assert.Same(original, history.Rows[0]);
        history.Scroll(int.MaxValue);
        Assert.Equal(9, history.Offset);
        history.SelectMode(log, Settings.ProgressReportMode.Summary);
        Assert.Equal(0, history.Offset);
        Assert.Same(original, Assert.Single(history.Rows));
        Assert.Empty(log.Visible);
        Assert.Equal(31, log.Entries.Count);
        history.SelectMode(log, Settings.ProgressReportMode.Detailed);
        history.Scroll(9);
        history.Refresh(log, Settings.ProgressReportMode.Detailed);
        Assert.Equal(9, history.Offset);
    }

    [Fact]
    public void RepeatedPublicationDoesNotDuplicateRecordsAndTenRowsDoNotLimitHistory()
    {
        var log = new MapMessageLog();
        var first = new MapMessageRecord(Guid.NewGuid().ToString("N"),Message(),new MapCell(8,9),2);
        Assert.True(log.Append(first));
        Assert.False(log.Append(first));
        for (int i=0;i<12;i++) Assert.True(log.Append(new(Guid.NewGuid().ToString("N"),Message())));
        Assert.Equal(13,log.Entries.Count);
        Assert.Equal(10,log.Visible.Count());
        Assert.DoesNotContain(first,log.Visible);
        Assert.Equal(new MapCell(8,9),log.Entries[0].Location);
    }

    [Fact]
    public void SimultaneousSpeechHasIndependentDelayAndRetainsMessageAfterDismissal()
    {
        var playback = new MapSpeechPlayback();
        var first = new MapSpeechMessage(Guid.NewGuid().ToString("N"),Message("first"),1,"actor",new(8,9));
        var second = first with { Id = Guid.NewGuid().ToString("N"), Message = Message("second"), DelaySeconds = 1 };
        Assert.False(playback.IsComplete(first.Id));
        playback.Publish(first); playback.Publish(second);
        Assert.False(playback.IsComplete(first.Id));
        Assert.True(playback.Entries[0].Visible);
        Assert.False(playback.Entries[1].Visible);
        playback.Advance(1,_ => true);
        Assert.All(playback.Entries,entry => Assert.True(entry.Visible));
        playback.Dismiss(first.Id);
        Assert.True(playback.IsComplete(first.Id));
        Assert.False(playback.IsComplete(second.Id));
        Assert.False(playback.Publish(first));
        Assert.Equal("second",Assert.Single(playback.Entries).Message.Message.BbCode);
        playback.Advance(4.9,_ => true);
        Assert.Single(playback.Entries);
        playback.Advance(.1,_ => true);
        Assert.Empty(playback.Entries);
        Assert.True(playback.IsComplete(second.Id));
    }

    [Fact]
    public void UnreadyResourcesDoNotConsumeLifetimeOrChangeOtherSpeech()
    {
        var playback = new MapSpeechPlayback();
        var first = new MapSpeechMessage(Guid.NewGuid().ToString("N"),Message(),1,"actor",new(8,9));
        var second = first with { Id = Guid.NewGuid().ToString("N") };
        playback.Publish(first); playback.Publish(second);
        playback.Advance(5,message => message.Id == second.Id);
        Assert.False(playback.IsComplete(first.Id));
        Assert.True(playback.IsComplete(second.Id));
        Assert.Equal(first.Id,Assert.Single(playback.Entries).Message.Id);
        playback.Advance(4.99,_ => true);
        Assert.Single(playback.Entries);
        playback.Advance(.01,_ => true);
        Assert.Empty(playback.Entries);
        Assert.False(playback.Publish(first));
    }

    [Fact]
    public void LayoutAvoidsOtherSpeechAndPreservesDirectionOnTies()
    {
        const double width = 280;
        var preferred = MapSpeechPlacement.RightBelow;
        var viewport = new OverlayBounds(0, 0, 1600, 900);
        var occupied = MapSpeechLayout.HitBounds(MapSpeechLayout.Bounds(800, 400, width, preferred));
        var layout = MapSpeechLayout.Place(800, 400, width, preferred, viewport, [occupied]);
        Assert.Equal(0, layout.Bounds.Overlap(occupied));
        Assert.Equal(preferred, MapSpeechLayout.Place(800, 400, width, preferred, viewport, []).Placement);
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(1365.333)]
    [InlineData(1792)]
    public void SpeechFitsAllViewportCornersAndRetainedFrame(double width)
    {
        foreach (var viewport in new[] { new OverlayBounds(0, 0, width, 768), new OverlayBounds(170, 0, 1024, 768) })
        foreach (double x in new[] { viewport.X, viewport.X + viewport.Width - 1 })
        foreach (double y in new[] { viewport.Y, viewport.Y + viewport.Height - 1 })
        foreach (var preferred in Enum.GetValues<MapSpeechPlacement>())
        {
            var layout = MapSpeechLayout.Place(x, y, 280, preferred, viewport, []);
            Assert.InRange(layout.Bounds.X, viewport.X, viewport.X + viewport.Width - 280);
            Assert.InRange(layout.Bounds.Y, viewport.Y, viewport.Y + viewport.Height - 108);
        }
    }

    [Fact]
    public void SpeechReflowsAfterResizeWithoutLosingItsAnchorPreference()
    {
        var wide = new OverlayBounds(0, 0, 1792, 768);
        var small = new OverlayBounds(0, 0, 1024, 768);
        var first = MapSpeechLayout.Place(1000, 400, 280, MapSpeechPlacement.RightBelow, wide, []);
        var resized = MapSpeechLayout.Place(1000, 400, 280, first.Placement, small, []);
        Assert.True(resized.Bounds.X + resized.Bounds.Width <= small.Width);
        Assert.NotEqual(first.Placement, resized.Placement);
        Assert.Equal(resized, MapSpeechLayout.Place(1000, 400, 280, resized.Placement, small, []));
    }
}
