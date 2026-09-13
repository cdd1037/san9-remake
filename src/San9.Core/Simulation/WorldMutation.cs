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
    private Dictionary<int, int?>? _leadershipBefore;
    private EngagementRelations? _engagements;
    private MilitaryRulebook? _engagementRules;

    internal EngagementRelations Engagements(MilitaryRulebook military)
    {
        if (_engagements is null || !ReferenceEquals(_engagementRules, military))
        {
            _engagements = MilitaryEngagements.Build(this, military);
            _engagementRules = military;
        }
        return _engagements;
    }

    private void LeadershipChanged()
    {
        _leadershipBefore ??= Officers.Values.Where(p => p.IsCommander && p.IsServing && p.SettlementId is not null)
            .GroupBy(p => p.SettlementId!.Value).ToDictionary(g => g.Key, g => (int?)g.First().Id);
    }

    internal void MaintainLeadership(List<DomainEvent> events)
    {
        if (_leadershipBefore is not null)
        {
            var before = _leadershipBefore;
            LeadershipMaintenance.Apply(this, _abilityRules.Governance);
            foreach (int place in before.Keys.Concat(Officers.Values.Where(p => p.IsCommander && p.SettlementId is not null)
                .Select(p => p.SettlementId!.Value)).Distinct().Order())
            {
                int? current = LeadershipMaintenance.Commander(this, place)?.Id;
                if (before.GetValueOrDefault(place) != current)
                    events.Add(new CommanderChangedEvent(Clock, place, current));
            }
            _leadershipBefore = null;
        }
    }

    internal WorldMutation(
        WorldState owner,
        OfficerAbilityRules abilityRules,
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
        _abilityRules = abilityRules;
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

    internal OfficerAbilities Abilities(OfficerState person) => _abilityRules.Evaluate(person, Treasures.Values);
    internal ImmutableDictionary<int, OfficerAbilities> AbilitiesFor(IEnumerable<OfficerState> people) =>
        _abilityRules.EvaluateMany(people, Treasures.Values);
    private readonly OfficerAbilityRules _abilityRules;

    internal void SetClock(GameClock clock)
    {
        EnsureOpen();
        Clock = clock;
    }

    internal void SetSimulation(SimulationState simulation)
    {
        EnsureOpen();
        if (simulation.Tasks != Simulation.Tasks && !simulation.Tasks.Where(t => t.Journey is not null).Select(t => t.OfficerId)
            .SequenceEqual(Simulation.Tasks.Where(t => t.Journey is not null).Select(t => t.OfficerId))) LeadershipChanged();
        if (simulation.Alliance?.MemberForceIds != Simulation.Alliance?.MemberForceIds) _engagements = null;
        Simulation = simulation;
    }

    internal ulong NextRandom(string name)
    {
        EnsureOpen();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var existing = Simulation.RandomStreams.SingleOrDefault(s => s.Name == name);
        var random = new StableRandom(existing?.State ?? StableRandom.DeriveSeed(Simulation.RunSeed, name));
        ulong result = random.NextUInt64();
        SetSimulation(Simulation with { RandomStreams = Simulation.RandomStreams
            .Where(s => s.Name != name).Append(new(name, random.State))
            .OrderBy(s => s.Name, StringComparer.Ordinal).ToImmutableArray() });
        return result;
    }

    internal void Replace(ForceState value)
    {
        if (Forces[value.Id].IsEliminated != value.IsEliminated) _engagements = null;
        Forces = Replace(Forces, value.Id, value);
    }
    internal void Replace(SettlementState value)
    {
        var previous = Settlements[value.Id];
        if (previous.OwnerId != value.OwnerId || previous.CanHostPersons != value.CanHostPersons || previous.IsDestroyed != value.IsDestroyed)
            LeadershipChanged();
        if (previous.OwnerId != value.OwnerId || previous.CanHostPersons != value.CanHostPersons || previous.IsDestroyed != value.IsDestroyed ||
            previous.Facility != value.Facility || (previous.Soldiers > 0) != (value.Soldiers > 0) ||
            (previous.Morale > 0) != (value.Morale > 0) || (previous.Durability > 0) != (value.Durability > 0) ||
            (previous.ConfusionDaysRemaining == 0) != (value.ConfusionDaysRemaining == 0)) _engagements = null;
        Settlements = Replace(Settlements, value.Id, value);
    }
    internal void AddSettlement(SettlementState value)
    {
        EnsureOpen();
        Settlements = Settlements.Add(value.Id, value);
        _engagements = null;
    }
    internal void Replace(OfficerState value)
    {
        var previous = Officers[value.Id];
        if (previous.OwnerId != value.OwnerId || previous.Status != value.Status || previous.SettlementId != value.SettlementId ||
            previous.UnitId != value.UnitId || previous.OfficeId != value.OfficeId || previous.Injury != value.Injury ||
            previous.BaseAbilities.Leadership != value.BaseAbilities.Leadership) LeadershipChanged();
        Officers = Replace(Officers, value.Id, value);
    }
    internal void Replace(UnitState value)
    {
        var previous = Units[value.Id];
        if (previous.OwnerId != value.OwnerId || previous.Position != value.Position || previous.FormationId != value.FormationId ||
            previous.IsEmbedded != value.IsEmbedded || previous.IsPersonnel != value.IsPersonnel ||
            previous.Order != value.Order || previous.EngagementTargetId != value.EngagementTargetId || previous.Condition != value.Condition ||
            (previous.Soldiers > 0) != (value.Soldiers > 0) || (previous.Morale > 0) != (value.Morale > 0)) _engagements = null;
        Units = Replace(Units, value.Id, value);
    }
    internal void AddUnit(UnitState unit)
    {
        EnsureOpen();
        Units = Units.Add(unit.Id, unit);
        _engagements = null;
    }
    internal void RemoveUnit(int id)
    {
        EnsureOpen();
        if (!Units.ContainsKey(id)) throw new InvalidOperationException("Unknown unit.");
        Units = Units.Remove(id);
        _engagements = null;
    }
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
    internal void Replace(TreasureState value)
    {
        var previous = Treasures[value.Id];
        if (previous.HolderId != value.HolderId || previous.Discovery != value.Discovery) LeadershipChanged();
        Treasures = Replace(Treasures, value.Id, value);
    }
    internal void Replace(ForceRelation value)
    {
        var previous = Relations[(value.FirstId, value.SecondId)];
        if (MilitaryDiplomacy.IsHostileRelation(previous.Value) != MilitaryDiplomacy.IsHostileRelation(value.Value)) _engagements = null;
        Relations = Replace(Relations, (value.FirstId, value.SecondId), value);
    }

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
