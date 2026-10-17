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
    public int Commerce { get; init; }
    public int MaxCommerce { get; init; }
    public int Cultivation { get; init; }
    public int MaxCultivation { get; init; }
    public ImmutableArray<DomesticActivity> UnavailableActivities { get; init; } = [];
}

/// <summary>Owned economic inputs only. Task payloads, foreign state and resolution randomness never cross this boundary.</summary>
public sealed record DomesticObservation(AiObservation Perspective, ImmutableArray<DomesticSettlementObservation> Settlements)
{
    public ImmutableDictionary<int, OfficerAbilities> Abilities { get; init; } = ImmutableDictionary<int, OfficerAbilities>.Empty;
    public int Trust { get; init; }
    public static DomesticObservation Capture(WorldSnapshot world, ControlSeat seat, CommandContext context,
        IReadOnlyDictionary<int, OfficerAbilities> abilities)
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
            return Settlement(s, account.Id, account.Treasury.Money, account.Treasury.Food,
                people[s.Id].OrderBy(p => p.Id).Select(p => p.Id).ToImmutableArray());
        }).ToImmutableArray()) { Trust = world.Forces.Single(f => f.Id == seat.ForceId).Trust,
            Abilities = world.Officers.Where(p => p.OwnerId == seat.ForceId).ToImmutableDictionary(p => p.Id, p => abilities[p.Id]) };
    }

    internal static DomesticSettlementObservation Settlement(SettlementState s, int accountId, uint money, uint food,
        ImmutableArray<int> officers)
    {
        return new DomesticSettlementObservation(s.Id, accountId, money, food,
            s.Soldiers, s.Morale, s.PublicOrder, s.Durability, s.MaxDurability,
            officers)
            { DraftPopulation = s.DraftPopulation, SoldierCapacity = s.SoldierCapacity, Wounded = s.Wounded, ConsumesFood = s.ConsumesFood,
                Commerce = s.Commerce, MaxCommerce = s.MaxCommerce, Cultivation = s.Cultivation, MaxCultivation = s.MaxCultivation,
                UnavailableActivities = Enum.GetValues<DomesticActivity>().Where(activity =>
                    s.Facility?.BuilderUnitId is not null ||
                    s.Kind != SettlementKind.City && activity is not (DomesticActivity.Repair or DomesticActivity.Train) ||
                    (activity switch
                    {
                        DomesticActivity.Patrol => s.PatrolUsed || s.PublicOrder >= 1000,
                        DomesticActivity.Commerce => s.CommerceUsed || s.Commerce >= s.MaxCommerce,
                        DomesticActivity.Cultivation => s.CultivationUsed || s.Cultivation >= s.MaxCultivation,
                        DomesticActivity.Repair => s.RepairUsed || s.Durability >= s.MaxDurability,
                        DomesticActivity.Recruit => s.RecruitUsed || s.Soldiers >= s.SoldierCapacity || s.DraftPopulation == 0,
                        DomesticActivity.Train => s.TrainUsed || s.Morale >= 100 || s.Soldiers == 0,
                        _ => true
                    })).ToImmutableArray() };
    }
}
