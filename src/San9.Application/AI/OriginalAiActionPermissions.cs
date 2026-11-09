using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

internal static class OriginalAiActionPermissions
{
    private static bool PlayerForce(WorldSnapshot world, int forceId) =>
        world.Simulation.Controls.Any(c => c.ForceId == forceId && c.Kind == ControllerKind.Player);

    internal static AutomaticCorpsOptions CorpsOptions(WorldSnapshot world, CompanionSettings companion, int forceId) =>
        PlayerForce(world, forceId)
            ? companion.PlayerDelegatedCorpsOptions : companion.ComputerCorpsOptions;

    internal static bool Allows(WorldSnapshot world, CompanionSettings companion, int forceId, OriginalAiWork work)
    {
        if (!OriginalAiActions.Handles(work)) return true;
        bool player = PlayerForce(world, forceId);
        var strategy = player ? companion.PlayerDelegatedStrategyPermissions : companion.ComputerStrategyPermissions;
        var diplomacy = player ? companion.PlayerDelegatedDiplomacyPermissions : companion.ComputerDiplomacyPermissions;
        return work switch
        {
            OriginalAiWork.FalseReport => strategy.FalseReport,
            OriginalAiWork.Confuse => strategy.Disorder,
            OriginalAiWork.Encourage => strategy.Inspire,
            OriginalAiWork.Reinforce => strategy.Reinforce,
            OriginalAiWork.Alienate => strategy.Alienate,
            OriginalAiWork.Burn => strategy.Burn,
            OriginalAiWork.Seize => strategy.Seize,
            OriginalAiWork.Gossip => strategy.Gossip,
            OriginalAiWork.Gift => diplomacy.Gift,
            OriginalAiWork.RequestAttack => diplomacy.RequestAttack,
            OriginalAiWork.ReturnPrisoner => diplomacy.ReturnPrisoner,
            OriginalAiWork.Surrender => diplomacy.Surrender,
            _ => true,
        };
    }
}
