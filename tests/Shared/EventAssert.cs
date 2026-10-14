using San9.Core.Diagnostics;
using San9.Core.Simulation;
using Xunit;

namespace San9.Tests;

internal static class EventAssert
{
    internal static void Equal(IEnumerable<DomainEvent> expected, IEnumerable<DomainEvent> actual) =>
        Assert.Null(EventDiagnostics.Compare(expected, actual));
}
