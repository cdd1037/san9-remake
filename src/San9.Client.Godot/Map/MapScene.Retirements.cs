using San9.Content.Interface;
using San9.Content.Maps;
using San9.Rendering;

namespace San9.Client.Godot;

internal sealed partial class MapScene
{
    private readonly List<UnitRetirementPlayback> _retirements = [];
    internal bool HasRetirements => _retirements.Count > 0;
    internal bool HasExpiredRetirements => _retirements.Any(r => r.IsComplete(MotionVisualTime));
    internal IEnumerable<MapAnimatedSprite> RetiringSprites => _retirements.SelectMany(r => r.Sample(MotionVisualTime));
    internal IEnumerable<string> RetirementAssets => _retirements.SelectMany(r => r.Assets).Distinct();
    internal IEnumerable<UnitRetirementPlayback.Sound> TakeRetirementSounds(MapAudioDefinition audio) =>
        _retirements.SelectMany(r => r.TakeSounds(MotionVisualTime, audio));

    private void PrepareRetirements(MapScene? previous)
    {
        if (previous is null || !ReferenceEquals(previous.Map, Map)) return;
        _retirements.AddRange(previous._retirements.Where(r => !r.IsComplete(MotionVisualTime)));
        if (Day?.Battle is not {} battle || MapFrame is null || Day.Viewport?.Invoke() is not {} viewport) return;
        var due = battle.MapChanges.Where(c => c.Retirement is not null &&
            MapFrame.RemovedUnits.Contains(c.Retirement.UnitId) && previous._unitStates.ContainsKey(c.Retirement.UnitId));
        foreach (var cue in due)
        {
            var fact = cue.Retirement!;
            string id = $"retirement:{Day.Before.World.Revision}:{cue.FactIndex}";
            if (_retirements.Any(r => r.Id == id)) continue;
            var point = fact.Target.Position is {} position ? previous.Project(position.X, position.Y) : default;
            if (!viewport.ContainsBody(new(point.X, point.Y))) continue;
            previous.AdvanceUnitPlayback();
            var state = previous._stateSampler(fact.UnitId, previous.LocalMotionTime)!;
            var retirement = new UnitRetirementPlayback(id, MotionVisualTime, state, previous._unitPlayback[fact.UnitId],
                Map.Units, Clips, _assets, fact.Winner is null, ElevationAt);
            if (retirement.Duration > 0) _retirements.Add(retirement);
        }
    }

    internal MapScene PruneRetirements() => new(Map, Terrain, Season, State, _assets, _positionTime, this, Day, _projection);
}
