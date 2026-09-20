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
    private MovementOccupancy? _movementOccupancy;
    internal IReadOnlyList<MapOccupancy> MovementOccupancy(MilitaryRulebook rules, int unit, int? entry, MapOccupancy area) =>
        (_movementOccupancy ??= new(this, rules)).Query(this, unit, entry, area);

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
        AllForces = forces;
        AllSettlements = settlements;
        AllOfficers = officers;
        Units = units;
        AllCorps = corps;
        AllTreasures = treasures;
        AllRelations = relations;
        RebuildActiveRecords();
    }

    internal GameClock Clock { get; private set; }
    internal SimulationState Simulation { get; private set; }
    internal ImmutableDictionary<int, ForceState> AllForces { get; private set; }
    internal ImmutableDictionary<int, SettlementState> AllSettlements { get; private set; }
    internal ImmutableDictionary<int, OfficerState> AllOfficers { get; private set; }
    internal ImmutableDictionary<int, UnitState> Units { get; private set; }
    internal ImmutableDictionary<int, CorpsState> AllCorps { get; private set; }
    internal ImmutableDictionary<int, TreasureState> AllTreasures { get; private set; }
    internal ImmutableDictionary<(int FirstId, int SecondId), ForceRelation> AllRelations { get; private set; }

    // Complete registries own state. These derived indices expose only participating objects to rules.
    // Rebuilt on activation changes; ordinary replacements update the relevant immutable index.
    internal ImmutableDictionary<int, ForceState> Forces { get; private set; } = ImmutableDictionary<int, ForceState>.Empty;
    internal ImmutableDictionary<int, SettlementState> Settlements { get; private set; } = ImmutableDictionary<int, SettlementState>.Empty;
    internal ImmutableDictionary<int, OfficerState> Officers { get; private set; } = ImmutableDictionary<int, OfficerState>.Empty;
    internal ImmutableDictionary<int, CorpsState> Corps => _activeCorps ??= AllCorps.RemoveRange(AllCorps.Values.Where(c => IsDormantForce(c.OwnerId)).Select(c => c.Id));
    internal ImmutableDictionary<int, TreasureState> Treasures => _activeTreasures ??= AllTreasures.RemoveRange(AllTreasures.Values.Where(t => t.HolderId is int id && AllOfficers.TryGetValue(id, out var holder) && holder.IsDormant).Select(t => t.Id));
    internal ImmutableDictionary<(int FirstId, int SecondId), ForceRelation> Relations => _activeRelations ??= AllRelations.RemoveRange(AllRelations.Keys.Where(k => IsDormantForce(k.FirstId) || IsDormantForce(k.SecondId)));
    private ImmutableDictionary<int, CorpsState>? _activeCorps;
    private ImmutableDictionary<int, TreasureState>? _activeTreasures;
    private ImmutableDictionary<(int FirstId, int SecondId), ForceRelation>? _activeRelations;
    private bool IsDormantForce(int id) => AllForces.TryGetValue(id, out var force) && force.IsDormant;

    private void RebuildActiveRecords()
    {
        Forces = AllForces.RemoveRange(AllForces.Values.Where(f => f.IsDormant).Select(f => f.Id));
        Settlements = AllSettlements.RemoveRange(AllSettlements.Values.Where(s => s.IsDormant).Select(s => s.Id));
        Officers = AllOfficers.RemoveRange(AllOfficers.Values.Where(p => p.IsDormant).Select(p => p.Id));
        _activeCorps = null; _activeTreasures = null; _activeRelations = null;
        _engagements = null; _movementOccupancy = null;
    }

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
        if (simulation.Alliance?.MemberForceIds != Simulation.Alliance?.MemberForceIds || simulation.AttackRequests != Simulation.AttackRequests) _engagements = null;
        Simulation = simulation;
    }

    internal void ConsumeEvent(EventCandidate candidate, EventOutcome outcome)
    {
        EnsureOpen();
        var keys = EventSelection.AddConsumedKey(Simulation.ConsumedEventKeys, candidate, outcome);
        SetSimulation(Simulation with { ConsumedEventKeys = keys });
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
        if (AllForces[value.Id].IsEliminated != value.IsEliminated) _engagements = null;
        bool activationChanged = AllForces[value.Id].IsDormant != value.IsDormant;
        AllForces = Replace(AllForces, value.Id, value);
        if (activationChanged) RebuildActiveRecords();
        else if (!value.IsDormant) Forces = Forces.SetItem(value.Id, value);
    }
    internal void Replace(SettlementState value)
    {
        var previous = AllSettlements[value.Id];
        if (previous.IsDormant != value.IsDormant || previous.OwnerId != value.OwnerId || previous.CanHostPersons != value.CanHostPersons || previous.IsDestroyed != value.IsDestroyed)
            LeadershipChanged();
        if (previous.OwnerId != value.OwnerId || previous.CanHostPersons != value.CanHostPersons || previous.IsDestroyed != value.IsDestroyed ||
            previous.Facility != value.Facility || (previous.Soldiers > 0) != (value.Soldiers > 0) ||
            (previous.Morale > 0) != (value.Morale > 0) || (previous.Durability > 0) != (value.Durability > 0) ||
            (previous.ConfusionDaysRemaining == 0) != (value.ConfusionDaysRemaining == 0)) _engagements = null;
        AllSettlements = Replace(AllSettlements, value.Id, value);
        if (previous.IsDormant != value.IsDormant) RebuildActiveRecords();
        else if (!value.IsDormant) Settlements = Settlements.SetItem(value.Id, value);
        if (previous.OwnerId != value.OwnerId && value.Kind == SettlementKind.City && !value.IsDestroyed &&
            value.OwnerId is int owner && Simulation.Controls.Any(c => c.ForceId == owner && c.Kind == ControllerKind.Player))
            SetSimulation(Simulation with { ForceConsolidationPending = true });
        if (!value.IsDormant) _movementOccupancy?.Set(value);
    }
    internal void AddSettlement(SettlementState value)
    {
        EnsureOpen();
        AllSettlements = AllSettlements.Add(value.Id, value);
        if (!value.IsDormant) Settlements = Settlements.Add(value.Id, value);
        if (!value.IsDormant) _movementOccupancy?.Set(value);
        _engagements = null;
    }
    internal void Replace(OfficerState value)
    {
        var previous = AllOfficers[value.Id];
        if (previous.IsDormant != value.IsDormant || previous.OwnerId != value.OwnerId || previous.Status != value.Status || previous.SettlementId != value.SettlementId ||
            previous.UnitId != value.UnitId || previous.OfficeId != value.OfficeId || previous.Injury != value.Injury ||
            previous.BaseAbilities.Leadership != value.BaseAbilities.Leadership) LeadershipChanged();
        AllOfficers = Replace(AllOfficers, value.Id, value);
        if (previous.IsDormant != value.IsDormant) RebuildActiveRecords();
        else if (!value.IsDormant) Officers = Officers.SetItem(value.Id, value);
    }
    internal void Replace(UnitState value)
    {
        var previous = Units[value.Id];
        if (previous.EffectiveOrder != value.EffectiveOrder || previous.FormationId != value.FormationId || previous.OwnerId != value.OwnerId ||
            previous.Position != value.Position && ReferenceEquals(previous.MovementPlan, value.MovementPlan))
            value = value with { MovementPlan = null };
        if (previous.OwnerId != value.OwnerId || previous.Position != value.Position || previous.FormationId != value.FormationId ||
            previous.IsEmbedded != value.IsEmbedded || previous.IsPersonnel != value.IsPersonnel ||
            previous.Order != value.Order || previous.EngagementTargetId != value.EngagementTargetId || previous.Condition != value.Condition ||
            (previous.Soldiers > 0) != (value.Soldiers > 0) || (previous.Morale > 0) != (value.Morale > 0)) _engagements = null;
        Units = Replace(Units, value.Id, value);
        _movementOccupancy?.Set(value);
    }
    internal void AddUnit(UnitState unit)
    {
        EnsureOpen();
        Units = Units.Add(unit.Id, unit);
        _movementOccupancy?.Set(unit);
        _engagements = null;
    }
    internal void RemoveUnit(int id)
    {
        EnsureOpen();
        if (!Units.ContainsKey(id)) throw new InvalidOperationException("Unknown unit.");
        Units = Units.Remove(id);
        _movementOccupancy?.RemoveUnit(id);
        _engagements = null;
    }
    internal void Replace(CorpsState value)
    {
        AllCorps = Replace(AllCorps, value.Id, value);
        _activeCorps = null;
    }
    internal void RemoveCorps(int id)
    {
        EnsureOpen();
        if (!AllCorps.ContainsKey(id)) throw new InvalidOperationException("Unknown corps.");
        AllCorps = AllCorps.Remove(id); _activeCorps = null;
    }
    // Retired forces remain in the world, so allocation never reuses a previous identity.
    internal void AddForce(ForceState force)
    {
        EnsureOpen();
        if (force.Origin is null || force.Id != checked(AllForces.Keys.DefaultIfEmpty(-1).Max() + 1))
            throw new ArgumentException("Invalid generated force identity or origin.");
        AllForces = AllForces.Add(force.Id, force);
        RebuildActiveRecords();
        _engagements = null;
    }

    internal void AddCorps(CorpsState corps)
    {
        EnsureOpen();
        AllCorps = AllCorps.Add(corps.Id, corps); _activeCorps = null;
    }
    internal void Replace(TreasureState value)
    {
        var previous = AllTreasures[value.Id];
        if (previous.HolderId != value.HolderId || previous.Discovery != value.Discovery) LeadershipChanged();
        AllTreasures = Replace(AllTreasures, value.Id, value); _activeTreasures = null;
    }
    internal void Replace(ForceRelation value)
    {
        var previous = AllRelations[(value.FirstId, value.SecondId)];
        if (MilitaryDiplomacy.IsHostileRelation(previous.Value) != MilitaryDiplomacy.IsHostileRelation(value.Value)) _engagements = null;
        AllRelations = Replace(AllRelations, (value.FirstId, value.SecondId), value); _activeRelations = null;
    }

    internal void SetRelation(ForceRelation value)
    {
        EnsureOpen();
        int previous = MilitaryDiplomacy.Relation(this, value.FirstId, value.SecondId);
        if (MilitaryDiplomacy.IsHostileRelation(previous) != MilitaryDiplomacy.IsHostileRelation(value.Value)) _engagements = null;
        AllRelations = AllRelations.SetItem((value.FirstId, value.SecondId), value); _activeRelations = null;
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
