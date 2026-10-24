using System.Runtime.CompilerServices;
using San9.Application.Presentation;
using San9.Content.Maps;

namespace San9.Client.Godot;

// Owned by one map world. Both immutable input identities determine a base projection;
// weak keys let published snapshots retire without retaining an entire session history.
internal sealed class MapSceneProjectionCache
{
    private readonly ConditionalWeakTable<StrategySnapshot, ConditionalWeakTable<MapPackage, MapSceneProjection>> _snapshots = new();
    public MapSceneProjection Get(StrategySnapshot state, MapPackage map, TurnDayPresentation? day = null)
    {
        var baseline = _snapshots.GetValue(state, static _ => new()).GetValue(map, key => MapSceneProjection.Create(state, key));
        return day is null ? baseline : baseline.ForDay(state, map, day.MapFrame, day.After);
    }
}
