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
    private WorldState(GameClock clock, ulong randomState)
    {
        _ = new GameClock(clock.Year, clock.Month, clock.Xun);
        Clock = clock;
        RandomState = randomState;
    }

    public GameClock Clock { get; private set; }

    public ulong RandomState { get; private set; }

    public static WorldState Create(GameClock clock, ulong seed) => Create(clock, seed, [], [], []);

    public static WorldState Create(GameClock clock, ulong seed, IEnumerable<ForceState> forces,
        IEnumerable<SettlementState> settlements, IEnumerable<OfficerState> officers, IEnumerable<UnitState>? units = null, IEnumerable<CorpsState>? corps = null, IEnumerable<TreasureState>? treasures = null, IEnumerable<ForceRelation>? relations = null)
    {
        var world = new WorldState(clock, seed);
        world._forces = IndexUnique(forces, value => value.Id);
        world._settlements = IndexUnique(settlements, value => value.Id);
        world._officers = IndexUnique(officers, value => value.Id);
        world._units = IndexUnique(units ?? [], value => value.Id);
        world._corps = IndexUnique(corps ?? [], value => value.Id);
        world._treasures = IndexUnique(treasures ?? [], value => value.Id);
        world._relations = IndexUnique(relations ?? [], value => (value.FirstId, value.SecondId));
        WorldInvariants.Validate(world.BeginMutation());
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

    internal WorldMutation BeginMutation() => new(this, Revision, Clock, RandomState, _forces, _settlements,
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
        RandomState = mutation.RandomState;
        Revision++;
        _snapshot = null;
        mutation.MarkCommitted();
    }

    internal WorldSnapshot CreateSnapshot()
    {
        if (_snapshot is not null && _snapshot.Revision == Revision) return _snapshot;
        string canonical = string.Create(
            CultureInfo.InvariantCulture,
            $"{Clock.Year}|{Clock.Month}|{(int)Clock.Xun}|{RandomState}");
        var forces = Array.AsReadOnly(_forces.Values.OrderBy(f => f.Id).ToArray());
        var settlements = Array.AsReadOnly(_settlements.Values.OrderBy(s => s.Id).ToArray());
        var officers = Array.AsReadOnly(_officers.Values.OrderBy(o => o.Id).ToArray());
        var corps = Array.AsReadOnly(_corps.Values.OrderBy(c => c.Id).ToArray());
        var relations = Array.AsReadOnly(_relations.Values.OrderBy(r => r.FirstId).ThenBy(r => r.SecondId).ToArray());
        var treasures = Array.AsReadOnly(_treasures.Values.OrderBy(t => t.Id).ToArray());
        var units = Array.AsReadOnly(_units.Values.OrderBy(u => u.Id).ToArray());
        canonical += JsonSerializer.Serialize(new { forces, settlements, officers, units, corps, treasures, relations });
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return _snapshot = new WorldSnapshot(Clock, RandomState, hash) { Forces = forces, Settlements = settlements, Officers = officers, Units = units, Corps = corps, Treasures = treasures, Relations = relations, Revision = Revision };
    }
}

public sealed record WorldSnapshot(
    GameClock Clock,
    ulong RandomState,
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
