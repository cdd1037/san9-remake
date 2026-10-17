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
    IReadOnlyList<MapOccupancy> MovementOccupancy(MilitaryRulebook rules, int owner, UnitPolicies policies, int? movingUnit, int? entry);
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
    private readonly ILookup<int, OfficerState> members;
    private MovementOccupancy? occupancy;
    private MilitaryRulebook? occupancyRules;
    private readonly Dictionary<(int Owner, UnitPolicies Policies, int? MovingUnit, int? Entry), IReadOnlyList<MapOccupancy>> passages = [];

    internal void SetForecastUnit(UnitState unit)
    {
        ((Dictionary<int, UnitState>)Units)[unit.Id] = unit;
        occupancy?.Set(unit);
        // A query excluding the moving subject depends only on the frozen external world.
        foreach (var key in passages.Keys)
            if (key.MovingUnit != unit.Id) passages.Remove(key);
    }

    public IEnumerable<OfficerState> ServingUnitMembers(int unitId) => members[unitId];
    public bool HasTreasureTactic(int officerId, int tacticId, TreasureRulebook rules) => treasures[officerId].Any(t =>
        t.Discovery != TreasureDiscovery.Disabled && t.Discovered && rules.Items[t.Id].TacticId == tacticId);

    public IReadOnlyList<MapOccupancy> MovementOccupancy(MilitaryRulebook rules, int owner, UnitPolicies policies, int? movingUnit, int? entry)
    {
        if (occupancy is null || !ReferenceEquals(occupancyRules, rules))
        {
            occupancy = new(this, rules);
            occupancyRules = rules;
            passages.Clear();
        }
        var key = (owner, policies, movingUnit, entry);
        if (!passages.TryGetValue(key, out var result))
        {
            result = occupancy.Query(this, owner, policies, movingUnit, entry).ToImmutableArray();
            passages.Add(key, result);
        }
        return result;
    }
}
