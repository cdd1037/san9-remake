using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Read capabilities shared by live transactions and frozen military queries.</summary>
internal interface IWorldQuery
{
    GameClock Clock { get; }
    SimulationState Simulation { get; }
    IReadOnlyDictionary<int, ForceState> Forces { get; }
    IReadOnlyDictionary<int, SettlementState> Settlements { get; }
    IReadOnlyDictionary<int, OfficerState> Officers { get; }
    IReadOnlyDictionary<int, UnitState> Units { get; }
    IReadOnlyDictionary<(int FirstId, int SecondId), ForceRelation> Relations { get; }
    IEnumerable<OfficerState> ServingUnitMembers(int unitId);
    bool HasTreasureTactic(int officerId, int tacticId, TreasureRulebook rules);
    bool BlocksWaterContact(MilitaryRulebook rules, WorldPosition position, int? target);
    IReadOnlyList<MapOccupancy> MovementOccupancy(MilitaryRulebook rules, int owner, UnitPolicies policies, int? movingUnit, int? entry, UnitState? mover = null);
}

/// <summary>Indexes immutable snapshot records without restoring an executable world.</summary>
internal sealed class WorldQuery : IWorldQuery
{
    internal WorldQuery(WorldSnapshot snapshot)
    {
        Clock = snapshot.Clock;
        Simulation = snapshot.Simulation;
        Forces = snapshot.Forces.Where(f => !f.IsDormant).ToDictionary(f => f.Id);
        Settlements = snapshot.Settlements.Where(s => !s.IsDormant).ToDictionary(s => s.Id);
        Officers = snapshot.Officers.Where(p => !p.IsDormant).ToDictionary(p => p.Id);
        Units = snapshot.Units.ToDictionary(u => u.Id);
        Relations = snapshot.Relations.Where(r => Forces.ContainsKey(r.FirstId) && Forces.ContainsKey(r.SecondId))
            .ToDictionary(r => (r.FirstId, r.SecondId));
        treasures = snapshot.Treasures.Where(t => t.HolderId is int id && Officers.ContainsKey(id)).ToLookup(t => t.HolderId!.Value);
        equipment = snapshot.EquipmentInitialized ? snapshot.Equipment.Where(e => e.Equipped && e.OfficerId is int id && Officers.ContainsKey(id))
            .ToLookup(e => e.OfficerId!.Value) : null;
        members = Officers.Values.Where(p => p.IsServing && p.UnitId is not null).ToLookup(p => p.UnitId!.Value);
    }

    public GameClock Clock { get; }
    public SimulationState Simulation { get; }
    public IReadOnlyDictionary<int, ForceState> Forces { get; }
    public IReadOnlyDictionary<int, SettlementState> Settlements { get; }
    public IReadOnlyDictionary<int, OfficerState> Officers { get; }
    public IReadOnlyDictionary<int, UnitState> Units { get; }
    public IReadOnlyDictionary<(int FirstId, int SecondId), ForceRelation> Relations { get; }
    private readonly ILookup<int, TreasureState> treasures;
    private readonly ILookup<int, EquipmentInstance>? equipment;
    private readonly ILookup<int, OfficerState> members;
    private MovementOccupancy? occupancy;
    private readonly Dictionary<(int Owner, UnitPolicies Policies, int? MovingUnit, int? Entry, MilitaryOrder? Order, MilitaryOrder? EffectiveOrder), IReadOnlyList<MapOccupancy>> passages = [];

    internal void SetForecastUnit(UnitState unit)
    {
        ((Dictionary<int, UnitState>)Units)[unit.Id] = unit;
        occupancy?.Set(unit);
        // A query excluding the moving subject depends only on the frozen external world.
        foreach (var key in passages.Keys)
            if (key.MovingUnit != unit.Id) passages.Remove(key);
    }

    public IEnumerable<OfficerState> ServingUnitMembers(int unitId) => members[unitId];
    public bool HasTreasureTactic(int officerId, int tacticId, TreasureRulebook rules) => equipment is not null
        ? equipment[officerId].Any(item => rules.Equipment.Items[item.DefinitionId].TacticId == tacticId)
        : treasures[officerId].Any(t => t.Discovery != TreasureDiscovery.Disabled && t.Discovered && rules.Items[t.Id].TacticId == tacticId);

    public bool BlocksWaterContact(MilitaryRulebook rules, WorldPosition position, int? target) =>
        OccupancyFor(rules).BlocksContact(position, target);

    private MovementOccupancy OccupancyFor(MilitaryRulebook rules)
    {
        if (occupancy is null || !ReferenceEquals(occupancy.Rules, rules))
        {
            occupancy = new(this, rules);
            passages.Clear();
        }
        return occupancy;
    }

    public IReadOnlyList<MapOccupancy> MovementOccupancy(MilitaryRulebook rules, int owner, UnitPolicies policies, int? movingUnit, int? entry, UnitState? mover = null)
    {
        var current = OccupancyFor(rules);
        mover ??= movingUnit is int id ? Units.GetValueOrDefault(id) : null;
        var key = (owner, policies, movingUnit, entry, mover?.Order, mover?.EffectiveOrder);
        if (!passages.TryGetValue(key, out var result))
        {
            result = current.Query(this, owner, policies, movingUnit, entry, mover).ToImmutableArray();
            passages.Add(key, result);
        }
        return result;
    }
}
