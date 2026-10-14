using San9.Core.Diagnostics;

namespace San9.Core.Tests;

public sealed class WorldDiagnosticsTests
{
    [Fact]
    public void ComparisonIgnoresRevisionAndCollectionOrderButReportsChangedField()
    {
        var before = SimulationEngineTests.CreateWorld().CreateSnapshot();
        Assert.Null(WorldDiagnostics.Compare(before, before));
        var reordered = before with { Revision = before.Revision + 1, Forces = before.Forces.Reverse().ToArray() };
        Assert.Null(WorldDiagnostics.Compare(before, reordered));
        Assert.True(WorldDiagnostics.AreEqual(before, before with { Revision = before.Revision + 1 }));
        Assert.True(WorldDiagnostics.AreEqual(before, reordered));
        var changed = before with { Forces = before.Forces.Select(f => f.Id == 1 ? f with { Trust = f.Trust + 1 } : f).ToArray() };
        Assert.False(WorldDiagnostics.AreEqual(before, changed));
        Assert.Equal("$.forces[0].trust", WorldDiagnostics.Compare(before, changed)?.Path);
    }

    [Fact]
    public void ComparisonIncludesUnsavedDailyContinuation()
    {
        var before = SimulationEngineTests.CreateWorld().CreateSnapshot();
        var changed = before with { Simulation = before.Simulation with { DailyAdvanceStarted = before.Clock } };
        Assert.False(WorldDiagnostics.AreEqual(before, changed));
        Assert.Equal("$.interaction.dailyAdvanceStarted", WorldDiagnostics.Compare(before, changed)?.Path);
    }
}
