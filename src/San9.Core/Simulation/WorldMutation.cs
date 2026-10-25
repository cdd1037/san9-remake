using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>
/// Stages one atomic world change. Handlers validate business preconditions; the engine
/// coordinates execution. Handlers preserve domain invariants when changing state.
/// </summary>
internal sealed partial class WorldMutation : IWorldQuery
{
    SimulationState IWorldQuery.Simulation => Simulation;
    IReadOnlyDictionary<int, ForceState> IWorldQuery.Forces => Forces;
    IReadOnlyDictionary<int, SettlementState> IWorldQuery.Settlements => Settlements;
    IReadOnlyDictionary<int, OfficerState> IWorldQuery.Officers => Officers;
    IReadOnlyDictionary<int, UnitState> IWorldQuery.Units => Units;
    IReadOnlyDictionary<(int FirstId, int SecondId), ForceRelation> IWorldQuery.Relations => Relations;
    IEnumerable<OfficerState> IWorldQuery.ServingUnitMembers(int unitId) => ServingUnitMembers(unitId);
    bool IWorldQuery.HasTreasureTactic(int officerId, int tacticId, TreasureRulebook rules) => HasTreasureTactic(officerId, tacticId, rules);
    IReadOnlyList<MapOccupancy> IWorldQuery.MovementOccupancy(MilitaryRulebook rules, int owner, UnitPolicies policies, int? movingUnit, int? entry) =>
        MovementOccupancy(rules, owner, policies, movingUnit, entry);

    bool IWorldQuery.BlocksWaterContact(MilitaryRulebook rules, WorldPosition position, int? target) =>
        OccupancyFor(rules).BlocksContact(position, target);

    private readonly WorldState _owner;
    private readonly long _expectedRevision;
    private bool _committed;
    // Relative to Clock, rather than to the enclosing AdvanceDay operation.
    // Date advancement opens a new day's command stage.
    internal bool CurrentDateCommandsConsumed { get; set; }
    private Dictionary<int, int?>? _leadershipBefore;
    private EngagementRelations? _engagements;
    private MilitaryRulebook? _engagementRules;
    private MovementOccupancy? _movementOccupancy;
    internal IReadOnlyList<MapOccupancy> MovementOccupancy(MilitaryRulebook rules, int owner, UnitPolicies policies, int? movingUnit, int? entry) =>
        OccupancyFor(rules).Query(this, owner, policies, movingUnit, entry);

