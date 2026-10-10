using Godot;
using San9.Rendering;
using San9.Core.Domain;

namespace San9.Client.Godot;

internal sealed partial class MapScene
{
    private sealed record MotionSegment(double Start, double End, double VisualStart,
        TurnDayPresentation? Day, Func<int, double, UnitVisualState?> Sample,
        Dictionary<int, MapUnitMotion> Paths);
    private MotionSegment[] _history = [];
    private Func<int, double, UnitVisualState?> _stateSampler = null!;
    private double _motionStart, _visualStart, _idlePositionStart;
    private const double HistoryMilliseconds = 2000;
    private double LocalMotionTime => Day?.Elapsed * 1000 ?? Math.Max(0, _positionTime() - _idlePositionStart);
    private double MotionTime => _motionStart + LocalMotionTime;
    private double MotionVisualTime => _visualStart + (Day?.VisualElapsed * 1000 ?? LocalMotionTime);

    private void PrepareHistory(MapScene? previous)
    {
        _stateSampler = StateSampler();
        _idlePositionStart = _positionTime();
        if (previous is null || !ReferenceEquals(previous.Map, Map)) return;
        if (ReferenceEquals(previous.Day, Day) && ReferenceEquals(previous.State, State))
        {
            _history = previous._history; _motionStart = previous._motionStart;
            _visualStart = previous._visualStart; _idlePositionStart = previous._idlePositionStart;
            return;
        }
        _motionStart = previous.MotionTime;
        _visualStart = previous.MotionVisualTime;
        _history = previous._history.Append(new(previous._motionStart, _motionStart,
                previous._visualStart, previous.Day, previous._stateSampler, previous._motions))
            .Where(s => s.End > _motionStart - HistoryMilliseconds && s.End > s.Start).ToArray();
    }
    private UnitVisualState? HistoryState(int id, double time)
    {
        if (time >= _motionStart) return _stateSampler(id, time - _motionStart);
        foreach (var segment in _history.Reverse())
            if (time >= segment.Start) return segment.Sample(id, Math.Min(time, segment.End) - segment.Start);
        return _history.Length > 0 ? _history[0].Sample(id, -MapMovementCadence.StepMilliseconds) : _stateSampler(id, time - _motionStart);
    }
    private double EmissionTime(double time)
    {
        if (time >= _motionStart)
            return _visualStart + (Day is null ? time - _motionStart :
                Day.VisualElapsed * 1000 - Day.VisualAge(time - _motionStart));
        foreach (var segment in _history.Reverse())
            if (time >= segment.Start)
                return segment.VisualStart + (segment.Day is null ? time - segment.Start :
                    segment.Day.VisualElapsed * 1000 - segment.Day.VisualAge(time - segment.Start));
        return time;
    }
    // Resource coverage uses the whole path; draw coverage only uses positions that
    // can still contribute bodies or finite trails to this frame.
    public bool IsUnitVisible(UnitVisual unit, Rect2 viewport)
    {
        double now = MotionTime, from = Math.Max(0, now - HistoryMilliseconds);
        Rect2? bounds = null;
        void Include(double x, double y)
        {
            var point = Project(x, y);
            bounds = bounds is {} area ? area.Expand(point) : new Rect2(point, Vector2.One);
        }
        void IncludeSegment(double start, double end, Func<int, double, UnitVisualState?> sample,
            Dictionary<int, MapUnitMotion> paths)
        {
            double a = Math.Max(from, start), b = Math.Min(now, end);
            if (a > b) return;
            if (paths.TryGetValue(unit.Id, out var path))
                foreach (var p in path.Segment(a - start, b - start)) Include(p.X, p.Y);
            else if (sample(unit.Id, 0) is {} state)
                Include(state.CellX * MapCoordinates.UnitsPerCell + MapCoordinates.UnitsPerCell / 2,
                    state.CellY * MapCoordinates.UnitsPerCell + MapCoordinates.UnitsPerCell / 2);
        }
        foreach (var segment in _history)
            IncludeSegment(segment.Start, segment.End, segment.Sample, segment.Paths);
        IncludeSegment(_motionStart, now, _stateSampler, _motions);
        return bounds is {} visible && visible.Grow(256).Intersects(viewport);
    }
}
