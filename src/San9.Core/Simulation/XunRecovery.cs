namespace San9.Core.Simulation;

internal static class XunRecovery
{
    internal static void Settlements(WorldMutation mutation)
    {
        foreach (var settlement in mutation.Settlements.Values)
            if (settlement.CommerceUsed || settlement.CultivationUsed || settlement.PatrolUsed || settlement.RepairUsed || settlement.RecruitUsed || settlement.TrainUsed || settlement.FoodTradeUsed)
                mutation.Replace(settlement with { CommerceUsed = false, CultivationUsed = false, PatrolUsed = false, RepairUsed = false, RecruitUsed = false, TrainUsed = false, FoodTradeUsed = false });
    }

    internal static void Units(WorldMutation mutation)
    {
        foreach (var unit in mutation.Units.Values)
            if (unit.CommandedThisXun) mutation.Replace(unit with { CommandedThisXun = false });
    }

    internal static void Personnel(WorldMutation mutation)
    {
        foreach (var officer in mutation.Officers.Values)
            if (officer.Acted || officer.RewardedThisXun)
                mutation.Replace(officer with { Acted = false, RewardedThisXun = false });
    }
}
