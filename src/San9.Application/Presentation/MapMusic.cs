using San9.Content.Interface;

namespace San9.Application.Presentation;

public static class MapMusic
{
    public static string Select(StrategySnapshot view, MapAudioDefinition audio)
    {
        int season = (view.Month - 1) / 3;
        var force = view.Forces.Single(f => f.Id == view.PlayerForceId);
        bool ranked = force.RankId is int rank && audio.RankedMusicRankIds.Contains(rank);
        return (ranked ? audio.RankedSeasonalMusic : audio.SeasonalMusic)[season];
    }
}
