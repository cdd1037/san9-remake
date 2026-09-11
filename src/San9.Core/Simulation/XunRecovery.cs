namespace San9.Core.Simulation;

internal static class XunRecovery
{
    internal static void Apply(WorldMutation mutation)
    {
        foreach (var settlement in mutation.Settlements.Values)
            if (settlement.CommerceUsed || settlement.CultivationUsed || settlement.PatrolUsed || settlement.RepairUsed || settlement.RecruitUsed || settlement.TrainUsed)
                mutation.Replace(settlement with { CommerceUsed = false, CultivationUsed = false, PatrolUsed = false, RepairUsed = false, RecruitUsed = false, TrainUsed = false });
        foreach (var officer in mutation.Officers.Values)
            if (officer.Acted)
                mutation.Replace(officer with { Acted = false });
    }
}
