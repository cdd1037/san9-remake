using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>
/// Stages one atomic world change. Handlers validate business preconditions; the engine
/// coordinates execution and the world validates invariants before publishing the final state.
/// </summary>
internal sealed class WorldMutation
{
    private readonly WorldState _owner;
    private readonly long _expectedRevision;
    private bool _committed;

    internal WorldMutation(
        WorldState owner,
        long expectedRevision,
        GameClock clock,
        SimulationState simulation,
        ImmutableDictionary<int, ForceState> forces,
        ImmutableDictionary<int, SettlementState> settlements,
        ImmutableDictionary<int, OfficerState> officers,
        ImmutableDictionary<int, UnitState> units,
        ImmutableDictionary<int, CorpsState> corps,
        ImmutableDictionary<int, TreasureState> treasures,
        ImmutableDictionary<(int FirstId, int SecondId), ForceRelation> relations)
    {
        _owner = owner;
        _expectedRevision = expectedRevision;
        Clock = clock;
        Simulation = simulation;
        Forces = forces;
        Settlements = settlements;
        Officers = officers;
        Units = units;
        Corps = corps;
        Treasures = treasures;
        Relations = relations;
    }

    internal GameClock Clock { get; private set; }
    internal SimulationState Simulation { get; private set; }
    internal ImmutableDictionary<int, ForceState> Forces { get; private set; }
    internal ImmutableDictionary<int, SettlementState> Settlements { get; private set; }
    internal ImmutableDictionary<int, OfficerState> Officers { get; private set; }
    internal ImmutableDictionary<int, UnitState> Units { get; private set; }
    internal ImmutableDictionary<int, CorpsState> Corps { get; private set; }
    internal ImmutableDictionary<int, TreasureState> Treasures { get; private set; }
    internal ImmutableDictionary<(int FirstId, int SecondId), ForceRelation> Relations { get; private set; }

    internal void SetClock(GameClock clock)
    {
        EnsureOpen();
        Clock = clock;
    }

    internal void SetSimulation(SimulationState simulation)
    {
        EnsureOpen();
        Simulation = simulation;
    }

    internal ulong NextRandom(string name)
    {
        EnsureOpen();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var existing = Simulation.RandomStreams.SingleOrDefault(s => s.Name == name);
        var random = new StableRandom(existing?.State ?? StableRandom.DeriveSeed(Simulation.InitialSeed, name));
        ulong result = random.NextUInt64();
        SetSimulation(Simulation with { RandomStreams = Simulation.RandomStreams
            .Where(s => s.Name != name).Append(new(name, random.State))
            .OrderBy(s => s.Name, StringComparer.Ordinal).ToImmutableArray() });
        return result;
    }

    internal void Replace(ForceState value) => Forces = Replace(Forces, value.Id, value);
    internal void Replace(SettlementState value) => Settlements = Replace(Settlements, value.Id, value);
    internal void Replace(OfficerState value) => Officers = Replace(Officers, value.Id, value);
    internal void Replace(UnitState value) => Units = Replace(Units, value.Id, value);
    internal void Replace(CorpsState value) => Corps = Replace(Corps, value.Id, value);
    internal void RemoveCorps(int id)
    {
        EnsureOpen();
        if (!Corps.ContainsKey(id)) throw new InvalidOperationException("Unknown corps.");
        Corps = Corps.Remove(id);
    }
    internal void AddCorps(CorpsState corps)
    {
        EnsureOpen();
        Corps = Corps.Add(corps.Id, corps);
    }
    internal void Replace(TreasureState value) => Treasures = Replace(Treasures, value.Id, value);
    internal void Replace(ForceRelation value) =>
        Relations = Replace(Relations, (value.FirstId, value.SecondId), value);

    private ImmutableDictionary<TKey, TValue> Replace<TKey, TValue>(
        ImmutableDictionary<TKey, TValue> source,
        TKey key,
        TValue value)
        where TKey : notnull
    {
        EnsureOpen();
        if (!source.ContainsKey(key)) throw new InvalidOperationException($"Cannot replace unknown {typeof(TValue).Name} '{key}'.");
        return source.SetItem(key, value);
    }

    internal void VerifyCommit(WorldState owner, long revision)
    {
        EnsureOpen();
        if (!ReferenceEquals(_owner, owner)) throw new InvalidOperationException("Mutation belongs to another world.");
        if (_expectedRevision != revision) throw new InvalidOperationException("Mutation is based on a stale world revision.");
    }

    internal void MarkCommitted() => _committed = true;

    private void EnsureOpen()
    {
        if (_committed) throw new InvalidOperationException("Mutation has already been committed.");
    }
}
