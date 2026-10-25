using San9.Core.Diagnostics;
using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class WorldDiagnosticsTests
{
    [Fact]
    public void ComparisonIgnoresRevisionAndForceOrderButReportsChangedField()
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
    public void ComparisonAndDiagnosticSerializationPreserveSemanticUnitOrder()
    {
        var before = SimulationEngineTests.CreateWorld().CreateSnapshot() with
        {
            Units = [new UnitState(30, 1, new(5, 5), 100), new UnitState(2, 1, new(5, 5), 100)]
        };
        var reordered = before with { Units = before.Units.Reverse().ToArray() };

        Assert.False(WorldDiagnostics.AreEqual(before, reordered));
        Assert.Equal("$.units[0].id", WorldDiagnostics.Compare(before, reordered)?.Path);
        Assert.Equal([30, 2], WorldDiagnostics.Capture(before).Units.Select(u => u.Id));
        Assert.Equal([2, 30], WorldDiagnostics.Capture(reordered).Units.Select(u => u.Id));
        Assert.NotEqual(before.ToDiagnosticJson(), reordered.ToDiagnosticJson());
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
