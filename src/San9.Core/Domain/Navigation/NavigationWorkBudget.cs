namespace San9.Core.Domain;

/// <summary>Limits optional forecasts without changing the work limits of actual navigation.</summary>
public sealed class NavigationWorkBudget(int maximumExpanded)
{
    private int remaining = maximumExpanded > 0 ? maximumExpanded : throw new ArgumentOutOfRangeException(nameof(maximumExpanded));
    public bool Exhausted { get; private set; }
    internal bool TryExpand()
    {
        if (remaining > 0) { remaining--; return true; }
        Exhausted = true;
        return false;
    }
}
