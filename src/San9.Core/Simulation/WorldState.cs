using System.Globalization;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed class WorldState
{
    private ImmutableDictionary<int, ForceState> _forces = ImmutableDictionary<int, ForceState>.Empty;
    private ImmutableDictionary<int, SettlementState> _settlements = ImmutableDictionary<int, SettlementState>.Empty;
    private ImmutableDictionary<int, OfficerState> _officers = ImmutableDictionary<int, OfficerState>.Empty;
    private ImmutableDictionary<int, UnitState> _units = ImmutableDictionary<int, UnitState>.Empty;
    private ImmutableDictionary<int, CorpsState> _corps = ImmutableDictionary<int, CorpsState>.Empty;
    private ImmutableDictionary<int, TreasureState> _treasures = ImmutableDictionary<int, TreasureState>.Empty;
    private ImmutableDictionary<(int FirstId, int SecondId), ForceRelation> _relations =
        ImmutableDictionary<(int FirstId, int SecondId), ForceRelation>.Empty;
    public long Revision { get; private set; }
    private WorldSnapshot? _snapshot;
    internal bool IsExecuting { get; private set; }
    internal void EnterExecution()
    {
        if (IsExecuting) throw new InvalidOperationException("World is already executing.");
        IsExecuting = true;
    }
    internal void ExitExecution() => IsExecuting = false;
    private WorldState(GameClock clock, ulong seed)
    {
        _ = new GameClock(clock.Year, clock.Month, clock.Day);
        Clock = clock;
        Simulation = new(clock, seed);
    }

    public GameClock Clock { get; private set; }

    public SimulationState Simulation { get; private set; }

    public static WorldState Create(GameClock clock, ulong seed) => Create(clock, seed, [], [], []);

    public static WorldState Create(GameClock clock, ulong seed, IEnumerable<ForceState> forces,
        IEnumerable<SettlementState> settlements, IEnumerable<OfficerState> officers, IEnumerable<UnitState>? units = null, IEnumerable<CorpsState>? corps = null, IEnumerable<TreasureState>? treasures = null, IEnumerable<ForceRelation>? relations = null, IEnumerable<ControlSeat>? controls = null)
    {
        var world = new WorldState(clock, seed);
        world._forces = IndexUnique(forces, value => value.Id);
        world._settlements = IndexUnique(settlements, value => value.Id);
        world._officers = IndexUnique(officers, value => value.Id);
        world._units = IndexUnique(units ?? [], value => value.Id);
        world._corps = IndexUnique(corps ?? [], value => value.Id);
        world._treasures = IndexUnique(treasures ?? [], value => value.Id);
        world._relations = IndexUnique(relations ?? [], value => (value.FirstId, value.SecondId));
        world.Simulation = world.Simulation with { Controls = (controls ?? []).ToImmutableArray() };
        WorldInvariants.Validate(world.BeginMutation());
        return world;
    }

    /// <summary>Restores authoritative stable state; disk/content validation belongs to Application.</summary>
    public static WorldState Restore(WorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Simulation.Boundary is not (SimulationBoundary.Stable or SimulationBoundary.Terminal))
            throw new ArgumentException("Only a committed stable boundary can be restored.");
        var world = Create(snapshot.Simulation.Started, snapshot.Simulation.InitialSeed, snapshot.Forces,
            snapshot.Settlements, snapshot.Officers, snapshot.Units, snapshot.Corps, snapshot.Treasures,
            snapshot.Relations, snapshot.Simulation.Controls);
        world.Clock = snapshot.Clock;
        world.Simulation = snapshot.Simulation;
        if (snapshot.Revision < 0) throw new ArgumentException("Invalid world revision.");
        world.Revision = snapshot.Revision;
        WorldInvariants.Validate(world.BeginMutation());
        if (world.CreateSnapshot().StateHash != snapshot.StateHash)
            throw new ArgumentException("World state hash mismatch.");
        return world;
    }

    private static ImmutableDictionary<TKey, TValue> IndexUnique<TKey, TValue>(
        IEnumerable<TValue> values, Func<TValue, TKey> identity) where TKey : notnull
    {
        var builder = ImmutableDictionary.CreateBuilder<TKey, TValue>();
        foreach (var value in values)
        {
            TKey key = identity(value);
            if (builder.ContainsKey(key)) throw new ArgumentException($"Duplicate {typeof(TValue).Name} identity '{key}'.");
            builder.Add(key, value);
        }
        return builder.ToImmutable();
    }

    internal WorldMutation BeginMutation() => new(this, Revision, Clock, Simulation, _forces, _settlements,
        _officers, _units, _corps, _treasures, _relations);

    internal void Commit(WorldMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        mutation.VerifyCommit(this, Revision);
        WorldInvariants.Validate(mutation);
        _forces = mutation.Forces;
        _settlements = mutation.Settlements;
        _officers = mutation.Officers;
        _units = mutation.Units;
        _corps = mutation.Corps;
        _treasures = mutation.Treasures;
        _relations = mutation.Relations;
        Clock = mutation.Clock;
        Simulation = mutation.Simulation;
        Revision++;
        _snapshot = null;
        mutation.MarkCommitted();
    }

    internal WorldSnapshot CreateSnapshot()
    {
        if (_snapshot is not null && _snapshot.Revision == Revision) return _snapshot;
        string canonical = string.Create(
            CultureInfo.InvariantCulture,
            $"{Clock.Year}|{Clock.Month}|{Clock.Day}");
        var forces = Array.AsReadOnly(_forces.Values.OrderBy(f => f.Id).ToArray());
        var settlements = Array.AsReadOnly(_settlements.Values.OrderBy(s => s.Id).ToArray());
        var officers = Array.AsReadOnly(_officers.Values.OrderBy(o => o.Id).ToArray());
        var corps = Array.AsReadOnly(_corps.Values.OrderBy(c => c.Id).ToArray());
        var relations = Array.AsReadOnly(_relations.Values.OrderBy(r => r.FirstId).ThenBy(r => r.SecondId).ToArray());
        var treasures = Array.AsReadOnly(_treasures.Values.OrderBy(t => t.Id).ToArray());
        var units = Array.AsReadOnly(_units.Values.OrderBy(u => u.Id).ToArray());
        canonical += JsonSerializer.Serialize(new { simulation = Simulation, forces, settlements, officers, units, corps, treasures, relations });
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return _snapshot = new WorldSnapshot(Clock, Simulation, hash) { Forces = forces, Settlements = settlements, Officers = officers, Units = units, Corps = corps, Treasures = treasures, Relations = relations, Revision = Revision };
    }
}

public sealed record WorldSnapshot(
    GameClock Clock,
    SimulationState Simulation,
    string StateHash)
{
    public IReadOnlyList<ForceState> Forces { get; init; } = [];
    public IReadOnlyList<SettlementState> Settlements { get; init; } = [];
    public IReadOnlyList<OfficerState> Officers { get; init; } = [];
    public IReadOnlyList<UnitState> Units { get; init; } = [];
    public IReadOnlyList<ForceRelation> Relations { get; init; } = [];
    public IReadOnlyList<CorpsState> Corps { get; init; } = [];
    public IReadOnlyList<TreasureState> Treasures { get; init; } = [];
    public long Revision { get; init; }
}
