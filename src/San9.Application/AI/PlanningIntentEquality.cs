using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

/// <summary>Batch-local rejection memory. No protocol encoding or persisted identities are involved.</summary>
public sealed class RejectedPlanningIntents
{
    private readonly HashSet<GameCommand> commands = new(PlanningIntentEquality.Instance);
    private readonly List<EconomicIntent> economics = [];
    private readonly List<DevelopmentCommand> developments = [];
    public void Add(GameCommand value) => commands.Add(value);
    public void Add(EconomicIntent value) => economics.Add(value);
    public void Add(DevelopmentCommand value) => developments.Add(value);
    public bool Contains(GameCommand value) => commands.Contains(value);
    public bool Contains(DevelopmentCommand value)
    {
        foreach (var saved in developments)
            if (PlanningIntentEquality.Development(saved, value)) return true;
        return false;
    }
    public bool Contains(EconomicIntent value)
    {
        foreach (var saved in economics)
        {
            if (saved.AccountId != value.AccountId || saved.SettlementId != value.SettlementId ||
                !saved.OfficerIds.AsSpan().SequenceEqual(value.OfficerIds.AsSpan())) continue;
            if ((saved, value) switch
            {
                (PurchaseFoodIntent a, PurchaseFoodIntent b) => a.Amount == b.Amount && a.Participants.AsSpan().SequenceEqual(b.Participants.AsSpan()),
                (DevelopIntent a, DevelopIntent b) => PlanningIntentEquality.Development(a.Development, b.Development),
                _ => false
            }) return true;
        }
        return false;
    }
}

/// <summary>Value equality for AI command payloads, including collection contents and ordering.</summary>
internal sealed class PlanningIntentEquality : IEqualityComparer<GameCommand>
{
    internal static readonly PlanningIntentEquality Instance = new();
    public bool Equals(GameCommand? x, GameCommand? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null || x.GetType() != y.GetType() || x.CommandId != y.CommandId ||
            x.IssuerId != y.IssuerId || x.Sequence != y.Sequence) return false;
        return (x, y) switch
        {
            (DeployUnitCommand a, DeployUnitCommand b) => a.SettlementId == b.SettlementId &&
                a.OfficerIds.AsSpan().SequenceEqual(b.OfficerIds.AsSpan()) && a.Soldiers == b.Soldiers &&
                a.FormationId == b.FormationId && a.Order == b.Order && a.DepartureDelayDays == b.DepartureDelayDays &&
                a.Waypoint == b.Waypoint && a.NavalFormationId == b.NavalFormationId && a.ConfirmAttack == b.ConfirmAttack &&
                a.Policies == b.Policies && MapEqual(a.Tactics, b.Tactics) && MapEqual(a.Ranks, b.Ranks),
            (TransportUnitCommand a, TransportUnitCommand b) => a.SettlementId == b.SettlementId &&
                a.OfficerIds.AsSpan().SequenceEqual(b.OfficerIds.AsSpan()) && a.Soldiers == b.Soldiers &&
                a.DestinationId == b.DestinationId && a.CargoMoney == b.CargoMoney && a.CargoFood == b.CargoFood &&
                a.Policies == b.Policies && a.Waypoint == b.Waypoint,
            (MoveOfficersCommand a, MoveOfficersCommand b) => a.DestinationId == b.DestinationId &&
                a.OfficerIds.AsSpan().SequenceEqual(b.OfficerIds.AsSpan()),
            (FoodTradeCommand a, FoodTradeCommand b) => a.SettlementId == b.SettlementId && a.Direction == b.Direction &&
                a.FoodAmount == b.FoodAmount && a.OfficerIds.AsSpan().SequenceEqual(b.OfficerIds.AsSpan()),
            (DevelopCommand a, DevelopCommand b) => Development(a.Development, b.Development),
            (AssignOfficesCommand a, AssignOfficesCommand b) => a.Assignments.AsSpan().SequenceEqual(b.Assignments.AsSpan()),
            _ => x == y
        };
    }

    // Collection fields are checked by Equals; use scalar routing fields to select a bucket without encoding payloads.
    public int GetHashCode(GameCommand value) => HashCode.Combine(value.GetType(), value.CommandId, value.IssuerId, value.Sequence, value switch
    {
        DeployUnitCommand c => HashCode.Combine(c.SettlementId, c.Soldiers, c.FormationId, c.Order),
        TransportUnitCommand c => HashCode.Combine(c.SettlementId, c.DestinationId, c.Soldiers),
        MoveOfficersCommand c => c.DestinationId,
        FoodTradeCommand c => HashCode.Combine(c.SettlementId, c.Direction, c.FoodAmount),
        DevelopCommand c => HashCode.Combine(c.Development.SettlementId, c.Development.Activity, c.Development.RecruitAmount),
        AssignOfficesCommand c => c.Assignments.Length,
        _ => value.GetHashCode()
    });

    internal static bool Development(DevelopmentCommand a, DevelopmentCommand b) => a.ForceId == b.ForceId &&
        a.SettlementId == b.SettlementId && a.Activity == b.Activity && a.RecruitAmount == b.RecruitAmount &&
        a.OfficerIds.AsSpan().SequenceEqual(b.OfficerIds.AsSpan());

    private static bool MapEqual<T>(ImmutableDictionary<int, T> a, ImmutableDictionary<int, T> b) =>
        ReferenceEquals(a, b) || a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var other) &&
            EqualityComparer<T>.Default.Equals(pair.Value, other));
}
