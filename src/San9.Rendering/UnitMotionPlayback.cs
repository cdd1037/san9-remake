using San9.Content.Maps;
using San9.Core.Domain;

namespace San9.Rendering;

/// <summary>Owns the display positions, waiting queues and timers of one unit's members.</summary>
public sealed class UnitMotionPlayback
{
    public sealed record Attack(string Id, UnitVisualAction Action, int Direction, double Start, double Duration, double Delay);
    public Attack? BodyAttack { get; private set; }
    public Attack? ActiveAttack => BodyAttack is {} attack && _visualTime >= attack.Start + attack.Delay &&
        _visualTime < attack.Start + attack.Duration ? attack : null;
    public void PlayAttack(Attack attack)
    {
        if (BodyAttack?.Id == attack.Id || BodyAttack is { } previous && previous.Start > attack.Start) return;
        BodyAttack = attack;
        Revision++;
    }
    private sealed class Member(MapUnitMember definition, double interval, uint frame, double time)
    {
        public readonly MapUnitMember Definition = definition;
        public Queue<(double X, double Y)> Waiting = new();
        public (double X, double Y) Base;
        public (int X, int Y) Offset;
        public readonly double Interval = interval;
        public double NextTick = time + interval;
        public long Frame = frame;
        public int Phase;
        public bool Pending;
        public int DirectionOffset, DirectionCooldown;
        public Member Copy()
        {
            var copy = (Member)MemberwiseClone();
            copy.Waiting = new(Waiting);
            return copy;
        }
    }
    public sealed record Trail(string ClipId, double X, double Y, double Depth, double Emitted, int Elevation);
    private readonly Dictionary<(int Group, int Index), Member> _members = [];
    private readonly List<Trail> _trails = [];
    private double _time, _visualTime, _nextPosition;
    private int _movement;
    private bool _moving;
    private MapUnitLayout? _layout;
    private int _formation = -1;
    private uint _soldiers;
    public long Revision { get; private set; }
    public bool IsInitialized { get; private set; }
    public double VisualTime => _visualTime;
    public IReadOnlyList<Trail> Trails => _trails;
    public UnitMotionPlayback Copy()
    {
        var copy = new UnitMotionPlayback
        {
            _time = _time,
            _visualTime = _visualTime,
            _nextPosition = _nextPosition,
            _movement = _movement,
            _moving = _moving,
            IsInitialized = IsInitialized,
            _layout = _layout,
            _formation = _formation,
            _soldiers = _soldiers,
            Revision = Revision
        };
        copy.BodyAttack = BodyAttack;
        foreach (var (key, member) in _members) copy._members.Add(key, member.Copy());
        copy._trails.AddRange(_trails);
        return copy;
    }
    private static bool Leader(MapUnitMember m) => m.Group == 0 && m.Index == 0;
    private static uint Seed(int unit, MapUnitMember m, int tick) =>
        UnitVisuals.Seed(unit, m.Index, unchecked((int)UnitVisuals.Seed(m.Group, 0, tick)));
    private static (double X, double Y) Point(UnitVisualState s, MapUnitMember m) =>
        (32 * (s.CellX + s.MapHeight - s.CellY) + 96 + m.X, 16 * (s.CellX + s.CellY + 1) + 112 + m.Y);
    private static MapUnitLayout Layout(UnitVisualState s, MapUnitCatalog catalog) => catalog.Formations
        .Single(f => f.FormationId == s.FormationId).Layouts.Where(l => l.MinimumSoldiers <= s.BodySoldiers)
        .MaxBy(l => l.MinimumSoldiers)!;

    private void Reconcile(UnitVisualState state, MapUnitCatalog catalog, double time)
    {
        if (_formation == state.FormationId && _soldiers == state.BodySoldiers) return;
        _formation = state.FormationId; _soldiers = state.BodySoldiers;
        var selected = Layout(state, catalog);
        if (ReferenceEquals(_layout, selected)) return;
        _layout = selected;
        var layout = selected.Members;
        var identities = layout.Select(m => (m.Group, m.Index)).ToHashSet();
        foreach (var key in _members.Keys.Where(k => !identities.Contains(k)).ToArray()) _members.Remove(key);
        foreach (var m in layout)
        {
            var key = (m.Group, m.Index);
            if (_members.TryGetValue(key, out var existing) && existing.Definition == m) continue;
            int disorder = (int)Math.Round(100 * UnitVisuals.Disorder(state, m));
            double interval = 100 + (Leader(m) || disorder == 0 ? 0 : Seed(state.Id, m, 1) % (uint)disorder);
            _members[key] = new(m, interval, Seed(state.Id, m, 5), time) { Base = Point(state, m) };
        }
    }

