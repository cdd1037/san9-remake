using San9.Application.Diagnostics;
using San9.Application;
using Xunit;

namespace San9.Tests;

internal static partial class StateAssert
{
    internal static void Equal(SessionSnapshot expected, SessionSnapshot actual) => Assert.Null(SessionDiagnostics.Compare(expected, actual));
    internal static void NotEqual(SessionSnapshot expected, SessionSnapshot actual) => Assert.False(SessionDiagnostics.AreEqual(expected, actual));
}
