namespace San9.Core.Simulation;

internal static class DailyRations
{
    internal static long Garrison(uint soldiers, uint wounded) => ((long)soldiers + wounded + 499) / 500;
    internal static long Field(uint soldiers, uint wounded) => 3 * ((long)soldiers + wounded + 333) / 1000;
}