    private MovementOccupancy OccupancyFor(MilitaryRulebook rules)
    {
        if (_movementOccupancy is null || !ReferenceEquals(_movementOccupancy.Rules, rules))
            _movementOccupancy = new(this, rules);
        return _movementOccupancy;
    }

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
            var units = Units.Values.Where(u => u.CommanderId is not null).ToDictionary(u => u.Id);
            LeadershipMaintenance.Apply(this, _abilityRules.Governance);
            foreach (int place in before.Keys.Concat(Officers.Values.Where(p => p.IsCommander && p.SettlementId is not null)
                .Select(p => p.SettlementId!.Value)).Distinct().Order())
            {
                int? current = LeadershipMaintenance.Commander(this, place)?.Id;
                if (before.GetValueOrDefault(place) != current)
                    events.Add(new CommanderChangedEvent(Clock, place, current)
                    { PreviousId = before.GetValueOrDefault(place), ForceId = Settlements.GetValueOrDefault(place)?.OwnerId });
            }
            foreach (var unit in Units.Values.Where(u => u.CommanderId is not null))
                if (units.TryGetValue(unit.Id, out var previous) && previous.CommanderId != unit.CommanderId && previous.OwnerId == unit.OwnerId)
                    events.Add(new UnitCommanderChangedEvent(Clock, BattleObjectContext.Capture(unit), previous.CommanderId!.Value, unit.CommanderId!.Value));
            _leadershipBefore = null;
        }
    }

    internal WorldMutation(
        WorldState owner,
        OfficerAbilityRules abilityRules,
        EconomyRulebook economy,
        long expectedRevision,
        GameClock clock,
        SimulationState simulation,
        ImmutableDictionary<int, ForceState> forces,
        ImmutableDictionary<int, SettlementState> settlements,
        ImmutableDictionary<int, OfficerState> officers,
        ImmutableDictionary<int, UnitState> units,
        ImmutableDictionary<int, CorpsState> corps,
        ImmutableDictionary<int, TreasureState> treasures,
        ImmutableDictionary<(int FirstId, int SecondId), ForceRelation> relations,
        ActiveWorldRecords activeRecords)
    {
        _owner = owner;
        _abilityRules = abilityRules;
        _economy = economy;
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
        Forces = activeRecords.Forces;
        Settlements = activeRecords.Settlements;
        Officers = activeRecords.Officers;
    }

    public GameClock Clock { get; private set; }
    // Ephemeral presentation samples; these are not stable save boundaries.
    internal WorldSnapshot CapturePresentation() => new(Clock, Simulation)
    {
        Forces = AllForces.Values.OrderBy(x => x.Id).Select(x => x with
        {
            AdditionalFormationIds = x.AdditionalFormationIds.Order().ToImmutableArray(),
            ProvidedTactics = x.ProvidedTactics.Order().ToImmutableArray(),
        }).ToImmutableArray(),
        Settlements = AllSettlements.Values.OrderBy(x => x.Id).ToImmutableArray(),
        Officers = AllOfficers.Values.OrderBy(x => x.Id).ToImmutableArray(),
        Units = Units.Values.OrderBy(x => x.Id).ToImmutableArray(),
        Corps = AllCorps.Values.OrderBy(x => x.Id).ToImmutableArray(),
        Treasures = AllTreasures.Values.OrderBy(x => x.Id).ToImmutableArray(),
        Relations = AllRelations.Values.OrderBy(x => x.FirstId).ThenBy(x => x.SecondId).ToImmutableArray(),
        Revision = _expectedRevision,
    };
    internal SimulationState Simulation { get; private set; }
    internal ImmutableDictionary<int, ForceState> AllForces { get; private set; }
    internal ImmutableDictionary<int, SettlementState> AllSettlements { get; private set; }
    internal ImmutableDictionary<int, OfficerState> AllOfficers { get; private set; }
    internal ImmutableDictionary<int, UnitState> Units { get; private set; }
    internal ImmutableDictionary<int, CorpsState> AllCorps { get; private set; }
    internal ImmutableDictionary<int, TreasureState> AllTreasures { get; private set; }
    internal ImmutableDictionary<(int FirstId, int SecondId), ForceRelation> AllRelations { get; private set; }

    // Complete registries own state. These derived indices expose only participating objects to rules.
    // Inherited from the committed world; both activation and ordinary changes update only their own index.
    internal ActiveWorldRecords ActiveRecords => new(Forces, Settlements, Officers);
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

    private void InvalidateParticipationQueries()
    {
        _activeCorps = null; _activeTreasures = null; _activeRelations = null;
        _engagements = null; _movementOccupancy = null;
        _unitMembers = null; _settlementMembers = null;
        _forcePersonnel = null;
        _holdings = null; _treasureEffects.Clear();
    }

    internal OfficerAbilities Abilities(OfficerState person) => _abilityRules.EvaluateWithTreasureBonus(person, TreasureEffects(person.Id, _abilityRules.Treasures).Bonuses);
    internal ImmutableDictionary<int, OfficerAbilities> AbilitiesFor(IEnumerable<OfficerState> people) =>
        people.ToImmutableDictionary(p => p.Id, Abilities);
    private readonly OfficerAbilityRules _abilityRules;
    private readonly EconomyRulebook _economy;

    internal void SetClock(GameClock clock)
    {
        EnsureOpen();
        if (Clock != clock) CurrentDateCommandsConsumed = false;
        Clock = clock;
    }

    internal Guid AllocateTaskId()
    {
        long identity = Simulation.NextTaskIdentity;
        SetSimulation(Simulation with { NextTaskIdentity = checked(identity + 1) });
        return SimulationIdentity.Task(identity);
    }

    internal void SetSimulation(SimulationState simulation)
    {
        EnsureOpen();
        bool courtChanged = simulation.ImperialCourtEnded != Simulation.ImperialCourtEnded ||
            simulation.Tasks != Simulation.Tasks && !_economy.ImperialCourt.SuccessionOrder.IsEmpty;
        int? previousEmperorOwner = courtChanged ? ForceTrust.EmperorOwner(this, _economy) : null;
        if (simulation.Tasks != Simulation.Tasks && !simulation.Tasks.Where(t => t.Journey is not null).Select(t => t.OfficerId)
            .SequenceEqual(Simulation.Tasks.Where(t => t.Journey is not null).Select(t => t.OfficerId))) LeadershipChanged();
        if (simulation.Alliance?.MemberForceIds != Simulation.Alliance?.MemberForceIds || simulation.AttackRequests != Simulation.AttackRequests) _engagements = null;
        if (simulation.Tasks != Simulation.Tasks) { _tasksByOfficer = null; _travelingOfficers = null; }
        Simulation = simulation;
        if (courtChanged)
        {
            int? emperorOwner = ForceTrust.EmperorOwner(this, _economy);
            if (previousEmperorOwner != emperorOwner) RefreshForceTrust(previousEmperorOwner, emperorOwner);
        }
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
        var previous = AllForces[value.Id];
        if (previous.IsEliminated != value.IsEliminated) _engagements = null;
        bool activationChanged = previous.IsDormant != value.IsDormant;
        // Callers may hold a record from before another input changed. Its derived total is not an input.
        if (value.Trust != previous.Trust) value = value with { Trust = previous.Trust };
        AllForces = Replace(AllForces, value.Id, value);
        Forces = value.IsDormant ? Forces.Remove(value.Id) : Forces.SetItem(value.Id, value);
        if (activationChanged) InvalidateParticipationQueries();
        if (activationChanged || previous.IsEliminated != value.IsEliminated ||
            previous.BaseTrust != value.BaseTrust || previous.LeaderId != value.LeaderId) RefreshForceTrust(value.Id);
    }
    internal void Replace(SettlementState value)
    {
        var previous = AllSettlements[value.Id];
        if (previous.OwnerId != value.OwnerId || value.IsDestroyed || value.IsDormant)
            value = value with { CombatTargetUnitId = null, SiegeContributions = null };
        bool courtChanged = !_economy.ImperialCourt.SuccessionOrder.IsEmpty &&
            (previous.OwnerId != value.OwnerId || previous.Kind != value.Kind || previous.IsDormant != value.IsDormant || previous.IsDestroyed != value.IsDestroyed);
        int? previousEmperorOwner = courtChanged ? ForceTrust.EmperorOwner(this, _economy) : null;
        EmperorTransfer.Observe(this, _economy, previous, value);
        if (previous.IsDormant != value.IsDormant || previous.OwnerId != value.OwnerId || previous.CanHostPersons != value.CanHostPersons || previous.IsDestroyed != value.IsDestroyed)
            LeadershipChanged();
        if (_engagements is not null && MilitaryEngagements.SettlementChanged(previous, value)) _engagements = null;
        AllSettlements = Replace(AllSettlements, value.Id, value);
        Settlements = value.IsDormant ? Settlements.Remove(value.Id) : Settlements.SetItem(value.Id, value);
        if (previous.IsDormant != value.IsDormant) InvalidateParticipationQueries();
        if (previous.OwnerId != value.OwnerId && value.Kind == SettlementKind.City && !value.IsDestroyed &&
            value.OwnerId is int owner && Simulation.Controls.Any(c => c.ForceId == owner && c.Kind == ControllerKind.Player))
            SetSimulation(Simulation with { ForceConsolidationPending = true });
        if (!value.IsDormant) _movementOccupancy?.Set(value);
        if ((previous.Kind == SettlementKind.City || value.Kind == SettlementKind.City) &&
            (previous.OwnerId != value.OwnerId || previous.Kind != value.Kind || previous.IsDormant != value.IsDormant ||
             previous.Population != value.Population || previous.DraftPopulation != value.DraftPopulation || previous.PublicOrder != value.PublicOrder))
            RefreshForceTrust(previous.OwnerId, value.OwnerId);
        if (courtChanged)
        {
            int? emperorOwner = ForceTrust.EmperorOwner(this, _economy);
            if (previousEmperorOwner != emperorOwner) RefreshForceTrust(previousEmperorOwner, emperorOwner);
        }
    }
    internal void AddSettlement(SettlementState value)
    {
        EnsureOpen();
        int? previousEmperorOwner = ForceTrust.EmperorOwner(this, _economy);
        AllSettlements = AllSettlements.Add(value.Id, value);
        if (!value.IsDormant) Settlements = Settlements.Add(value.Id, value);
        if (!value.IsDormant) _movementOccupancy?.Set(value);
        _engagements = null;
        if (value.Kind == SettlementKind.City) RefreshForceTrust(value.OwnerId);
        int? emperorOwner = ForceTrust.EmperorOwner(this, _economy);
        if (previousEmperorOwner != emperorOwner) RefreshForceTrust(previousEmperorOwner, emperorOwner);
    }
    internal void Replace(OfficerState value)
    {
        var previous = AllOfficers[value.Id];
        bool courtChanged = _economy.ImperialCourt.SuccessionOrder.Contains(value.Id) &&
            (previous.IsDormant != value.IsDormant || previous.Status != value.Status ||
             previous.SettlementId != value.SettlementId || previous.RegionId != value.RegionId || previous.UnitId != value.UnitId);
        int? previousEmperorOwner = courtChanged ? ForceTrust.EmperorOwner(this, _economy) : null;
        bool sealChanged = previous.IsDormant != value.IsDormant && _economy.ImperialSealId is int seal &&
            AllTreasures.TryGetValue(seal, out var item) && item.HolderId == value.Id;
        int? previousSealOwner = sealChanged ? ForceTrust.SealOwner(this, _economy) : null;
        if (previous.IsDormant != value.IsDormant || previous.OwnerId != value.OwnerId || previous.Status != value.Status || previous.SettlementId != value.SettlementId ||
            previous.UnitId != value.UnitId || previous.OfficeId != value.OfficeId || previous.Injury != value.Injury ||
            previous.BaseAbilities.Leadership != value.BaseAbilities.Leadership) LeadershipChanged();
        AllOfficers = Replace(AllOfficers, value.Id, value);
        UpdateMember(previous, value);
        Officers = value.IsDormant ? Officers.Remove(value.Id) : Officers.SetItem(value.Id, value);
        if (previous.IsDormant != value.IsDormant) InvalidateParticipationQueries();
        if (courtChanged)
        {
            int? emperorOwner = ForceTrust.EmperorOwner(this, _economy);
            if (previousEmperorOwner != emperorOwner) RefreshForceTrust(previousEmperorOwner, emperorOwner);
        }
        if (sealChanged) RefreshForceTrust(previousSealOwner, ForceTrust.SealOwner(this, _economy));
    }
    internal void AssignMilitaryOrder(int unitId, MilitaryOrder order, UnitPolicies policies, WorldPosition? waypoint)
    {
        ReplaceUnit(Units[unitId].WithMilitaryOrder(order, policies, waypoint) with { CommandedThisXun = true },
            assignsNavigation: true);
    }

    internal void Replace(UnitState value) => ReplaceUnit(value, assignsNavigation: false);

    internal UnitState AdvanceNavigation(int unitId, MilitaryNavigation.UnitStep step)
    {
        var moved = MilitaryNavigation.Apply(Units[unitId], step);
        StoreUnit(moved);
        return moved;
    }

    private void ReplaceUnit(UnitState value, bool assignsNavigation)
    {
        StoreUnit(MilitaryUnitTransitions.Apply(Units[value.Id], value, assignsNavigation));
    }

    private void StoreUnit(UnitState value)
    {
        var previous = Units[value.Id];
        if (_engagements is not null && MilitaryEngagements.UnitChanged(previous, value, _engagementRules!)) _engagements = null;
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
        foreach (var place in Settlements.Values.Where(s => s.CombatTargetUnitId == id).ToArray())
            Replace(place with { CombatTargetUnitId = null });
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
        if (!force.IsDormant) Forces = Forces.Add(force.Id, force);
        InvalidateParticipationQueries();
        RefreshForceTrust(force.Id);
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
        bool sealChanged = value.Id == _economy.ImperialSealId &&
            (previous.HolderId != value.HolderId || previous.Discovery != value.Discovery);
        int? previousSealOwner = sealChanged ? ForceTrust.SealOwner(this, _economy) : null;
        if (previous.HolderId != value.HolderId || previous.Discovery != value.Discovery) LeadershipChanged();
        AllTreasures = Replace(AllTreasures, value.Id, value); _activeTreasures = null;
        UpdateHolding(previous, value);
        if (sealChanged) RefreshForceTrust(previousSealOwner, ForceTrust.SealOwner(this, _economy));
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
