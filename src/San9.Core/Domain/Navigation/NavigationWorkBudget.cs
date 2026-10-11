namespace San9.Core.Domain;

/// <summary>Limits optional forecasts without changing the work limits of actual navigation.</summary>
public sealed class NavigationWorkBudget(int maximumExpanded, Func<bool>? spend = null)
{
    private int remaining = maximumExpanded > 0 ? maximumExpanded : throw new ArgumentOutOfRangeException(nameof(maximumExpanded));
    public int Expanded => maximumExpanded - remaining;
    public bool Exhausted { get; private set; }
    internal bool TryExpand()
    {
        if (remaining > 0 && (spend is null || spend())) { remaining--; return true; }
        Exhausted = true;
        return false;
    }
}
