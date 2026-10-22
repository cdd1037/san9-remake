using System.Collections.Immutable;
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
    private ActiveWorldRecords _activeRecords = new(
        ImmutableDictionary<int, ForceState>.Empty, ImmutableDictionary<int, SettlementState>.Empty,
        ImmutableDictionary<int, OfficerState>.Empty);
    // Published arrays depend only on their immutable collection root, not the world revision.
    private IReadOnlyList<ForceState>? _publishedForces;
    private IReadOnlyList<SettlementState>? _publishedSettlements;
    private IReadOnlyList<OfficerState>? _publishedOfficers;
    private IReadOnlyList<UnitState>? _publishedUnits;
    private IReadOnlyList<CorpsState>? _publishedCorps;
    private IReadOnlyList<TreasureState>? _publishedTreasures;
    private IReadOnlyList<ForceRelation>? _publishedRelations;
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
        IEnumerable<SettlementState> settlements, IEnumerable<OfficerState> officers, IEnumerable<UnitState>? units = null, IEnumerable<CorpsState>? corps = null, IEnumerable<TreasureState>? treasures = null, IEnumerable<ForceRelation>? relations = null, IEnumerable<ControlSeat>? controls = null, int computerDamageBonusPercent = 0, AllianceState? alliance = null, IEnumerable<AttackRequestState>? attackRequests = null, bool annualAppearancesEnabled = true, bool naturalDeathsEnabled = true, bool freeOfficerMigrationEnabled = true, bool battleDeathsEnabled = false, bool officeRequestsEnabled = true, IEnumerable<PersistentEventKey>? consumedEventKeys = null, bool forceConsolidationEnabled = true, bool forceEmergenceEnabled = true, bool cityIndependenceEnabled = true, bool imperialCourtEnded = false, GameClock? threeVisitsStarted = null, int foodExchangeRate = 1, bool settlementDefectionsEnabled = true, bool unitDefectionsEnabled = true)
    {
        var world = new WorldState(clock, seed);
        world._forces = IndexUnique(forces, value => value.Id);
        world._settlements = IndexUnique(settlements, value => value.Id);
        world._officers = IndexUnique(officers, value => value.Id);
        world._units = IndexUnique(units ?? [], value => value.Id);
        world._corps = IndexUnique(corps ?? [], value => value.Id);
        world._treasures = IndexUnique(treasures ?? [], value => value.Id);
        world._relations = IndexUnique(relations ?? [], value => (value.FirstId, value.SecondId));
        world.Simulation = world.Simulation with { Controls = (controls ?? []).ToImmutableArray(), ComputerDamageBonusPercent = computerDamageBonusPercent, Alliance = alliance,
            UnitDefectionsEnabled = unitDefectionsEnabled, SettlementDefectionsEnabled = settlementDefectionsEnabled, FoodExchangeRate = foodExchangeRate, ThreeVisitsStarted = threeVisitsStarted, ImperialCourtEnded = imperialCourtEnded,
            AnnualAppearancesEnabled = annualAppearancesEnabled, NaturalDeathsEnabled = naturalDeathsEnabled,
            OfficeRequestsEnabled = officeRequestsEnabled, ForceEmergenceEnabled = forceEmergenceEnabled, CityIndependenceEnabled = cityIndependenceEnabled, ForceConsolidationEnabled = forceConsolidationEnabled,
            ConsumedEventKeys = (consumedEventKeys ?? []).OrderBy(k => k.Value, StringComparer.Ordinal).ToImmutableArray(),
            FreeOfficerMigrationEnabled = freeOfficerMigrationEnabled,
            BattleDeathsEnabled = battleDeathsEnabled,
            AttackRequests = (attackRequests ?? []).OrderBy(r => r.RequestedForceId).ToImmutableArray(),
            NextUnitId = world._units.Count == 0 ? 0 : (long)world._units.Keys.Max() + 1 };
        world._activeRecords = ActiveWorldRecords.Create(world._forces, world._settlements, world._officers);
        WorldInvariants.Validate(world.BeginMutation());
        return world;
    }

    /// <summary>Clones committed runtime state, including pending interactions; save validation belongs to Application.</summary>
    public static WorldState Restore(WorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Simulation.Boundary is not (SimulationBoundary.Stable or SimulationBoundary.Terminal or SimulationBoundary.AwaitingDecision or SimulationBoundary.Advancing))
            throw new ArgumentException("Only a committed stable boundary can be restored.");
        if (snapshot.Revision < 0) throw new ArgumentException("Invalid world revision.");
        var world = new WorldState(snapshot.Clock, snapshot.Simulation.InitialSeed)
        {
            _forces = IndexUnique(snapshot.Forces, value => value.Id),
            _settlements = IndexUnique(snapshot.Settlements, value => value.Id),
            _officers = IndexUnique(snapshot.Officers, value => value.Id),
            _units = IndexUnique(snapshot.Units, value => value.Id),
            _corps = IndexUnique(snapshot.Corps, value => value.Id),
            _treasures = IndexUnique(snapshot.Treasures, value => value.Id),
            _relations = IndexUnique(snapshot.Relations, value => (value.FirstId, value.SecondId)),
            Simulation = snapshot.Simulation,
            Revision = snapshot.Revision,
        };
        world._activeRecords = ActiveWorldRecords.Create(world._forces, world._settlements, world._officers);
        WorldInvariants.Validate(world.BeginMutation());
        return world;
    }

    // Internal branches share immutable committed records; a commit replaces only the branch's roots.
    internal WorldState Fork()
    {
        if (IsExecuting) throw new InvalidOperationException("Cannot fork an executing world.");
        return new(Clock, Simulation.InitialSeed)
        {
            _forces = _forces, _settlements = _settlements, _officers = _officers, _units = _units,
            _corps = _corps, _treasures = _treasures, _relations = _relations,
            _activeRecords = _activeRecords,
            Simulation = Simulation, Revision = Revision, _snapshot = _snapshot,
            _publishedForces = _publishedForces,
            _publishedSettlements = _publishedSettlements,
            _publishedOfficers = _publishedOfficers,
            _publishedUnits = _publishedUnits,
            _publishedCorps = _publishedCorps,
            _publishedTreasures = _publishedTreasures,
            _publishedRelations = _publishedRelations,
        };
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

    internal WorldMutation BeginMutation(OfficerAbilityRules? abilities = null, EconomyRulebook? economy = null) => new(this, abilities ?? OfficerAbilityRules.Empty, economy ?? EconomyRulebook.Empty, Revision, Clock, Simulation, _forces, _settlements,
        _officers, _units, _corps, _treasures, _relations, _activeRecords);

    // Loading reconstructs derived totals without creating another gameplay transaction or revision.
    internal void RestoreDerivedTrust(WorldMutation mutation)
    {
        mutation.VerifyCommit(this, Revision);
        if (!ReferenceEquals(_forces, mutation.AllForces))
        {
            _forces = mutation.AllForces;
            _activeRecords = mutation.ActiveRecords;
            _publishedForces = null;
            _snapshot = null;
        }
        mutation.MarkCommitted();
    }

    internal void Commit(WorldMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        mutation.VerifyCommit(this, Revision);
        if (!ReferenceEquals(_forces, mutation.AllForces)) _publishedForces = null;
        if (!ReferenceEquals(_settlements, mutation.AllSettlements)) _publishedSettlements = null;
        if (!ReferenceEquals(_officers, mutation.AllOfficers)) _publishedOfficers = null;
        if (!ReferenceEquals(_units, mutation.Units)) _publishedUnits = null;
        if (!ReferenceEquals(_corps, mutation.AllCorps)) _publishedCorps = null;
        if (!ReferenceEquals(_treasures, mutation.AllTreasures)) _publishedTreasures = null;
        if (!ReferenceEquals(_relations, mutation.AllRelations)) _publishedRelations = null;
        _forces = mutation.AllForces;
        _settlements = mutation.AllSettlements;
        _officers = mutation.AllOfficers;
        _units = mutation.Units;
        _corps = mutation.AllCorps;
        _treasures = mutation.AllTreasures;
        _relations = mutation.AllRelations;
        _activeRecords = mutation.ActiveRecords;
        Clock = mutation.Clock;
        Simulation = mutation.Simulation;
        Revision++;
        _snapshot = null;
        mutation.MarkCommitted();
    }

    internal WorldSnapshot CreateSnapshot()
    {
        if (_snapshot is not null && _snapshot.Revision == Revision) return _snapshot;
        var forces = _publishedForces ??= Array.AsReadOnly(_forces.Values.OrderBy(f => f.Id).Select(f => f with { AdditionalFormationIds = f.AdditionalFormationIds.Order().ToImmutableArray(), ProvidedTactics = f.ProvidedTactics.Order().ToImmutableArray() }).ToArray());
        var settlements = _publishedSettlements ??= Array.AsReadOnly(_settlements.Values.OrderBy(s => s.Id).ToArray());
        var officers = _publishedOfficers ??= Array.AsReadOnly(_officers.Values.OrderBy(o => o.Id).ToArray());
        var corps = _publishedCorps ??= Array.AsReadOnly(_corps.Values.OrderBy(c => c.Id).ToArray());
        var relations = _publishedRelations ??= Array.AsReadOnly(_relations.Values.OrderBy(r => r.FirstId).ThenBy(r => r.SecondId).ToArray());
        var treasures = _publishedTreasures ??= Array.AsReadOnly(_treasures.Values.OrderBy(t => t.Id).ToArray());
        var units = _publishedUnits ??= Array.AsReadOnly(_units.Values.OrderBy(u => u.Id).ToArray());
        return _snapshot = new WorldSnapshot(Clock, SnapshotSimulation()) { Forces = forces, Settlements = settlements, Officers = officers, Units = units, Corps = corps, Treasures = treasures, Relations = relations, Revision = Revision };
    }

    private SimulationState SnapshotSimulation() => Simulation with
    {
        Tasks = Simulation.Tasks.OrderBy(t => t.Sequence).ThenBy(t => t.OriginSettlementId)
            .ThenBy(t => t.OfficerId).ThenBy(t => t.Id).ToImmutableArray()
    };
}

public sealed record WorldSnapshot(
    GameClock Clock,
    SimulationState Simulation)
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
