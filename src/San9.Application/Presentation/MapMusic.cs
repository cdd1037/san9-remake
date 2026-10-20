using San9.Content.Interface;
using San9.Core.Domain;

namespace San9.Application.Presentation;

public static class MapMusic
{
    public static string Select(StrategySnapshot view, MapAudioDefinition audio, bool execution)
    {
        int season = (view.Month - 1) / 3;
        var force = view.Forces.SingleOrDefault(f => f.Id == view.PlayerForceId);
        bool ranked = force?.RankId is int rank && audio.RankedMusicRankIds.Contains(rank);
        if (execution)
        {
            var owners = view.World.Simulation.Controls.Where(c => c.Kind == San9.Core.Simulation.ControllerKind.Player).Select(c => c.ForceId).ToHashSet();
            ranked = view.Forces.Any(f => owners.Contains(f.Id) && f.RankId is int id && audio.RankedMusicRankIds.Contains(id));
            // Settlements own garrisons in the remake; the original enumerates embedded unit objects.
            if (view.World.Settlements.Any(s => s.OwnerId is int owner && owners.Contains(owner) && !s.IsDestroyed && view.IsSettlementEngaged(s.Id)))
                return ranked ? audio.ExecutionMusic.RankedBattle : audio.ExecutionMusic.Battle;
            bool military = false;
            foreach (var unit in view.World.Units)
            {
                if (!owners.Contains(unit.OwnerId) || unit.IsPersonnel) continue;
                if (unit.PassiveEngagement || !unit.IsEmbedded && unit.ActiveEngagement)
                    return ranked ? audio.ExecutionMusic.RankedBattle : audio.ExecutionMusic.Battle;
                military |= !unit.IsEmbedded && unit.EffectiveOrderClass == UnitOrderClass.Military;
            }
            return military ? ranked ? audio.ExecutionMusic.RankedMarch : audio.ExecutionMusic.March
                : audio.ExecutionMusic.Domestic;
        }
        return (ranked ? audio.RankedSeasonalMusic : audio.SeasonalMusic)[season];
    }
}
