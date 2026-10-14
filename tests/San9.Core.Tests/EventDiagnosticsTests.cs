using San9.Core.Diagnostics;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class EventDiagnosticsTests
{
    [Fact]
    public void OrderedComparisonPreservesEventTypeLengthAndFieldDifferences()
    {
        DomainEvent first = new StageCompletedEvent(new(190, 1, 1), ResolutionStage.XunUpdate);
        DomainEvent second = new StageCompletedEvent(new(190, 1, 2), ResolutionStage.XunUpdate);
        Assert.Null(EventDiagnostics.Compare([first], [first with { }]));
        Assert.Equal("$[0].clock.day", EventDiagnostics.Compare([first], [second])?.Path);
        Assert.Equal("$[0].clock.day", EventDiagnostics.Compare([first, second], [second, first])?.Path);
        Assert.Equal("$[1]", EventDiagnostics.Compare([first], [first, second])?.Path);
        Assert.NotNull(EventDiagnostics.Compare([first], [new XunAdvancedEvent(new(190, 1, 1), new(190, 1, 11))]));
    }
}
