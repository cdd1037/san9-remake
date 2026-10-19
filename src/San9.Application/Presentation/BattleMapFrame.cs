using System.Collections.Frozen;

namespace San9.Application.Presentation;

/// <summary>Map state at a presentation boundary, independent of report playback and domain publication.</summary>
public sealed class BattleMapFrame
{
    public IReadOnlyDictionary<int, SettlementView> Settlements { get; }
    public IReadOnlySet<int> RemovedUnits { get; }

    public BattleMapFrame(StrategySnapshot before, IEnumerable<BattleMapChangeCue> changes, IEnumerable<BattleValueCue> values)
    {
        var places = before.Settlements.ToDictionary(s => s.Id);
        var checkpoints = new Dictionary<int, int>();
        var removed = new HashSet<int>();
        // Playback time controls eligibility; facts control the order of state changes.
        foreach (var change in changes.OrderBy(c => c.FactIndex))
        {
            if (change.RemovedUnitId is int unit) removed.Add(unit);
            if (change.Settlement is not { } place) continue;
            places[place.Id] = place;
            checkpoints[place.Id] = change.FactIndex;
        }
        foreach (var value in values.Where(v => v.IsSettlement).OrderBy(v => v.FactIndex))
        {
            // A lifecycle result already includes earlier damage; only subsequent facts add deltas.
            if (checkpoints.TryGetValue(value.TargetId, out int checkpoint) && value.FactIndex <= checkpoint) continue;
            if (!places.TryGetValue(value.TargetId, out var place) || place.IsDestroyed) continue;
            places[place.Id] = place with
            {
                Soldiers = (uint)Math.Clamp((long)place.Soldiers + value.Soldiers, 0, uint.MaxValue),
                Morale = Math.Clamp(place.Morale + value.Morale, 0, 100),
                Durability = Math.Clamp(place.Durability + value.Durability, 0, place.MaxDurability)
            };
        }
        Settlements = places.ToFrozenDictionary();
        RemovedUnits = removed.ToFrozenSet();
    }
}
