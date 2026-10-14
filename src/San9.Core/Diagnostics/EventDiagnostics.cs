using San9.Core.Simulation;

namespace San9.Core.Diagnostics;

public static class EventDiagnostics
{
    public static StateDifference? Compare(IEnumerable<DomainEvent> expected, IEnumerable<DomainEvent> actual)
    {
        if (ReferenceEquals(expected, actual)) return null;
        using var left = expected.GetEnumerator();
        using var right = actual.GetEnumerator();
        var comparison = new DiagnosticComparison(WorldDiagnosticJsonContext.Default, true);
        for (int index = 0; ; index++)
        {
            bool hasLeft = left.MoveNext(), hasRight = right.MoveNext();
            if (!hasLeft && !hasRight) return null;
            if (hasLeft != hasRight)
                return new($"$[{index}]", hasLeft ? left.Current.GetType().Name : "<end>", hasRight ? right.Current.GetType().Name : "<end>");
            if (!comparison.Check(left.Current, right.Current, $"$[{index}]"))
                return comparison.Difference;
        }
    }
}
