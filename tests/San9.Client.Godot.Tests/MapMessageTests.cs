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
        Assert.Equal(2, log.ReadHistory().Rows.Length);
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
    public void HistoryUsesCapturedMessagesBeyondVisibleRowsAndPreservesEscapedText()
    {
        var log = new MapMessageLog();
        var original = new MapMessageRecord(Guid.NewGuid().ToString("N"), Message("[preset=blue-outline]李儒[/preset] [lb]preset=green]"));
        int changed = 0;
        log.Changed += () => changed++;
        log.Append(original); log.Append(original);
        for (int i = 0; i < 12; i++) log.Append(new(Guid.NewGuid().ToString("N"), Message()));
        var history = log.ReadHistory();
        Assert.Equal(13, history.Rows.Length);
        Assert.Equal(13, changed);
        Assert.Equal("李儒 [preset=green]", history.Rows[0].Cells[1].Text);
        Assert.Same(original.Message, log.Entries[history.Rows[0].Key.Id].Message);
        Assert.DoesNotContain(original, log.Visible);
    }

    [Fact]
    public void HistorySortingKeepsMessageIdentityAndOrdersDaysWithinTheSameXun()
    {
        var log = new MapMessageLog();
        log.Append(new(Guid.NewGuid().ToString("N"), new("later", new(190, 1, 3), "[preset=blue-outline]乙[/preset]")));
        log.Append(new(Guid.NewGuid().ToString("N"), new("earlier", new(190, 1, 2), "[preset=green-outline]甲[/preset]")));
        var browser = new InformationBrowser();
        browser.Open(InformationCategory.Records);
        browser.Order(log.ReadHistory());
        browser.Sort(0, 2);
        browser.Sort(0, 2); // Existing table interaction toggles descending first, then ascending.
        var rows = browser.Order(log.ReadHistory());
        Assert.Equal("earlier", log.Entries[rows[0].Key.Id].Message.Key);
        Assert.Equal("190年1月2日", rows[0].Cells[0].Text);
        Assert.Equal("later", log.Entries[rows[1].Key.Id].Message.Key);
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
        playback.Publish(first); playback.Publish(second);
        Assert.True(playback.Entries[0].Visible);
        Assert.False(playback.Entries[1].Visible);
        playback.Advance(1,_ => true);
        Assert.All(playback.Entries,entry => Assert.True(entry.Visible));
        playback.Dismiss(first.Id);
        Assert.False(playback.Publish(first));
        Assert.Equal("second",Assert.Single(playback.Entries).Message.Message.BbCode);
        playback.Advance(4.9,_ => true);
        Assert.Single(playback.Entries);
        playback.Advance(.1,_ => true);
        Assert.Empty(playback.Entries);
    }

    [Fact]
    public void UnreadyResourcesDoNotConsumeLifetimeOrChangeOtherSpeech()
    {
        var playback = new MapSpeechPlayback();
        var first = new MapSpeechMessage(Guid.NewGuid().ToString("N"),Message(),1,"actor",new(8,9));
        var second = first with { Id = Guid.NewGuid().ToString("N") };
        playback.Publish(first); playback.Publish(second);
        playback.Advance(5,message => message.Id == second.Id);
        Assert.Equal(first.Id,Assert.Single(playback.Entries).Message.Id);
        playback.Advance(4.99,_ => true);
        Assert.Single(playback.Entries);
        playback.Advance(.01,_ => true);
        Assert.Empty(playback.Entries);
        Assert.False(playback.Publish(first));
    }

    [Fact]
    public void LayoutUsesOverlapAndPreservesPreferredDirectionOnTiesEvenOffscreen()
    {
        const double width = 280;
        var preferred = MapSpeechPlacement.RightBelow;
        var occupied = MapSpeechLayout.HitBounds(MapSpeechLayout.Bounds(0,0,width,preferred));
        var placement = MapSpeechLayout.Choose(0,0,width,preferred,[occupied]);
        Assert.Equal(0,MapSpeechLayout.Bounds(0,0,width,placement).Overlap(occupied));
        Assert.Equal(preferred,MapSpeechLayout.Choose(-1000,-1000,width,preferred,[]));
    }
}
