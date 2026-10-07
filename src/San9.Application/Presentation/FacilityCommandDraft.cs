using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

/// <summary>Local preparation bound to one world revision. Only CreateCommand crosses the mutation boundary.</summary>
public sealed class FacilityCommandDraft
{
    private readonly GameSession _session;
    public StrategySnapshot View { get; }
    public SettlementState Home { get; }
    public string Kind { get; }
    public DomesticActivity? Activity { get; }
    public FoodTradeDirection Direction => Kind == "buy" ? FoodTradeDirection.Buy : FoodTradeDirection.Sell;
    public bool IsTrade => Activity is null;
    public string Title => Kind switch { "commerce" => "商业", "cultivate" => "开垦", "repair" => "修筑", "recruit" => "征兵", "train" => "训练", "buy" => "买进", "sell" => "卖出", _ => throw new InvalidOperationException() };
    public string AbilityTitle => Activity is DomesticActivity.Repair or DomesticActivity.Recruit or DomesticActivity.Train ? "统率" : "政治";
    public ImmutableArray<int> Candidates { get; }
    public ImmutableArray<int> Members { get; private set; } = [];
    public uint FoodAmount { get; private set; }
    public uint RecruitAmount { get; private set; }
    public FoodTradeQuote? Quote { get; private set; }
    public uint Money { get; }
    public uint Food { get; }
    public int SelectionLimit => 5;
    public bool IsCurrent => _session.Snapshot.Revision == View.World.Revision;
    public int RecruitCount => DomesticDevelopment.RecruitCount(Home, Money, Members.Select(id => View.Officers.Single(o => o.Id == id).Abilities).ToArray());
    public uint Cost => IsTrade ? (uint)(Direction == FoodTradeDirection.Buy
        ? ((long)FoodAmount + View.World.Simulation.FoodExchangeRate - 1) / View.World.Simulation.FoodExchangeRate
        : FoodAmount / View.World.Simulation.FoodExchangeRate)
        : Activity == DomesticActivity.Train ? 0 : Activity == DomesticActivity.Recruit ? (RecruitAmount + 9) / 10 : (uint)Members.Length * 50;
    public bool CanExecute => IsCurrent && !Members.IsEmpty && (IsTrade
        ? Quote is { Available: true } && FoodAmount > 0 && FoodAmount <= Quote.MaximumFoodAmount
        : Cost <= Money && (Activity != DomesticActivity.Recruit || RecruitAmount > 0 && RecruitAmount <= RecruitCount));
    public static bool Supports(string kind) => kind is "commerce" or "cultivate" or "repair" or "recruit" or "train" or "buy" or "sell";
    public static string? UnavailableReasonKey(StrategySnapshot view, int id, string kind)
    {
        string? Reason()
        {
            var home = view.World.Settlements.SingleOrDefault(s => s.Id == id);
            if (!Supports(kind) || home is null || home.IsDormant || home.IsDestroyed || !home.CanHostPersons || home.Facility?.BuilderUnitId is not null || home.OwnerId != view.PlayerForceId ||
                (home.Kind != SettlementKind.City && kind is not ("repair" or "train"))) return "unavailable";
            if (view.World.Corps.Any(c => c.OwnerId == view.PlayerForceId && c.IsRoot && c.ControlMode == CorpsControlMode.Automatic) ||
                home.CorpsId is int corps && view.World.Corps.Any(c => c.Id == corps && c.ControlMode != CorpsControlMode.Direct)) return "unavailable";
            if (home.ConfusionDaysRemaining > 0) return "confused";
            if (view.IsSettlementEngaged(id)) return "engaged";
            if (!view.Officers.Any(o => o.IsAvailable && o.OwnerId == home.OwnerId && o.SettlementId == id)) return "no-officer";
            bool used = kind switch { "commerce" => home.CommerceUsed, "cultivate" => home.CultivationUsed, "repair" => home.RepairUsed,
                "recruit" => home.RecruitUsed, "train" => home.TrainUsed, _ => home.FoodTradeUsed };
            if (used) return "already-used";
            var treasury = Account(view, home).Treasury;
            return kind switch {
                "commerce" when home.Commerce >= home.MaxCommerce => "commerce-max",
                "cultivate" when home.Cultivation >= home.MaxCultivation => "cultivation-max",
                "repair" when home.Durability >= home.MaxDurability => "durability-max",
                "train" when home.Soldiers == 0 => "no-soldiers",
                "train" when home.Morale >= 100 => "morale-max",
                "recruit" when home.Soldiers >= home.SoldierCapacity => "soldiers-max",
                "recruit" when home.DraftPopulation == 0 => "no-recruits",
                "commerce" or "cultivate" or "repair" when treasury.Money < 50 => "funds",
                "recruit" or "buy" when treasury.Money == 0 => "funds",
                "buy" or "sell" when !home.MerchantPresent => "merchant-absent",
                "buy" when treasury.Food >= 1000000 => "food-max",
                "sell" when treasury.Money >= 1000000 => "money-max",
                "sell" when treasury.Food < view.World.Simulation.FoodExchangeRate => "no-food",
                _ => null };
        }
        return Reason() is string reason ? "ui.command-reason." + reason : null;
    }
    private static CorpsState Account(StrategySnapshot view, SettlementState home) => view.World.Corps.Single(c => home.CorpsId is int id ? c.Id == id : c.OwnerId == home.OwnerId && c.IsRoot);
    public FacilityCommandDraft(GameSession session, int id, string kind)
    {
        _session = session; View = session.View; Kind = kind;
        if (UnavailableReasonKey(View, id, kind) is string reason) throw new ArgumentException(reason, nameof(id));
        Home = View.World.Settlements.Single(s => s.Id == id);
        Activity = kind switch { "commerce" => DomesticActivity.Commerce, "cultivate" => DomesticActivity.Cultivation,
            "repair" => DomesticActivity.Repair, "recruit" => DomesticActivity.Recruit, "train" => DomesticActivity.Train, _ => null };
        var treasury = Account(View, Home).Treasury; Money = treasury.Money; Food = treasury.Food;
        Candidates = View.Officers.Where(o => o.IsAvailable && o.OwnerId == Home.OwnerId && o.SettlementId == id)
            .OrderByDescending(o => AbilityTitle == "统率" ? o.Abilities.Leadership : o.Abilities.Politics).ThenBy(o => o.Id).Select(o => o.Id).ToImmutableArray();
        if (Candidates.Length == 1) SetMembers(Candidates);
    }
    public bool CanSelect(IReadOnlyCollection<int> ids) => IsCurrent && ids.Count <= SelectionLimit && ids.Distinct().Count() == ids.Count && !ids.Except(Candidates).Any() &&
        (Activity is not (DomesticActivity.Commerce or DomesticActivity.Cultivation or DomesticActivity.Repair) || ids.Count * 50 <= Money);
    public bool SetMembers(IEnumerable<int> ids)
    {
        int[] selected = ids.ToArray();
        if (!CanSelect(selected)) return false;
        Members = DomesticDevelopment.OrderParticipants(View.World.Officers.Where(p => selected.Contains(p.Id)),
            View.Officers.ToDictionary(o => o.Id, o => o.Abilities), Activity ?? DomesticActivity.Commerce).Select(p => p.Id).ToImmutableArray();
        if (IsTrade)
        {
            Quote = _session.GetFoodTradeQuote(Home.OwnerId!.Value, Home.Id, Members, Direction);
            FoodAmount = Quote.Available ? Quote.MaximumFoodAmount : 0;
        }
        if (Activity == DomesticActivity.Recruit) RecruitAmount = (uint)RecruitCount;
        return true;
    }
    public bool SetFoodAmount(uint value)
    {
        if (!IsCurrent || Quote is not { Available: true } || value == 0 || value > Quote.MaximumFoodAmount) return false;
        FoodAmount = value; return true;
    }
    public GameCommand CreateCommand(Guid id, long sequence)
    {
        if (!CanExecute) throw new InvalidOperationException("Invalid or stale facility preparation.");
        return Activity is {} activity ? new DevelopCommand(id, Home.OwnerId!.Value, sequence,
            new(Home.OwnerId.Value, Home.Id, Members, activity) { RecruitAmount = activity == DomesticActivity.Recruit ? RecruitAmount : null })
            : new FoodTradeCommand(id, Home.OwnerId!.Value, sequence, Home.Id, Members, Direction, FoodAmount);
    }
    public bool SetRecruitAmount(uint value)
    {
        if (!IsCurrent || Activity != DomesticActivity.Recruit || value == 0 || value > RecruitCount) return false;
        RecruitAmount = value; return true;
    }
}