    /// <summary>A movement starts member delays; publishing another day retains the unconsumed path.</summary>
    public void Begin(UnitVisualState before, MapUnitCatalog catalog, double time, double visualTime, bool moving)
    {
        Reconcile(before, catalog, visualTime);
        _time = time; _visualTime = visualTime; _nextPosition = time;
        if (moving && !_moving)
        {
            foreach (var member in _members.Values.Where(m => !m.Pending))
            {
                var m = member.Definition;
                int count = (int)(UnitVisuals.Disorder(before, m) * (Seed(before.Id, m, _movement) % 800) / 100);
                member.Waiting.Clear();
                for (int i = 0; i < count; i++) member.Waiting.Enqueue(Point(before, m));
            }
            _movement++;
        }
        else if (!moving)
            foreach (var member in _members.Values.Where(m => !m.Pending)) member.Base = Point(before, member.Definition);
        _moving = moving;
        IsInitialized = true;
    }

    public void Advance(double time, double visualTime, bool motionEnabled, Func<double, UnitVisualState> stateAt,
        MapUnitCatalog catalog, IReadOnlyDictionary<string, MapAnimationClip> clips, Func<double, double> emissionTime)
    {
        if (time < _time || visualTime < _visualTime) throw new ArgumentOutOfRangeException(nameof(time));
        double first = emissionTime(_time), last = emissionTime(time);
        double AtMotionTime(double visual)
        {
            if (visual >= last) return time;
            if (Math.Abs((last - first) - (time - _time)) < .00001) return Math.Clamp(_time + visual - first, _time, time);
            double lo = _time, hi = time;
            for (int i = 0; i < 40; i++)
            {
                double middle = (lo + hi) / 2;
                if (emissionTime(middle) < visual) lo = middle; else hi = middle;
            }
            return hi;
        }
        while (true)
        {
            double tick = _members.Count == 0 ? double.PositiveInfinity : _members.Values.Min(m => m.NextTick);
            // Preparation retains the first callback; deferred effects begin at the current visual time.
            double positionTick = motionEnabled && _nextPosition <= time + 1e-6
                ? Math.Max(_visualTime, emissionTime(_nextPosition)) : double.PositiveInfinity;
            double next = Math.Min(tick, positionTick);
            if (next > visualTime + 1e-6) break;
            double motionTime = tick <= positionTick ? AtMotionTime(next) : _nextPosition;
            var state = stateAt(motionTime);
            Reconcile(state, catalog, next);
            // Timer callbacks continue during pause; only position consumption and sway are gated.
            bool advancing = motionEnabled && Math.Abs(emissionTime(motionTime) - next) < .00001;
            foreach (var member in _members.Values.Where(m => m.NextTick <= next))
            {
                AdvanceMember(member, state, advancing);
                Revision++;
            }
            if (positionTick <= next)
            {
                PublishPositions(stateAt(_nextPosition), catalog, _nextPosition, positionTick);
                _nextPosition += MapMovementCadence.StepMilliseconds;
            }
        }
        var current = stateAt(time);
        Reconcile(current, catalog, visualTime);
        if (current.PositionMode == UnitPositionMode.Formation) SynchronizeFormation(current);
        _trails.RemoveAll(t => visualTime - t.Emitted >= clips[t.ClipId].Frames.Length * clips[t.ClipId].FrameMilliseconds);
        _time = time; _visualTime = visualTime;
    }

