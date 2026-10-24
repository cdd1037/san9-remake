namespace San9.Core.Domain;

public static class TreasuryRules
{
    public const uint Capacity = 10_000_000;

    public static uint Receivable(uint balance) => balance >= Capacity ? 0 : Capacity - balance;

    // A credit cannot erase existing inventory above capacity, such as merged corps balances.
    public static long Project(long balance, long change) => change > 0
        ? balance + Math.Min(change, Math.Max(0, Capacity - balance))
        : Math.Max(0, balance + change);
}
