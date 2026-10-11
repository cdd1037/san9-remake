using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

public sealed record DomesticSettlementObservation(int Id, int AccountId, uint Money, uint Food,
    uint Soldiers, int Morale, int PublicOrder, int Durability, int MaxDurability, ImmutableArray<int> OfficerIds)
{
    public uint DraftPopulation { get; init; }
    public uint SoldierCapacity { get; init; }
    public uint Wounded { get; init; }
    public bool ConsumesFood { get; init; } = true;
}

/// <summary>Owned economic inputs only. Task payloads, foreign state and resolution randomness never cross this boundary.</summary>
public sealed record DomesticObservation(AiObservation Perspective, ImmutableArray<DomesticSettlementObservation> Settlements)
{
    public ImmutableDictionary<int, OfficerAbilities> Abilities { get; init; } = ImmutableDictionary<int, OfficerAbilities>.Empty;
    public static DomesticObservation Capture(WorldSnapshot world, ControlSeat seat, CommandContext context)
    {
        var perspective = AiObservation.FromSnapshot(seat.ForceId, world);
        if (!world.Forces.Any(f => f.Id == seat.ForceId && !f.IsDormant && !f.IsEliminated)) return new(perspective, []);
        var busy = world.Simulation.Tasks.Select(t => t.OfficerId).ToHashSet();
        var people = world.Officers.Where(p => !p.IsDormant && p.IsServing && !p.Acted && p.OwnerId == seat.ForceId &&
            p.SettlementId is not null && !busy.Contains(p.Id)).ToLookup(p => p.SettlementId!.Value);
        var accounts = world.Corps.Where(c => c.OwnerId == seat.ForceId).ToDictionary(c => c.Id);
        var root = accounts.Values.Single(c => c.IsRoot);
        var places = world.Settlements.Where(s => !s.IsDormant && !s.IsDestroyed &&
            CorpsControl.CanControlSettlement(world, seat, context, s.Id)).OrderBy(s => s.Id);
        return new(perspective, places.Select(s =>
        {
            var account = s.CorpsId is int id ? accounts[id] : root;
            return new DomesticSettlementObservation(s.Id, account.Id, account.Treasury.Money, account.Treasury.Food,
                s.Soldiers, s.Morale, s.PublicOrder, s.Durability, s.MaxDurability,
                people[s.Id].OrderBy(p => p.Id).Select(p => p.Id).ToImmutableArray())
                { DraftPopulation = s.DraftPopulation, SoldierCapacity = s.SoldierCapacity, Wounded = s.Wounded, ConsumesFood = s.ConsumesFood };
        }).ToImmutableArray());
    }
}