    private static void AdvanceMember(Member member, UnitVisualState state, bool advancing)
    {
        if (advancing && state.PositionMode == UnitPositionMode.Follow)
        {
            if (!state.IsMoving && member.Pending)
            {
                if (member.Waiting.TryDequeue(out var point)) member.Base = point;
                else member.Pending = false;
            }
            else
            {
                member.Phase = (member.Phase + member.Definition.Group / 2 + 6) % 256;
                int wave = ((int)Math.Round(256 * Math.Sin(member.Phase * Math.Tau / 256)) + 256) / 2;
                int spread = state.Condition switch
                {
                    UnitCondition.Disguised => 40,
                    UnitCondition.NoFormation => 50,
                    UnitCondition.Confused => 60,
                    UnitCondition.Routed => 100,
                    UnitCondition.HighSpirits => 25,
                    _ => 35
                };
                int percentage = spread * (int)Math.Round(100 * UnitVisuals.Disorder(state, member.Definition)) * wave / 25600;
                member.Offset = (percentage * member.Definition.X / 100, percentage * member.Definition.Y / 100);
            }
        }
        member.Frame++; AdvanceDirection(member, state);
        member.NextTick += member.Interval;
    }

    private void PublishPositions(UnitVisualState state, MapUnitCatalog catalog, double motionTime, double visualTime)
    {
        if (state.PositionMode == UnitPositionMode.Formation) SynchronizeFormation(state);
        if (state.ReceivesPositionUpdates || state.IsMoving)
            foreach (var member in _members.Values)
            {
                var point = Point(state, member.Definition);
                if (point != member.Base)
                {
                    if (Leader(member.Definition)) member.Base = point;
                    else
                    {
                        if (member.Waiting.TryDequeue(out var prior)) member.Base = prior;
                        member.Waiting.Enqueue(point); member.Pending = true;
                    }
                    Revision++;
                }
                var body = catalog.Bodies.Single(b => b.Id == member.Definition.BodyId);
                if (!state.IsMoving || state.Action is UnitVisualAction.Melee or UnitVisualAction.Ranged || body.Trail is not {} trail ||
                    Seed(state.Id, member.Definition, (int)(motionTime / MapMovementCadence.StepMilliseconds) + 100) % 1600 >= body.TrailChance * 16) continue;
                var pose = Position(member.Definition);
                _trails.Add(new(trail.ClipId, pose.X + trail.X, pose.Y + trail.Y,
                    pose.Y + 30000 - (body.Wake is null ? 0 : 1), visualTime, state.Elevation));
            }
    }

    private void SynchronizeFormation(UnitVisualState state)
    {
        // A formation correction owns every member position. Marching queues must not
        // defer followers or survive into the next snapshot after the correction ends.
        foreach (var member in _members.Values)
        {
            var point = Point(state, member.Definition);
            if (member.Base == point && member.Waiting.Count == 0 && !member.Pending) continue;
            member.Base = point;
            member.Waiting.Clear();
            member.Pending = false;
            Revision++;
        }
    }

    private static void AdvanceDirection(Member member, UnitVisualState state)
    {
        if (state.Condition == UnitCondition.Normal) { member.DirectionOffset = member.DirectionCooldown = 0; return; }
        if (Leader(member.Definition) || state.Condition == UnitCondition.HighSpirits) return;
        if (member.DirectionCooldown > 0) { member.DirectionCooldown--; return; }
        int group = member.Definition.Group;
        member.DirectionOffset = state.Condition switch
        {
            UnitCondition.Disguised => 1,
            UnitCondition.NoFormation => group == 0 ? 0 : group % 2 == 0 ? 1 : -1,
            UnitCondition.Confused => group % 2 == 0 ? -1 : 1,
            UnitCondition.Routed => group % 2 == 0 ? 1 : -1,
            _ => 0
        };
        member.DirectionCooldown = state.Condition == UnitCondition.Confused ? 0 : 1;
    }
    public (double X, double Y) Position(MapUnitMember definition)
    {
        var m = _members[(definition.Group, definition.Index)];
        return (m.Base.X + m.Offset.X, m.Base.Y + m.Offset.Y);
    }
    public double Interval(MapUnitMember definition) => _members[(definition.Group, definition.Index)].Interval;
    public int Frame(MapUnitMember definition, int count) => (int)(_members[(definition.Group, definition.Index)].Frame % count);
    public int Direction(MapUnitMember definition, UnitVisualState state) =>
        (state.Direction + _members[(definition.Group, definition.Index)].DirectionOffset + 8) % 8;
}
