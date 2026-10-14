using System.Text.Json;
using San9.Core.Diagnostics;

namespace San9.Core.Tests;

public sealed class DiagnosticJsonTests
{
    [Fact]
    public void DifferencePathSkipsSemanticallyEqualNumberRepresentations()
    {
        using var expected = JsonDocument.Parse("{\"a\":1,\"b\":2}");
        using var equivalent = JsonDocument.Parse("{\"a\":1.0,\"b\":2e0}");
        using var changed = JsonDocument.Parse("{\"a\":1.0,\"b\":3}");
        Assert.Null(DiagnosticJson.Compare(expected.RootElement, equivalent.RootElement));
        Assert.Equal(new StateDifference("$.b", "2", "3"), DiagnosticJson.Compare(expected.RootElement, changed.RootElement));
    }
}
