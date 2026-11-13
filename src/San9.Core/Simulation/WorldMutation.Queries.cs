using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

internal sealed partial class WorldMutation
{
    // Transaction-local derived indices. Authoritative records remain in the world registries.
    private Dictionary<int, ImmutableDictionary<int, OfficerState>>? _unitMembers;
    private Dictionary<int, ImmutableDictionary<int, OfficerState>>? _settlementMembers;
    private ILookup<int, PersistentTask>? _tasksByOfficer;
    private Dictionary<int, (int Serving, int NonRulers)>? _forcePersonnel;
    private Dictionary<int, List<int>>? _ownedOfficers;
    private HashSet<int>? _travelingOfficers;
    private Dictionary<int, ImmutableDictionary<int, TreasureState>>? _holdings;
    private sealed record HeldTreasureEffects(OfficerAbilities Bonuses, bool EscapeMount, ImmutableHashSet<int> Tactics);
    private readonly Dictionary<TreasureRulebook, Dictionary<int, HeldTreasureEffects>> _treasureEffects = [];

    private void EnsureMembers()
    {
        if (_unitMembers is not null) return;
        _unitMembers = [];
        _settlementMembers = [];
        foreach (var person in Officers.Values) SetMember(person);
    }

    private void SetMember(OfficerState person)
    {
        if (person.UnitId is int unit)
            _unitMembers![unit] = _unitMembers.GetValueOrDefault(unit, ImmutableDictionary<int, OfficerState>.Empty).SetItem(person.Id, person);
        if (person.SettlementId is int settlement)
            _settlementMembers![settlement] = _settlementMembers.GetValueOrDefault(settlement, ImmutableDictionary<int, OfficerState>.Empty).SetItem(person.Id, person);
    }

    private void UpdateMember(OfficerState previous, OfficerState current)
    {
        if (previous.OwnerId != current.OwnerId || previous.Status != current.Status || previous.IsDormant != current.IsDormant)
            _forcePersonnel = null;
        if (previous.OwnerId != current.OwnerId || previous.IsDormant != current.IsDormant) _ownedOfficers = null;
        if (_unitMembers is null) return;
        if (previous.UnitId is int unit && _unitMembers.TryGetValue(unit, out var members))
            _unitMembers[unit] = members.Remove(previous.Id);
        if (previous.SettlementId is int settlement && _settlementMembers!.TryGetValue(settlement, out members))
            _settlementMembers[settlement] = members.Remove(previous.Id);
        if (!current.IsDormant) SetMember(current);
    }

    internal IEnumerable<OfficerState> UnitMembers(int unitId)
    {
        EnsureMembers();
        return _unitMembers!.GetValueOrDefault(unitId, ImmutableDictionary<int, OfficerState>.Empty).Values;
    }

    internal IEnumerable<OfficerState> SettlementMembers(int settlementId)
    {
        EnsureMembers();
        return _settlementMembers!.GetValueOrDefault(settlementId, ImmutableDictionary<int, OfficerState>.Empty).Values;
    }

    /// <summary>The active officers of one force, in registry order (corps membership scans read these instead of every
    /// officer). Rebuilt after any change of owner or activation.</summary>
    internal IEnumerable<OfficerState> OwnedOfficers(int forceId)
    {
        if (_ownedOfficers is null)
        {
            _ownedOfficers = [];
            foreach (var person in Officers.Values)
                if (person.OwnerId is int owner)
                    (_ownedOfficers.TryGetValue(owner, out var ids) ? ids : _ownedOfficers[owner] = []).Add(person.Id);
        }
        return _ownedOfficers.TryGetValue(forceId, out var list) ? list.Select(id => Officers[id]) : [];
    }

    internal IEnumerable<OfficerState> ServingUnitMembers(int unitId) => UnitMembers(unitId).Where(p => p.IsServing);
    internal IEnumerable<OfficerState> ServingSettlementMembers(int settlementId) =>
        SettlementMembers(settlementId).Where(p => p.IsServing && !IsTraveling(p.Id));

    // Qualification queries repeatedly ask whether a force has personnel to target. Only ownership,
    // status and activation affect these counts; attributes, action flags and location do not.
    internal (int Serving, int NonRulers) ForcePersonnel(int forceId)
    {
        if (_forcePersonnel is null)
        {
            _forcePersonnel = [];
            foreach (var person in Officers.Values)
                if (person.IsServing && person.OwnerId is int owner)
                {
                    var previous = _forcePersonnel.GetValueOrDefault(owner);
                    _forcePersonnel[owner] = (previous.Serving + 1, previous.NonRulers + (person.Status == OfficerStatus.Ruler ? 0 : 1));
                }
        }
        return _forcePersonnel.GetValueOrDefault(forceId);
    }

