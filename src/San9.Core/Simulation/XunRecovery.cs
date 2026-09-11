namespace San9.Core.Simulation;

internal static class XunRecovery
{
    internal static void Apply(WorldMutation mutation)
    {
        foreach (var settlement in mutation.Settlements.Values)
            if (settlement.CommerceUsed || settlement.CultivationUsed)
                mutation.Replace(settlement with { CommerceUsed = false, CultivationUsed = false });
        foreach (var officer in mutation.Officers.Values)
            if (officer.Acted)
                mutation.Replace(officer with { Acted = false });
    }
}
