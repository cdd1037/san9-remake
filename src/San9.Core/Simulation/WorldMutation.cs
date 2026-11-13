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
    IReadOnlyList<MapOccupancy> IWorldQuery.MovementOccupancy(MilitaryRulebook rules, int owner, UnitPolicies policies, int? movingUnit, int? entry, UnitState? mover) =>
        MovementOccupancy(rules, owner, policies, movingUnit, entry, mover);

    bool IWorldQuery.BlocksWaterContact(MilitaryRulebook rules, WorldPosition position, int? target) =>
        OccupancyFor(rules).BlocksContact(position, target);

    private readonly WorldState _owner;
    private readonly long _expectedRevision;
    private bool _committed;
    // Relative to Clock, rather than to the enclosing AdvanceDay operation.
    // The countdown pass precedes combat and task execution; new journeys then start counting next date.
    internal bool CurrentDateCountdownConsumed { get; set; }
    private Dictionary<int, int?>? _leadershipBefore;
    private EngagementView? _engagements;
    internal EngagementView? EngagementCache => _engagements;
    private MovementOccupancy? _movementOccupancy;
    internal IReadOnlyList<MapOccupancy> MovementOccupancy(MilitaryRulebook rules, int owner, UnitPolicies policies, int? movingUnit, int? entry, UnitState? mover = null) =>
        OccupancyFor(rules).Query(this, owner, policies, movingUnit, entry, mover);

    private MovementOccupancy OccupancyFor(MilitaryRulebook rules)
    {
        if (_movementOccupancy is null || !ReferenceEquals(_movementOccupancy.Rules, rules))
            _movementOccupancy = new(this, rules);
        return _movementOccupancy;
    }

    internal EngagementRelations Engagements(MilitaryRulebook military)
    {
        if (_engagements is null || !ReferenceEquals(_engagements.Rules, military))
            _engagements = new(military, MilitaryEngagements.Build(this, military));
        return _engagements.Relations;
    }

    // Leadership is consistent when every place's commander flag and every unit's commander follow from the current
    // selection inputs. Then a change only needs its own places reselected: the places named at each change, their
    // commanders before it and the officers it touched are recorded, and maintenance reselects those alone. A world
    // whose consistency is not known (a new world, a load, a commit with pending changes) reselects everything once.
    private bool _leadershipConsistent;
    private HashSet<int>? _leadershipOfficers;
    internal bool LeadershipConsistentAtCommit => _leadershipConsistent && _leadershipBefore is null;

    // Certified by an unchanged governance pass; only membership, survival and service inputs invalidate it.
    // New/load entry is uncertified. Commits and internal forks carry the fact, never the save format.
    internal bool GovernanceConsistent { get; private set; }
    internal bool BeginGovernanceMaintenance()
    {
        if (GovernanceConsistent) return false;
        GovernanceConsistent = true;
        return true;
    }

    private void LeadershipChanged(int? firstPlace = null, int? secondPlace = null, int? firstOfficer = null, int? secondOfficer = null)
    {
        if (!_leadershipConsistent)
        {
            _leadershipBefore ??= Officers.Values.Where(p => p.IsCommander && p.IsServing && p.SettlementId is not null)
                .GroupBy(p => p.SettlementId!.Value).ToDictionary(g => g.Key, g => (int?)g.First().Id);
            return;
        }
        _leadershipBefore ??= [];
        _leadershipOfficers ??= [];
        void Place(int? place)
        {
            if (place is not int id || _leadershipBefore.ContainsKey(id)) return;
            // the commander on record before this change (the same reading as the full snapshot above)
            _leadershipBefore[id] = SettlementMembers(id).FirstOrDefault(p => p.IsCommander && p.IsServing)?.Id;
        }
        void Officer(int? officer)
        {
            if (officer is not int id || !_leadershipOfficers.Add(id)) return;
            if (AllOfficers.TryGetValue(id, out var person))
            {
                Place(person.SettlementId);
            }
        }
        Place(firstPlace); Place(secondPlace);
        Officer(firstOfficer); Officer(secondOfficer);
    }

    internal void MaintainLeadership(List<DomainEvent> events)
    {
        if (_leadershipBefore is not { } before) return;
        var units = Units.Values.Where(u => u.CommanderId is not null).ToDictionary(u => u.Id);
        IEnumerable<int> places = before.Keys;
        if (_leadershipConsistent)
        {
            LeadershipMaintenance.Apply(this, _abilityRules.Governance, places, _leadershipOfficers!);
        }
        else
        {
            LeadershipMaintenance.Apply(this, _abilityRules.Governance);
            places = places.Concat(Officers.Values.Where(p => p.IsCommander && p.SettlementId is not null)
                .Select(p => p.SettlementId!.Value)).Distinct();
        }
        foreach (int place in places.Order())
        {
            int? previous = before.GetValueOrDefault(place);
            int? current = LeadershipMaintenance.Commander(this, place)?.Id;
            if (previous != current)
                events.Add(new CommanderChangedEvent(Clock, place, current)
                { PreviousId = previous, ForceId = Settlements.GetValueOrDefault(place)?.OwnerId });
        }
        foreach (var unit in Units.Values.Where(u => u.CommanderId is not null))
            if (units.TryGetValue(unit.Id, out var previous) && previous.CommanderId != unit.CommanderId && previous.OwnerId == unit.OwnerId)
                events.Add(new UnitCommanderChangedEvent(Clock, BattleObjectContext.Capture(unit), previous.CommanderId!.Value, unit.CommanderId!.Value));
        _leadershipBefore = null;
        _leadershipOfficers = null;
        _leadershipConsistent = true;
    }

    internal WorldMutation(
        WorldState owner,
        OfficerAbilityRules abilityRules,
        EconomyRulebook economy,
        EquipmentRulebook equipmentRules,
        long expectedRevision,
        GameClock clock,
        SimulationState simulation,
        ImmutableDictionary<int, ForceState> forces,
        ImmutableDictionary<int, SettlementState> settlements,
        ImmutableDictionary<int, OfficerState> officers,
        ImmutableDictionary<int, UnitState> units,
        ImmutableArray<int> unitOrder,
        ImmutableDictionary<int, CorpsState> corps,
        ImmutableDictionary<int, TreasureState> treasures,
        ImmutableDictionary<long, EquipmentInstance> equipment,
        ImmutableDictionary<(int FirstId, int SecondId), ForceRelation> relations,
        ActiveWorldRecords activeRecords, CompanionSettings companion)
    {
        _owner = owner;
        Companion = companion;
        _leadershipConsistent = owner.LeadershipConsistent;
        GovernanceConsistent = owner.GovernanceConsistent;
        _engagements = owner.EngagementCache;
        _abilityRules = abilityRules;
        OfficerGrowth = abilityRules.Growth.Resolve(companion);
        _economy = economy;
        _equipmentRules = equipmentRules;
        _expectedRevision = expectedRevision;
        Clock = clock;
        Simulation = simulation;
        AllForces = forces;
        AllSettlements = settlements;
        AllOfficers = officers;
        Units = units;
        UnitOrder = unitOrder;
        AllCorps = corps;
        AllTreasures = treasures;
        AllEquipment = equipment;
        AllRelations = relations;
        Forces = activeRecords.Forces;
        Settlements = activeRecords.Settlements;
        Officers = activeRecords.Officers;
    }

    /// <summary>Immutable rules for this transaction, never part of world snapshots or persistence.</summary>
    internal CompanionSettings Companion { get; }
    public GameClock Clock { get; private set; }
    // Ephemeral presentation samples; these are not stable save boundaries.
    internal WorldSnapshot CapturePresentation() => new(Clock, Simulation)
    {
        Forces = AllForces.Values.OrderBy(x => x.Id).Select(x => x with
        {
            FormationGrants = x.FormationGrants.OrderBy(g => g.FormationId).ToImmutableArray(),
            ProvidedTactics = x.ProvidedTactics.Order().ToImmutableArray(),
        }).ToImmutableArray(),
        Settlements = AllSettlements.Values.OrderBy(x => x.Id).ToImmutableArray(),
        Officers = AllOfficers.Values.OrderBy(x => x.Id).ToImmutableArray(),
        Units = OrderedUnits.ToImmutableArray(),
        Corps = AllCorps.Values.OrderBy(x => x.Id).ToImmutableArray(),
        Treasures = AllTreasures.Values.OrderBy(x => x.Id).ToImmutableArray(),
        Equipment = AllEquipment.Values.OrderBy(x => x.Id).ToImmutableArray(),
        EquipmentInitialized = _owner.EquipmentInitialized,
        Relations = AllRelations.Values.OrderBy(x => x.FirstId).ThenBy(x => x.SecondId).ToImmutableArray(),
        DefaultLoyalty = DefaultLoyalty,
        Revision = _expectedRevision,
    };
    internal int DefaultLoyalty => _owner.DefaultLoyalty;
    internal SimulationState Simulation { get; private set; }
    internal ImmutableDictionary<int, ForceState> AllForces { get; private set; }
    internal ImmutableDictionary<int, SettlementState> AllSettlements { get; private set; }
    internal ImmutableDictionary<int, OfficerState> AllOfficers { get; private set; }
    internal ImmutableDictionary<int, UnitState> Units { get; private set; }
    // Declaration/creation order is meaningful when a policy's candidate keys compare equal.
    internal ImmutableArray<int> UnitOrder { get; private set; }
    internal IEnumerable<UnitState> OrderedUnits => UnitOrder.Select(id => Units[id]);
    internal ImmutableDictionary<int, CorpsState> AllCorps { get; private set; }
    internal ImmutableDictionary<int, TreasureState> AllTreasures { get; private set; }
    internal ImmutableDictionary<long, EquipmentInstance> AllEquipment { get; private set; }
    internal bool EquipmentInitialized => _owner.EquipmentInitialized;
    private readonly EquipmentRulebook _equipmentRules;
    internal IEnumerable<EquipmentCombatEffects> EquipmentCombatEffects(int officerId) => AllEquipment.Values
        .Where(item => item.Equipped && item.OfficerId == officerId)
        .Select(item => _equipmentRules.Items[item.DefinitionId].CombatEffects)
        .OfType<EquipmentCombatEffects>();
    private ImmutableDictionary<int, OfficerAbilities>? _equipmentBonuses;
    private string? _equipmentExchange;
    internal void BeginEquipmentExchange(BattleSource source, BattleSource target) =>
        _equipmentExchange = $"{Clock.DayNumber}:{(source.IsSettlement ? 's' : 'u')}{source.Id}:{(target.IsSettlement ? 's' : 'u')}{target.Id}";

    internal void RecordEquipmentUse(BattleSource source, EquipmentUse use)
    {
        if (!_owner.EquipmentInitialized || _equipmentExchange is null || !source.IsPresent(this)) return;
        var owner = source.IsSettlement ? Settlements[source.Id].OwnerId : Units[source.Id].OwnerId;
        var people = source.IsSettlement ? ServingSettlementMembers(source.Id) : ServingUnitMembers(source.Id);
        var ids = people.Where(person => person.OwnerId == owner && !IsTraveling(person.Id) &&
            (source.IsSettlement || Units[source.Id].Detachments.IsEmpty ||
             Units[source.Id].Detachments.TryGetValue(person.Id, out var detachment) && detachment.Soldiers > 0))
            .Select(person => person.Id).ToHashSet();
        RecordEquipmentUse(ids, use);
    }

    internal void RecordEquipmentUse(IEnumerable<int> officers, EquipmentUse use)
    {
        if (!_owner.EquipmentInitialized || _equipmentExchange is null || Simulation.NewGame.EquipmentDevelopmentEnabled == false) return;
        var ids = officers.ToHashSet();
        string dayPrefix = $"{Clock.DayNumber}:";
        foreach (var item in AllEquipment.Values.Where(item => item.Equipped && item.OfficerId is int id && ids.Contains(id) &&
                     item.LastExperienceExchange != _equipmentExchange && item.ExperienceExchanges?.Contains(_equipmentExchange) != true)
                     .OrderBy(item => item.Id).ToArray())
        {
            var updated = EquipmentRules.RecordUse(item, _equipmentRules.Items[item.DefinitionId], use);
            if (updated != item)
            {
                var exchanges = (item.ExperienceExchanges ?? ImmutableSortedSet<string>.Empty)
                    .Where(value => value.StartsWith(dayPrefix, StringComparison.Ordinal)).ToImmutableSortedSet(StringComparer.Ordinal);
                if (item.LastExperienceExchange?.StartsWith(dayPrefix, StringComparison.Ordinal) == true)
                    exchanges = exchanges.Add(item.LastExperienceExchange);
                SetEquipment(updated with { LastExperienceExchange = _equipmentExchange, ExperienceExchanges = exchanges.Add(_equipmentExchange) });
            }
        }
    }
    internal void SetEquipment(EquipmentInstance item)
    {
        ArgumentNullException.ThrowIfNull(item);
        EnsureOpen();
        var previous = AllEquipment.GetValueOrDefault(item.Id);
        if (previous == item) return;
        bool sealChanged = _economy.ImperialSealId is int seal &&
            (previous?.LegacyTreasureId == seal || item.LegacyTreasureId == seal) &&
            (previous?.Equipped != item.Equipped || previous?.OfficerId != item.OfficerId);
        int? previousSealOwner = sealChanged ? ForceTrust.SealOwner(this, _economy) : null;
        _equipmentBonuses = null;
        foreach (var effects in _treasureEffects.Values)
        {
            if (previous?.OfficerId is int oldHolder) effects.Remove(oldHolder);
            if (item.OfficerId is int holder) effects.Remove(holder);
        }
        LeadershipChanged(firstOfficer: previous?.OfficerId, secondOfficer: item.OfficerId);
        AllEquipment = AllEquipment.SetItem(item.Id, item);
        if (sealChanged) RefreshForceTrust(previousSealOwner, ForceTrust.SealOwner(this, _economy));
    }
    internal void ConvertPendingArmament(EquipmentInstance pending, int forceId)
    {
        if (pending.PendingArmamentCredit is not int credit || !AllEquipment.ContainsKey(pending.Id) ||
            !AllForces.TryGetValue(forceId, out var owner) || owner.IsEliminated)
            throw new InvalidOperationException("Invalid pending armament conversion.");
        Replace(owner with { ArmamentMigrationCredit = checked((owner.ArmamentMigrationCredit ?? 0) + credit) });
        AllEquipment = AllEquipment.Remove(pending.Id);
        _equipmentBonuses = null;
        if (pending.OfficerId is int officer)
            foreach (var effects in _treasureEffects.Values) effects.Remove(officer);
    }
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
        _forcePersonnel = null; _ownedOfficers = null;
        _holdings = null; _treasureEffects.Clear();
    }

    internal OfficerGrowthRules OfficerGrowth { get; }
    internal OfficerAbilities Abilities(OfficerState person) => _abilityRules.EvaluateWithTreasureBonus(person, _owner.EquipmentInitialized
        ? (_equipmentBonuses ??= EquipmentRules.BonusesByOfficer(AllEquipment.Values, _equipmentRules.Items)).GetValueOrDefault(person.Id)
        : TreasureEffects(person.Id, _abilityRules.Treasures).Bonuses);
    internal ImmutableDictionary<int, OfficerAbilities> AbilitiesFor(IEnumerable<OfficerState> people) =>
        people.ToImmutableDictionary(p => p.Id, Abilities);
    private readonly OfficerAbilityRules _abilityRules;
    private readonly EconomyRulebook _economy;

    internal void SetClock(GameClock clock)
    {
        EnsureOpen();
        if (Clock != clock) CurrentDateCountdownConsumed = false;
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
            .SequenceEqual(Simulation.Tasks.Where(t => t.Journey is not null).Select(t => t.OfficerId)))
        {
            if (!_leadershipConsistent) LeadershipChanged();
            else
            {
                var travelling = simulation.Tasks.Where(t => t.Journey is not null).Select(t => t.OfficerId).ToHashSet();
                travelling.SymmetricExceptWith(Simulation.Tasks.Where(t => t.Journey is not null).Select(t => t.OfficerId));
                foreach (int officer in travelling.Order()) LeadershipChanged(firstOfficer: officer);
                if (travelling.Count == 0) LeadershipChanged();   // the same travellers in another order: nothing to reselect
            }
        }
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
        if (previous.IsDormant != value.IsDormant || previous.IsEliminated != value.IsEliminated || previous.LeaderId != value.LeaderId)
            GovernanceConsistent = false;
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
        if (previous.OwnerId != value.OwnerId || previous.CorpsId != value.CorpsId || previous.Kind != value.Kind ||
            previous.IsDormant != value.IsDormant || previous.IsDestroyed != value.IsDestroyed ||
            previous.CanHostPersons != value.CanHostPersons || (previous.Durability > 0) != (value.Durability > 0) || previous.Facility != value.Facility)
            GovernanceConsistent = false;
        if (previous.OwnerId != value.OwnerId || value.IsDestroyed || value.IsDormant)
            value = value with { CombatTargetUnitId = null, SiegeContributions = null };
        bool courtChanged = !_economy.ImperialCourt.SuccessionOrder.IsEmpty &&
            (previous.OwnerId != value.OwnerId || previous.Kind != value.Kind || previous.IsDormant != value.IsDormant || previous.IsDestroyed != value.IsDestroyed);
        int? previousEmperorOwner = courtChanged ? ForceTrust.EmperorOwner(this, _economy) : null;
        EmperorTransfer.Observe(this, _economy, previous, value);
        if (previous.IsDormant != value.IsDormant || previous.OwnerId != value.OwnerId || previous.CanHostPersons != value.CanHostPersons || previous.IsDestroyed != value.IsDestroyed)
            LeadershipChanged(value.Id);
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
        GovernanceConsistent = false;
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
        value = value.WithLoyaltyDefault(DefaultLoyalty);
        var previous = AllOfficers[value.Id];
        if (previous.IsServing != value.IsServing || (previous.IsServing || value.IsServing) &&
            (previous.IsDormant != value.IsDormant || previous.OwnerId != value.OwnerId ||
             previous.SettlementId != value.SettlementId || previous.UnitId != value.UnitId))
            GovernanceConsistent = false;
        bool courtChanged = _economy.ImperialCourt.SuccessionOrder.Contains(value.Id) &&
            (previous.IsDormant != value.IsDormant || previous.Status != value.Status ||
             previous.SettlementId != value.SettlementId || previous.RegionId != value.RegionId || previous.UnitId != value.UnitId);
        int? previousEmperorOwner = courtChanged ? ForceTrust.EmperorOwner(this, _economy) : null;
        bool sealChanged = previous.IsDormant != value.IsDormant && _economy.ImperialSealId is int seal &&
            AllTreasures.TryGetValue(seal, out var item) && item.HolderId == value.Id;
        int? previousSealOwner = sealChanged ? ForceTrust.SealOwner(this, _economy) : null;
        if (previous.IsDormant != value.IsDormant || previous.OwnerId != value.OwnerId || previous.Status != value.Status || previous.SettlementId != value.SettlementId ||
            previous.UnitId != value.UnitId || previous.OfficeId != value.OfficeId || previous.Injury != value.Injury ||
            previous.BaseAbilities.Leadership != value.BaseAbilities.Leadership) LeadershipChanged(previous.SettlementId, value.SettlementId, value.Id);
        AllOfficers = Replace(AllOfficers, value.Id, value);
        UpdateMember(previous, value);
        if (previous.UnitId is int oldUnitId && (value.UnitId != oldUnitId || !value.IsServing) &&
            Units.TryGetValue(oldUnitId, out var oldUnit) && oldUnit.Detachments.ContainsKey(value.Id))
        {
            var oldForce = AllForces[oldUnit.OwnerId];
            var returned = oldForce.ArmamentInventory ?? ImmutableDictionary<string, uint>.Empty;
            var reserve = oldForce.ArmamentReserve ?? ImmutableDictionary<string, ulong>.Empty;
            foreach (var issue in new[] { oldUnit.Detachments[value.Id].Weapon, oldUnit.Detachments[value.Id].Armor })
                if (issue is not null) (returned, reserve) = ArmamentInventory.ReturnWithReserve(returned, reserve, issue);
            if (oldUnit.Detachments[value.Id].Weapon is not null || oldUnit.Detachments[value.Id].Armor is not null)
                Replace(oldForce with { ArmamentInventory = returned.IsEmpty ? null : returned, ArmamentReserve = reserve.IsEmpty ? null : reserve });
            var remaining = oldUnit.Detachments.Remove(value.Id);
            Replace(oldUnit with { Detachments = DetachmentAllocation.Resize(remaining, oldUnit.Soldiers) });
        }
        Officers = value.IsDormant ? Officers.Remove(value.Id) : Officers.SetItem(value.Id, value);
        if (value.IsServing && value.OwnerId is int servingForce)
        {
            foreach (var pending in AllEquipment.Values.Where(item => item.OfficerId == value.Id && item.PendingArmamentCredit is not null)
                         .OrderBy(item => item.Id).ToArray())
            {
                ConvertPendingArmament(pending, servingForce);
            }
        }
        if (previous.IsServing && !value.IsServing)
            foreach (var equipment in AllEquipment.Values.Where(item => item.OfficerId == value.Id && item.Equipped).ToArray())
                SetEquipment(equipment with { Equipped = false });
        if (!previous.IsServing && value.IsServing && EquipmentInitialized)
        {
            foreach (var issued in AllEquipment.Values.Where(item => item.OfficerId == value.Id && item.EquipOnService == true)
                         .OrderBy(item => item.Id).ToArray())
            {
                var pendingItem = issued with { EquipOnService = null };
                SetEquipment(pendingItem);
                var definition = _equipmentRules.Items[pendingItem.DefinitionId];
                if (!EquipmentRules.MeetsRequirements(definition, value.BaseAbilities) ||
                    AllEquipment.Values.Any(e => e.OfficerId == value.Id && e.Equipped && _equipmentRules.Items[e.DefinitionId].Slot == definition.Slot)) continue;
                var equipped = EquipmentRules.Equip(AllEquipment.Values, _equipmentRules.Items, value.Id, pendingItem.Id);
                if (equipped is null) continue;
                foreach (var changed in equipped.Value)
                    if (changed != AllEquipment[changed.Id]) SetEquipment(changed);
            }
        }
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
        if (!value.Detachments.IsEmpty && previous.Soldiers != value.Soldiers &&
            ReferenceEquals(previous.Detachments, value.Detachments))
            value = value with { Detachments = value.Soldiers < previous.Soldiers
                ? DetachmentAllocation.ResizeForCombatLoss(value.Detachments, value.Soldiers)
                : DetachmentAllocation.Resize(value.Detachments, value.Soldiers) };
        if (previous.OwnerId != value.OwnerId || previous.CorpsId != value.CorpsId || previous.ServicePlaceId != value.ServicePlaceId ||
            previous.IsEmbedded != value.IsEmbedded || TransportDestination(previous) != TransportDestination(value))
            GovernanceConsistent = false;
        if (_engagements is not null && MilitaryEngagements.UnitChanged(previous, value, _engagements.Rules)) _engagements = null;
        Units = Replace(Units, value.Id, value);
        _movementOccupancy?.Set(value);
    }
    private static int? TransportDestination(UnitState unit) =>
        unit.EffectiveOrder is { Kind: MilitaryOrderKind.Transport } order ? order.SettlementId : null;

    internal void AddUnit(UnitState unit)
    {
        EnsureOpen();
        GovernanceConsistent = false;
        Units = Units.Add(unit.Id, unit);
        UnitOrder = UnitOrder.Add(unit.Id);
        _movementOccupancy?.Set(unit);
        _engagements = null;
    }
    internal void RemoveUnit(int id)
    {
        EnsureOpen();
        if (!Units.ContainsKey(id)) throw new InvalidOperationException("Unknown unit.");
        GovernanceConsistent = false;
        Units = Units.Remove(id);
        UnitOrder = UnitOrder.Remove(id);
        foreach (var place in Settlements.Values.Where(s => s.CombatTargetUnitId == id).ToArray())
            Replace(place with { CombatTargetUnitId = null });
        _movementOccupancy?.RemoveUnit(id);
        _engagements = null;
    }
    internal void Replace(CorpsState value)
    {
        var records = Replace(AllCorps, value.Id, value);
        if (ReferenceEquals(AllCorps, records)) return;
        var previous = AllCorps[value.Id];
        if (previous.OwnerId != value.OwnerId || previous.IsRoot != value.IsRoot || previous.LeaderId != value.LeaderId)
            GovernanceConsistent = false;
        AllCorps = records;
        _activeCorps = null;
    }
    internal void RemoveCorps(int id)
    {
        EnsureOpen();
        if (!AllCorps.ContainsKey(id)) throw new InvalidOperationException("Unknown corps.");
        GovernanceConsistent = false;
        AllCorps = AllCorps.Remove(id); _activeCorps = null;
    }
    // Retired forces remain in the world, so allocation never reuses a previous identity.
    internal void AddForce(ForceState force)
    {
        EnsureOpen();
        if (force.Origin is null || force.Id != checked(AllForces.Keys.DefaultIfEmpty(-1).Max() + 1))
            throw new ArgumentException("Invalid generated force identity or origin.");
        AllForces = AllForces.Add(force.Id, force);
        GovernanceConsistent = false;
        if (!force.IsDormant) Forces = Forces.Add(force.Id, force);
        InvalidateParticipationQueries();
        RefreshForceTrust(force.Id);
        _engagements = null;
    }

    internal void AddCorps(CorpsState corps)
    {
        EnsureOpen();
        GovernanceConsistent = false;
        AllCorps = AllCorps.Add(corps.Id, corps); _activeCorps = null;
    }
    internal void Replace(TreasureState value)
    {
        var previous = AllTreasures[value.Id];
        bool sealChanged = value.Id == _economy.ImperialSealId &&
            (previous.HolderId != value.HolderId || previous.Discovery != value.Discovery);
        int? previousSealOwner = sealChanged ? ForceTrust.SealOwner(this, _economy) : null;
        if (previous.HolderId != value.HolderId || previous.Discovery != value.Discovery) LeadershipChanged(firstOfficer: previous.HolderId, secondOfficer: value.HolderId);
        AllTreasures = Replace(AllTreasures, value.Id, value); _activeTreasures = null;
        SynchronizeTreasureEquipment(value);
        UpdateHolding(previous, value);
        if (sealChanged) RefreshForceTrust(previousSealOwner, ForceTrust.SealOwner(this, _economy));
    }

    private void SynchronizeTreasureEquipment(TreasureState treasure)
    {
        if (!_owner.EquipmentInitialized) return;
        var definition = _equipmentRules.Items.Values.SingleOrDefault(item => item.LegacyTreasureId == treasure.Id);
        if (definition is null) return;
        var instance = AllEquipment.Values.SingleOrDefault(item => item.LegacyTreasureId == treasure.Id);
        int? holder = treasure.Discovered ? treasure.HolderId : null;
        if (instance is null)
        {
            if (treasure.Discovery == TreasureDiscovery.Disabled) return;
            long id = checked(AllEquipment.Keys.DefaultIfEmpty(-1).Max() + 1);
            SetEquipment(new EquipmentInstance(id, definition.Id, holder, null, false) { LegacyTreasureId = treasure.Id });
            return;
        }
        if (instance.OfficerId != holder || instance.InventoryForceId is not null && (holder is not null || !treasure.Discovered))
            SetEquipment(instance with { OfficerId = holder, InventoryForceId = null, Equipped = false, EquipOnService = null });
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