    private void EnsureTasks()
    {
        if (_tasksByOfficer is not null) return;
        _tasksByOfficer = Simulation.Tasks.ToLookup(t => t.OfficerId);
        _travelingOfficers = Simulation.Tasks.Where(t => t.Journey is not null).Select(t => t.OfficerId).ToHashSet();
    }

    internal IEnumerable<PersistentTask> TasksFor(int officerId) { EnsureTasks(); return _tasksByOfficer![officerId]; }
    internal bool IsBusy(int officerId) { EnsureTasks(); return _tasksByOfficer!.Contains(officerId); }
    internal bool IsTraveling(int officerId) { EnsureTasks(); return _travelingOfficers!.Contains(officerId); }

    internal IEnumerable<TreasureState> HeldTreasures(int officerId)
    {
        if (_holdings is null)
        {
            _holdings = [];
            foreach (var item in Treasures.Values)
                if (item.HolderId is int holder)
                    _holdings[holder] = _holdings.GetValueOrDefault(holder, ImmutableDictionary<int, TreasureState>.Empty).Add(item.Id, item);
        }
        return _holdings.GetValueOrDefault(officerId, ImmutableDictionary<int, TreasureState>.Empty).Values;
    }

    private void UpdateHolding(TreasureState previous, TreasureState current)
    {
        bool effectsChanged = previous.HolderId != current.HolderId || previous.Discovery != current.Discovery;
        if (previous.HolderId is int oldHolder)
        {
            if (effectsChanged)
                foreach (var effects in _treasureEffects.Values) effects.Remove(oldHolder);
            if (_holdings is not null && _holdings.TryGetValue(oldHolder, out var items))
                _holdings[oldHolder] = items.Remove(previous.Id);
        }
        if (current.HolderId is int holder)
        {
            if (effectsChanged)
                foreach (var effects in _treasureEffects.Values) effects.Remove(holder);
            if (_holdings is not null && (!AllOfficers.TryGetValue(holder, out var person) || !person.IsDormant))
                _holdings[holder] = _holdings.GetValueOrDefault(holder, ImmutableDictionary<int, TreasureState>.Empty).SetItem(current.Id, current);
        }
    }

    private HeldTreasureEffects TreasureEffects(int officerId, TreasureRulebook rules)
    {
        if (_owner.EquipmentInitialized)
        {
            var items = AllEquipment.Values.Where(item => item.Equipped && item.OfficerId == officerId)
                .Select(item => _equipmentRules.Items[item.DefinitionId]).ToArray();
            return new(default, items.Any(item => item.EscapeMount),
                items.Where(item => item.TacticId is not null).Select(item => item.TacticId!.Value).ToImmutableHashSet());
        }
        if (!_treasureEffects.TryGetValue(rules, out var effects))
        {
            effects = [];
            _treasureEffects.Add(rules, effects);
        }
        if (!effects.TryGetValue(officerId, out var result))
        {
            OfficerAbilities bonuses = default;
            bool mount = false;
            var tactics = ImmutableHashSet.CreateBuilder<int>();
            foreach (var item in HeldTreasures(officerId))
            {
                if (item.Discovery == TreasureDiscovery.Disabled) continue;
                var rule = rules.Items[item.Id];
                var value = rule.AbilityBonuses;
                bonuses = new(Math.Max(bonuses.War, value.War), Math.Max(bonuses.Intelligence, value.Intelligence),
                    Math.Max(bonuses.Politics, value.Politics), Math.Max(bonuses.Leadership, value.Leadership));
                mount |= rule.EscapeMount;
                if (item.Discovered && rule.TacticId is int tactic) tactics.Add(tactic);
            }
            result = new(bonuses, mount, tactics.ToImmutable());
            effects.Add(officerId, result);
        }
        return result;
    }

    internal bool HasEscapeMount(int officerId, TreasureRulebook rules) => TreasureEffects(officerId, rules).EscapeMount;
    internal bool HasTreasureTactic(int officerId, int tacticId, TreasureRulebook rules) => TreasureEffects(officerId, rules).Tactics.Contains(tacticId);
}
