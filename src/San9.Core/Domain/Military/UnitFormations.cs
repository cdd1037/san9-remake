using System.Collections.Concurrent;

namespace San9.Core.Domain;

/// <summary>A movement topology and time scale shared by execution and forecasts.</summary>
public sealed record UnitTravel(BattlefieldMap Map, Footprint Shape, TravelMode Mode, int DailyBudget)
{
    public IEnumerable<WorldPosition> Entrances(BattlefieldPlace place) => Map.Entrances(place, Shape);
}

public sealed partial class MilitaryRulebook
{
    private readonly ConcurrentDictionary<(int, int?), UnitTravel> travelProfiles = new();

    public int? NavalFormationForDeployment(int formationId) =>
        Formations[formationId].TravelMode == TravelMode.Land ? BasicNavalFormationId : null;

    public FormationRule Formation(UnitState unit, WorldPosition? position = null) =>
        Formation(unit.FormationId!.Value, unit.NavalFormationId, position ?? unit.Position);

    public FormationRule Formation(int formationId, int? navalFormationId, WorldPosition position) =>
        Formations[navalFormationId is int naval && Map.IsWater(position) ? naval : formationId];

    public uint Capacity(UnitState unit) => unit.NavalFormationId is int naval
        ? Math.Min(Formations[unit.FormationId!.Value].SoldierCapacity, Formations[naval].SoldierCapacity)
        : Formations[unit.FormationId!.Value].SoldierCapacity;

    public BattleRank Rank(UnitState unit, int officerId)
    {
        if (unit.NavalFormationId is null || !Map.IsWater(unit.Position)) return unit.MemberRanks.GetValueOrDefault(officerId, BattleRank.Middle);
        var ranks = Formation(unit).CaptureRanks;
        int slot = Array.IndexOf(unit.MemberRanks.OrderBy(p => p.Value).ThenBy(p => p.Key).Select(p => p.Key).ToArray(), officerId);
        return slot >= 0 && slot < ranks.Length ? ranks[slot] : BattleRank.Middle;
    }

    public UnitTravel Travel(UnitState unit) => Travel(unit.FormationId!.Value, unit.NavalFormationId);

    public UnitTravel Travel(int formationId, int? navalFormationId) => travelProfiles.GetOrAdd((formationId, navalFormationId), key =>
    {
        var land = Formations[key.Item1];
        if (key.Item2 is not int navalId) return new(Map, land.Footprint, land.TravelMode, land.MovementBudget);
        var naval = Formations[navalId];
        // Integer time units preserve fractional progress across shorelines with different speeds.
        int budget = checked((int)TravelBudget(land, naval));
        return new(Map.ForTravel(land, naval, budget),
            new(Math.Max(land.Footprint.Width, naval.Footprint.Width), Math.Max(land.Footprint.Height, naval.Footprint.Height)),
            TravelMode.Land, budget);
    });

    private static long TravelBudget(FormationRule land, FormationRule naval)
    {
        int a = land.MovementBudget, b = naval.MovementBudget;
        while (b != 0) (a, b) = (b, a % b);
        return (long)land.MovementBudget / a * naval.MovementBudget;
    }

    public bool ValidFormationPair(int formationId, int? navalFormationId)
    {
        if (!Formations.TryGetValue(formationId, out var land)) return false;
        if (navalFormationId is null) return true;
        if (land.TravelMode != TravelMode.Land || !Formations.TryGetValue(navalFormationId.Value, out var naval) ||
            naval.TravelMode != TravelMode.Water || land.MovementBudget <= 0 || naval.MovementBudget <= 0) return false;
        long budget = TravelBudget(land, naval);
        if (budget > int.MaxValue) return false;
        long maximum = Math.Max(Map.MaximumStepCost(TravelMode.Land) * (budget / land.MovementBudget),
            Map.MaximumStepCost(TravelMode.Water) * (budget / naval.MovementBudget));
        return budget + maximum - 1 <= int.MaxValue;
    }
}
