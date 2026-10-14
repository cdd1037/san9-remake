using San9.Core.Diagnostics;
using San9.Core.Simulation;
using Xunit;

namespace San9.Tests;

internal static partial class StateAssert
{
    internal static void Equal(WorldSnapshot expected, WorldSnapshot actual) => Assert.Null(WorldDiagnostics.Compare(expected, actual));
    internal static void NotEqual(WorldSnapshot expected, WorldSnapshot actual) => Assert.False(WorldDiagnostics.AreEqual(expected, actual));
}
