namespace San9.Core.Domain;

/// <summary>Optional thread-local counters; never consulted by simulation decisions.</summary>
public sealed class PathSearchDiagnostics : IDisposable
{
    [ThreadStatic] private static PathSearchDiagnostics? current;
    private readonly PathSearchDiagnostics? previous;
    public PathSearchDiagnostics() { previous = current; current = this; }
    internal static PathSearchDiagnostics? Current => current;
    public long Queries { get; internal set; }
    public long Expanded { get; internal set; }
    public long MaximumExpanded { get; internal set; }
    public long EndpointExpanded { get; internal set; }
    public long WorkspaceNodes { get; internal set; }
    public long WorkspaceGrowths { get; internal set; }
    public void Dispose() => current = previous;
}
